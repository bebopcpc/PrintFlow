using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// إيه اللي بيترسم قبل تجميع الشرائح وإيه اللي بعده.
///
/// دي فكرة الميزة كلها: الإعدادين "ترقيم الورقة كاملة بدل الشريحة" و"العلامة
/// على الورقة كاملة" مش محتاجين كود رسم جديد — هما بس بيحددوا **مكان**
/// الإضافة في السلسلة. الرقم اللي بيتحط قبل التجميع بيصغّر مع الشريحة،
/// واللي بعده بيفضل بحجمه على الورقة.
/// </summary>
public class SlidePipelineTests
{
    [Fact]
    public void Numbering_The_Whole_Sheet_Happens_After_The_Slides_Are_Placed()
    {
        var app = new AppSettings { NumberWholeSheetInsteadOfSlide = true };

        Assert.False(SlidePipeline.BeforeSlides(app).PageNumbers);
        Assert.True(SlidePipeline.AfterSlides(app).PageNumbers);
    }

    [Fact]
    public void Numbering_Each_Slide_Happens_Before_They_Are_Placed()
    {
        var app = new AppSettings { NumberWholeSheetInsteadOfSlide = false };

        Assert.True(SlidePipeline.BeforeSlides(app).PageNumbers);
        Assert.False(SlidePipeline.AfterSlides(app).PageNumbers);
    }

    [Fact]
    public void The_Watermark_Follows_Its_Own_Setting_Not_The_Numbering_One()
    {
        var app = new AppSettings
        {
            NumberWholeSheetInsteadOfSlide = true,
            WatermarkOnWholeSheet = false
        };

        Assert.True(SlidePipeline.BeforeSlides(app).Watermark);
        Assert.True(SlidePipeline.AfterSlides(app).PageNumbers);
        Assert.False(SlidePipeline.AfterSlides(app).Watermark);
    }

    /// <summary>
    /// النص المخصص (اسم المطبعة، "نسخة للمراجعة") بيتكتب مرة على الورقة —
    /// مش ٦ مرات على كل شريحة.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Custom_Text_Always_Goes_On_The_Sheet(bool wholeSheetNumbering)
    {
        var app = new AppSettings { NumberWholeSheetInsteadOfSlide = wholeSheetNumbering };

        Assert.False(SlidePipeline.BeforeSlides(app).CustomText);
        Assert.True(SlidePipeline.AfterSlides(app).CustomText);
    }

    /// <summary>
    /// كل إضافة لازم تترسم **مرة واحدة بالظبط** — لا مرتين ولا ولا مرة.
    /// لو الاتنين طلعوا true، هيتكتب رقمين على نفس الورقة.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Nothing_Is_Drawn_Twice_And_Nothing_Is_Lost(bool wholeSheetNumber, bool wholeSheetWatermark)
    {
        var app = new AppSettings
        {
            NumberWholeSheetInsteadOfSlide = wholeSheetNumber,
            WatermarkOnWholeSheet = wholeSheetWatermark
        };

        var before = SlidePipeline.BeforeSlides(app);
        var after = SlidePipeline.AfterSlides(app);

        Assert.NotEqual(before.PageNumbers, after.PageNumbers);
        Assert.NotEqual(before.Watermark, after.Watermark);
        Assert.NotEqual(before.CustomText, after.CustomText);
    }

    [Fact]
    public void With_No_Slides_Everything_Is_Drawn_In_One_Go()
    {
        var stage = SlidePipeline.Everything();

        Assert.True(stage.PageNumbers && stage.Watermark && stage.CustomText);
        Assert.False(stage.Nothing);
    }

    // ══════════ تصفية الطلب ══════════

    [Fact]
    public void Keeping_Only_One_Overlay_Drops_The_Others()
    {
        var full = FullRequest();

        var only = full.KeepOnly(new OverlayStage(PageNumbers: true, Watermark: false, CustomText: false));

        Assert.NotNull(only.PageNumbers);
        Assert.Null(only.Watermark);
        Assert.Null(only.CustomText);
    }

    [Fact]
    public void An_Empty_Stage_Leaves_Nothing_To_Draw()
    {
        var stripped = FullRequest().KeepOnly(new OverlayStage(false, false, false));

        Assert.False(stripped.HasAnyOverlay);
    }

    [Fact]
    public void A_Request_With_Overlays_Reports_That_It_Has_Them()
    {
        Assert.True(FullRequest().HasAnyOverlay);
    }

    /// <summary>التصفية مابتلمسش الملفات ولا مسار المخرج.</summary>
    [Fact]
    public void Filtering_Keeps_The_Files_And_The_Output_Path()
    {
        var full = FullRequest();

        var filtered = full.KeepOnly(new OverlayStage(true, false, false));

        Assert.Equal(full.InputFiles, filtered.InputFiles);
        Assert.Equal(full.OutputPath, filtered.OutputPath);
    }

    private static MergeRequest FullRequest() => MergeRequest.From(
        new PrintSettings { NumberPagesPerFile = true },
        new AppSettings
        {
            WatermarkEnabled = true,
            WatermarkText = "سري",
            CustomTextEnabled = true,
            CustomText = "مطبعة النور"
        },
        new[] { "a.pdf" },
        "out.pdf");
}
