using PrintFlow.Application;
using PrintFlow.Domain;
using PrintFlow.Presentation;

namespace PrintFlow.Tests;

/// <summary>
/// دي أهم فايدة عملية من الـ ViewModel: بقينا نختبر منطق الصفحة الرئيسية
/// من غير ما نفتح البرنامج ولا نحتاج طابعة حقيقية.
/// </summary>
public class MainViewModelTests : IDisposable
{
    private readonly string _tempFolder;

    public MainViewModelTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "PrintFlowTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
        catch
        {
            // تنضيف بعد التست - مش مشكلة لو فشل
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AddFiles_Skips_NonPdf_Duplicates_And_Missing()
    {
        var vm = CreateViewModel();
        string pdf = MakeFile("a.pdf");
        string word = MakeFile("b.docx");

        int added = vm.AddFiles(new[] { pdf, word, pdf, Path.Combine(_tempFolder, "ghost.pdf") });

        Assert.Equal(1, added);
        Assert.Single(vm.Files);
        Assert.Equal("a.pdf", vm.Files[0].FileName);
    }

    [Fact]
    public async Task Refresh_Keeps_User_Selection_Across_Updates()
    {
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready, isDefault: true),
            Printer("Canon", PrinterStatus.Ready));

        var vm = CreateViewModel(repo);
        await vm.RefreshPrintersAsync();

        vm.Printers.First(p => p.Name == "Canon").IsSelected = true;

        // نفس الطابعات بس الحالة اتغيرت — الاختيار المفروض يفضل زي ما هو
        repo.Printers = new List<Printer>
        {
            Printer("HP", PrinterStatus.Error, isDefault: true),
            Printer("Canon", PrinterStatus.Ready)
        };

        await vm.RefreshPrintersAsync();

        Assert.True(vm.Printers.First(p => p.Name == "Canon").IsSelected);
        Assert.Equal(PrinterStatus.Error, vm.Printers.First(p => p.Name == "HP").Status);
    }

    [Fact]
    public async Task Refresh_Removes_Printers_That_Disappeared()
    {
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready),
            Printer("Canon", PrinterStatus.Ready));

        var vm = CreateViewModel(repo);
        await vm.RefreshPrintersAsync();
        Assert.Equal(2, vm.Printers.Count);

        repo.Printers = new List<Printer> { Printer("HP", PrinterStatus.Ready) };
        await vm.RefreshPrintersAsync();

        Assert.Single(vm.Printers);
        Assert.Equal("HP", vm.Printers[0].Name);
    }

    [Fact]
    public async Task Process_Merges_Then_Prints_To_Default_Printer()
    {
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready, isDefault: true),
            Printer("Canon", PrinterStatus.Ready));
        var printer = new FakePrintService();

        var vm = CreateViewModel(repo, printer);
        await vm.RefreshPrintersAsync();
        vm.AddFiles(new[] { MakeFile("a.pdf"), MakeFile("b.pdf") });

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Single(printer.Jobs);
        Assert.Equal("HP", printer.Jobs[0].PrinterName);
        Assert.Equal(1, printer.Jobs[0].Copies);
    }

    // ══════════ الطابعة بتتاخد من أنهي تاب ══════════

    /// <summary>
    /// "الطابعة الافتراضية للبرنامج" في تاب الإعدادات العامة كانت **إعداد ميت**:
    /// بيتحفظ في الملف ومحدش بيقراه خالص. المستخدم يختار طابعة هناك والبرنامج
    /// يطبع على افتراضية ويندوز من غير أي تفسير. دلوقتي هي نقطة البداية.
    /// </summary>
    [Fact]
    public async Task The_General_Tab_Default_Becomes_The_Starting_Printer()
    {
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready, isDefault: true),
            Printer("Canon", PrinterStatus.Ready));

        var vm = CreateViewModel(repo);
        vm.App.DefaultPrinterName = "Canon";

        await vm.RefreshPrintersAsync();

        Assert.Equal("Canon", vm.Settings.PrinterName);
    }

    /// <summary>
    /// بس اختيار المستخدم في تاب الرئيسية هو اللي بيحصل فعلًا — الإعدادات
    /// العامة نقطة بداية، مش أمر بيلغي اللي قدامك.
    /// </summary>
    [Fact]
    public async Task The_Main_Tab_Choice_Wins_For_The_Actual_Print()
    {
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready, isDefault: true),
            Printer("Canon", PrinterStatus.Ready));
        var printer = new FakePrintService();

        var vm = CreateViewModel(repo, printer);
        vm.App.DefaultPrinterName = "Canon";
        await vm.RefreshPrintersAsync();

        vm.Settings.PrinterName = "HP";      // المستخدم غيّرها في الرئيسية
        vm.Settings.PrintDirectlyAfterProcessing = true;
        vm.AddFiles(new[] { MakeFile("a.pdf") });
        await vm.ProcessCommand.ExecuteAsync();

        Assert.Single(printer.Jobs);
        Assert.Equal("HP", printer.Jobs[0].PrinterName);
    }

    /// <summary>لو الافتراضية المحفوظة مش متوصلة النهارده، بنرجع لافتراضية ويندوز.</summary>
    [Fact]
    public async Task A_Missing_Saved_Default_Falls_Back_To_The_Windows_Default()
    {
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready, isDefault: true),
            Printer("Canon", PrinterStatus.Ready));

        var vm = CreateViewModel(repo);
        vm.App.DefaultPrinterName = "طابعة اتشالت من زمان";

        await vm.RefreshPrintersAsync();

        Assert.Equal("HP", vm.Settings.PrinterName);
    }

    // ══════════ مهلة انتظار الطباعة ══════════

    /// <summary>
    /// عدد صفحات المستند الناتج لازم يوصل لأمر الطباعة، وإلا مهلة الانتظار
    /// هتتحسب غلط والجوبات الكبيرة هتتقتل في نص الطباعة.
    /// </summary>
    [Fact]
    public async Task The_Merged_Page_Count_Reaches_The_Print_Job()
    {
        var repo = new FakePrinterRepository(Printer("HP", PrinterStatus.Ready, isDefault: true));
        var printer = new FakePrintService();
        var merge = new FakeMergeService { PageCount = 210 };

        var vm = CreateViewModel(repo, printer, merge);
        await vm.RefreshPrintersAsync();

        vm.Settings.PrintDirectlyAfterProcessing = true;
        vm.AddFiles(new[] { MakeFile("a.pdf") });
        await vm.ProcessCommand.ExecuteAsync();

        Assert.Single(printer.Jobs);
        Assert.Equal(210, printer.Jobs[0].PageCount);
    }

    /// <summary>
    /// الطابعة كانت شغالة وقت ما بدأت المعالجة، واتفصلت وإحنا بندمج ٢١٠ صفحة.
    ///
    /// من غير تحديث الحالة لحظة الطباعة، كنا هنبعت لطابعة مش موجودة —
    /// و SumatraPDF بيرجّع كود 0 في الحالة دي (بسبب -silent) فالمستخدم
    /// بيقرا "نجاح" ومفيش ورقة طلعت. ده اتأكد عمليًا مش تخمين.
    /// </summary>
    [Fact]
    public async Task A_Printer_That_Dies_During_Processing_Is_Caught_Before_Sending()
    {
        var repo = new FakePrinterRepository(Printer("HP", PrinterStatus.Ready, isDefault: true));
        var printer = new FakePrintService();

        var vm = CreateViewModel(repo, printer);
        await vm.RefreshPrintersAsync();

        Assert.Equal("HP", vm.Settings.PrinterName);

        // الطابعة اتفصلت بعد ما البرنامج شافها شغالة
        repo.Printers = [Printer("HP", PrinterStatus.Offline, isDefault: true)];

        vm.Settings.PrintDirectlyAfterProcessing = true;
        vm.AddFiles(new[] { MakeFile("a.pdf") });
        await vm.ProcessCommand.ExecuteAsync();

        Assert.Empty(printer.Jobs);
        Assert.Contains("مؤهلة", vm.StatusText);
    }

    /// <summary>والعكس: طابعة رجعت تشتغل قبل الطباعة لازم تتقبل.</summary>
    [Fact]
    public async Task A_Printer_That_Comes_Back_Before_Printing_Is_Used()
    {
        var repo = new FakePrinterRepository(Printer("HP", PrinterStatus.Offline, isDefault: true));
        var printer = new FakePrintService();

        var vm = CreateViewModel(repo, printer);
        await vm.RefreshPrintersAsync();

        repo.Printers = [Printer("HP", PrinterStatus.Ready, isDefault: true)];

        vm.Settings.PrintDirectlyAfterProcessing = true;
        vm.AddFiles(new[] { MakeFile("a.pdf") });
        await vm.ProcessCommand.ExecuteAsync();

        Assert.Single(printer.Jobs);
        Assert.Equal("HP", printer.Jobs[0].PrinterName);
    }

    [Fact]
    public async Task Offline_Printers_Are_Never_Sent_To()
    {
        var repo = new FakePrinterRepository(Printer("HP", PrinterStatus.Offline, isDefault: true));
        var printer = new FakePrintService();

        var vm = CreateViewModel(repo, printer);
        await vm.RefreshPrintersAsync();
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Empty(printer.Jobs);
        Assert.Contains("مؤهلة", vm.StatusText);
    }

    [Fact]
    public async Task Distribute_Splits_Total_Copies_Across_Selected_Printers()
    {
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready),
            Printer("Canon", PrinterStatus.Ready),
            Printer("Epson", PrinterStatus.Ready));
        var printer = new FakePrintService();

        var vm = CreateViewModel(repo, printer);
        await vm.RefreshPrintersAsync();
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        vm.Settings.UseMultiplePrinters = true;
        vm.Settings.DistributeCopies = true;
        vm.Settings.TotalCopies = 10;
        foreach (var p in vm.Printers)
        {
            p.IsSelected = true;
        }

        await vm.ProcessCommand.ExecuteAsync();

        // ═══ ليه مابنعدّش الجوبات هنا ═══
        //
        // التست ده كان بيقول Jobs.Count == 3 — جوب واحد لكل مكنة. وده كان
        // صح لما نصيب المكنة كان بيتبعت أمر طباعة واحد كبير.
        //
        // من ١.٩.٦ النصيب بيتقطّع لقطع صغيرة (WorkSlicing) عشان لو مكنة
        // وقعت في نص شغلها يبقى اللي في الشك قطعة، مش نصيب كامل. فعدد
        // الجوبات بقى **تفصيلة داخلية** بتتغيّر مع حجم القطعة — تثبيته في
        // تست معناه إن أي تعديل في التقطيع يكسّر تست مالوش علاقة بالمعنى.
        //
        // اللي مايتغيّرش ولازم يفضل محروس تلات حاجات:

        // ١) مفيش نسخة ضاعت ولا اتكررت — الشرط اللي المطبعة بتدفع تمنه
        Assert.Equal(10, printer.Jobs.Sum(j => j.Copies));

        // ٢) الشغل وصل المكن التلاتة فعلًا، مش اتكوّم على واحدة
        Assert.Equal(3, printer.Jobs.Select(j => j.PrinterName).Distinct().Count());

        // ٣) والقسمة فضلت عادلة: ١٠ على ٣ = ٤ و٣ و٣
        var shares = new[] { "HP", "Canon", "Epson" }
            .Select(name => printer.Jobs.Where(j => j.PrinterName == name).Sum(j => j.Copies))
            .OrderBy(share => share)
            .ToList();

        Assert.Equal(new[] { 3, 3, 4 }, shares);
    }

    [Fact]
    public async Task Uniform_Mode_Sends_Full_Count_To_Each_Printer()
    {
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready),
            Printer("Canon", PrinterStatus.Ready));
        var printer = new FakePrintService();

        var vm = CreateViewModel(repo, printer);
        await vm.RefreshPrintersAsync();
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        vm.Settings.UseMultiplePrinters = true;
        vm.Settings.DistributeCopies = false;
        vm.Settings.TotalCopies = 4;
        foreach (var p in vm.Printers)
        {
            p.IsSelected = true;
        }

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(2, printer.Jobs.Count);
        Assert.All(printer.Jobs, j => Assert.Equal(4, j.Copies));
    }

    // ══════════ اختيار المكن: الوعد الجديد ══════════
    //
    // كل التستات دي بتشترك في حاجة واحدة: **مفيش أي مفتاح بيتظبط فيها**.
    // ده هو المقصود منها بالظبط. لو رجع يوم وبقى لازم تظبط إعداد عشان
    // التوزيع يشتغل، التستات دي هي اللي هتقع.

    [Fact]
    public async Task Ticking_Two_Printers_Is_Enough_To_Split_The_Work()
    {
        // ═══ ده البلاغ بالنص ═══
        //
        // "مش عارف اختار أكتر من واحدة" — من مستخدم عنده سبع طابعات
        // والميزة شغالة قدامه، بس ورا تلات خطوات في تلات أماكن.
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready),
            Printer("Canon", PrinterStatus.Ready));
        var printer = new FakePrintService();

        var vm = CreateViewModel(repo, printer);
        await vm.RefreshPrintersAsync();
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        vm.Settings.TotalCopies = 10;

        foreach (var p in vm.Printers)
        {
            p.IsSelected = true;
        }

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(10, printer.Jobs.Sum(j => j.Copies));
        Assert.Equal(5, printer.Jobs.Where(j => j.PrinterName == "HP").Sum(j => j.Copies));
        Assert.Equal(5, printer.Jobs.Where(j => j.PrinterName == "Canon").Sum(j => j.Copies));
    }

    [Fact]
    public async Task Ticking_One_Printer_Sends_Everything_To_It()
    {
        // مكنة واحدة مالهاش حاجة تتقسّم عليها. القاعدة نفسها بتغطّي
        // الحالتين من غير ما المستخدم يفكّر في "وضع".
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready),
            Printer("Canon", PrinterStatus.Ready));
        var printer = new FakePrintService();

        var vm = CreateViewModel(repo, printer);
        await vm.RefreshPrintersAsync();
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        vm.Settings.TotalCopies = 8;

        foreach (var p in vm.Printers)
        {
            p.IsSelected = p.Name == "Canon";
        }

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(8, printer.Jobs.Sum(j => j.Copies));
        Assert.All(printer.Jobs, j => Assert.Equal("Canon", j.PrinterName));
    }

    [Fact]
    public async Task Asking_For_The_Full_Count_On_Every_Machine_Still_Works()
    {
        // الحالة النادرة: نسخة كاملة لكل فرع. بقت محتاجة علامة بدل ما
        // كانت هي الافتراضي — بس لسه موجودة، مااتشالتش.
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready),
            Printer("Canon", PrinterStatus.Ready));
        var printer = new FakePrintService();

        var vm = CreateViewModel(repo, printer);
        await vm.RefreshPrintersAsync();
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        vm.Settings.TotalCopies = 6;
        vm.SameCountOnEveryPrinter = true;

        foreach (var p in vm.Printers)
        {
            p.IsSelected = true;
        }

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(12, printer.Jobs.Sum(j => j.Copies));
        Assert.Equal(6, printer.Jobs.Where(j => j.PrinterName == "HP").Sum(j => j.Copies));
        Assert.Equal(6, printer.Jobs.Where(j => j.PrinterName == "Canon").Sum(j => j.Copies));
    }

    [Fact]
    public async Task The_Summary_Shows_The_Split_Before_Anything_Prints()
    {
        // المستخدم لازم يشوف القسمة **قبل** ما الورق يطلع. السطر ده
        // بيتحسب بنفس دالة التوزيع اللي بتشتغل وقت الطباعة، فمستحيل
        // يقول حاجة والورق يطلع حاجة تانية.
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready),
            Printer("Canon", PrinterStatus.Ready),
            Printer("Epson", PrinterStatus.Ready));

        var vm = CreateViewModel(repo);
        await vm.RefreshPrintersAsync();
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        vm.Settings.TotalCopies = 50;

        foreach (var p in vm.Printers)
        {
            p.IsSelected = true;
        }

        Assert.Contains("3 مكن مختارة", vm.PrinterChoiceSummary);
        Assert.Contains("هيتقسّم", vm.PrinterChoiceSummary);
    }

    [Fact]
    public async Task Unticking_Everything_Still_Prints_Instead_Of_Refusing()
    {
        // المستخدم شال التعليم عن كل حاجة وضغط طباعة. مايصحش البرنامج
        // يقف في وشه — بيرجع للطابعة الافتراضية ويقولها في السطر.
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready),
            Printer("Canon", PrinterStatus.Ready));
        var printer = new FakePrintService();

        var vm = CreateViewModel(repo, printer);
        await vm.RefreshPrintersAsync();
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        foreach (var p in vm.Printers)
        {
            p.IsSelected = false;
        }

        await vm.ProcessCommand.ExecuteAsync();

        Assert.NotEmpty(printer.Jobs);
        Assert.Contains("مفيش مكنة معلّمة", vm.PrinterChoiceSummary);
    }
    
    /// <summary>
    /// ⚠ السطر ده كان بيقول «هيتطبع على PrintFlow لوحدها».
    ///
    /// و PrintFlow دي طابعة الاستقبال الوهمية — الطباعة عليها ممنوعة،
    /// لأن الجوب بيرجع للبرنامج تاني وتبقى حلقة بتاكل القرص.
    ///
    /// السبب إن السطر كان بيختار الاحتياطي بنسخة تانية من المنطق من غير
    /// ما تسأل «هو مؤهل؟»، بينما الطباعة بتسأل. فالشاشة كانت بتوعد بمكنة
    /// والضغط بيقول «مفيش طابعة مؤهلة» — تناقض قدام اللي واقف على المكن.
    /// </summary>
    [Fact]
    public async Task The_Summary_Never_Names_A_Printer_The_Order_Cannot_Use()
    {
        var repo = new FakePrinterRepository(
            Printer(VirtualPrinter.PrinterName, PrinterStatus.Ready, isDefault: true),
            Printer("HP", PrinterStatus.Paused));

        var vm = CreateViewModel(repo, new FakePrintService());
        await vm.RefreshPrintersAsync();

        foreach (var p in vm.Printers)
        {
            p.IsSelected = false;
        }

        Assert.DoesNotContain(VirtualPrinter.PrinterName, vm.PrinterChoiceSummary);
        Assert.Contains("مفيش طابعة مؤهلة", vm.PrinterChoiceSummary);
    }

    [Fact]
    public async Task Selected_Printers_Are_Saved_Into_Settings_For_Presets()
    {
        var repo = new FakePrinterRepository(
            Printer("HP", PrinterStatus.Ready),
            Printer("Canon", PrinterStatus.Ready));

        var vm = CreateViewModel(repo);
        await vm.RefreshPrintersAsync();
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        // ═══ اتغيّر في ١.٩.٦ ═══
        //
        // الطابعة الافتراضية بقت **معلّمة من الأول**. قبل كده القايمة كانت
        // بتفتح فاضية والطباعة بتروح لطابعة مالهاش أي أثر ظاهر في الواجهة —
        // يعني الواجهة بتقول حاجة والبرنامج بيعمل حاجة تانية.
        Assert.Equal(new[] { "HP" }, vm.Printers.Where(p => p.IsSelected).Select(p => p.Name));

        vm.Printers.First(p => p.Name == "Canon").IsSelected = true;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(new[] { "HP", "Canon" }, vm.Settings.SelectedPrinters);

        // وشيل التعليم بيشيلها من الحفظ كمان — الاتجاه ده كان مش متختبر
        // خالص، وهو اللي المستخدم بيعمله لما يقفل مكنة للصيانة
        vm.Printers.First(p => p.Name == "HP").IsSelected = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(new[] { "Canon" }, vm.Settings.SelectedPrinters);
    }

    [Fact]
    public async Task Print_Command_Is_Blocked_Before_Processing()
    {
        var vm = CreateViewModel();
        Assert.False(vm.PrintCommand.CanExecute(null));

        vm.AddFiles(new[] { MakeFile("a.pdf") });
        vm.Settings.PrintDirectlyAfterProcessing = false;
        await vm.ProcessCommand.ExecuteAsync();

        Assert.True(vm.PrintCommand.CanExecute(null));
    }

    [Fact]
    public void Reset_Clears_Files_And_Restores_Defaults()
    {
        var vm = CreateViewModel();
        vm.AddFiles(new[] { MakeFile("a.pdf") });
        vm.Settings.TotalCopies = 9;
        vm.Settings.Grayscale = true;

        vm.ResetCommand.Execute(null);

        Assert.Empty(vm.Files);
        Assert.Equal(1, vm.Settings.TotalCopies);
        Assert.False(vm.Settings.Grayscale);
    }

    // ══════════ وصول الإعدادات العامة لخدمة الدمج ══════════

    [Fact]
    public async Task Watermark_Settings_Reach_The_Merge_Request()
    {
        var merge = new FakeMergeService();
        var vm = CreateViewModel(mergeService: merge);
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        vm.App.WatermarkEnabled = true;
        vm.App.WatermarkText = "مطبعة النور";
        vm.App.WatermarkColorHex = "#FF0000";
        vm.App.WatermarkFontFamily = "Times New Roman";
        vm.App.WatermarkFontSize = 72;
        vm.App.WatermarkOpacityPercent = 30;
        vm.App.WatermarkRotationDegrees = 20;
        vm.App.WatermarkBold = true;

        vm.Settings.PrintDirectlyAfterProcessing = false;
        await vm.ProcessCommand.ExecuteAsync();

        var watermark = merge.LastRequest?.Watermark;
        Assert.NotNull(watermark);
        Assert.Equal("مطبعة النور", watermark.Text);
        Assert.Equal("#FF0000", watermark.ColorHex);
        Assert.Equal("Times New Roman", watermark.FontFamily);
        Assert.Equal(72, watermark.FontSize);
        Assert.Equal(30, watermark.OpacityPercent);
        Assert.Equal(20, watermark.RotationDegrees);
        Assert.True(watermark.Bold);
    }

    [Fact]
    public async Task Watermark_Is_Absent_When_Disabled()
    {
        var merge = new FakeMergeService();
        var vm = CreateViewModel(mergeService: merge);
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        vm.App.WatermarkEnabled = false;
        vm.App.WatermarkText = "مش المفروض تظهر";

        vm.Settings.PrintDirectlyAfterProcessing = false;
        await vm.ProcessCommand.ExecuteAsync();

        Assert.Null(merge.LastRequest?.Watermark);
    }

    [Fact]
    public async Task Page_Numbering_Style_Reaches_The_Merge_Request()
    {
        var merge = new FakeMergeService();
        var vm = CreateViewModel(mergeService: merge);
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        vm.Settings.NumberPagesPerFile = true;
        vm.App.PageNumberPosition = ContentPosition.TopRight;
        vm.App.PageNumberColorHex = "#0000FF";
        vm.App.PageNumberFontSize = 14;
        vm.App.PageNumberEdgeMargin = 30;

        vm.Settings.PrintDirectlyAfterProcessing = false;
        await vm.ProcessCommand.ExecuteAsync();

        var numbers = merge.LastRequest?.PageNumbers;
        Assert.NotNull(numbers);
        Assert.Equal(ContentPosition.TopRight, numbers.Position);
        Assert.Equal("#0000FF", numbers.ColorHex);
        Assert.Equal(14, numbers.FontSize);
        Assert.Equal(30, numbers.EdgeMargin);
    }

    [Fact]
    public async Task No_Page_Numbers_When_Checkbox_Is_Off()
    {
        var merge = new FakeMergeService();
        var vm = CreateViewModel(mergeService: merge);
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        vm.Settings.NumberPagesPerFile = false;
        vm.Settings.PrintDirectlyAfterProcessing = false;
        await vm.ProcessCommand.ExecuteAsync();

        Assert.Null(merge.LastRequest?.PageNumbers);
    }

    // ══════════ وضع "من غير دمج" ══════════

    /// <summary>
    /// الباج اللي كان موجود: شيل علامة "دمج الملفات" والعلامة المائية
    /// والترقيم بيختفوا تمامًا — الملفات كانت بتتطبع زي ما هي بالظبط.
    /// </summary>
    [Fact]
    public async Task Each_File_Is_Processed_On_Its_Own_When_Merging_Is_Off()
    {
        var merge = new FakeMergeService { PageCount = 4 };
        var vm = CreateViewModel(mergeService: merge, pdfInfo: new FakePdfInfoService { PageCount = 4 });

        vm.AddFiles(new[] { MakeFile("a.pdf"), MakeFile("b.pdf"), MakeFile("c.pdf") });
        vm.Settings.MergeFiles = false;
        vm.Settings.NumberPagesPerFile = true;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(3, merge.Requests.Count);
        Assert.All(merge.Requests, r => Assert.Single(r.InputFiles));
        Assert.All(merge.Requests, r => Assert.NotNull(r.PageNumbers));
    }

    /// <summary>
    /// الترقيم المتصل لازم يفضل متصل حتى والملفات منفصلة:
    /// الأول ١..٤ والتاني بيكمّل من ٥ والتالت من ٩، وكلهم "من ١٢".
    /// </summary>
    [Fact]
    public async Task Numbering_Runs_Continuously_Across_Separate_Files()
    {
        var merge = new FakeMergeService { PageCount = 4 };
        var vm = CreateViewModel(mergeService: merge, pdfInfo: new FakePdfInfoService { PageCount = 4 });

        vm.AddFiles(new[] { MakeFile("a.pdf"), MakeFile("b.pdf"), MakeFile("c.pdf") });
        vm.Settings.MergeFiles = false;
        vm.Settings.NumberPagesPerFile = true;
        vm.Settings.PrintDirectlyAfterProcessing = false;
        vm.App.RestartNumberingForEachFile = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal([1, 5, 9], merge.Requests.Select(r => r.PageNumbers!.FirstPageNumber));
        Assert.All(merge.Requests, r => Assert.Equal(12, r.PageNumbers!.TotalPages));
    }

    /// <summary>
    /// أهم فرق عن وضع الدمج: ملف بايظ وسط ٢٠ ملف **مايوقفش** الباقي.
    /// اللي واقف على الماكينة يخسر ١٩ ملف سليم عشان واحد بايظ = سلوك غلط.
    /// </summary>
    [Fact]
    public async Task One_Bad_File_Does_Not_Stop_The_Rest()
    {
        var merge = new FakeMergeService { PageCount = 2 };
        merge.FailFor.Add("bad.pdf");

        var vm = CreateViewModel(mergeService: merge, pdfInfo: new FakePdfInfoService { PageCount = 2 });
        vm.AddFiles(new[] { MakeFile("good1.pdf"), MakeFile("bad.pdf"), MakeFile("good2.pdf") });
        vm.Settings.MergeFiles = false;
        vm.Settings.NumberPagesPerFile = true;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(2, vm.OutputFileCount);
        Assert.Contains("فشل", vm.StatusText);
        Assert.Contains(vm.Log, line => line.Contains("bad.pdf"));
    }

    /// <summary>والملف البايظ لازم يتقال بالاسم، مش "حصل خطأ" وخلاص.</summary>
    [Fact]
    public async Task The_Failed_File_Is_Named_In_The_Log()
    {
        var merge = new FakeMergeService();
        merge.FailFor.Add("تالف.pdf");

        var vm = CreateViewModel(mergeService: merge, pdfInfo: new FakePdfInfoService { PageCount = 1 });
        vm.AddFiles(new[] { MakeFile("سليم.pdf"), MakeFile("تالف.pdf") });
        vm.Settings.MergeFiles = false;
        vm.Settings.NumberPagesPerFile = true;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Contains(vm.Log, line => line.Contains("تالف.pdf"));
        Assert.DoesNotContain(vm.Log, line => line.Contains("سليم.pdf") && line.Contains("فشل"));
    }

    /// <summary>
    /// مفيش ترقيم ولا علامة مائية ولا نص؟ يبقى إعادة كتابة الملفات هدر —
    /// وكمان بتضيّع جودة الأصل من غير أي فايدة. بنطبع الأصول زي ما هي.
    /// </summary>
    [Fact]
    public async Task Files_Are_Not_Rewritten_When_There_Is_Nothing_To_Add()
    {
        var merge = new FakeMergeService();
        var vm = CreateViewModel(mergeService: merge);

        vm.AddFiles(new[] { MakeFile("a.pdf"), MakeFile("b.pdf") });
        vm.Settings.MergeFiles = false;
        vm.Settings.NumberPagesPerFile = false;
        vm.App.WatermarkEnabled = false;
        vm.App.CustomTextEnabled = false;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Empty(merge.Requests);
        Assert.Equal(2, vm.OutputFileCount);
    }

    /// <summary>كل ملف لازم يروح لمسار مختلف، حتى لو الأصول بنفس الاسم.</summary>
    [Fact]
    public async Task Files_With_The_Same_Name_Get_Different_Outputs()
    {
        var merge = new FakeMergeService { PageCount = 1 };
        var vm = CreateViewModel(mergeService: merge, pdfInfo: new FakePdfInfoService { PageCount = 1 });

        string first = MakeFile("فاتورة.pdf");
        string nested = Path.Combine(_tempFolder, "تاني");
        Directory.CreateDirectory(nested);
        string second = Path.Combine(nested, "فاتورة.pdf");
        File.WriteAllText(second, "test");

        vm.AddFiles(new[] { first, second });
        vm.Settings.MergeFiles = false;
        vm.Settings.NumberPagesPerFile = true;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        var outputs = merge.Requests.Select(r => r.OutputPath).ToList();
        Assert.Equal(2, outputs.Distinct().Count());
    }

    [Fact]
    public void Sorting_By_Name_Reorders_Files()
    {
        var vm = CreateViewModel();
        vm.AddFiles(new[] { MakeFile("c.pdf"), MakeFile("a.pdf"), MakeFile("b.pdf") });

        vm.App.FileSortOrder = FileSortOrder.ByName;
        vm.SortFiles();

        Assert.Equal(new[] { "a.pdf", "b.pdf", "c.pdf" }, vm.Files.Select(f => f.FileName));
    }

    /// <summary>
    /// قايمة "ترتيب الملفات" كانت بتتحفظ ومحصلش حاجة — زرار وهمي.
    /// دلوقتي مجرد ما المستخدم يغيّر الاختيار، القايمة نفسها بتترتب.
    /// </summary>
    [Fact]
    public void Changing_The_Sort_Order_Reorders_Immediately()
    {
        var vm = CreateViewModel();
        vm.AddFiles(new[] { MakeFile("c.pdf"), MakeFile("a.pdf"), MakeFile("b.pdf") });

        vm.App.FileSortOrder = FileSortOrder.ByName;

        Assert.Equal(new[] { "a.pdf", "b.pdf", "c.pdf" }, vm.Files.Select(f => f.FileName));
    }

    // ══════════ حذف ملف واحد ══════════

    /// <summary>
    /// الزرار الأحمر تحت بيمسح **كل** القايمة. لما تكون محمّل 20 ملف وعايز
    /// تشيل واحد بس، ده مش حل — فبقى في زرار لكل صف.
    /// </summary>
    [Fact]
    public void Removing_One_File_Keeps_The_Rest()
    {
        var vm = CreateViewModel();
        vm.AddFiles(new[] { MakeFile("a.pdf"), MakeFile("b.pdf"), MakeFile("c.pdf") });

        vm.RemoveFileCommand.Execute(vm.Files[1]);

        Assert.Equal(new[] { "a.pdf", "c.pdf" }, vm.Files.Select(f => f.FileName));
    }

    [Fact]
    public void Removing_A_File_That_Is_Not_There_Does_Nothing()
    {
        var vm = CreateViewModel();
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        vm.RemoveFileCommand.Execute(null);
        vm.RemoveFileCommand.Execute(new PdfFileItem("ghost.pdf", 10, DateTime.UtcNow));

        Assert.Single(vm.Files);
    }

    [Fact]
    public void Removing_The_Last_File_Disables_Processing()
    {
        var vm = CreateViewModel();
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        vm.RemoveFileCommand.Execute(vm.Files[0]);

        Assert.Empty(vm.Files);
        Assert.False(vm.ProcessCommand.CanExecute(null));
    }

    // ══════════ عدد صفحات كل ملف ══════════

    [Fact]
    public async Task Page_Counts_Are_Loaded_And_Shown_Per_File()
    {
        var vm = CreateViewModel(pdfInfo: new FakePdfInfoService { PageCount = 7 });
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        await vm.LoadPageCountsAsync();

        Assert.Equal(7, vm.Files[0].PageCount);
        Assert.Contains("7 صفحة", vm.Files[0].DisplayText);
    }

    [Fact]
    public async Task The_Header_Shows_The_Total_Page_Count()
    {
        var vm = CreateViewModel(pdfInfo: new FakePdfInfoService { PageCount = 5 });
        vm.AddFiles(new[] { MakeFile("a.pdf"), MakeFile("b.pdf") });

        await vm.LoadPageCountsAsync();

        Assert.Contains("2 ملف", vm.FilesCountText);
        Assert.Contains("10 صفحة", vm.FilesCountText);
    }

    /// <summary>
    /// ملف تالف أو محمي بباسورد مش هينفع نقرا صفحاته. ده لازم يعدي بهدوء:
    /// الملف يفضل في القايمة والباقي يشتغل عادي.
    /// </summary>
    [Fact]
    public async Task A_File_Whose_Pages_Cannot_Be_Read_Is_Not_A_Failure()
    {
        var vm = CreateViewModel(pdfInfo: new FakePdfInfoService { PageCount = null });
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        await vm.LoadPageCountsAsync();

        Assert.Single(vm.Files);
        Assert.Null(vm.Files[0].PageCount);
        Assert.Contains("1 ملف", vm.FilesCountText);
    }

    [Fact]
    public async Task Without_A_Page_Reader_Nothing_Breaks()
    {
        var vm = CreateViewModel();
        vm.AddFiles(new[] { MakeFile("a.pdf") });

        await vm.LoadPageCountsAsync();

        Assert.Single(vm.Files);
        Assert.Null(vm.Files[0].PageCount);
    }

    // ══════════ استرجاع الإعدادات الافتراضية ══════════

    /// <summary>كل خصايص AppSettings اللي ينفع تتكتب.</summary>
    private static List<System.Reflection.PropertyInfo> WritableAppSettings()
        => typeof(AppSettings).GetProperties()
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToList();

    private static bool IsConnection(System.Reflection.PropertyInfo property)
        => property.IsDefined(typeof(ConnectionSettingAttribute), inherit: true);

    /// <summary>
    /// التست ده حارس على باج بيتكرر بطبعه: النسخة القديمة من
    /// RestoreDefaultAppSettings كانت لستة مكتوبة بالإيد، وفعلًا نسيت خاصية
    /// جديدة فكان الزرار بيسيبها زي ما هي من غير ما حد ياخد باله. بنمشي
    /// بالـ Reflection على **كل** خاصية عشان أي حاجة تتضاف بكرة تبقى مغطّاة.
    ///
    /// الاستثناء الوحيد: الخصايص المعلّمة بـ ConnectionSetting. دي بتتفحص
    /// في تست لوحدها تحت.
    /// </summary>
    [Fact]
    public void Restoring_Defaults_Resets_Every_Single_Property()
    {
        var vm = CreateViewModel();
        var defaults = new AppSettings();

        var writable = WritableAppSettings().Where(p => !IsConnection(p)).ToList();

        Assert.NotEmpty(writable);

        // بنغيّر كل خاصية عن قيمتها الافتراضية
        foreach (var property in writable)
        {
            property.SetValue(vm.App, Different(property.GetValue(defaults), property.PropertyType));
        }

        vm.RestoreDefaultAppSettingsCommand.Execute(null);

        var stillWrong = writable
            .Where(p => !Equals(p.GetValue(vm.App), p.GetValue(defaults)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(stillWrong.Count == 0,
            "خصائص مارجعتش لقيمتها الافتراضية: " + string.Join("، ", stillWrong));
    }

    /// <summary>
    /// زرار "استعادة الإعدادات الافتراضية" مايقفلش الاستقبال.
    ///
    /// السبب: رجوع لون الترقيم للافتراضي بيبان في نص ثانية. قفل الاستقبال
    /// مابيبانش خالص — البرنامج بيفضل شكله سليم والشغل الجاي من بره بيروح
    /// في الهوا لحد ما حد يسأل "فين الملف اللي بعتّهولك؟"
    /// </summary>
    [Fact]
    public void Restoring_Defaults_Leaves_The_Reception_Wiring_Alone()
    {
        var vm = CreateViewModel();

        vm.App.ReceiveFromVirtualPrinter = true;
        vm.App.HotFolder = @"\\SERVER\شغل";
        vm.App.PrintReceivedAutomatically = true;

        vm.RestoreDefaultAppSettingsCommand.Execute(null);

        Assert.True(vm.App.ReceiveFromVirtualPrinter);
        Assert.Equal(@"\\SERVER\شغل", vm.App.HotFolder);
        Assert.True(vm.App.PrintReceivedAutomatically);
    }

    /// <summary>
    /// وبيقول بالنص إن الاستقبال ماتغيرش — الصمت هنا هو اللي بيخلي الناس
    /// تفتكر إن الزرار قفله.
    /// </summary>
    [Fact]
    public void Restoring_Defaults_Says_The_Reception_Was_Left_Alone()
    {
        var vm = CreateViewModel();

        vm.RestoreDefaultAppSettingsCommand.Execute(null);

        Assert.Contains("الاستقبال", vm.StatusText);
    }

    /// <summary>
    /// الخصايص المعلّمة كتوصيلة لازم تفضل قليلة ومقصودة.
    ///
    /// من غير الحارس ده، أي حد (أنا بالذات) ممكن يحل تست فاشل بإضافة
    /// [ConnectionSetting] على الخاصية بدل ما يصلّح المشكلة — وساعتها
    /// زرار الافتراضي يبقى مالوش لازمة من غير ما حد ياخد باله.
    /// </summary>
    [Fact]
    public void Only_The_Reception_Settings_Are_Exempt_From_The_Defaults_Button()
    {
        var exempt = WritableAppSettings().Where(IsConnection).Select(p => p.Name).OrderBy(n => n).ToList();

        Assert.Equal(
            new[]
            {
                nameof(AppSettings.HotFolder),
                nameof(AppSettings.PrintReceivedAutomatically),
                nameof(AppSettings.ReceiveFromVirtualPrinter)
            },
            exempt);
    }

    // ══════════ التصفير الأحمر ══════════

    /// <summary>
    /// زرار "حذف الملفات وإرجاع الإعدادات" الأحمر مايلمسش الإعدادات العامة.
    ///
    /// ═══ ليه التست ده موجود ═══
    ///
    /// جه بلاغ من التجربة الحقيقية إن الزرار ده بيقفل مربع "استقبال من
    /// طابعة PrintFlow". الكود مكانش بيعمل كده — بس الرسالة كانت بتقول
    /// "اترجعت الإعدادات للوضع الافتراضي" على إطلاقها، والزرار بيمسح
    /// اللوج اللي فيه سطر الاستقبال. الانطباع كان مفهوم تمامًا.
    ///
    /// التست ده بيثبّت الحدود بالأرقام مش بالكلام: أي حد يضيف بكرة سطر
    /// في Reset بيلمس App، ده هيقع ويقوله اسم الخاصية.
    /// </summary>
    [Fact]
    public void Reset_Never_Touches_The_General_Settings()
    {
        var vm = CreateViewModel();
        var writable = WritableAppSettings();

        // بنبعّد كل خاصية عن قيمتها الافتراضية عشان لو التصفير رجّعها نشوفه
        var defaults = new AppSettings();

        foreach (var property in writable)
        {
            property.SetValue(vm.App, Different(property.GetValue(defaults), property.PropertyType));
        }

        var before = writable.ToDictionary(p => p.Name, p => p.GetValue(vm.App));

        vm.ResetCommand.Execute(null);

        var changed = writable
            .Where(p => !Equals(p.GetValue(vm.App), before[p.Name]))
            .Select(p => p.Name)
            .ToList();

        Assert.True(changed.Count == 0,
            "زرار التصفير الأحمر لمس إعدادات عامة: " + string.Join("، ", changed));
    }

    /// <summary>ورسالته بتقول بالنص إنه رجّع إيه وساب إيه.</summary>
    [Fact]
    public void Reset_Says_What_It_Did_Not_Touch()
    {
        var vm = CreateViewModel();

        vm.ResetCommand.Execute(null);

        Assert.Contains("الجوب", vm.StatusText);
        Assert.Contains("الاستقبال", vm.StatusText);
    }

    /// <summary>بترجّع قيمة مختلفة أكيد عن اللي داخلة، مهما كان نوعها.</summary>
    private static object? Different(object? value, Type type)
    {
        if (type == typeof(bool)) return !(bool)value!;
        if (type == typeof(string)) return (string?)value == "مختلف" ? "غير" : "مختلف";
        if (type == typeof(int)) return (int)value! + 7;

        // السعر decimal. من غير الحالة دي، المساعد بيرجّع نفس القيمة —
        // فالخاصية ماتتغيّرش، ومحدش بيحفظ، والتست بيقع بسبب المساعد
        // نفسه مش بسبب باج حقيقي.
        if (type == typeof(decimal)) return (decimal)value! + 0.5m;

        if (type.IsEnum)
        {
            foreach (var candidate in Enum.GetValues(type))
            {
                if (!Equals(candidate, value)) return candidate;
            }
        }

        return value;
    }

    // ══════════ مساعدات وفيكات ══════════

    private MainViewModel CreateViewModel(
        FakePrinterRepository? repository = null,
        FakePrintService? printService = null,
        FakeMergeService? mergeService = null,
        FakePdfInfoService? pdfInfo = null)
        => new(
            repository ?? new FakePrinterRepository(),
            mergeService ?? new FakeMergeService(),
            printService ?? new FakePrintService(),
            pdfInfo: pdfInfo);

    private string MakeFile(string name)
    {
        string path = Path.Combine(_tempFolder, name);
        File.WriteAllText(path, "test");
        return path;
    }

    private static Printer Printer(string name, PrinterStatus status, bool isDefault = false)
        => new() { Name = name, Status = status, IsDefault = isDefault, Port = "USB001" };

    private sealed class FakePrinterRepository : IPrinterRepository
    {
        public FakePrinterRepository(params Printer[] printers) => Printers = printers.ToList();

        public List<Printer> Printers { get; set; }

        public Task<List<Printer>> GetPrintersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Printers.ToList());

        public string SendTestPage(string printerName) => "ok";
        public PrinterCapabilities GetCapabilities(string printerName) => new();
    }

    private sealed class FakeMergeService : IPdfMergeService
    {
        public MergeRequest? LastRequest => Requests.LastOrDefault();

        /// <summary>كل الطلبات بالترتيب — وضع "من غير دمج" بيعمل طلب لكل ملف.</summary>
        public List<MergeRequest> Requests { get; } = new();

        /// <summary>لو اتحدد، بيرجّع العدد ده بدل عدد الملفات — عشان نختبر تمرير عدد الصفحات.</summary>
        public int? PageCount { get; set; }

        /// <summary>أسامي ملفات مصدر المفروض الدمج يفشل معاها (محاكاة ملف تالف).</summary>
        public List<string> FailFor { get; } = new();

        public MergeResult Merge(MergeRequest request)
        {
            Requests.Add(request);

            string? bad = request.InputFiles.FirstOrDefault(
                f => FailFor.Any(name => Path.GetFileName(f) == name));

            if (bad is not null)
            {
                return MergeResult.Failed($"الملف \"{Path.GetFileName(bad)}\" تالف أو مش PDF سليم.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            File.WriteAllText(request.OutputPath, "merged");

            return MergeResult.Succeeded(
                $"تم دمج {request.InputFiles.Count} ملف.",
                PageCount ?? request.InputFiles.Count);
        }
    }

    private sealed class FakePdfInfoService : IPdfInfoService
    {
        public int? PageCount { get; set; }

        /// <summary>null = مقدرناش نقرا المقاس، فالمعاينة بتفضل على A4.</summary>
        public (double Width, double Height)? PageSize { get; set; }

        public int? TryGetPageCount(string filePath) => PageCount;

        public (double Width, double Height)? TryGetPageSize(string filePath) => PageSize;
    }

    private sealed class FakePrintService : IPdfPrintService
    {
        private readonly Lock _gate = new();

        public List<PrintJob> Jobs { get; } = new();

        public Task<PrintOutcome> PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Jobs.Add(job);
            }

            return Task.FromResult(
                PrintOutcome.Delivered($"[نجاح] {job.Copies} نسخة إلى {job.PrinterName}"));
        }
    }
}
