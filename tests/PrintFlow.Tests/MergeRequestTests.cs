using PrintFlow.Domain;

namespace PrintFlow.Tests;

public class MergeRequestTests
{
    private static MergeRequest Full() => new()
    {
        InputFiles = ["a.pdf"],
        OutputPath = "out.pdf",
        PageNumbers = new PageNumberStyle(),
        Watermark = new WatermarkStyle { Text = "تجربة" },
        CustomText = new OverlayTextStyle { Text = "المطبعة" },
        PagesToDelete = "1,3"
    };

    // ══════════ الحذف بيتبني من الإعدادات ══════════

    [Fact]
    public void Deletion_Is_Carried_When_The_Box_Is_Checked()
    {
        var print = new PrintSettings { DeletePages = true, PagesToDelete = "2-4" };

        var request = MergeRequest.From(print, new AppSettings(), ["a.pdf"], "out.pdf");

        Assert.Equal("2-4", request.PagesToDelete);
    }

    [Fact]
    public void Deletion_Is_Dropped_When_The_Box_Is_Unchecked()
    {
        // النص بيفضل في الخانة بعد ما المستخدم يشيل العلامة — وده مقصود عشان
        // ما يضطرش يكتبه تاني. بس العلامة هي اللي بتقرر.
        var print = new PrintSettings { DeletePages = false, PagesToDelete = "2-4" };

        var request = MergeRequest.From(print, new AppSettings(), ["a.pdf"], "out.pdf");

        Assert.Null(request.PagesToDelete);
    }

    // ══════════ مراحل الإضافات ══════════

    [Fact]
    public void KeepOnly_Drops_What_The_Stage_Does_Not_Want()
    {
        var kept = Full().KeepOnly(new OverlayStage(PageNumbers: true, Watermark: false, CustomText: false));

        Assert.NotNull(kept.PageNumbers);
        Assert.Null(kept.Watermark);
        Assert.Null(kept.CustomText);
    }

    [Fact]
    public void KeepOnly_Keeps_The_Deletion_Because_It_Reads_The_Originals()
    {
        var kept = Full().KeepOnly(SlidePipeline.Everything());

        Assert.Equal("1,3", kept.PagesToDelete);
    }

    [Fact]
    public void OverlayOnly_Always_Clears_The_Deletion()
    {
        // ده هو الحاجز. الملف الداخل للمرحلة دي اتحذف منه خلاص؛ لو الحذف
        // اتكرر هيتنفّذ على أرقام الورق المجمّع ويشيل ورق عشوائي.
        var overlay = Full().OverlayOnly(SlidePipeline.Everything(), "stage1.pdf", "final.pdf");

        Assert.Null(overlay.PagesToDelete);
    }

    [Fact]
    public void OverlayOnly_Points_At_The_Intermediate_File()
    {
        var overlay = Full().OverlayOnly(SlidePipeline.Everything(), "stage1.pdf", "final.pdf");

        Assert.Equal(["stage1.pdf"], overlay.InputFiles);
        Assert.Equal("final.pdf", overlay.OutputPath);
    }

    [Fact]
    public void OverlayOnly_Filters_The_Overlays_Like_KeepOnly()
    {
        var overlay = Full().OverlayOnly(
            new OverlayStage(PageNumbers: false, Watermark: true, CustomText: false), "a", "b");

        Assert.Null(overlay.PageNumbers);
        Assert.NotNull(overlay.Watermark);
        Assert.Null(overlay.CustomText);
    }

    // ══════════ "في شغل ولا لأ" ══════════

    [Fact]
    public void Nothing_To_Do_Means_No_Overlays_And_No_Deletion()
    {
        var bare = new MergeRequest { InputFiles = ["a.pdf"], OutputPath = "b.pdf" };

        Assert.True(bare.HasNothingToDo);
    }

    [Fact]
    public void Deletion_Alone_Counts_As_Work()
    {
        // من غير ده، وضع "من غير دمج" كان هيعدّي الملفات زي ما هي والصفحات
        // المطلوب حذفها هتفضل مكانها
        var request = new MergeRequest { InputFiles = ["a.pdf"], OutputPath = "b.pdf", PagesToDelete = "1" };

        Assert.False(request.HasNothingToDo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_Deletion_Text_Is_Not_Work(string? text)
    {
        var request = new MergeRequest { InputFiles = ["a.pdf"], OutputPath = "b.pdf", PagesToDelete = text };

        Assert.True(request.HasNothingToDo);
    }

    [Fact]
    public void Any_Overlay_Counts_As_Work()
    {
        var request = new MergeRequest
        {
            InputFiles = ["a.pdf"],
            OutputPath = "b.pdf",
            Watermark = new WatermarkStyle { Text = "x" }
        };

        Assert.False(request.HasNothingToDo);
        Assert.True(request.HasAnyOverlay);
    }
}
