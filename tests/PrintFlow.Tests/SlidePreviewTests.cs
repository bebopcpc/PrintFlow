using PrintFlow.Application;
using PrintFlow.Domain;
using PrintFlow.Presentation;

namespace PrintFlow.Tests;

/// <summary>
/// المعاينة الحية لشكل الورقة.
///
/// أهم شرط فيها إنها **مستحيل تكذب**: بتتحسب بنفس دوال SheetLayout اللي
/// بتحسب الطباعة الحقيقية. لو كان ليها حسابات خاصة بيها، أي تعديل في
/// الطباعة كان هيخلي المستخدم يشوف حاجة ويطلعله حاجة تانية.
/// </summary>
public class SlidePreviewTests
{
    [Fact]
    public void The_Preview_Shows_One_Box_Per_Slide()
    {
        var vm = CreateViewModel();

        vm.Settings.SlidesPerSheet = 6;

        Assert.Equal(6, vm.SlidePreview.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6], vm.SlidePreview.Select(c => c.Number));
    }

    [Fact]
    public void Changing_The_Count_Redraws_The_Preview_Immediately()
    {
        var vm = CreateViewModel();

        vm.Settings.SlidesPerSheet = 4;
        Assert.Equal(4, vm.SlidePreview.Count);

        vm.Settings.SlidesPerSheet = 9;
        Assert.Equal(9, vm.SlidePreview.Count);
    }

    [Theory]
    [InlineData(nameof(PrintSettings.SlideOrder))]
    [InlineData(nameof(PrintSettings.SlideStart))]
    [InlineData(nameof(PrintSettings.SlideMargin))]
    [InlineData(nameof(PrintSettings.SlideOrientation))]
    public void Every_Slide_Setting_Refreshes_The_Preview(string setting)
    {
        var vm = CreateViewModel();
        vm.Settings.SlidesPerSheet = 4;

        var before = Snapshot(vm);

        switch (setting)
        {
            case nameof(PrintSettings.SlideOrder):
                vm.Settings.SlideOrder = SlideOrder.Vertical; break;
            case nameof(PrintSettings.SlideStart):
                vm.Settings.SlideStart = SlideStart.Left; break;
            case nameof(PrintSettings.SlideMargin):
                vm.Settings.SlideMargin = 60; break;
            case nameof(PrintSettings.SlideOrientation):
                vm.Settings.SlideOrientation = PageOrientation.Landscape; break;
        }

        Assert.NotEqual(before, Snapshot(vm));
    }

    /// <summary>
    /// الشرط الأساسي: المعاينة والطباعة بيقولوا نفس الكلام.
    /// نفس النسب، نفس الترتيب، نفس المواقع — مصغّرة بس.
    /// </summary>
    [Theory]
    [InlineData(4, SlideOrder.Horizontal, SlideStart.Right)]
    [InlineData(6, SlideOrder.Horizontal, SlideStart.Right)]
    [InlineData(9, SlideOrder.Vertical, SlideStart.Left)]
    public void The_Preview_Matches_What_Will_Actually_Print(int perSheet, SlideOrder order, SlideStart start)
    {
        var vm = CreateViewModel();
        vm.Settings.SlideMargin = 15;
        vm.Settings.SlideOrder = order;
        vm.Settings.SlideStart = start;
        vm.Settings.SlidesPerSheet = perSheet;

        // نفس حسابات الطباعة على A4 طولية
        var grid = SheetLayout.ChooseGrid(perSheet, 595, 842, 595, 842, 15);
        double scale = vm.SlidePreviewWidth / 595;

        for (int i = 0; i < perSheet; i++)
        {
            var real = SheetLayout.SlotFor(i, grid, 595, 842, 595, 842, 15, order, start);
            var shown = vm.SlidePreview[i];

            Assert.Equal(real.X * scale, shown.X, 3);
            Assert.Equal(real.Y * scale, shown.Y, 3);
            Assert.Equal(real.Width * scale, shown.Width, 3);
            Assert.Equal(real.Height * scale, shown.Height, 3);
        }
    }

    /// <summary>أول شريحة في وضع "يمين" لازم تبان في النص الأيمن من المعاينة.</summary>
    [Fact]
    public void The_First_Box_Sits_On_The_Right_For_Arabic()
    {
        var vm = CreateViewModel();
        vm.Settings.SlideStart = SlideStart.Right;
        vm.Settings.SlidesPerSheet = 4;

        var first = vm.SlidePreview[0];

        Assert.True(first.X > vm.SlidePreviewWidth / 2,
            $"أول خانة عند {first.X:0.0} والنص عند {vm.SlidePreviewWidth / 2:0.0}");
    }

    [Fact]
    public void The_Preview_Never_Spills_Outside_The_Sheet()
    {
        var vm = CreateViewModel();

        foreach (int count in new[] { 1, 2, 4, 6, 8, 9, 16 })
        {
            vm.Settings.SlidesPerSheet = count;

            foreach (var cell in vm.SlidePreview)
            {
                Assert.True(cell.X >= -0.01 && cell.Y >= -0.01, $"{count}: خانة بره");
                Assert.True(cell.X + cell.Width <= vm.SlidePreviewWidth + 0.01, $"{count}: خانة طالعة عرض");
                Assert.True(cell.Y + cell.Height <= vm.SlidePreviewHeight + 0.01, $"{count}: خانة طالعة طول");
                Assert.True(cell.Width > 0 && cell.Height > 0, $"{count}: خانة بمقاس صفر");
            }
        }
    }

    [Fact]
    public void A_Landscape_Sheet_Is_Drawn_Wider_Than_Tall()
    {
        var vm = CreateViewModel();

        vm.Settings.SlideOrientation = PageOrientation.Landscape;

        Assert.True(vm.SlidePreviewWidth > vm.SlidePreviewHeight);
    }

    [Fact]
    public void One_Slide_Says_So_In_Plain_Words()
    {
        var vm = CreateViewModel();

        vm.Settings.SlidesPerSheet = 1;

        Assert.Single(vm.SlidePreview);
        Assert.Contains("لوحدها", vm.SlideLayoutSummary);
    }

    [Fact]
    public void The_Summary_Names_The_Grid()
    {
        var vm = CreateViewModel();

        vm.Settings.SlidesPerSheet = 6;

        Assert.Contains("3", vm.SlideLayoutSummary);
        Assert.Contains("2", vm.SlideLayoutSummary);
    }

    /// <summary>المعاينة موجودة من أول ما البرنامج يفتح، مش لما المستخدم يلمس حاجة.</summary>
    [Fact]
    public void There_Is_Something_To_Look_At_Before_Touching_Anything()
    {
        var vm = CreateViewModel();

        Assert.NotEmpty(vm.SlidePreview);
        Assert.NotEmpty(vm.SlideLayoutSummary);
        Assert.True(vm.SlidePreviewWidth > 0 && vm.SlidePreviewHeight > 0);
    }

    // ══════════ ملخص الكتيّب ══════════

    /// <summary>
    /// أهم حاجة في الملخص: يقول عدد الصفحات الفاضية **قبل** الطباعة.
    /// لو ماقالش، اللي على الماكينة هيكتشفها بعد ما الورق يطلع.
    /// </summary>
    [Fact]
    public void The_Booklet_Summary_Warns_About_Blank_Pages_Before_Printing()
    {
        var vm = CreateViewModel();
        vm.Settings.BookletMode = true;

        AddFileWithPages(vm, 6);

        Assert.Contains("فاضية", vm.BookletSummary);
        Assert.Contains("2", vm.BookletSummary);
    }

    [Fact]
    public void A_Multiple_Of_Four_Needs_No_Warning()
    {
        var vm = CreateViewModel();
        vm.Settings.BookletMode = true;

        AddFileWithPages(vm, 8);

        Assert.DoesNotContain("فاضية", vm.BookletSummary);
        Assert.Contains("2 ورقة", vm.BookletSummary);
    }

    [Fact]
    public void There_Is_No_Booklet_Summary_When_The_Mode_Is_Off()
    {
        var vm = CreateViewModel();
        AddFileWithPages(vm, 8);

        Assert.Empty(vm.BookletSummary);
    }

    [Fact]
    public void Turning_The_Booklet_On_Asks_For_Files_When_There_Are_None()
    {
        var vm = CreateViewModel();

        vm.Settings.BookletMode = true;

        Assert.Contains("حمّل", vm.BookletSummary);
    }

    private static void AddFileWithPages(MainViewModel vm, int pages)
    {
        string path = Path.Combine(Path.GetTempPath(), "PFPrev_" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllText(path, "x");

        try
        {
            vm.AddFiles(new[] { path });
            vm.Files[0].PageCount = pages;

            // عدد الصفحات بيتملى بعدين، فالملخص محتاج يتحدث تاني
            vm.Settings.BookletMode = !vm.Settings.BookletMode;
            vm.Settings.BookletMode = !vm.Settings.BookletMode;
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static string Snapshot(MainViewModel vm) =>
        string.Join("|", vm.SlidePreview.Select(c => $"{c.Number}:{c.X:0.00},{c.Y:0.00},{c.Width:0.00}"));

    private static MainViewModel CreateViewModel()
        => new(new StubPrinters(), new StubMerge(), new StubPrint());

    private sealed class StubPrinters : IPrinterRepository
    {
        public Task<List<Printer>> GetPrintersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Printer>());
        public string SendTestPage(string printerName) => "ok";
        public PrinterCapabilities GetCapabilities(string printerName) => new();
    }

    private sealed class StubMerge : IPdfMergeService
    {
        public MergeResult Merge(MergeRequest request) => MergeResult.Succeeded("", 1);
    }

    private sealed class StubPrint : IPdfPrintService
    {
        public Task<PrintOutcome> PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
            => Task.FromResult(PrintOutcome.Delivered("ok"));
    }
}
