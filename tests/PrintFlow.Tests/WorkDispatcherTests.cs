using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// الموزّع الحي: مين بياخد إيه وهو الشغل ماشي.
///
/// ═══ أنهي تست هو الأهم ═══
///
/// مش <see cref="A_Dead_Machines_Work_Moves_To_The_Others"/> — ده الميزة.
/// الأهم هو <see cref="Work_That_Already_Went_Out_Is_Never_Sent_Again"/>.
///
/// السبب: لو الميزة مااشتغلتش، المطبعة بتشوف مكنة واقفة وبتحل المشكلة
/// بإيدها. لكن لو البرنامج أعاد شغل **كان اتبعت فعلًا**، الورق بيطلع
/// مرتين والفلوس بتضيع ومحدش بيكتشف غير بالعد. الميزة تحسين؛ عدم
/// التكرار شرط.
/// </summary>
public class WorkDispatcherTests
{
    // ══════════ أدوات ══════════

    private static WorkUnit Unit(string name, int copies = 1, int pages = 10)
        => new(name, pages, copies);

    private static Dictionary<string, IReadOnlyList<WorkUnit>> Lanes(
        params (string Printer, WorkUnit[] Units)[] lanes)
        => lanes.ToDictionary(
            lane => lane.Printer,
            lane => (IReadOnlyList<WorkUnit>)lane.Units.ToList(),
            StringComparer.Ordinal);

    /// <summary>صحة مكنة بتتحكم فيها بقاعدة لكل اسم.</summary>
    private sealed class Health : IPrinterHealth
    {
        private readonly Dictionary<string, Func<PrinterHealth>> _rules = new(StringComparer.Ordinal);

        public Health Says(string printer, Func<PrinterHealth> rule)
        {
            _rules[printer] = rule;
            return this;
        }

        public Health Stopped(string printer, string reason = "الورق خلص")
            => Says(printer, () => PrinterHealth.Stopped(reason));

        public Task<PrinterHealth> CheckAsync(string printer, CancellationToken cancellationToken = default)
            => Task.FromResult(_rules.TryGetValue(printer, out var rule) ? rule() : PrinterHealth.Fine);
    }

    /// <summary>بيسجّل كل نداء طباعة، وبيرد بالنتيجة اللي التست عايزها.</summary>
    private sealed class Press
    {
        private readonly Lock _gate = new();

        public List<(string Printer, WorkUnit Unit)> Calls { get; } = [];

        public Func<string, WorkUnit, PrintOutcome> Answer { get; set; } =
            (printer, unit) => PrintOutcome.Delivered($"[نجاح] {unit.Copies} إلى {printer}");

        /// <summary>تأخير حقيقي صغير — عشان نقدر نختبر السباقات.</summary>
        public int DelayMilliseconds { get; set; }

        public async Task<PrintOutcome> PrintAsync(string printer, WorkUnit unit, CancellationToken token)
        {
            if (DelayMilliseconds > 0)
            {
                await Task.Delay(DelayMilliseconds, token);
            }
            else
            {
                await Task.Yield();
            }

            lock (_gate)
            {
                Calls.Add((printer, unit));
            }

            return Answer(printer, unit);
        }

        public int CopiesPrinted => Calls.Sum(call => call.Unit.Copies);

        public int CopiesOn(string printer)
            => Calls.Where(call => call.Printer == printer).Sum(call => call.Unit.Copies);

        public int TimesSent(string document)
            => Calls.Count(call => call.Unit.Path == document);
    }

    /// <summary>
    /// ساعة بتمشي لوحدها مع كل نظرة، فمهلة الاستسلام بتوصل بسرعة وبثبات
    /// بدل ما التست يستنى دقايق حقيقية.
    /// </summary>
    private static Func<DateTimeOffset> TickingClock(int stepMilliseconds = 100)
    {
        long ticks = 0;
        return () => DateTimeOffset.UnixEpoch.AddMilliseconds(
            Interlocked.Add(ref ticks, stepMilliseconds));
    }

    private static Task<DispatchReport> Run(
        Dictionary<string, IReadOnlyList<WorkUnit>> lanes,
        Press press,
        IPrinterHealth? health = null,
        DispatchOptions? options = null,
        List<string>? said = null,
        bool realDelay = false)
        => WorkDispatcher.RunAsync(
            lanes,
            press.PrintAsync,
            health,
            options ?? new DispatchOptions { GiveUpAfter = TimeSpan.FromSeconds(1) },
            say: line => said?.Add(line),
            wait: realDelay
                ? (delay, token) => Task.Delay(1, token)
                : async (_, _) => await Task.Yield(),
            clock: TickingClock());

    // ══════════ الحالة الطبيعية ══════════

    [Fact]
    public async Task Every_Piece_Reaches_A_Printer_When_All_Is_Well()
    {
        var press = new Press();

        var report = await Run(
            Lanes(
                ("مكنة1", [Unit("أ.pdf", 3), Unit("أ.pdf", 3)]),
                ("مكنة2", [Unit("ب.pdf", 4)])),
            press);

        Assert.Equal(10, press.CopiesPrinted);
        Assert.True(report.Clean);
        Assert.Empty(report.NeverSent);
        Assert.Empty(report.InDoubt);
    }

    [Fact]
    public async Task Each_Machine_Prints_Its_Own_Share_When_Nothing_Goes_Wrong()
    {
        // الطوابير المنفصلة هي اللي بتحافظ على عدالة الخطة. لو الموزّع
        // بقى طابور مشترك، التوزيع بيبقى "اللي يلحق" مش "اللي الخطة قالت".
        var press = new Press();

        await Run(
            Lanes(
                ("مكنة1", [Unit("أ.pdf", 17)]),
                ("مكنة2", [Unit("أ.pdf", 17)]),
                ("مكنة3", [Unit("أ.pdf", 16)])),
            press);

        Assert.Equal(17, press.CopiesOn("مكنة1"));
        Assert.Equal(17, press.CopiesOn("مكنة2"));
        Assert.Equal(16, press.CopiesOn("مكنة3"));
    }

    [Fact]
    public async Task Nothing_Is_Printed_Twice_When_All_Is_Well()
    {
        var press = new Press();

        await Run(
            Lanes(
                ("مكنة1", [Unit("أ.pdf", 5)]),
                ("مكنة2", [Unit("ب.pdf", 5)])),
            press);

        Assert.Equal(1, press.TimesSent("أ.pdf"));
        Assert.Equal(1, press.TimesSent("ب.pdf"));
    }

    // ══════════ العطل: الشغل بيكمّل على الباقي ══════════

    [Fact]
    public async Task A_Dead_Machines_Work_Moves_To_The_Others()
    {
        // ═══ ده الطلب بالنص ═══
        //
        // "لو برنتر فصل أو خلص ورق، في محل العطل يكمل الشغل على الباقي
        //  مش ياخد الأوردر لوحده."
        //
        // مكنتين، كل واحدة نصيبها ٤ قطع. الواقعة عمرها ما هتقدر تسحب،
        // فالشغل كله لازم يخرج من السليمة — من غير ما نخسر ولا نسخة.
        var press = new Press();

        var report = await Run(
            Lanes(
                ("سليمة", [Unit("أ.pdf"), Unit("أ.pdf"), Unit("أ.pdf"), Unit("أ.pdf")]),
                ("واقعة", [Unit("أ.pdf"), Unit("أ.pdf"), Unit("أ.pdf"), Unit("أ.pdf")])),
            press,
            new Health().Stopped("واقعة"));

        Assert.Equal(8, press.CopiesPrinted);
        Assert.Equal(8, press.CopiesOn("سليمة"));
        Assert.Equal(0, press.CopiesOn("واقعة"));
        Assert.Empty(report.NeverSent);
    }

    [Fact]
    public async Task A_Machine_That_Refuses_The_Job_Hands_It_To_Someone_Else()
    {
        // مكنة بترد بـ NotSent — يعني الأمر اترفض قبل ما أي ورق يتحرك.
        // القطعة دي **آمن** تروح لغيرها، ولازم تروح.
        var press = new Press
        {
            Answer = (printer, unit) => printer == "رافضة"
                ? PrintOutcome.NotSent("[فشل] الطابعة مش متاحة")
                : PrintOutcome.Delivered("[نجاح]")
        };

        var report = await Run(
            Lanes(
                ("رافضة", [Unit("أ.pdf", 6)]),
                ("سليمة", [])),
            press,
            options: new DispatchOptions
            {
                StrikesBeforeRetiring = 1,
                GiveUpAfter = TimeSpan.FromSeconds(1)
            },
            realDelay: true);

        Assert.Equal(6, press.CopiesOn("سليمة"));
        Assert.Empty(report.NeverSent);
    }

    [Fact]
    public async Task The_Last_Piece_Is_Not_Left_Behind_When_It_Bounces_Back()
    {
        // ═══ سباق حقيقي، مش نظري ═══
        //
        // القطعة الأخيرة بتتشال من الطابور، فالطوابير بتبقى فاضية وهي
        // لسه ماشية. لو العمال التانيين مشيوا في اللحظة دي، وبعدين
        // القطعة فشلت ورجعت الطابور — مش هيبقى فيه حد ياخدها، وهتطلع
        // "ماتبعتش" وإحنا كان عندنا مكنة فاضية.
        //
        // التأخير هنا مقصود عشان يفتح الشباك ده على الآخر.
        var press = new Press
        {
            DelayMilliseconds = 30,
            Answer = (printer, unit) => printer == "رافضة"
                ? PrintOutcome.NotSent("[فشل]")
                : PrintOutcome.Delivered("[نجاح]")
        };

        var report = await Run(
            Lanes(
                ("رافضة", [Unit("أ.pdf", 9)]),
                ("سليمة", [])),
            press,
            options: new DispatchOptions
            {
                StrikesBeforeRetiring = 1,
                GiveUpAfter = TimeSpan.FromSeconds(1)
            },
            realDelay: true);

        Assert.Empty(report.NeverSent);
        Assert.Equal(9, press.CopiesOn("سليمة"));
    }

    [Fact]
    public async Task A_Piece_That_Bounces_Back_Is_Never_Lost_While_The_Log_Is_Written()
    {
        // ═══ ليه التست ده شكله غريب ═══
        //
        // فيه سباق حقيقي في الموزّع: بين لحظة ما الطباعة ترجع "مارحتش"
        // ولحظة ما القطعة ترجع الطابور، القطعة بتبقى **في إيدنا** —
        // الطوابير فاضية وهي مش فيها. لو باقي العمال بصّوا في اللحظة
        // دي، هيشوفوا "خلاص خلص" ويمشيوا، والقطعة ترجع بعدهم ومتلاقيش
        // حد ياخدها.
        //
        // الشباك ده عرضه ميكروثانية، فالتست العادي بيمسكه مرة من كام
        // مرة — وفعلًا: التخريب عدّى مرة والتست فاضل أخضر. تست بيمسك
        // العطل أحيانًا مش حارس، ده إنذار كداب.
        //
        // الحيلة هنا إننا نفتح الشباك على الآخر بحاجة **شرعية**: كتابة
        // اللوج بتحصل جوه الشباك بالظبط. بنخلّيها بطيئة، فالشباك يبقى
        // ٦٠ مللي بدل ميكروثانية — ولو الحماية اتشالت، العطل بيحصل كل
        // مرة مش مرة من عشرة.
        var press = new Press
        {
            Answer = (printer, unit) => printer == "رافضة"
                ? PrintOutcome.NotSent("[فشل] الطابعة مش متاحة")
                : PrintOutcome.Delivered("[نجاح]")
        };

        var said = new List<string>();
        int slowed = 0;

        void Say(string line)
        {
            lock (said)
            {
                said.Add(line);
            }

            // أول سطر فشل بس — ده اللي بيتكتب جوه الشباك
            if (line.Contains("[فشل]") && Interlocked.Increment(ref slowed) == 1)
            {
                Thread.Sleep(60);
            }
        }

        var report = await WorkDispatcher.RunAsync(
            Lanes(
                ("رافضة", [Unit("ملزمة.pdf", 9)]),
                ("سليمة", [])),
            press.PrintAsync,
            health: null,
            options: new DispatchOptions
            {
                StrikesBeforeRetiring = 1,
                GiveUpAfter = TimeSpan.FromSeconds(5)
            },
            say: Say,
            wait: (delay, token) => Task.Delay(1, token),
            clock: TickingClock());

        // القطعة لازم تلاقي المكنة السليمة مستنياها
        Assert.Empty(report.NeverSent);
        Assert.Equal(9, press.CopiesOn("سليمة"));
    }

    [Fact]
    public async Task A_Free_Machine_Helps_One_That_Is_Falling_Behind()
    {
        // المكن مش بتطبع بنفس السرعة، والخطة بتتحسب بالصفحات مش بالسرعة.
        // المكنة اللي طابورها بيكوّم (بتقول "مشغولة") محتاجة مساعدة،
        // واللي فاضية لازم تشيل من شغلها بدل ما تقف تتفرّج.
        var press = new Press();

        await Run(
            Lanes(
                ("بتتأخر", [Unit("أ.pdf"), Unit("أ.pdf"), Unit("أ.pdf"), Unit("أ.pdf")]),
                ("فاضية", [])),
            press,
            new Health().Says("بتتأخر", () => PrinterHealth.Busy("طابورها مكوّم")),
            realDelay: true);

        Assert.Equal(4, press.CopiesPrinted);
        Assert.Equal(4, press.CopiesOn("فاضية"));
    }

    [Fact]
    public async Task A_Machine_Keeping_Up_Does_Not_Get_Its_Work_Taken()
    {
        // ═══ التست ده اتكتب بعد ما التخريب فضح باج حقيقي ═══
        //
        // أول نسخة من الموزّع كانت بتسمح لأي مكنة تشيل من أي طابور فيه
        // شغل. وعشان الإرسال بيخلص في أجزاء من الثانية، أول مكنة تبدأ
        // كانت بتخلص طابورها وتلاقي الباقيين لسه ماتحركوش فتشفط شغلهم.
        //
        // الخطة العادلة ١٧/١٧/١٦ كانت بتطلع ٣٣/١٧/٠ — يعني كل حساب
        // التوزيع بيتحوّل لـ "اللي يلحق". التست ده هو اللي بيمسك ده.
        var press = new Press();

        await Run(
            Lanes(
                ("مكنة1", [Unit("أ.pdf", 17)]),
                ("مكنة2", [Unit("أ.pdf", 17)]),
                ("مكنة3", [Unit("أ.pdf", 16)])),
            press,
            realDelay: true);

        Assert.Equal(17, press.CopiesOn("مكنة1"));
        Assert.Equal(17, press.CopiesOn("مكنة2"));
        Assert.Equal(16, press.CopiesOn("مكنة3"));
    }

    [Fact]
    public async Task A_Machine_That_Is_Fixed_Comes_Back_On_Its_Own()
    {
        // الورق بيخلص وبيتحط تاني. المفروض المكنة ترجع تشتغل من غير ما
        // حد يقفل البرنامج أو يعيد الأوردر.
        int looks = 0;

        var health = new Health().Says("بتصلّح", () =>
            Interlocked.Increment(ref looks) <= 3
                ? PrinterHealth.Stopped("الورق خلص")
                : PrinterHealth.Fine);

        var press = new Press();
        var said = new List<string>();

        await Run(
            Lanes(("بتصلّح", [Unit("أ.pdf", 4)])),
            press,
            health,
            said: said);

        Assert.Equal(4, press.CopiesOn("بتصلّح"));
        Assert.Contains(said, line => line.Contains("رجعت"));
    }

    // ══════════ الشرط: مفيش ورق بيطلع مرتين ══════════

    [Fact]
    public async Task Work_That_Already_Went_Out_Is_Never_Sent_Again()
    {
        // ═══ أهم تست في الملف ═══
        //
        // Abandoned معناها: الأمر **وصل** الطابعة وبعدين وقف. ممكن يكون
        // طلع منه ورق وممكن لأ — إحنا مش عارفين.
        //
        // الغريزة بتقول "ابعتها لمكنة تانية عشان نضمن". ودي بالظبط
        // الغلطة اللي بتخلّي المطبعة تطبع ٥٠ ملزمة مرتين.
        //
        // القاعدة: بنوقف، وبنقول اللي في الشك بالاسم، والبني آدم يقرر.
        var press = new Press
        {
            Answer = (printer, unit) => printer == "وقفت"
                ? PrintOutcome.Abandoned("[فشل] مردّتش")
                : PrintOutcome.Delivered("[نجاح]")
        };

        var report = await Run(
            Lanes(
                ("وقفت", [Unit("مشكوك.pdf", 12)]),
                ("سليمة", [Unit("عادي.pdf", 5)])),
            press,
            realDelay: true);

        // اتبعتت مرة واحدة بس — عمرها ما اتنقلت
        Assert.Equal(1, press.TimesSent("مشكوك.pdf"));
        Assert.Equal(0, press.CopiesOn("سليمة") - 5);

        // والشك اتقال صراحة مش اتبلع
        Assert.Single(report.InDoubt);
        Assert.Equal("مشكوك.pdf", report.InDoubt[0].Path);
        Assert.Equal(12, report.InDoubt[0].Copies);
        Assert.False(report.Clean);
    }

    [Fact]
    public async Task A_Machine_That_Stalled_Mid_Job_Stops_Getting_New_Work()
    {
        // بعد ما وقفت وهي شغالة، مابنديهاش حاجة جديدة — إحنا مش عارفين
        // هي في إيه أصلًا.
        var press = new Press
        {
            Answer = (printer, unit) => printer == "وقفت"
                ? PrintOutcome.Abandoned("[فشل] مردّتش")
                : PrintOutcome.Delivered("[نجاح]")
        };

        await Run(
            Lanes(
                ("وقفت", [Unit("أ.pdf"), Unit("أ.pdf"), Unit("أ.pdf")]),
                ("سليمة", [])),
            press,
            realDelay: true);

        Assert.Equal(1, press.Calls.Count(call => call.Printer == "وقفت"));
    }

    [Fact]
    public async Task A_Broken_File_Is_Not_Passed_Around_Every_Machine()
    {
        // ملف مش موجود هيفشل على كل مكنة بالظبط زي ما فشل على الأولى.
        // تدويره على العشر مكن = عشر تشغيلات على الفاضي وعشر سطور فشل
        // في اللوج.
        var press = new Press
        {
            Answer = (printer, unit) => unit.Path == "بايظ.pdf"
                ? PrintOutcome.BadJob("[فشل] الملف مش موجود")
                : PrintOutcome.Delivered("[نجاح]")
        };

        var report = await Run(
            Lanes(
                ("مكنة1", [Unit("بايظ.pdf", 3)]),
                ("مكنة2", []),
                ("مكنة3", [])),
            press,
            realDelay: true);

        Assert.Equal(1, press.TimesSent("بايظ.pdf"));
        Assert.Single(report.Rejected);
        Assert.False(report.Clean);
    }

    [Fact]
    public async Task A_Broken_File_Does_Not_Kill_The_Machine_That_Found_It()
    {
        // الملف البايظ مش عيب المكنة — لازم تكمّل شغلها عادي بعده.
        var press = new Press
        {
            Answer = (printer, unit) => unit.Path == "بايظ.pdf"
                ? PrintOutcome.BadJob("[فشل] الملف مش موجود")
                : PrintOutcome.Delivered("[نجاح]")
        };

        await Run(
            Lanes(("مكنة1", [Unit("بايظ.pdf"), Unit("سليم.pdf", 7)])),
            press);

        Assert.Equal(7, press.Calls
            .Where(call => call.Unit.Path == "سليم.pdf")
            .Sum(call => call.Unit.Copies));
    }

    // ══════════ الكابح: مشغولة مش واقفة ══════════

    [Fact]
    public async Task A_Busy_Machine_Is_Waited_For_Not_Skipped()
    {
        // "مشغولة" حالة طبيعية لمكنة بتطبع. مايصحش نعتبرها عطل ونشيل
        // شغلها — هي هتفضى وتكمّل.
        int looks = 0;

        var health = new Health().Says("بتطبع", () =>
            Interlocked.Increment(ref looks) <= 3
                ? PrinterHealth.Busy("لسه قدامها ٣ جوبات")
                : PrinterHealth.Fine);

        var press = new Press();
        var said = new List<string>();

        await Run(Lanes(("بتطبع", [Unit("أ.pdf", 5)])), press, health, said: said);

        Assert.Equal(5, press.CopiesOn("بتطبع"));
        Assert.DoesNotContain(said, line => line.Contains("[وقفة]"));
    }

    [Fact]
    public async Task A_Faulted_Machine_Is_Announced_Once_Not_On_Every_Look()
    {
        // اللوج اللي بيتملى بنفس السطر كل شوية بيبقى ملوش لازمة —
        // المستخدم بيبطّل يقراه فبيفوّت السطور المهمة.
        //
        // ⚠ الطباعة البطيئة تحت مش زيادة: النسخة الأولى من التست كانت
        // بمكنة سريعة، فكانت بتخلص كل الشغل قبل ما الواقعة تبص على
        // نفسها تاني مرة — يعني نظرة واحدة، يعني سطر واحد **مهما كان**.
        // شيلنا الحارس في التخريب والتست فضل أخضر. البطء بيدّي الواقعة
        // فرصة تبص عشرات المرات، وساعتها الحارس بيبقى هو الفرق.
        var press = new Press { DelayMilliseconds = 40 };
        var said = new List<string>();

        await Run(
            Lanes(
                ("واقعة", [Unit("أ.pdf"), Unit("أ.pdf"), Unit("أ.pdf"), Unit("أ.pdf")]),
                ("سليمة", [])),
            press,
            new Health().Stopped("واقعة", "الورق خلص"),
            said: said,
            realDelay: true);

        Assert.Equal(1, said.Count(line => line.Contains("[وقفة]") && line.Contains("واقعة")));
    }

    // ══════════ لما كله يقع ══════════

    [Fact]
    public async Task When_Every_Machine_Is_Down_We_Say_What_Was_Left()
    {
        // مافيش حاجة تتعمل — بس البرنامج مايفضلش يلف للأبد، ومايقولش
        // "خلص" وهو مابعتش حاجة.
        var press = new Press();

        var report = await Run(
            Lanes(
                ("واقعة1", [Unit("أ.pdf", 4)]),
                ("واقعة2", [Unit("ب.pdf", 6)])),
            press,
            new Health().Stopped("واقعة1").Stopped("واقعة2"));

        Assert.Empty(press.Calls);
        Assert.Equal(10, report.NeverSent.Sum(unit => unit.Copies));
        Assert.False(report.Clean);
        Assert.Contains("ماوصلتش", report.Summarise());
    }

    [Fact]
    public async Task A_Machine_Stuck_Busy_Forever_Does_Not_Hang_The_Order()
    {
        // مكنة بتقول "مشغولة" على طول وطابورها مش بيتحرك (جوب زنق في
        // السبولر من غير ما ويندوز يبلّغ عن عطل). من غير شبكة الأمان،
        // الحلقة دي بتفضل تلف للأبد والبرنامج بيعلّق.
        var press = new Press();

        var report = await Run(
            Lanes(("زنقانة", [Unit("أ.pdf", 4)])),
            press,
            new Health().Says("زنقانة", () => PrinterHealth.Busy("طابورها واقف")));

        Assert.Empty(press.Calls);
        Assert.Equal(4, report.NeverSent.Sum(unit => unit.Copies));
    }

    [Fact]
    public async Task An_Empty_Order_Finishes_Instead_Of_Waiting()
    {
        var press = new Press();

        var report = await Run(Lanes(("مكنة1", []), ("مكنة2", [])), press);

        Assert.Empty(press.Calls);
        Assert.True(report.Clean);
    }

    // ══════════ التقرير ══════════

    [Fact]
    public async Task The_Report_Counts_What_Each_Machine_Actually_Did()
    {
        // "المتوقع" و"اللي حصل" حاجتين مختلفتين لما يبقى فيه عطل.
        // التقرير لازم يقول التاني.
        var press = new Press();

        var report = await Run(
            Lanes(
                ("سليمة", [Unit("أ.pdf", 3, pages: 10)]),
                ("واقعة", [Unit("ب.pdf", 2, pages: 10)])),
            press,
            new Health().Stopped("واقعة"));

        var good = report.Printers.Single(p => p.PrinterName == "سليمة");
        var bad = report.Printers.Single(p => p.PrinterName == "واقعة");

        Assert.Equal(5, good.Copies);
        Assert.Equal(50, good.Pages);
        Assert.Equal(0, bad.Copies);
        Assert.True(bad.Retired);
        Assert.Equal(50, report.PagesSent);
    }

    [Fact]
    public async Task The_Summary_Stays_Quiet_When_Nothing_Went_Wrong()
    {
        var press = new Press();

        var report = await Run(Lanes(("مكنة1", [Unit("أ.pdf", 3)])), press);

        string summary = report.Summarise();

        Assert.DoesNotContain("⚠", summary);
        Assert.Contains("مكنة1", summary);
    }
}
