using PrintFlow.Domain;
using PrintFlow.Presentation;

namespace PrintFlow.Tests;

/// <summary>
/// بيحمي صف التقدم — السطر اللي اللي واقف على المكنة بيقرا منه.
///
/// ═══ ليه ده يستاهل تستات ═══
///
/// الصف ده وقع فيه **تلات باجات** في التجربة الحقيقية، وكلهم من نوع
/// واحد: **الرقم بيكدب والمستخدم بيصدّقه**. مفيش رسالة خطأ ولا تحذير —
/// بار أخضر ١٠٠٪ على مكنة ميتة، وأوردر خلص بيقفل على ٣٠٪.
///
/// كل تست تحت مربوط بواحد منهم.
/// </summary>
public class PrinterProgressTests
{
    private static WorkUnit Piece(int copies, int pages) => new("doc.pdf", pages, copies);

    private static PrintOutcome Ok => PrintOutcome.Delivered("تمام");

    // ══════════ فاضل كام نسخة ══════════

    [Fact]
    public void It_Says_How_Many_Copies_Are_Left()
    {
        var row = new PrinterProgress("HP", pagesPlanned: 200, copiesPlanned: 10);

        row.Record(Piece(copies: 3, pages: 20), Ok);

        Assert.Equal(7, row.CopiesLeft);
        Assert.Contains("فاضل 7 من 10 نسخة", row.Caption);
    }

    /// <summary>
    /// المكنة اللي سرقت شغل بتطلّع أكتر من نصيبها. «فاضل -٤» كلام
    /// مالوش معنى لواحد واقف قدام مكنة.
    /// </summary>
    [Fact]
    public void Copies_Left_Never_Goes_Negative()
    {
        var row = new PrinterProgress("HP", pagesPlanned: 200, copiesPlanned: 10);

        row.Record(Piece(copies: 14, pages: 20), Ok);

        Assert.Equal(0, row.CopiesLeft);
    }

    /// <summary>
    /// مانعرفش النصيب بالنسخ؟ نرجع للصيغة القديمة بدل ما نقول
    /// «فاضل ٠ من ٠» وهي شغّالة.
    /// </summary>
    [Fact]
    public void With_No_Planned_Copies_It_Falls_Back_To_The_Old_Wording()
    {
        var row = new PrinterProgress("HP", pagesPlanned: 200);

        row.Record(Piece(copies: 3, pages: 20), Ok);

        // بيقول اللي عملته، ومايقولش «فاضل ٠ من ٠» وهي شغّالة
        Assert.Contains("3 نسخة", row.Caption);
        Assert.Contains("60/200 صفحة", row.Caption);
        Assert.Equal(0, row.CopiesLeft);
    }

    // ══════════ باج ب: أوردر خلص وبارات على ٣٠٪ ══════════

    /// <summary>
    /// ⚠ حصل فعلًا: أوردر خلّص ٥٧٠ من ٥٧٠ صفحة وقفل على بارات بتقول
    /// ٣٠٪ و٨٠٪ و١٠٠٪ — لأن المقام كان الخطة اللي بطلت صح بعد السرقة.
    /// اللي في المطبعة قراها «مكنتين ماخلصوش» وراح يدوّر على ورق مش ناقص.
    ///
    /// الخطة **توقُّع**. لما الأوردر يخلص، نصيبها الحقيقي هو اللي عملته.
    /// </summary>
    [Fact]
    public void A_Finished_Order_Closes_At_A_Hundred_Percent()
    {
        var row = new PrinterProgress("HP", pagesPlanned: 190, copiesPlanned: 10);

        // سرقت شغل: طلّعت أقل من نصيبها المخطط لأن غيرها شال منها
        row.Record(Piece(copies: 3, pages: 19), Ok);

        row.Finish(orderCompleted: true);

        Assert.Equal("خلصت", row.State);
        Assert.Equal(100, row.Percent);
        Assert.Equal(0, row.PagesLeft);
        Assert.Equal(0, row.CopiesLeft);
    }

    /// <summary>الأوردر اتوقف بالإيد = محدش بيتقال عنه «خلص».</summary>
    [Fact]
    public void A_Cancelled_Order_Is_Not_Called_Finished()
    {
        var row = new PrinterProgress("HP", pagesPlanned: 190, copiesPlanned: 10);
        row.Record(Piece(copies: 3, pages: 19), Ok);

        row.Finish(orderCompleted: false);

        Assert.Equal("اتوقفت", row.State);
    }

    [Fact]
    public void A_Machine_That_Never_Worked_Says_So()
    {
        var row = new PrinterProgress("HP", pagesPlanned: 190, copiesPlanned: 10);

        row.Finish(orderCompleted: true);

        Assert.Equal("ماشتغلتش", row.State);
    }

    // ══════════ باج ج: مكنة ميتة بتقفل خضرا ١٠٠٪ ══════════

    /// <summary>
    /// ⚠ أخطر واحد فيهم. المكنة الموقوفة أو اللي الورق خلص منها
    /// **مابتوصلهاش قطعة أصلًا** — الموزّع بيسأل عن حالتها ويبطّل يبعتلها.
    /// يعني مفيش نتيجة فشل توصل الصف ده أبدًا، وFinish كان بيلاقيه نضيف
    /// فيقفله على **أخضر «خلصت»** وهو ماطلّعش ولا ورقة.
    /// </summary>
    [Fact]
    public void A_Machine_That_Died_Stays_Red_After_The_Order_Ends()
    {
        var row = new PrinterProgress("HP", pagesPlanned: 190, copiesPlanned: 10);

        row.Record(Piece(copies: 2, pages: 19), Ok);
        row.Stopped("الطابعة موقوفة");

        row.Finish(orderCompleted: true);

        Assert.True(row.IsFaulted);
        Assert.Equal("الطابعة موقوفة", row.State);
        Assert.Contains("الطابعة موقوفة", row.Caption);
        Assert.NotEqual(100, row.Percent);
    }

    [Fact]
    public void Stopped_Without_A_Reason_Still_Says_Something()
    {
        var row = new PrinterProgress("HP", 100, 5);

        row.Stopped("   ");

        Assert.True(row.IsFaulted);
        Assert.Equal("وقفت", row.State);
    }

    // ══════════ اللي مايتحسبش ورق ══════════

    /// <summary>
    /// النتيجة المشكوك فيها مابتتحسبش ورق. البار اللي بيعدّ شغل مش
    /// متأكد منه بيكدب على اللي واقف قدام المكنة.
    /// </summary>
    [Theory]
    [InlineData(PrintResult.NotSent)]
    [InlineData(PrintResult.Abandoned)]
    public void A_Doubtful_Result_Adds_No_Paper_And_Marks_The_Fault(PrintResult kind)
    {
        var row = new PrinterProgress("HP", 100, 5);

        row.Record(Piece(2, 20), new PrintOutcome(kind, "مشكلة"));

        Assert.Equal(0, row.PagesDone);
        Assert.Equal(0, row.CopiesDone);
        Assert.True(row.IsFaulted);
    }

    [Fact]
    public void A_Skipped_Piece_Changes_Nothing()
    {
        var row = new PrinterProgress("HP", 100, 5);

        row.Record(Piece(2, 20), PrintOutcome.Skipped("نصيبها صفر"));

        Assert.Equal(0, row.PagesDone);
        Assert.False(row.IsFaulted);
    }

    // ══════════ سطر الطابعة ══════════

    /// <summary>
    /// السطر التاني بييجي من ويندوز. لما الطابور يفضى مايفضلش مكتوب
    /// كلام قديم عن شغل خلص من ساعة.
    /// </summary>
    [Fact]
    public void The_Queue_Line_Shows_Only_While_There_Is_Work()
    {
        var row = new PrinterProgress("HP", 100, 5);

        Assert.False(row.HasQueueNews);

        row.Queue = new PrinterQueueState(JobsWaiting: 1, PagesPrinted: 47, PagesTotal: 180);

        Assert.True(row.HasQueueNews);
        Assert.Contains("47", row.QueueCaption);
        Assert.Contains("180", row.QueueCaption);

        row.Queue = PrinterQueueState.Idle;

        Assert.False(row.HasQueueNews);
    }

    /// <summary>المكنة الواقعة سطرها الأحمر بيكفي — مش وقت أرقام طابور.</summary>
    [Fact]
    public void A_Faulted_Row_Hides_The_Queue_Line()
    {
        var row = new PrinterProgress("HP", 100, 5);
        row.Queue = new PrinterQueueState(1, 47, 180);

        row.Stopped("وقفت");

        Assert.False(row.HasQueueNews);
    }
}
