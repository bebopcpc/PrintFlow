using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// بيحمي تحذير «طباعة مباشرة».
///
/// التحذير القديم كان جملة ثابتة بتقول ٤ حاجات والحقيقة ٨. والنتيجة
/// كانت أسوأ من مفيش تحذير خالص: مستخدم ظبط مقياس الصفحة ٩٢٪، قرا
/// تحذير المقياس **مش مذكور فيه**، فاستنتج إنه اتطبّق — وطبع أوردر
/// من غير هامش.
///
/// التستات دي بتضمن إن **كل** ميزة معالجة ليها تست بيقول اسمها لازم
/// يظهر. أي ميزة جديدة تتضاف من غير ما تتحط هنا، هتفضل مخفية — فالتست
/// اللي بيعدّ الحاجات في الآخر هو اللي هيصرخ.
/// </summary>
public class SkippedProcessingTests
{
    private static (PrintSettings Print, AppSettings App) Clean()
        => (new PrintSettings { MergeFiles = false }, new AppSettings());

    // ══════════ الحاجة اللي ضيّعت ورق حقيقي ══════════

    /// <summary>
    /// ⚠ ده بالظبط اللي حصل في المطبعة: مقياس ٩٢٪ متظبّط، والتحذير
    /// مكانش بيقول عنه حاجة.
    /// </summary>
    [Fact]
    public void Page_Scale_Is_Named_In_The_Warning()
    {
        var (print, app) = Clean();
        print.ScalePercent = 92;

        string line = SkippedProcessing.Describe(print, app, fileCount: 1);

        Assert.Contains("مقياس الصفحة", line);
        Assert.Contains("92", line);
    }

    // ══════════ التمانية كلهم ══════════

    [Fact]
    public void Merging_Is_Named_When_There_Is_More_Than_One_File()
    {
        var (print, app) = Clean();
        print.MergeFiles = true;

        Assert.Contains("دمج", SkippedProcessing.Describe(print, app, fileCount: 5));
    }

    /// <summary>
    /// دمج ملف واحد مالوش معنى. التحذير اللي فيه حشو بيتقرا أقل، واللي
    /// مابيتقراش مابيحميش حد.
    /// </summary>
    [Fact]
    public void Merging_Is_Not_Named_For_A_Single_File()
    {
        var (print, app) = Clean();
        print.MergeFiles = true;

        Assert.Empty(SkippedProcessing.ListFor(print, app, fileCount: 1));
    }

    [Fact]
    public void Booklet_Is_Named()
    {
        var (print, app) = Clean();
        print.BookletMode = true;

        Assert.Contains("بوكليت", SkippedProcessing.Describe(print, app, 1));
    }

    [Fact]
    public void Slides_Are_Named()
    {
        var (print, app) = Clean();
        print.SlidesPerSheet = 4;

        string line = SkippedProcessing.Describe(print, app, 1);

        Assert.Contains("شرائح", line);
        Assert.Contains("4", line);
    }

    [Fact]
    public void Deleted_Pages_Are_Named_With_The_Actual_Numbers()
    {
        var (print, app) = Clean();
        print.DeletePages = true;
        print.PagesToDelete = "1-4";

        string line = SkippedProcessing.Describe(print, app, 1);

        Assert.Contains("حذف الصفحات", line);
        Assert.Contains("1-4", line);
    }

    /// <summary>العلامة مفتوحة بس الخانة فاضية = مفيش حاجة تضيع أصلًا.</summary>
    [Fact]
    public void Delete_Pages_With_An_Empty_Box_Is_Not_Named()
    {
        var (print, app) = Clean();
        print.DeletePages = true;
        print.PagesToDelete = "   ";

        Assert.Empty(SkippedProcessing.ListFor(print, app, 1));
    }

    [Fact]
    public void Page_Numbering_Is_Named()
    {
        var (print, app) = Clean();
        print.NumberPagesPerFile = true;

        Assert.Contains("ترقيم", SkippedProcessing.Describe(print, app, 1));
    }

    [Fact]
    public void The_Watermark_Is_Named()
    {
        var (print, app) = Clean();
        app.WatermarkEnabled = true;
        app.WatermarkText = "عيّنة";

        Assert.Contains("العلامة المائية", SkippedProcessing.Describe(print, app, 1));
    }

    /// <summary>
    /// نفس الشرط اللي السلسلة نفسها بتستخدمه: علامة مفتوحة من غير نص
    /// ولا صورة مابتترسمش أصلًا، فمالهاش لازمة في التحذير.
    /// </summary>
    [Fact]
    public void An_Empty_Watermark_Is_Not_Named()
    {
        var (print, app) = Clean();
        app.WatermarkEnabled = true;
        app.WatermarkText = "";

        Assert.Empty(SkippedProcessing.ListFor(print, app, 1));
    }

    [Fact]
    public void Custom_Text_Is_Named()
    {
        var (print, app) = Clean();
        app.CustomTextEnabled = true;
        app.CustomText = "سري";

        Assert.Contains("النص المخصص", SkippedProcessing.Describe(print, app, 1));
    }

    // ══════════ الحالة النضيفة ══════════

    /// <summary>
    /// مفيش إعداد شغّال = مفيش حاجة تضيع، والتحذير لازم يقول كده صراحة
    /// بدل ما يخوّف على الفاضي.
    /// </summary>
    [Fact]
    public void Nothing_Enabled_Says_Nothing_Will_Be_Lost()
    {
        var (print, app) = Clean();

        Assert.Empty(SkippedProcessing.ListFor(print, app, 1));
        Assert.Contains("مفيش حاجة هتضيع", SkippedProcessing.Describe(print, app, 1));
    }

    // ══════════ الحارس ══════════

    /// <summary>
    /// كل الحاجات مفتوحة مع بعض = التمانية لازم يظهروا.
    ///
    /// الرقم ٨ مكتوب هنا عن قصد: لو حد ضاف ميزة معالجة جديدة ونسي
    /// يضيفها في <see cref="SkippedProcessing"/>، التست ده مش هيقع —
    /// بس أول ما يضيفها هيقع ويفكّره يحدّث الرقم. ودي أرخص طريقة
    /// نخلّي القايمة تفضل كاملة.
    /// </summary>
    [Fact]
    public void Everything_At_Once_Lists_All_Eight()
    {
        var print = new PrintSettings
        {
            MergeFiles = true,
            BookletMode = true,
            SlidesPerSheet = 2,
            DeletePages = true,
            PagesToDelete = "3",
            NumberPagesPerFile = true,
            ScalePercent = 95
        };

        var app = new AppSettings
        {
            WatermarkEnabled = true,
            WatermarkText = "عيّنة",
            CustomTextEnabled = true,
            CustomText = "سري"
        };

        var lost = SkippedProcessing.ListFor(print, app, fileCount: 3);

        Assert.Equal(8, lost.Count);
    }
}
