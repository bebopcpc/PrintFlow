using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// بيحمي قياس سرعة المكن — الميزة اللي التوزيع كله بيتبني عليها.
///
/// ═══ ليه دي أخطر من غيرها ═══
///
/// القياس الغلط مابيبانش. مافيش رسالة خطأ ولا ورق ناقص — بس المكنة
/// السريعة بتاخد نصيب أصغر من حقها في **كل أوردر جاي**، والمطبعة بتشتغل
/// أبطأ من قدرتها وهي فاكرة إن ده أقصى اللي عندها.
///
/// التستات دي بتثبّت القرارات اللي اتاخدت بعد ما وقعنا فيها في التجربة
/// الحقيقية — وكل واحد فيهم مكتوب جنبه السبب.
///
/// الساعة بتتحقن، فالتستات دي **مالهاش دعوة بالوقت الحقيقي** وبتخلص في
/// جزء من الثانية.
/// </summary>
public class PrinterSpeedBookTests
{
    /// <summary>ساعة بالإيد — بنحرّكها إحنا بدل ما نستنى.</summary>
    private sealed class FakeClock
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Now() => _now;

        public void Advance(double seconds) => _now = _now.AddSeconds(seconds);
    }

    /// <summary>كتاب بمجلد مؤقت لوحده — التستات مالهاش دعوة بـ %AppData%.</summary>
    private static (PrinterSpeedBook Book, FakeClock Clock, string Folder) NewBook()
    {
        var clock = new FakeClock();
        string folder = Path.Combine(Path.GetTempPath(), "printflow-speed-" + Guid.NewGuid().ToString("N"));

        return (new PrinterSpeedBook(folder, clock.Now), clock, folder);
    }

    // ══════════ الحالة الأساسية ══════════

    [Fact]
    public void A_Finished_Order_Teaches_The_Book()
    {
        var (book, clock, _) = NewBook();

        book.OrderStarted();
        book.NoteDelivered("HP", 100);
        clock.Advance(50);

        string learned = book.OrderFinished();

        Assert.Contains("HP", learned);
        Assert.Equal(2d, book.Snapshot().For("HP"), 3);   // ١٠٠ صفحة ÷ ٥٠ ثانية
    }

    /// <summary>
    /// ⚠ ده **الباج الحقيقي** اللي وقع في المطبعة، والتست ده موجود عشان
    /// ماينفعش يرجع تاني.
    ///
    /// القياس القديم كان بيقسّم على «وقت المكنة لحد آخر قطعة سلّمتها».
    /// النتيجة كانت مقلوبة تمامًا:
    ///
    ///   • مكنة سلّمت ٥٧ صفحة في أول ١١ ثانية وبعدين قعدت تتفرّج ٤ دقايق
    ///     → اتسجّلت ٥ ص/ث (الأسرع!)
    ///   • مكنة شالت ٣٩٩ صفحة على ٢٥١ ثانية (٧٠٪ من الأوردر)
    ///     → اتسجّلت ١.٦ ص/ث (الأبطأ!)
    ///
    /// دلوقتي المقام واحد لكل المكن: **زمن الأوردر كله**. فاللي وقف
    /// يتفرّج بيبان إنه وقف.
    /// </summary>
    [Fact]
    public void The_Machine_That_Did_Most_Of_The_Work_Measures_Fastest()
    {
        var (book, clock, _) = NewBook();

        book.OrderStarted();

        // البرّاقة: شغل كتير بدري وبعدين وقفت
        book.NoteDelivered("Burst", 57);
        clock.Advance(11);

        // الشغّالة: فضلت شغالة لآخر الأوردر
        book.NoteDelivered("Steady", 399);
        clock.Advance(240);

        book.OrderFinished();

        var speeds = book.Snapshot();

        Assert.True(speeds.For("Steady") > speeds.For("Burst"),
            $"الشغّالة {speeds.For("Steady"):0.00} لازم تبقى أسرع من البرّاقة {speeds.For("Burst"):0.00}");
    }

    // ══════════ اللي مايتصدّقش ══════════

    /// <summary>
    /// أوردر ٣ صفحات مابيقولش حاجة عن سرعة مكنة. لو حسبناه، أول أوردر
    /// صغير هيلوّث الكتاب لشهر.
    /// </summary>
    [Fact]
    public void A_Tiny_Sample_Is_Ignored()
    {
        var (book, clock, _) = NewBook();

        book.OrderStarted();
        book.NoteDelivered("HP", PrinterSpeedBook.MinimumPages - 1);
        clock.Advance(30);

        Assert.Empty(book.OrderFinished());
        Assert.True(book.Snapshot().IsEmpty);
    }

    /// <summary>
    /// المكنة اللي الورق خلص منها خمس دقايق مش «بطيئة» — دي كانت واقفة.
    /// لو حسبناها هتفضل واخدة نصيب أقل في كل أوردر جاي، عقوبة على عطل خلص.
    /// </summary>
    [Fact]
    public void A_Machine_That_Stalled_Is_Not_Measured()
    {
        var (book, clock, _) = NewBook();

        book.OrderStarted();
        book.NoteDelivered("HP", 500);
        book.Distrust("HP");
        clock.Advance(60);

        book.OrderFinished();

        Assert.True(book.Snapshot().IsEmpty);
    }

    /// <summary>أوردر خلص في لحظة = القسمة مالهاش معنى.</summary>
    [Fact]
    public void An_Instant_Order_Is_Ignored()
    {
        var (book, clock, _) = NewBook();

        book.OrderStarted();
        book.NoteDelivered("HP", 1000);
        clock.Advance(1);

        Assert.Empty(book.OrderFinished());
        Assert.True(book.Snapshot().IsEmpty);
    }

    // ══════════ الخلط: أوردر شاذ مايقلبش التوزيع ══════════

    /// <summary>
    /// القياس الجديد بياخد ٣٠٪ بس والباقي للمحفوظ. يعني أوردر واحد غريب
    /// بيحرّك الرقم شوية مش بيقلبه.
    /// </summary>
    [Fact]
    public void A_New_Reading_Is_Blended_With_The_Old_One()
    {
        var (book, clock, _) = NewBook();

        // أوردر أول: ١٠٠ صفحة ÷ ٥٠ ثانية = ٢ ص/ث
        book.OrderStarted();
        book.NoteDelivered("HP", 100);
        clock.Advance(50);
        book.OrderFinished();

        // أوردر تاني شاذ: ١٠٠ صفحة ÷ ١٠ ثواني = ١٠ ص/ث
        book.OrderStarted();
        book.NoteDelivered("HP", 100);
        clock.Advance(10);
        book.OrderFinished();

        double blended = book.Snapshot().For("HP");
        double expected = (2d * (1 - PrinterSpeedBook.NewSampleWeight))
                          + (10d * PrinterSpeedBook.NewSampleWeight);

        Assert.Equal(expected, blended, 3);

        // والأهم: مانطّش على الرقم الجديد
        Assert.True(blended < 10d);
        Assert.True(blended > 2d);
    }

    // ══════════ الحفظ بين التشغيلات ══════════

    [Fact]
    public void What_The_Book_Learned_Survives_A_Restart()
    {
        var (book, clock, folder) = NewBook();

        book.OrderStarted();
        book.NoteDelivered("HP", 100);
        clock.Advance(50);
        book.OrderFinished();

        // كتاب جديد على نفس المجلد = زي ما البرنامج اتقفل واتفتح
        var reopened = new PrinterSpeedBook(folder);

        Assert.Equal(2d, reopened.Snapshot().For("HP"), 3);

        try { Directory.Delete(folder, recursive: true); } catch { /* تنضيف */ }
    }

    [Fact]
    public void Forget_Puts_Everything_Back_To_Equal()
    {
        var (book, clock, _) = NewBook();

        book.OrderStarted();
        book.NoteDelivered("HP", 100);
        clock.Advance(50);
        book.OrderFinished();

        book.Forget();

        Assert.True(book.Snapshot().IsEmpty);
    }

    /// <summary>
    /// اللقطة **نسخة**. لو رجّعت المرجع الأصلي، الموزّع كان ممكن يلاقي
    /// السرعات بتتغيّر تحت رجليه في نص التوزيع.
    /// </summary>
    [Fact]
    public void A_Snapshot_Does_Not_Change_Under_The_Balancer()
    {
        var (book, clock, _) = NewBook();

        book.OrderStarted();
        book.NoteDelivered("HP", 100);
        clock.Advance(50);
        book.OrderFinished();

        var snapshot = book.Snapshot();

        book.OrderStarted();
        book.NoteDelivered("HP", 1000);
        clock.Advance(10);
        book.OrderFinished();

        Assert.Equal(2d, snapshot.For("HP"), 3);
    }

    /// <summary>
    /// كل أوردر بيبدأ من نضيف. من غير ده، صفحات الأوردر اللي فات كانت
    /// هتتحسب على زمن الأوردر الجديد.
    /// </summary>
    [Fact]
    public void A_New_Order_Does_Not_Carry_The_Old_Ones_Pages()
    {
        var (book, clock, _) = NewBook();

        book.OrderStarted();
        book.NoteDelivered("HP", 100);
        clock.Advance(50);
        book.OrderFinished();

        // أوردر تاني، المكنة دي ماشتغلتش فيه خالص
        book.OrderStarted();
        book.NoteDelivered("Other", 100);
        clock.Advance(50);
        book.OrderFinished();

        // رقمها لازم يفضل زي ما هو، ماتأثرش بأوردر ماشتغلتش فيه
        Assert.Equal(2d, book.Snapshot().For("HP"), 3);
    }
}
