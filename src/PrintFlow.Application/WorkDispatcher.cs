using PrintFlow.Domain;

namespace PrintFlow.Application;

/// <summary>مقادير الموزّع. كلها ليها قيم معقولة، والتستات بتغيّرها عشان تجري بسرعة.</summary>
public sealed record DispatchOptions
{
    /// <summary>المكنة الواقفة (عطل) بنسأل عنها تاني كل قد إيه.</summary>
    public TimeSpan RecheckDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// المكنة المشغولة بنسأل عنها تاني كل قد إيه.
    ///
    /// أقصر من مهلة العطل عن قصد: دي بتحصل كل شوية في الشغل العادي،
    /// والتأخير فيها بيتحوّل مباشرة لمكنة واقفة بين قطعة والتانية.
    /// </summary>
    public TimeSpan BusyRecheckDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// كل المكن واقفة ومفيش أي تقدم بقى له قد إيه → نسيب الباقي ونقول الحقيقة.
    ///
    /// عشر دقايق مش رقم عشوائي: ده الوقت اللي بيكفي حد يحط ورق أو يفتح
    /// مكنة اتقفلت. أقل من كده هنسيب شغل والمطبعة كانت هتخلصه.
    /// </summary>
    public TimeSpan GiveUpAfter { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>كام فشل ورا بعض قبل ما نسيب المكنة خالص.</summary>
    public int StrikesBeforeRetiring { get; init; } = 2;
}

/// <summary>اللي مكنة واحدة عملته فعلًا لما الشغل خلص.</summary>
public sealed record PrinterTally(
    string PrinterName,
    int Units,
    int Copies,
    int Pages,
    string? RetiredBecause)
{
    public bool Retired => RetiredBecause is not null;
}

/// <summary>
/// نتيجة التوزيع كاملة.
///
/// التلات قوايم دي هي اللي بتفرّق بين برنامج بيقول الحقيقة وبرنامج
/// بيقول "خلص" وسايب المطبعة تكتشف الناقص بالعد:
///
///   • <see cref="NeverSent"/> — شغل ماوصلش أي مكنة. مضمون إنه ماطبعش،
///     فينفع يتبعت تاني بأمان.
///   • <see cref="InDoubt"/> — شغل اتبعت ووقف. ممكن يكون طلع كله أو نصه
///     أو ولا حاجة. البني آدم بس هو اللي يقدر يقرر.
///   • <see cref="Rejected"/> — ملفات المشكلة فيها هي نفسها.
/// </summary>
public sealed record DispatchReport(
    IReadOnlyList<string> Lines,
    IReadOnlyList<PrinterTally> Printers,
    IReadOnlyList<WorkUnit> NeverSent,
    IReadOnlyList<WorkUnit> InDoubt,
    IReadOnlyList<WorkUnit> Rejected)
{
    /// <summary>خلص كله من غير أي ناقص ولا شك.</summary>
    public bool Clean => NeverSent.Count == 0 && InDoubt.Count == 0 && Rejected.Count == 0;

    /// <summary>إجمالي الورق اللي اتبعت فعلًا.</summary>
    public int PagesSent => Printers.Sum(p => p.Pages);

    /// <summary>سطر عربي بيتقال للمستخدم في الآخر.</summary>
    public string Summarise()
    {
        var working = Printers.Where(p => p.Units > 0).ToList();

        string line = working.Count == 0
            ? "مفيش أي شغل اتبعت."
            : $"خلص: {PagesSent} صفحة على {working.Count} مكنة " +
              $"({string.Join("، ", working.Select(p => $"{p.PrinterName} {p.Copies} نسخة"))}).";

        var retired = Printers.Where(p => p.Retired).ToList();

        if (retired.Count > 0)
        {
            line += $" مكن وقفت في النص: {string.Join("، ", retired.Select(p => $"{p.PrinterName} ({p.RetiredBecause})"))}.";
        }

        if (InDoubt.Count > 0)
        {
            line += $" ⚠ {InDoubt.Sum(u => u.Copies)} نسخة مش متأكدين طلعت ولا لأ — راجعها بنفسك قبل ما تعيدها.";
        }

        if (NeverSent.Count > 0)
        {
            line += $" ⚠ {NeverSent.Sum(u => u.Copies)} نسخة ماوصلتش أي مكنة خالص.";
        }

        if (Rejected.Count > 0)
        {
            line += $" ⚠ {Rejected.Count} قطعة اترفضت (مشكلة في الملف مش في المكنة).";
        }

        return line;
    }
}

/// <summary>
/// بيشغّل خطة التوزيع على المكن **وهي شغالة**، مش بيبعتها كلها مرة واحدة.
///
/// ═══ الفرق عن اللي كان ═══
///
/// النسخة القديمة كانت بتعمل كده:
///
///     var tasks = plan.Assignments.Select(a => print(a));
///     await Task.WhenAll(tasks);
///
/// يعني الخطة بتتحسب مرة واحدة من الأرقام، وكل الأوامر بتتبعت في نفس
/// اللحظة، وبعدها مفيش أي تدخل. ده كان بيشتغل تمام طول ما كل المكن
/// سليمة — وبيقع في أول عطل حقيقي:
///
///   • مكنة الورق خلص منها بتفضل تقبل جوبات وتكوّمها في طابورها،
///     والمكن التانية بتخلص وتقف تتفرّج. الأوردر بيستنى أبطأ حاجة
///     في الأوضة.
///   • مكنة اتفصلت بعد ما الأوامر اتبعتت = نصيبها كله ضاع، والمستخدم
///     مايعرفش غير لما يعد الورق.
///
/// ═══ اللي بيحصل دلوقتي ═══
///
/// كل مكنة عندها **طابور** (نصيبها من الخطة العادلة، مقطّع لقطع صغيرة)
/// و**عامل** بيلف عليه:
///
///   ١) يسأل المكنة: تقدري تاخدي شغل دلوقتي؟ (<see cref="IPrinterHealth"/>)
///      لأ → يقول السبب مرة واحدة، ويستنى ويسأل تاني. الشغل اللي في
///      طابورها بيبقى متاح لغيرها فورًا، فالباقي مابيقفش.
///   ٢) آه → ياخد أول قطعة من طابوره. طابوره فاضي؟ **يشيل قطعة من
///      طابور مكنة تانية** — الأولوية للمكن الواقفة، وبعدها لأطول
///      طابور. ودي اللي بتخلّي المكنة السريعة تساعد البطيئة بدل ما
///      تقف.
///   ٣) القطعة فشلت قبل ما ورق يتحرك؟ ترجع الطابور وغيرها يشيلها.
///      فشلت بعد ما اتبعتت؟ **مابنعيدهاش** — بنقول اللي حصل بالظبط
///      وسايبين القرار للبني آدم، عشان ماتطلعش مرتين.
///
/// النتيجة: العطل بيكلّف قطعة واحدة، مش أوردر.
///
/// ═══ ملحوظة عن السرعة ═══
///
/// السحب من طابور الغير مش بس للأعطال. المكن مش بتطبع بنفس السرعة أصلًا
/// (موديلات مختلفة، شبكة، درايفرات)، والخطة بتتحسب بالصفحات لأن مفيش
/// طريقة نعرف بيها السرعة قبل ما نشتغل. السحب بيصحّح ده لوحده أثناء
/// الشغل — اللي بيخلص بدري بيشيل من اللي لسه.
/// </summary>
public sealed class WorkDispatcher
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedList<WorkUnit>> _lanes;
    private readonly Dictionary<string, Counter> _counters;
    private readonly HashSet<string> _retired = new(StringComparer.Ordinal);

    /// <summary>
    /// آخر حالة معروفة لكل مكنة — بيملاها كل عامل عن مكنته.
    ///
    /// موجودة عشان قرار "أشيل من طابور غيري ولا لأ" لازم يتاخد **جوه
    /// القفل** وبسرعة، ومينفعش نروح نسأل WMI ونحنا ماسكين القفل.
    /// </summary>
    private readonly Dictionary<string, PrinterHealth> _lastKnown = new(StringComparer.Ordinal);

    private readonly List<string> _lines = [];
    private readonly List<WorkUnit> _inDoubt = [];
    private readonly List<WorkUnit> _rejected = [];

    private readonly Func<string, WorkUnit, CancellationToken, Task<PrintOutcome>> _print;
    private readonly IPrinterHealth _health;
    private readonly DispatchOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _say;

    private DateTimeOffset _lastProgress;
    private int _inFlight;
    private bool _stopEverything;

    private sealed class Counter
    {
        public int Units;
        public int Copies;
        public int Pages;
        public string? RetiredBecause;
    }

    private WorkDispatcher(
        IReadOnlyDictionary<string, IReadOnlyList<WorkUnit>> lanes,
        Func<string, WorkUnit, CancellationToken, Task<PrintOutcome>> print,
        IPrinterHealth health,
        DispatchOptions options,
        Action<string>? say,
        Func<TimeSpan, CancellationToken, Task> wait,
        Func<DateTimeOffset> clock)
    {
        _lanes = lanes.ToDictionary(
            pair => pair.Key,
            pair => new LinkedList<WorkUnit>(pair.Value),
            StringComparer.Ordinal);

        _counters = _lanes.Keys.ToDictionary(name => name, _ => new Counter(), StringComparer.Ordinal);

        _print = print;
        _health = health;
        _options = options;
        _say = say;
        _wait = wait;
        _clock = clock;
        _lastProgress = clock();
    }

    /// <summary>بيشغّل التوزيع ويستنى لحد ما يخلص، ويرجّع اللي حصل بالظبط.</summary>
    public static async Task<DispatchReport> RunAsync(
        IReadOnlyDictionary<string, IReadOnlyList<WorkUnit>> lanes,
        Func<string, WorkUnit, CancellationToken, Task<PrintOutcome>> print,
        IPrinterHealth? health = null,
        DispatchOptions? options = null,
        Action<string>? say = null,
        Func<TimeSpan, CancellationToken, Task>? wait = null,
        Func<DateTimeOffset>? clock = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(print);

        var dispatcher = new WorkDispatcher(
            lanes,
            print,
            health ?? new AlwaysFinePrinterHealth(),
            options ?? new DispatchOptions(),
            say,
            wait ?? ((delay, token) => Task.Delay(delay, token)),
            clock ?? (() => DateTimeOffset.UtcNow));

        return await dispatcher.ExecuteAsync(cancellationToken);
    }

    private async Task<DispatchReport> ExecuteAsync(CancellationToken cancellationToken)
    {
        var workers = _lanes.Keys
            .Select(printer => WorkAsync(printer, cancellationToken))
            .ToList();

        await Task.WhenAll(workers);

        return BuildReport();
    }

    // ══════════ العامل ══════════

    private async Task WorkAsync(string printer, CancellationToken cancellationToken)
    {
        string? standingComplaint = null;

        try
        {
            await WorkLoopAsync(printer, () => standingComplaint, value => standingComplaint = value, cancellationToken);
        }
        finally
        {
            // المكنة اللي سابت الشغل وهي واقفة لازم تتقال في التقرير حتى
            // لو المكن التانية خلّصت شغلها. من غير كده، أوردر خلص بمكنة
            // واقفة بيبان "نضيف" تمامًا — واللي في المطبعة مايعرفش إن
            // فيه مكنة محتاجة ورق.
            if (standingComplaint is not null)
            {
                Retire(printer, standingComplaint);
            }
        }
    }

    private async Task WorkLoopAsync(
        string printer,
        Func<string?> readComplaint,
        Action<string?> writeComplaint,
        CancellationToken cancellationToken)
    {
        int strikes = 0;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested || StopRequested)
            {
                return;
            }

            if (LanesEmpty())
            {
                // ⚠ مانمشيش لمجرد إن الطوابير فضيت.
                //
                // فيه قطعة ممكن تكون لسه ماشية عند مكنة تانية، ولو فشلت
                // **هترجع الطابور** ومحتاجة حد يشيلها. لو كل العمال مشيوا
                // في اللحظة دي، القطعة دي بتفضل في الطابور للأبد ومحدش
                // بياخدها — وتطلع في التقرير "ماتبعتش" وإحنا كان عندنا
                // مكن فاضية كانت تعملها.
                if (!AnythingInFlight())
                {
                    return;
                }

                if (!await SleepAsync(_options.BusyRecheckDelay, cancellationToken))
                {
                    return;
                }

                continue;
            }

            var health = await AskHealthAsync(printer, cancellationToken);

            if (health is null)
            {
                // اتلغى وإحنا بنسأل
                return;
            }

            // بنسجّلها عشان المكن التانية تعرف تقرر: أسحب من طابورها ولا
            // أسيبها تعمل شغلها بنفسها؟ (شوف WorthTaking)
            Remember(printer, health);

            if (!health.CanTakeWork)
            {
                // ═══ مشغولة: مش عطل ═══
                //
                // دي الحالة الطبيعية لمكنة بتطبع وطابورها فيه شغل. مابنكتبش
                // حاجة في اللوج ومابنبدأش نعد — هي شغالة فعلًا وهتفضى.
                if (!health.IsFault)
                {
                    // شبكة أمان: مكنة بتقول "مشغولة" وطابورها مش بيتحرك
                    // خالص، **ومفيش أي مكنة تانية بتتقدّم كمان**. من غير
                    // ده الحلقة دي ممكن تفضل تلف للأبد على جوب زنق في
                    // السبولر من غير ما ويندوز يبلّغ عن أي عطل.
                    if (IdleTooLong())
                    {
                        Retire(printer, health.Reason ?? "طابورها واقف");
                        Say($"[توقف] '{printer}' طابورها مش بيتحرك ومفيش أي مكنة بتشتغل — سيبناها.");
                        return;
                    }

                    if (!await SleepAsync(_options.BusyRecheckDelay, cancellationToken))
                    {
                        return;
                    }

                    continue;
                }

                string reason = health.Reason ?? "واقفة";

                // بنقول السبب **مرة واحدة** لكل حالة. من غير كده اللوج
                // بيتملى بنفس السطر كل خمس ثواني والمستخدم بيبطّل يقراه.
                if (!string.Equals(reason, readComplaint(), StringComparison.Ordinal))
                {
                    writeComplaint(reason);
                    Say($"[وقفة] '{printer}': {reason} — الشغل الباقي هيمشي على المكن التانية.");
                }

                if (IdleTooLong())
                {
                    Retire(printer, reason);
                    writeComplaint(null);
                    Say($"[توقف] '{printer}' فضلت واقفة ({reason}) ومفيش أي مكنة بتتحرك — سيبناها.");
                    return;
                }

                if (!await SleepAsync(_options.RecheckDelay, cancellationToken))
                {
                    return;
                }

                continue;
            }

            if (readComplaint() is not null)
            {
                Say($"[رجعت] '{printer}' اتصلّحت ورجعت تشتغل.");
                writeComplaint(null);
            }

            if (!TryTake(printer, out var unit))
            {
                // مافيش حاجة نقدر ناخدها **دلوقتي**: يا مكنة تانية سبقتنا
                // على آخر قطعة، يا الشغل الفاضل عند مكنة قادرة تعمله
                // بنفسها. بنستنى بدل ما نمشي — لو وقعت، إحنا هنا.
                if (!await SleepAsync(_options.BusyRecheckDelay, cancellationToken))
                {
                    return;
                }

                continue;
            }

            // ═══ ليه العدّاد بيلفّ على القرار كمان ═══
            //
            // العدّاد ده هو اللي بيمنع باقي العمال إنهم يمشوا وهم فاكرين
            // إن الشغل خلص (شوف LanesEmpty فوق). أول نسخة كانت بتنقّصه
            // أول ما الطباعة ترجع — **قبل** ما نقرر نعمل إيه بالنتيجة —
            // وده ساب شباك صغير:
            //
            //   الطباعة رجعت "مارحتش" → العدّاد بقى صفر → الطوابير لسه
            //   فاضية (القطعة في إيدنا) → باقي العمال شافوا "خلاص خلص"
            //   ومشيوا → وبعدها بجزء من الثانية القطعة رجعت الطابور
            //   ومالقتش حد ياخدها.
            //
            // النتيجة: قطعة بتتقال "ماوصلتش أي مكنة" وإحنا كان عندنا
            // مكنة فاضية قاعدة مستنية.
            //
            // ⚠ صدق مع النفس: التست اللي بيمسك ده عدّى بالصدفة أول مرة.
            // مامسكهوش غير لما التستات اتشغّلت مع بعض والتوقيت اتغيّر.
            // يعني السباق ده كان هيوصل المطبعة.
            EnterFlight();

            PrintOutcome outcome;

            try
            {
                outcome = await CallPrinterAsync(printer, unit, cancellationToken);
            }
            catch (Exception)
            {
                LeaveFlight();
                throw;
            }

            try
            {

            Say(outcome.Message);

            switch (outcome.Kind)
            {
                case PrintResult.Delivered:
                case PrintResult.Skipped:
                    Record(printer, unit);
                    strikes = 0;
                    break;

                case PrintResult.NotSent:
                    // مفيش ورق اتحرك → آمن ترجع الطابور وحد تاني يشيلها
                    GiveBack(printer, unit);
                    strikes++;

                    if (strikes >= _options.StrikesBeforeRetiring)
                    {
                        Retire(printer, $"فشلت {strikes} مرة ورا بعض");
                        Say($"[توقف] '{printer}' فشلت {strikes} مرة ورا بعض — شغلها راح للمكن التانية.");
                        return;
                    }

                    break;

                case PrintResult.Abandoned:
                    // ⚠ أهم سطر في الملف: القطعة دي **اتبعتت** وبعدين وقفت.
                    // ممكن يكون طلع منها ورق وممكن لأ. لو رميناها على مكنة
                    // تانية "عشان نضمن"، المطبعة ممكن تطبع نفس الملزمة
                    // مرتين وتدفع تمنها. بنوقف، وبنقول بالظبط إيه اللي في
                    // الشك، والبني آدم هو اللي يقرر.
                    MarkInDoubt(unit);
                    Retire(printer, "وقفت وهي شغالة");
                    Say($"[شك] '{printer}' وقفت وهي شغالة على {Name(unit)} ({unit.Copies} نسخة). " +
                        "مابنعيدهاش لوحدنا عشان ماتطلعش مرتين — عُد الورق الطالع منها وقرّر.");
                    return;

                case PrintResult.BadJob:
                    // المشكلة في الملف مش في المكنة — نقلها لمكنة تانية
                    // هيفشل بالظبط زي ما فشل هنا. بنسيبها ونكمّل، والمكنة
                    // مالهاش ذنب فمابتاخدش نقطة.
                    MarkRejected(unit);
                    break;

                case PrintResult.Cancelled:
                    GiveBack(printer, unit);
                    StopEverything();
                    return;
            }

            }
            finally
            {
                LeaveFlight();
            }
        }
    }

    /// <summary>بينام المدة دي. بيرجّع false لو الإلغاء حصل وإحنا نايمين.</summary>
    private async Task<bool> SleepAsync(TimeSpan howLong, CancellationToken cancellationToken)
    {
        try
        {
            await _wait(howLong, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// بينده خدمة الطباعة ويحوّل أي انفجار لنتيجة مفهومة.
    ///
    /// **مابيلمسش عدّاد الطيران** عن قصد — العدّاد لازم يفضل شغّال لحد
    /// ما نتيجة القطعة تتسجّل أو ترجع الطابور، وده بيحصل عند اللي
    /// بينده (شوف الشرح فوق).
    /// </summary>
    private async Task<PrintOutcome> CallPrinterAsync(
        string printer,
        WorkUnit unit,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _print(printer, unit, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return PrintOutcome.Cancelled($"[إلغاء] اتلغت الطباعة على '{printer}'.");
        }
        catch (Exception exception)
        {
            // استثناء خرج من خدمة الطباعة نفسها = مفيش أمر اتبعت للطابعة،
            // فآمن ننقل الشغل. لو ده اتغير يومًا، لازم التصنيف يتغيّر معاه.
            return PrintOutcome.NotSent(
                $"[فشل] '{printer}' — {Name(unit)}: {exception.Message}");
        }
    }

    /// <summary>بيسأل عن الحالة. null معناها الإلغاء حصل وإحنا بنسأل.</summary>
    private async Task<PrinterHealth?> AskHealthAsync(string printer, CancellationToken cancellationToken)
    {
        try
        {
            return await _health.CheckAsync(printer, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            // مقدرناش نسأل؟ بنفترض إنها سليمة — نفس سلوك البرنامج قبل
            // الفحص ما يتضاف. منع الطباعة بسبب فشل **فحص** أوحش من
            // العطل اللي الفحص موجود عشانه.
            return PrinterHealth.Fine;
        }
    }

    // ══════════ الطوابير ══════════

    /// <summary>
    /// بياخد القطعة اللي بعدها: من طابوره الأول، وبعدين — **لو الأمر
    /// يستاهل** — من طابور غيره.
    ///
    /// ═══ إمتى يبقى السحب من الغير مسموح ═══
    ///
    /// دي أدق نقطة في الملف كله، واكتشفناها بتست وقع مش بالتفكير:
    ///
    /// أول نسخة كانت بتسمح بالسحب من أي طابور فيه شغل. النتيجة إن أول
    /// مكنة تلحق كانت بتشفط الأوردر كله. السبب إن الإرسال بيخلص في
    /// أجزاء من الثانية، فالمكنة اللي بدأت الأول بتخلص طابورها وتلاقي
    /// الباقيين لسه ماتحركوش، فتشيل شغلهم. الخطة العادلة (١٧/١٧/١٦)
    /// كانت بتطلع ٣٣/١٧/٠ — يعني التوزيع اتلغى من غير ما حد ياخد باله.
    ///
    /// القاعدة الصح: **مانشيلش من مكنة قادرة تعمل شغلها بنفسها.**
    ///
    ///   • سابت الشغل (retired) → اشيل. شغلها مش هيتحرك تاني أبدًا.
    ///   • فيها عطل → اشيل. مش عارفين هتقعد واقفة قد إيه.
    ///   • مشغولة (طابورها مكوّم) → اشيل. دي اللي بتتأخر فعلًا ومحتاجة
    ///     مساعدة، وده اللي بيخلي المكنة السريعة تسند البطيئة.
    ///   • جاهزة → **سيبها**. هي هتاخد قطعتها اللي جاية في ثواني، وأنا
    ///     لو شيلتها منها بكون كسرت الخطة على الفاضي.
    ///
    /// وترتيب الضحايا: اللي سابت الشغل الأول (شغلها ميت)، وبعدين أتقل
    /// طابور. عند التساوي بنرتّب بالاسم فالنتيجة ثابتة وينفع تتختبر.
    /// </summary>
    private bool TryTake(string printer, out WorkUnit unit)
    {
        lock (_gate)
        {
            if (_lanes.TryGetValue(printer, out var own) && own.First is { } mine)
            {
                own.RemoveFirst();
                unit = mine.Value;
                return true;
            }

            var victim = _lanes
                .Where(lane => lane.Value.Count > 0 && WorthTaking(lane.Key))
                .OrderByDescending(lane => _retired.Contains(lane.Key))
                .ThenByDescending(lane => lane.Value.Sum(u => u.Weight))
                .ThenBy(lane => lane.Key, StringComparer.Ordinal)
                .Select(lane => lane.Value)
                .FirstOrDefault();

            if (victim?.First is { } stolen)
            {
                victim.RemoveFirst();
                unit = stolen.Value;
                return true;
            }
        }

        unit = null!;
        return false;
    }

    /// <summary>لازم تتنده جوه القفل.</summary>
    private bool WorthTaking(string victim)
    {
        if (_retired.Contains(victim))
        {
            return true;
        }

        // مالناش خبر عنها لسه؟ نفترض إنها بخير ونسيبها. العامل بتاعها
        // بيسأل عن حالتها في أول لفة، فالفراغ ده عمره ثواني.
        return _lastKnown.TryGetValue(victim, out var health) && !health.CanTakeWork;
    }

    private void Remember(string printer, PrinterHealth health)
    {
        lock (_gate)
        {
            _lastKnown[printer] = health;
        }
    }

    /// <summary>
    /// بترجّع القطعة **لأول** الطابور مش لآخره.
    ///
    /// عشان القطعة الفاشلة تتشال فورًا من أي مكنة سليمة بدل ما تستنى ورا
    /// الشغل كله وتطلع آخر حاجة — والوقت ده هو اللي المكن التانية بتخلص
    /// فيه، فكانت هتفضل واقفة لوحدها في الآخر.
    /// </summary>
    private void GiveBack(string printer, WorkUnit unit)
    {
        lock (_gate)
        {
            if (!_lanes.TryGetValue(printer, out var lane))
            {
                lane = new LinkedList<WorkUnit>();
                _lanes[printer] = lane;
            }

            lane.AddFirst(unit);
        }
    }

    private bool LanesEmpty()
    {
        lock (_gate)
        {
            return _lanes.Values.All(lane => lane.Count == 0);
        }
    }

    /// <summary>فيه قطعة ماشية عند أي مكنة دلوقتي؟</summary>
    private bool AnythingInFlight()
    {
        lock (_gate)
        {
            return _inFlight > 0;
        }
    }

    // ══════════ العدّ والتقارير ══════════

    private void Record(string printer, WorkUnit unit)
    {
        lock (_gate)
        {
            if (!_counters.TryGetValue(printer, out var counter))
            {
                counter = new Counter();
                _counters[printer] = counter;
            }

            counter.Units++;
            counter.Copies += unit.Copies;
            counter.Pages += unit.Weight;

            _lastProgress = _clock();
        }
    }

    private void Retire(string printer, string because)
    {
        lock (_gate)
        {
            _retired.Add(printer);

            if (_counters.TryGetValue(printer, out var counter))
            {
                counter.RetiredBecause ??= because;
            }
        }
    }

    private void MarkInDoubt(WorkUnit unit)
    {
        lock (_gate)
        {
            _inDoubt.Add(unit);
        }
    }

    private void MarkRejected(WorkUnit unit)
    {
        lock (_gate)
        {
            _rejected.Add(unit);
        }
    }

    private void EnterFlight()
    {
        lock (_gate)
        {
            _inFlight++;
        }
    }

    private void LeaveFlight()
    {
        lock (_gate)
        {
            _inFlight--;
        }
    }

    private void StopEverything()
    {
        lock (_gate)
        {
            _stopEverything = true;
        }
    }

    private bool StopRequested
    {
        get
        {
            lock (_gate)
            {
                return _stopEverything;
            }
        }
    }

    /// <summary>
    /// مفيش أي حاجة اتحركت بقى لها فترة طويلة؟
    ///
    /// **بنحسب المكنة اللي بتطبع دلوقتي على إنها تقدّم**، حتى لو لسه
    /// ماخلصتش. من غير كده، ملزمة كبيرة بتاخد ربع ساعة على مكنة كانت
    /// هتخلّي باقي المكن الواقفة تستسلم وهي مش لازم.
    /// </summary>
    private bool IdleTooLong()
    {
        lock (_gate)
        {
            return _inFlight == 0 && _clock() - _lastProgress > _options.GiveUpAfter;
        }
    }

    private void Say(string line)
    {
        lock (_gate)
        {
            _lines.Add(line);
        }

        // النداء بره القفل عن قصد: الواجهة بتحوّل السطر لثريد تاني،
        // وده ينفع يعمل قفلة متبادلة لو حصل والقفل لسه ماتسابش.
        _say?.Invoke(line);
    }

    private static string Name(WorkUnit unit) => Path.GetFileName(unit.Path);

    private DispatchReport BuildReport()
    {
        lock (_gate)
        {
            var printers = _counters
                .Select(pair => new PrinterTally(
                    pair.Key,
                    pair.Value.Units,
                    pair.Value.Copies,
                    pair.Value.Pages,
                    pair.Value.RetiredBecause))
                .OrderBy(p => p.PrinterName, StringComparer.Ordinal)
                .ToList();

            var leftover = _lanes.Values.SelectMany(lane => lane).ToList();

            return new DispatchReport(
                [.. _lines],
                printers,
                leftover,
                [.. _inDoubt],
                [.. _rejected]);
        }
    }
}
