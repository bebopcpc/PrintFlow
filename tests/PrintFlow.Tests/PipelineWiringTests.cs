using PrintFlow.Application;
using PrintFlow.Domain;
using PrintFlow.Presentation;

namespace PrintFlow.Tests;

/// <summary>
/// التستات دي بتختبر **السلك** مش الحسابات.
///
/// ليه ملف لوحده: في 1.6.1 كان الكتيّب حساباته مضبوطة ومتختبرة بالكامل
/// (<see cref="BookletImpositionTests"/>) والمُجمّع بيرسمه صح
/// (<see cref="PdfSlideComposerTests"/>) — ومع ذلك الميزة مكانتش شغالة خالص،
/// لأن الـ ViewModel كان بيسأل <c>SlidesPerSheet &lt;= 1</c> بنفسه فمكانش
/// بينده المُجمّع أصلًا.
///
/// يعني ٤٤٢ تست كانوا ناجحين والميزة واقفة. الفجوة كانت: مفيش تست بيسأل
/// "هل الطلب وصل للخدمة الصح؟". دي الفجوة دي.
/// </summary>
public class PipelineWiringTests : IDisposable
{
    private readonly string _folder;

    public PipelineWiringTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PrintFlowWiring_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // تنضيف بعد التست
        }

        GC.SuppressFinalize(this);
    }

    // ══════════ الكتيّب ══════════

    [Fact]
    public async Task Booklet_Alone_Reaches_The_Composer()
    {
        // ده الباج بالظبط: كتيّب متعلّم عليه، وعدد الشرائح ١ (الافتراضي)
        var composer = new FakeSlideComposer();
        var vm = Build(composer: composer);

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.BookletMode = true;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Single(composer.Requests);
        Assert.True(composer.Requests[0].Booklet);
    }

    [Fact]
    public async Task Booklet_Start_Reaches_The_Composer()
    {
        var composer = new FakeSlideComposer();
        var vm = Build(composer: composer);

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.BookletMode = true;
        vm.Settings.BookletStart = BookletStart.Left;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(BookletStart.Left, composer.Requests[0].BookletStart);
    }

    [Fact]
    public async Task No_Booklet_And_One_Slide_Skips_The_Composer_Entirely()
    {
        // الحالة الشائعة لازم تفضل مرحلة واحدة من غير أي ملف مؤقت
        var composer = new FakeSlideComposer();
        var merge = new RecordingMergeService();
        var vm = Build(composer: composer, merge: merge);

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Empty(composer.Requests);
        Assert.Single(merge.Requests);
        Assert.DoesNotContain(".stage", merge.Requests[0].OutputPath);
    }

    [Fact]
    public async Task Slides_Reach_The_Composer()
    {
        var composer = new FakeSlideComposer();
        var vm = Build(composer: composer);

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.SlidesPerSheet = 4;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Single(composer.Requests);
        Assert.Equal(4, composer.Requests[0].SlidesPerSheet);
    }

    // ══════════ المقياس ══════════

    [Fact]
    public async Task Hundred_Percent_Skips_The_Scaler()
    {
        var scaler = new FakePageScaler();
        var vm = Build(scaler: scaler);

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.ScalePercent = 100;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Empty(scaler.Requests);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(90)]
    [InlineData(140)]
    public async Task Other_Percentages_Reach_The_Scaler(int percent)
    {
        var scaler = new FakePageScaler();
        var vm = Build(scaler: scaler);

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.ScalePercent = percent;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Single(scaler.Requests);
        Assert.Equal(percent, scaler.Requests[0].Percent);
    }

    [Fact]
    public async Task Scaling_Runs_After_The_Composer()
    {
        // الترتيب مهم: المقياس على **الورقة النهائية**. لو اشتغل قبل التجميع
        // كان هيصغّر كل شريحة لوحدها وبعدين التجميع يكبرها تاني.
        var composer = new FakeSlideComposer();
        var scaler = new FakePageScaler();
        var vm = Build(composer: composer, scaler: scaler);

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.SlidesPerSheet = 4;
        vm.Settings.ScalePercent = 80;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Single(scaler.Requests);
        Assert.Equal(composer.Requests[0].OutputPath, scaler.Requests[0].InputPath);
    }

    // ══════════ حذف الصفحات ══════════

    [Fact]
    public async Task Pages_To_Delete_Reach_The_Merge_Service()
    {
        var merge = new RecordingMergeService();
        var vm = Build(merge: merge);

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.DeletePages = true;
        vm.Settings.PagesToDelete = "1,3-5";
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal("1,3-5", merge.Requests[0].PagesToDelete);
    }

    [Fact]
    public async Task Unchecked_Box_Means_No_Deletion_Even_With_Text_Left_Behind()
    {
        // المستخدم كتب أرقام وبعدين شال العلامة — النص بيفضل في الخانة.
        // من غير الشرط ده كان هيتشال صفحات وهو فاكر إنه قفل الخيار.
        var merge = new RecordingMergeService();
        var vm = Build(merge: merge);

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.DeletePages = false;
        vm.Settings.PagesToDelete = "1,3-5";
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Null(merge.Requests[0].PagesToDelete);
    }

    [Fact]
    public async Task Deletion_Never_Runs_Twice_On_The_Composed_Sheets()
    {
        // أخطر حالة في السلسلة كلها: لو الحذف اتكرر بعد التجميع، هيشيل
        // **ورق مجمّع** بأرقام المستند الأصلي.
        var merge = new RecordingMergeService();
        var vm = Build(merge: merge, composer: new FakeSlideComposer());

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.DeletePages = true;
        vm.Settings.PagesToDelete = "1";
        vm.Settings.SlidesPerSheet = 4;
        vm.Settings.NumberPagesPerFile = true;
        vm.App.NumberWholeSheetInsteadOfSlide = true;   // بيجبر مرحلة إضافات بعد التجميع
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.True(merge.Requests.Count >= 2, "المفروض في مرحلتين دمج: قبل التجميع وبعده");
        Assert.Equal("1", merge.Requests[0].PagesToDelete);

        foreach (var request in merge.Requests.Skip(1))
        {
            Assert.Null(request.PagesToDelete);
        }
    }

    // ══════════ وضع "من غير دمج" ══════════

    [Fact]
    public async Task Without_Merging_Slides_Still_Run()
    {
        // الشرط القديم كان بيسأل عن الإضافات بس، فالوضع ده + شرائح كان
        // بيعدّي الملفات زي ما هي
        var composer = new FakeSlideComposer();
        var vm = Build(composer: composer);

        vm.AddFiles([MakeFile("a.pdf"), MakeFile("b.pdf")]);
        vm.Settings.MergeFiles = false;
        vm.Settings.SlidesPerSheet = 2;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(2, composer.Requests.Count);
    }

    [Fact]
    public async Task Without_Merging_Deletion_Still_Runs()
    {
        var merge = new RecordingMergeService();
        var vm = Build(merge: merge);

        vm.AddFiles([MakeFile("a.pdf"), MakeFile("b.pdf")]);
        vm.Settings.MergeFiles = false;
        vm.Settings.DeletePages = true;
        vm.Settings.PagesToDelete = "1";
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(2, merge.Requests.Count);
        Assert.All(merge.Requests, r => Assert.Equal("1", r.PagesToDelete));
    }

    [Fact]
    public async Task Without_Merging_Scale_Still_Runs()
    {
        var scaler = new FakePageScaler();
        var vm = Build(scaler: scaler);

        vm.AddFiles([MakeFile("a.pdf"), MakeFile("b.pdf")]);
        vm.Settings.MergeFiles = false;
        vm.Settings.ScalePercent = 75;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(2, scaler.Requests.Count);
    }

    [Fact]
    public async Task Without_Merging_And_Nothing_Asked_Passes_Files_Through()
    {
        // الجانب التاني من نفس الشرط: من غير أي شغل مطلوب، الملفات الأصلية
        // بتتطبع زي ما هي — من غير إعادة كتابة ومن غير ضياع جودة
        var merge = new RecordingMergeService();
        var vm = Build(merge: merge, composer: new FakeSlideComposer(), scaler: new FakePageScaler());

        vm.AddFiles([MakeFile("a.pdf"), MakeFile("b.pdf")]);
        vm.Settings.MergeFiles = false;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Empty(merge.Requests);
    }

    // ══════════ الملفات المؤقتة ══════════

    [Fact]
    public async Task Intermediate_Files_Are_Cleaned_Up()
    {
        var vm = Build(composer: new FakeSlideComposer(), scaler: new FakePageScaler());

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.SlidesPerSheet = 4;
        vm.Settings.ScalePercent = 80;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        string temp = Path.Combine(Path.GetTempPath(), "PrintFlow");

        Assert.Empty(Directory.Exists(temp)
            ? Directory.GetFiles(temp, "*.stage*.pdf")
            : []);
    }

    [Fact]
    public async Task A_Failing_Stage_Names_Itself()
    {
        var composer = new FakeSlideComposer { Fail = true };
        var vm = Build(composer: composer);

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.SlidesPerSheet = 4;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Contains(vm.Log, line => line.Contains("تجميع الشرائح"));
    }

    // ══════════ تحويل الصور ══════════

    [Fact]
    public void An_Image_Is_Converted_And_Added_As_Pdf()
    {
        var converter = new FakeImageConverter();
        var vm = Build(converter: converter);

        int added = vm.AddFiles([MakeFile("فاتورة.jpg")]);

        Assert.Equal(1, added);
        Assert.Single(converter.Requests);
        Assert.EndsWith(".pdf", vm.Files[0].FullPath);
    }

    [Fact]
    public void The_List_Shows_The_Original_Image_Name()
    {
        // المستخدم رمى "فاتورة.jpg" — مش المفروض يدوّر عليها في القايمة
        // تحت اسم تاني
        var vm = Build(converter: new FakeImageConverter());

        vm.AddFiles([MakeFile("فاتورة.jpg")]);

        Assert.Contains("فاتورة.jpg", vm.Files[0].DisplayText);
        Assert.True(vm.Files[0].WasConverted);
    }

    [Fact]
    public void The_Same_Image_Twice_Is_Added_Once()
    {
        // من غير تتبّع المصدر، كل تحويل بياخد اسم فريد فالاتنين كانوا هيعدّوا
        var converter = new FakeImageConverter();
        var vm = Build(converter: converter);

        string image = MakeFile("a.jpg");

        vm.AddFiles([image]);
        vm.AddFiles([image]);

        Assert.Single(vm.Files);
        Assert.Single(converter.Requests);
    }

    [Fact]
    public void A_Pdf_Is_Not_Sent_To_The_Converter()
    {
        var converter = new FakeImageConverter();
        var vm = Build(converter: converter);

        vm.AddFiles([MakeFile("a.pdf")]);

        Assert.Empty(converter.Requests);
        Assert.False(vm.Files[0].WasConverted);
    }

    [Fact]
    public void Office_Files_Are_Refused_By_Name_Not_Silently()
    {
        var vm = Build();

        int added = vm.AddFiles([MakeFile("عرض.pptx")]);

        Assert.Equal(0, added);
        Assert.Contains("عرض.pptx", vm.StatusText);
    }

    [Fact]
    public void Unsupported_Images_Say_Which_Formats_Do_Work()
    {
        var vm = Build();

        vm.AddFiles([MakeFile("scan.tiff")]);

        Assert.Contains("scan.tiff", vm.StatusText);
        Assert.Contains("JPG", vm.StatusText);
    }

    [Fact]
    public void A_Failed_Conversion_Does_Not_Add_The_File()
    {
        var vm = Build(converter: new FakeImageConverter { Fail = true });

        int added = vm.AddFiles([MakeFile("bad.jpg")]);

        Assert.Equal(0, added);
        Assert.Empty(vm.Files);
    }

    [Fact]
    public void Images_Without_A_Converter_Are_Not_Added()
    {
        // الخدمة مش موجودة (تست قديم مثلًا) — مانضيفش صورة السلسلة
        // مش هتعرف تقراها
        var vm = Build();

        Assert.Equal(0, vm.AddFiles([MakeFile("a.jpg")]));
    }

    [Fact]
    public void A_Converted_Image_Goes_Through_The_Whole_Pipeline()
    {
        // أهم نتيجة من التحويل وقت التحميل: الصورة بتاخد ترقيم وعلامة مائية
        // وتجميع زي أي PDF، من غير أي كود خاص بالصور في السلسلة
        var merge = new RecordingMergeService();
        var composer = new FakeSlideComposer();
        var vm = Build(merge: merge, composer: composer, converter: new FakeImageConverter());

        vm.AddFiles([MakeFile("a.jpg"), MakeFile("b.jpg")]);
        vm.Settings.SlidesPerSheet = 2;
        vm.Settings.PrintDirectlyAfterProcessing = false;

        vm.ProcessCommand.ExecuteAsync().GetAwaiter().GetResult();

        Assert.Equal(2, merge.Requests[0].InputFiles.Count);
        Assert.All(merge.Requests[0].InputFiles, f => Assert.EndsWith(".pdf", f));
        Assert.Single(composer.Requests);
    }

    // ══════════ توزيع الشغل على المكن ══════════

    /// <summary>بيجهّز فيو موديل فيه ملفات متعالجة وطابعات مختارة.</summary>
    private async Task<(MainViewModel Vm, StubPrintService Printer)> ReadyToPrint(
        int documentCount, int printerCount, int pagesEach = 40)
    {
        var printers = new StubPrinterRepository(printerCount);
        var printService = new StubPrintService();

        var vm = new MainViewModel(
            printers,
            new RecordingMergeService { PageCount = pagesEach },
            printService,
            pdfInfo: null,
            slideComposer: null,
            pageScaler: null,
            imageConverter: null);

        vm.AddFiles(Enumerable.Range(1, documentCount).Select(i => MakeFile($"ملزمة{i}.pdf")).ToArray());

        vm.Settings.MergeFiles = false;              // عشان كل ملزمة تفضل لوحدها
        vm.Settings.NumberPagesPerFile = true;       // أي معالجة عشان الملفات تتكتب
        vm.Settings.PrintDirectlyAfterProcessing = false;
        vm.Settings.UseMultiplePrinters = true;

        await vm.RefreshPrintersAsync();

        foreach (var p in vm.Printers)
        {
            p.IsSelected = true;
        }

        await vm.ProcessCommand.ExecuteAsync();

        return (vm, printService);
    }

    [Fact]
    public async Task Fifty_Booklets_Spread_Across_Ten_Machines()
    {
        // ده الطلب بالنص: ٥٠ ملزمة على ١٠ مكن بضغطة زرار واحدة
        var (vm, printer) = await ReadyToPrint(documentCount: 50, printerCount: 10);

        vm.Settings.DistributeCopies = true;

        await vm.PrintCommand.ExecuteAsync();

        Assert.Equal(10, printer.Jobs.Select(j => j.PrinterName).Distinct().Count());
        Assert.Equal(50, printer.Jobs.Count);
        Assert.All(
            printer.Jobs.GroupBy(j => j.PrinterName),
            group => Assert.Equal(5, group.Count()));
    }

    [Fact]
    public async Task Every_Booklet_Is_Printed_Exactly_Once()
    {
        // أخطر عطل ممكن: ملزمة تضيع فالزبون ياخد ناقص، أو تتكرر فالورق يضيع
        var (vm, printer) = await ReadyToPrint(documentCount: 50, printerCount: 10);

        vm.Settings.DistributeCopies = true;

        await vm.PrintCommand.ExecuteAsync();

        Assert.Equal(50, printer.Jobs.Select(j => j.FilePath).Distinct().Count());
        Assert.All(printer.Jobs, job => Assert.Equal(1, job.Copies));
    }

    [Fact]
    public async Task Distribution_Carries_The_Copies_Too()
    {
        // ٤ ملازم × ٥ نسخ = ٢٠ وحدة شغل على ٤ مكن = ٥ لكل مكنة
        var (vm, printer) = await ReadyToPrint(documentCount: 4, printerCount: 4);

        vm.Settings.DistributeCopies = true;
        vm.Settings.TotalCopies = 5;

        await vm.PrintCommand.ExecuteAsync();

        foreach (var group in printer.Jobs.GroupBy(j => j.FilePath))
        {
            Assert.Equal(5, group.Sum(j => j.Copies));
        }

        Assert.All(
            printer.Jobs.GroupBy(j => j.PrinterName),
            group => Assert.Equal(5, group.Sum(j => j.Copies)));
    }

    [Fact]
    public async Task Without_Distribution_Every_Machine_Prints_Everything()
    {
        // السلوك القديم لازم يفضل زي ما هو لما التوزيع يبقى مقفول
        var (vm, printer) = await ReadyToPrint(documentCount: 4, printerCount: 3);

        vm.Settings.DistributeCopies = false;
        vm.Settings.TotalCopies = 2;

        await vm.PrintCommand.ExecuteAsync();

        Assert.Equal(12, printer.Jobs.Count);   // ٤ ملازم × ٣ مكن
        Assert.All(printer.Jobs, job => Assert.Equal(2, job.Copies));
    }

    [Fact]
    public async Task Each_Job_Carries_Its_Own_Page_Count_Not_The_Batch_Total()
    {
        // مهلة الانتظار بتتحسب من عدد الصفحات. لو بعتنا إجمالي الدفعة مع
        // كل جوب، ملزمة ٤٠ صفحة كانت هتاخد مهلة ٢٠٠٠ صفحة.
        var (vm, printer) = await ReadyToPrint(documentCount: 5, printerCount: 2, pagesEach: 40);

        vm.Settings.DistributeCopies = true;

        await vm.PrintCommand.ExecuteAsync();

        Assert.All(printer.Jobs, job => Assert.Equal(40, job.PageCount));
    }

    [Fact]
    public async Task Idle_Machines_Are_Named_In_The_Log()
    {
        // ٣ ملازم على ١٠ مكن — اللي في المطبعة لازم يعرف مين قاعد فاضي
        var (vm, _) = await ReadyToPrint(documentCount: 3, printerCount: 10);

        vm.Settings.DistributeCopies = true;

        await vm.PrintCommand.ExecuteAsync();

        Assert.Contains(vm.Log, line => line.Contains("ماخدتش شغل"));
    }

    [Fact]
    public async Task The_Split_Is_Written_To_The_Log_Before_Printing()
    {
        var (vm, _) = await ReadyToPrint(documentCount: 20, printerCount: 4);

        vm.Settings.DistributeCopies = true;

        await vm.PrintCommand.ExecuteAsync();

        Assert.Contains(vm.Log, line => line.Contains("التوزيع:"));
    }

    [Fact]
    public async Task One_Machine_Selected_Means_No_Distribution()
    {
        var (vm, printer) = await ReadyToPrint(documentCount: 6, printerCount: 1);

        vm.Settings.DistributeCopies = true;
        vm.Settings.TotalCopies = 3;

        await vm.PrintCommand.ExecuteAsync();

        Assert.Equal(6, printer.Jobs.Count);
        Assert.All(printer.Jobs, job => Assert.Equal(3, job.Copies));
    }

    // ══════════ وصف التوزيع قبل الضغط ══════════

    [Fact]
    public async Task The_Merge_Trap_Is_Called_Out_Before_Printing()
    {
        // أهم تحذير في الميزة كلها: "وزّع الـ ٥٠ ملزمة" + الدمج شغّال
        // = مستند واحد، والتوزيع مش هيعمل اللي المستخدم متوقعه
        var vm = Build(printers: new StubPrinterRepository(5));
        await vm.RefreshPrintersAsync();

        vm.AddFiles([MakeFile("a.pdf"), MakeFile("b.pdf"), MakeFile("c.pdf")]);
        vm.Settings.MergeFiles = true;
        vm.Settings.UseMultiplePrinters = true;
        vm.Settings.DistributeCopies = true;

        Assert.True(vm.DistributionIsWarning);
        Assert.Contains("اقفل الدمج", vm.DistributionSummary);
    }

    [Fact]
    public async Task Turning_Merge_Off_Clears_The_Warning()
    {
        var vm = Build(printers: new StubPrinterRepository(5));
        await vm.RefreshPrintersAsync();

        foreach (var p in vm.Printers) { p.IsSelected = true; }

        vm.AddFiles([MakeFile("a.pdf"), MakeFile("b.pdf")]);
        vm.Settings.UseMultiplePrinters = true;
        vm.Settings.DistributeCopies = true;
        vm.Settings.MergeFiles = true;

        Assert.True(vm.DistributionIsWarning);

        vm.Settings.MergeFiles = false;

        Assert.False(vm.DistributionIsWarning);
    }

    [Fact]
    public async Task One_File_With_Merge_On_Is_Not_A_Trap()
    {
        // ملف واحد + دمج = مفيش حاجة تضيع، فمفيش داعي نخوّف المستخدم
        var vm = Build(printers: new StubPrinterRepository(3));
        await vm.RefreshPrintersAsync();

        foreach (var p in vm.Printers) { p.IsSelected = true; }

        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.MergeFiles = true;
        vm.Settings.UseMultiplePrinters = true;
        vm.Settings.DistributeCopies = true;

        Assert.False(vm.DistributionIsWarning);
    }

    [Fact]
    public async Task Selecting_A_Machine_Updates_The_Split_Immediately()
    {
        // المستخدم بيعلّم على مكنة ولازم الرقم يتحرك قدامه، مش بعد ما يضغط
        var vm = Build(printers: new StubPrinterRepository(4));
        await vm.RefreshPrintersAsync();

        vm.AddFiles([MakeFile("a.pdf"), MakeFile("b.pdf")]);
        vm.Files[0].PageCount = 100;
        vm.Files[1].PageCount = 100;
        vm.Settings.MergeFiles = false;
        vm.Settings.DistributeCopies = true;

        // من ١.٩.٦ الطابعة الافتراضية بتبقى معلّمة من الأول، فالقايمة
        // عمرها ما بتفتح فاضية. البداية بقت مكنة واحدة مش صفر.
        Assert.Contains("1 مكنة", vm.DistributionSummary);

        vm.Printers[0].IsSelected = true;
        vm.Printers[1].IsSelected = true;

        Assert.Contains("2 مكنة", vm.DistributionSummary);
        Assert.Contains("100", vm.DistributionSummary);   // ٢٠٠ صفحة ÷ ٢ مكنة
    }

    [Fact]
    public void No_Distribution_Means_No_Summary()
    {
        var vm = Build();

        vm.AddFiles([MakeFile("a.pdf")]);

        // التوزيع بقى مفتوح افتراضيًا في ١.٩.٦، فالتست ده لازم يقفله
        // بإيده عشان يختبر اللي اسمه بيقوله فعلًا.
        vm.Settings.DistributeCopies = false;

        Assert.Equal("", vm.DistributionSummary);
    }

    // ══════════ الوصف اللي بيظهر للمستخدم ══════════

    [Fact]
    public void Delete_Summary_Is_Empty_When_The_Box_Is_Off()
    {
        var vm = Build();
        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Settings.PagesToDelete = "1-3";

        Assert.Equal("", vm.PagesToDeleteSummary);
    }

    [Fact]
    public void Delete_Summary_Updates_As_The_User_Types()
    {
        var vm = Build();
        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Files[0].PageCount = 10;
        vm.Settings.DeletePages = true;

        vm.Settings.PagesToDelete = "1,2";

        Assert.Contains("2", vm.PagesToDeleteSummary);
        Assert.Contains("8", vm.PagesToDeleteSummary);
    }

    [Fact]
    public void Delete_Summary_Warns_When_Nothing_Is_Understood()
    {
        var vm = Build();
        vm.AddFiles([MakeFile("a.pdf")]);
        vm.Files[0].PageCount = 10;
        vm.Settings.DeletePages = true;

        vm.Settings.PagesToDelete = "الصفحة الأولى";

        Assert.Contains("مفيش أرقام", vm.PagesToDeleteSummary);
    }

    [Fact]
    public void Delete_Summary_Warns_Against_The_Shortest_File_Not_The_Longest()
    {
        // ملف ٢٠ صفحة وملف ٣ صفحات، والمستخدم كتب "1-3".
        // على الملف الكبير ده حذف عادي، وعلى الصغير ده مسح للملف كله.
        // التحذير لازم يطلع — الخطر بيتقاس على أضعف حالة مش على المتوسط.
        var vm = Build();
        vm.AddFiles([MakeFile("big.pdf"), MakeFile("small.pdf")]);
        vm.Files[0].PageCount = 20;
        vm.Files[1].PageCount = 3;
        vm.Settings.DeletePages = true;

        vm.Settings.PagesToDelete = "1-3";

        Assert.Contains("كل صفحات الملف", vm.PagesToDeleteSummary);
    }

    [Fact]
    public void Scale_Summary_Starts_At_The_Neutral_Message()
    {
        Assert.Contains("الطبيعي", Build().ScaleSummary);
    }

    [Fact]
    public void Scale_Summary_Distinguishes_Shrinking_From_Cropping()
    {
        var vm = Build();

        vm.Settings.ScalePercent = 85;
        Assert.Contains("هامش أبيض", vm.ScaleSummary);

        vm.Settings.ScalePercent = 150;
        Assert.Contains("هيتقص", vm.ScaleSummary);
    }

    // ══════════ الاستقبال من بره البرنامج ══════════

    [Fact]
    public void An_Arriving_Job_Lands_In_The_File_List()
    {
        var watcher = new FakeIncomingWatcher();
        var vm = Build(incoming: watcher);

        string job = MakeFile("job_20260824_145203_001.pdf");
        watcher.Deliver(new IncomingFile(job, IncomingSource.VirtualPrinter, 1234));

        Assert.Single(vm.Files);
        Assert.Contains(vm.Log, l => l.Contains("[استقبال]") && l.Contains("طابعة"));
    }

    [Fact]
    public void An_Arriving_Job_Goes_Through_The_Same_Checks_As_A_Manual_Load()
    {
        // ملف بصيغة مش مدعومة وصل من المجلد المراقَب — لازم يترفض
        // بنفس القواعد بالظبط، مش يعدي لأنه جاي من مصدر "موثوق"
        var watcher = new FakeIncomingWatcher();
        var vm = Build(incoming: watcher);

        watcher.Deliver(new IncomingFile(MakeFile("notes.txt"), IncomingSource.HotFolder, 10));

        Assert.Empty(vm.Files);
        Assert.Contains(vm.Log, l => l.Contains("مادخلش القايمة"));
    }

    [Fact]
    public void The_Same_Job_Arriving_Twice_Is_Added_Once()
    {
        var watcher = new FakeIncomingWatcher();
        var vm = Build(incoming: watcher);

        string job = MakeFile("job.pdf");
        watcher.Deliver(new IncomingFile(job, IncomingSource.VirtualPrinter, 100));
        watcher.Deliver(new IncomingFile(job, IncomingSource.VirtualPrinter, 100));

        Assert.Single(vm.Files);
    }

    [Fact]
    public void Nothing_Prints_By_Itself_Unless_Asked()
    {
        // ورق بيطلع من غير ما حد ضغط حاجة ده سلوك مخيف في مطبعة
        var watcher = new FakeIncomingWatcher();
        var printer = new StubPrintService();
        var vm = new MainViewModel(
            new StubPrinterRepository(1), new RecordingMergeService(), printer,
            incomingWatcher: watcher);

        watcher.Deliver(new IncomingFile(MakeFile("job.pdf"), IncomingSource.VirtualPrinter, 100));

        Assert.False(vm.App.PrintReceivedAutomatically);
        Assert.Empty(printer.Jobs);
    }

    [Fact]
    public void Turning_Reception_On_Starts_The_Watcher()
    {
        var watcher = new FakeIncomingWatcher();
        var vm = Build(incoming: watcher);

        vm.App.ReceiveFromVirtualPrinter = true;

        Assert.True(watcher.Running);
        Assert.Contains("PrintFlow", vm.ReceptionStatus);
    }

    [Fact]
    public void Turning_Everything_Off_Stops_The_Watcher()
    {
        var watcher = new FakeIncomingWatcher();
        var vm = Build(incoming: watcher);

        vm.App.ReceiveFromVirtualPrinter = true;
        vm.App.ReceiveFromVirtualPrinter = false;

        Assert.False(watcher.Running);
        Assert.Contains("مقفول", vm.ReceptionStatus);
    }

    [Fact]
    public void A_Hot_Folder_Alone_Is_Enough_To_Start_Watching()
    {
        var watcher = new FakeIncomingWatcher();
        var vm = Build(incoming: watcher);

        vm.App.HotFolder = _folder;

        Assert.True(watcher.Running);
        Assert.Equal(_folder, watcher.HotFolder);
    }

    [Fact]
    public void The_Watcher_Reports_Land_In_The_Log()
    {
        var watcher = new FakeIncomingWatcher();
        var vm = Build(incoming: watcher);

        watcher.Report("[تنبيه] حاجة حصلت");

        Assert.Contains(vm.Log, l => l.Contains("حاجة حصلت"));
    }

    // ══════════ مساعدات ══════════

    private MainViewModel Build(
        RecordingMergeService? merge = null,
        FakeSlideComposer? composer = null,
        FakePageScaler? scaler = null,
        FakeImageConverter? converter = null,
        StubPrinterRepository? printers = null,
        FakeIncomingWatcher? incoming = null)
        => new(
            printers ?? new StubPrinterRepository(),
            merge ?? new RecordingMergeService(),
            new StubPrintService(),
            slideComposer: composer,
            pageScaler: scaler,
            imageConverter: converter,
            incomingWatcher: incoming);

    private string MakeFile(string name)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, "pdf");
        return path;
    }

    private sealed class RecordingMergeService : IPdfMergeService
    {
        public List<MergeRequest> Requests { get; } = new();

        /// <summary>عدد صفحات المستند الناتج — التوزيع بيتحسب بيه.</summary>
        public int PageCount { get; init; } = 4;

        public MergeResult Merge(MergeRequest request)
        {
            Requests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            File.WriteAllText(request.OutputPath, "merged");
            return MergeResult.Succeeded("تم الدمج.", PageCount);
        }
    }

    private sealed class FakeSlideComposer : IPdfSlideComposer
    {
        public List<SlideRequest> Requests { get; } = new();

        public bool Fail { get; init; }

        public MergeResult Compose(SlideRequest request)
        {
            Requests.Add(request);

            if (Fail)
            {
                return MergeResult.Failed("مقدرناش نجمّع.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            File.WriteAllText(request.OutputPath, "composed");
            return MergeResult.Succeeded("اتجمّعت.", 1);
        }
    }

    private sealed class FakePageScaler : IPdfPageScaler
    {
        public List<ScaleRequest> Requests { get; } = new();

        public MergeResult Scale(ScaleRequest request)
        {
            Requests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            File.WriteAllText(request.OutputPath, "scaled");
            return MergeResult.Succeeded("اتغيّر المقياس.", 1);
        }
    }

    private sealed class FakeImageConverter : IImageToPdfConverter
    {
        public List<ImageConvertRequest> Requests { get; } = new();

        public bool Fail { get; init; }

        public MergeResult Convert(ImageConvertRequest request)
        {
            Requests.Add(request);

            if (Fail)
            {
                return MergeResult.Failed("الصورة تالفة.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            File.WriteAllText(request.OutputPath, "converted");
            return MergeResult.Succeeded("اتحوّلت.", 1);
        }
    }

    private sealed class FakeIncomingWatcher : IIncomingJobWatcher
    {
        public event Action<IncomingFile>? JobArrived;
        public event Action<string>? Reported;

        public bool IsRunning => Running;
        public bool Running { get; private set; }
        public string? HotFolder { get; private set; }

        public void Start(string spoolFolder, string queueFolder, string? hotFolder)
        {
            Running = true;
            HotFolder = hotFolder;
        }

        public void Stop() => Running = false;

        public void Deliver(IncomingFile file) => JobArrived?.Invoke(file);

        public void Report(string line) => Reported?.Invoke(line);
    }

    private sealed class StubPrinterRepository : IPrinterRepository
    {
        private readonly List<Printer> _printers;

        public StubPrinterRepository(int count = 0)
            => _printers = Enumerable.Range(1, count)
                .Select(i => new Printer
                {
                    Name = $"مكنة{i}",
                    Status = PrinterStatus.Ready,
                    Port = $"USB{i:000}",
                    IsDefault = i == 1
                })
                .ToList();

        public Task<List<Printer>> GetPrintersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_printers.ToList());

        public string SendTestPage(string printerName) => "ok";

        public PrinterCapabilities GetCapabilities(string printerName) => new();
    }

    private sealed class StubPrintService : IPdfPrintService
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
