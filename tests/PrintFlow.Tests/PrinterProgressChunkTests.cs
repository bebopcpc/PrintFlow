using PrintFlow.Domain;
using PrintFlow.Presentation;

namespace PrintFlow.Tests;

/// <summary>
/// بيحرس حركة البار في الجوب اللي بيتبعت **على دفعات**.
///
/// ═══ المشكلة اللي التستات دي اتعملت عشانها ═══
///
/// في اختبار حقيقي على ١٠ نسخ × ١٨٠ صفحة، البار فضل يقول
/// «0/1800 (0٪) — فاضل 10 من 10» لمدة **عشر دقايق**، وبعدين قفز ١٠٠٪
/// مرة واحدة. وفي نفس اللحظات دي، مراقبة طابور ويندوز كانت بتقول:
///
///     00:13:37   total=70     ← دفعة بتتزوّد
///     00:13:58   total=145    ← نفس الدفعة
///     00:14:18   total=25     ← دفعة **جديدة**، اللي قبلها خرجت
///
/// يعني خمس دفعات خلصت والبرنامج ساكت.
///
/// ودي مش مشكلة تجميلية: اللي واقف على المكنة بيفتكر البرنامج واقف،
/// فيدوس «إيقاف فوري». وقتها الطابعات بتفضل تطلّع اللي في ذاكرتها وتقف
/// في أوقات مختلفة — وهي بالحرف شكوى «وقفت عند الورقة ٤٠».
///
/// ═══ الفخ اللي التستات دي بتحرسه ═══
///
/// لما بقينا نحسب الدفعات وهي ماشية، بقى فيه مصدرين للعد: النداءات
/// أثناء الشغل، والنتيجة النهائية في الآخر. لو الاتنين حسبوا، الرقم
/// بيتضاعف — ١٠ نسخة تبقى ٢٠ والبار يقول ٢٠٠٪.
///
/// الحل إن اللي بينده يخصم اللي اتحسب. والتست رقم ٣ تحت هو اللي بيمسك
/// الغلطة دي لو حد رجّعها.
/// </summary>
public class PrinterProgressChunkTests
{
    // ═══════════ الأساسيات ═══════════

    [Fact]
    public void A_Chunk_Moves_The_Bar_While_The_Job_Is_Still_Running()
    {
        var row = new PrinterProgress("HP-1", pagesPlanned: 1800, copiesPlanned: 10);

        row.NoteChunk(copies: 1, pages: 180);

        Assert.Equal(1, row.CopiesDone);
        Assert.Equal(180, row.PagesDone);
        Assert.Equal(10, row.Percent);
    }

    /// <summary>
    /// الحالة اللي كانت بتحصل: البار ساكت لحد الآخر. لازم يبقى
    /// **مستحيل** بعد التعديل.
    /// </summary>
    [Fact]
    public void Five_Chunks_Show_Five_Chunks_Not_Zero()
    {
        var row = new PrinterProgress("HP-1", pagesPlanned: 1800, copiesPlanned: 10);

        for (int i = 0; i < 5; i++)
        {
            row.NoteChunk(1, 180);
        }

        Assert.Equal(5, row.CopiesDone);
        Assert.Equal(900, row.PagesDone);
        Assert.Equal(50, row.Percent);
        Assert.Equal(5, row.CopiesLeft);
    }

    [Fact]
    public void A_Chunk_Says_The_Machine_Is_Printing()
    {
        var row = new PrinterProgress("HP-1", 1800, 10);

        row.NoteChunk(1, 180);

        Assert.Equal("بتطبع", row.State);
        Assert.False(row.IsFaulted);
    }

    [Fact]
    public void An_Empty_Chunk_Changes_Nothing()
    {
        var row = new PrinterProgress("HP-1", 1800, 10);

        row.NoteChunk(0, 0);

        Assert.Equal(0, row.CopiesDone);
        Assert.Equal(0, row.PagesDone);
        Assert.Equal("مستنية", row.State);
    }

    // ═══════════ الفخ: العد المزدوج ═══════════

    /// <summary>
    /// ⚠ **أهم تست في الملف.**
    ///
    /// ده بيحاكي الرحلة كاملة: عشر دفعات بتتحسب وهي ماشية، وبعدين
    /// النتيجة النهائية بتوصل. اللي بينده لازم يخصم اللي اتحسب، وإلا
    /// الرقم بيتضاعف.
    /// </summary>
    [Fact]
    public void Ten_Chunks_Then_The_Final_Result_Still_Counts_Ten()
    {
        var row = new PrinterProgress("HP-1", pagesPlanned: 1800, copiesPlanned: 10);
        var unit = new WorkUnit("order.pdf", Pages: 180, Copies: 10);

        int credited = 0;

        // الدفعات وهي ماشية
        for (int i = 0; i < 10; i++)
        {
            credited += 1;
            row.NoteChunk(1, 180);
        }

        // النتيجة النهائية — الباقي بس
        var rest = unit with { Copies = Math.Max(0, unit.Copies - credited) };
        row.Record(rest, PrintOutcome.Delivered("[نجاح] اتسلّمت 10 نسخة على 10 دفعة."));

        Assert.Equal(10, row.CopiesDone);      // مش ٢٠
        Assert.Equal(1800, row.PagesDone);     // مش ٣٦٠٠
        Assert.Equal(100, row.Percent);        // مش ٢٠٠
        Assert.Equal(0, row.CopiesLeft);
    }

    /// <summary>
    /// الجوب اللي مااتقسمش (دفعة واحدة) مابينديش النداء خالص، فالنتيجة
    /// النهائية هي اللي بتحسب. لازم يفضل شغّال بالظبط زي الأول.
    /// </summary>
    [Fact]
    public void A_Single_Batch_Job_Still_Counts_Through_The_Result_Only()
    {
        var row = new PrinterProgress("HP-1", pagesPlanned: 180, copiesPlanned: 1);
        var unit = new WorkUnit("order.pdf", Pages: 180, Copies: 1);

        // مفيش NoteChunk — credited = 0
        row.Record(unit, PrintOutcome.Delivered("[نجاح] اتسلّمت 1 نسخة."));

        Assert.Equal(1, row.CopiesDone);
        Assert.Equal(180, row.PagesDone);
        Assert.Equal(100, row.Percent);
    }

    // ═══════════ اللي حصل فعلًا بيفضل محسوب ═══════════

    /// <summary>
    /// وقعت بعد ٣ دفعات: التلاتة دول **ورق طلع فعلًا** ولازم يفضلوا
    /// في العداد. الصف بيتقفل أحمر، بس الرقم بيقول الحقيقة عشان اللي
    /// في المطبعة يعرف يعد الباقي.
    /// </summary>
    [Fact]
    public void Chunks_Delivered_Before_A_Failure_Stay_Counted()
    {
        var row = new PrinterProgress("HP-1", pagesPlanned: 1800, copiesPlanned: 10);
        var unit = new WorkUnit("order.pdf", Pages: 180, Copies: 10);

        int credited = 0;

        for (int i = 0; i < 3; i++)
        {
            credited += 1;
            row.NoteChunk(1, 180);
        }

        var rest = unit with { Copies = Math.Max(0, unit.Copies - credited) };
        row.Record(rest, PrintOutcome.Abandoned("[فشل] اتسلّم 3 من 10 وبعدين وقف."));

        Assert.Equal(3, row.CopiesDone);
        Assert.Equal(540, row.PagesDone);
        Assert.True(row.IsFaulted);
        Assert.Equal("في الشك — راجعها", row.State);
    }

    /// <summary>
    /// الإيقاف الفوري وسط الدفعات: اللي اتطبع بيفضل محسوب، والحالة
    /// «اتوقفت» مش «وقعت» — القرار كان بني آدم مش عطل.
    /// </summary>
    [Fact]
    public void Chunks_Delivered_Before_A_Stop_Stay_Counted()
    {
        var row = new PrinterProgress("HP-1", pagesPlanned: 1800, copiesPlanned: 10);
        var unit = new WorkUnit("order.pdf", Pages: 180, Copies: 10);

        int credited = 0;

        for (int i = 0; i < 4; i++)
        {
            credited += 1;
            row.NoteChunk(1, 180);
        }

        var rest = unit with { Copies = Math.Max(0, unit.Copies - credited) };
        row.Record(rest, PrintOutcome.Cancelled("[إلغاء] اتوقفت بعد 4 من 10 نسخة."));

        Assert.Equal(4, row.CopiesDone);
        Assert.Equal(720, row.PagesDone);
        Assert.Equal("اتوقفت", row.State);
        Assert.False(row.IsFaulted);
    }

    // ═══════════ التعايش مع سرقة الشغل ═══════════

    /// <summary>
    /// مكنة سرقت شغل: طلّعت أكتر من نصيبها. البار مايعديش ١٠٠٪، والرقم
    /// اللي جنبه هو اللي بيقول الحقيقة — نفس قاعدة <c>Percent</c>.
    /// </summary>
    [Fact]
    public void Stolen_Work_Does_Not_Push_The_Bar_Past_A_Hundred()
    {
        var row = new PrinterProgress("HP-1", pagesPlanned: 360, copiesPlanned: 2);

        for (int i = 0; i < 5; i++)
        {
            row.NoteChunk(1, 180);
        }

        Assert.Equal(5, row.CopiesDone);
        Assert.Equal(900, row.PagesDone);
        Assert.Equal(100, row.Percent);
        Assert.Equal(0, row.CopiesLeft);
    }

    /// <summary>
    /// وبعد ما الأوردر يخلص، الخطة بتترجع للحقيقة — فالسطر مايقولش
    /// «فاضل ٠ من ٢» وهو عمل ٥.
    /// </summary>
    [Fact]
    public void Finishing_Rebases_The_Plan_Onto_What_Actually_Happened()
    {
        var row = new PrinterProgress("HP-1", pagesPlanned: 360, copiesPlanned: 2);

        for (int i = 0; i < 5; i++)
        {
            row.NoteChunk(1, 180);
        }

        row.Finish(orderCompleted: true);

        Assert.Equal(5, row.CopiesPlanned);
        Assert.Equal(900, row.PagesPlanned);
        Assert.Equal("خلصت", row.State);
        Assert.Contains("5", row.Caption);
    }
}
