using System.Reflection;
using PrintFlow.Application;
using PrintFlow.Domain;
using PrintFlow.Presentation;

namespace PrintFlow.Tests;

/// <summary>
/// الضمان الوحيد اللي مالوش علاج لما يقع: **الأوردر مايتبعتش مرتين.**
///
/// ═══ البلاغ اللي التستات دي اتكتبت بسببه ═══
///
/// في ١.٩.٧ طلبنا ٥٧٠ صفحة وطلع ١١٤٠. معمل الاختبار قفشها (عدّاد ويندوز
/// نفسه، مش لوج البرنامج). السبب مكانش في <see cref="WorkDispatcher"/> —
/// هو كان بيشتغل صح تمامًا. السبب إن **اتنين منه اشتغلوا مع بعض** على نفس
/// الملفات.
///
/// إزاي: «بدء معالجة الملفات» كان بيرجّع IsBusy لـ false وينوّر زرار
/// «طباعة الآن»، وبعدين يبدأ الطباعة التلقائية. المستخدم يشوف البرنامج
/// شكله فاضي فيضغط، فيمشي أوردر تاني جنب الأول.
///
/// الدرس: الحارس اللي على الزرار ليه طريق حواليه. القفل لازم يبقى على
/// الفعل نفسه. التستات دي بتقفل كل باب على الطباعة، واحد واحد.
///
/// كل تست هنا بيطلب **٣٠ نسخة** وبيتأكد إن اللي اتبعت ٣٠ بالظبط.
/// أي رقم أكبر معناه ورق وحبر اتصرفوا على الفاضي.
/// </summary>
public class PrintOnceTests : IDisposable
{
    private const int Copies = 30;

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "pf-once-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* تنضيف */ }
        GC.SuppressFinalize(this);
    }

    // ══════════ الأبواب اللي بتوصّل للطباعة ══════════

    [Fact]
    public async Task Pressing_Print_While_The_Automatic_Print_Runs_Does_Not_Send_It_Twice()
    {
        // ده البلاغ الأصلي بالظبط.
        var (vm, printer) = Build();

        var processing = vm.ProcessCommand.ExecuteAsync();
        await printer.WaitForFirstJob();

        // الزرار المفروض يكون مقفول والأوردر بيتبعت
        Assert.False(vm.PrintCommand.CanExecute(null));

        // وحتى لو اتنفّذ بالغصب، مايعديش
        await vm.PrintCommand.ExecuteAsync();

        await Finish(printer, processing);

        Assert.Equal(Copies, printer.CopiesSent);
    }

    [Fact]
    public async Task Pressing_Print_While_The_Files_Are_Still_Being_Merged_Does_Not_Send_It_Twice()
    {
        // النافذة دي أبكر من اللي فوق بخطوة: الدمج لسه شغّال، يعني
        // IsPrinting لسه false — فالقفل بتاع الطباعة لوحده مابيغطّيهاش.
        // الحارس هنا هو IsBusy. الأوردر اللي فات لسه في _output، فالزرار
        // عنده حاجة يبعتها.
        var (vm, printer, merge) = BuildWithSlowMerge();

        printer.Release();
        await vm.ProcessCommand.ExecuteAsync();   // أوردر أول كامل
        await Drain();
        int afterFirst = printer.CopiesSent;

        merge.Hold();
        var second = vm.ProcessCommand.ExecuteAsync();
        await merge.WaitUntilMerging();

        Assert.True(vm.IsBusy);
        Assert.False(vm.PrintCommand.CanExecute(null));

        await vm.PrintCommand.ExecuteAsync();     // وحتى بالغصب

        merge.Release();
        await second;
        await Drain();

        Assert.Equal(Copies, afterFirst);
        Assert.Equal(Copies * 2, printer.CopiesSent);
    }

    [Fact]
    public async Task Starting_A_Second_Batch_While_One_Is_Printing_Does_Not_Send_It_Twice()
    {
        // الملفات الجاية من المجلد المراقب بتدخل من الباب ده.
        var (vm, printer) = Build();

        var first = vm.ProcessCommand.ExecuteAsync();
        await printer.WaitForFirstJob();

        Assert.False(vm.ProcessCommand.CanExecute(null));
        await vm.ProcessCommand.ExecuteAsync();

        await Finish(printer, first);

        Assert.Equal(Copies, printer.CopiesSent);
    }

    [Fact]
    public async Task Two_Direct_Calls_To_Print_Overlap_And_Only_One_Survives()
    {
        // ده اللي بيختبر القفل الجوّاني لوحده: مفيش زرار ولا أمر في النص،
        // نداء مباشر مرتين. لو القفل ده اتشال، التست ده هو اللي هيقع الأول.
        var (vm, printer) = Build(autoPrint: false);
        await vm.ProcessCommand.ExecuteAsync();

        var print = typeof(MainViewModel).GetMethod(
            "PrintAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var first = (Task)print.Invoke(vm, null)!;
        await printer.WaitForFirstJob();
        var second = (Task)print.Invoke(vm, null)!;

        printer.Release();
        await Task.WhenAll(first, second);
        await Drain();

        Assert.Equal(Copies, printer.CopiesSent);
    }

    [Fact]
    public async Task Reset_Cannot_Pull_The_Work_Out_From_Under_A_Running_Order()
    {
        // Reset بيمسح _output. لو ده حصل والموزّع شغّال، الأرض بتتسحب
        // من تحته في نص الأوردر.
        var (vm, printer) = Build();

        var processing = vm.ProcessCommand.ExecuteAsync();
        await printer.WaitForFirstJob();

        Assert.False(vm.ResetCommand.CanExecute(null));

        vm.ResetCommand.Execute(null);   // وحتى بالغصب
        Assert.NotEmpty(vm.Files);

        await Finish(printer, processing);

        Assert.Equal(Copies, printer.CopiesSent);
    }

    // ══════════ الحالة الطبيعية لسه شغّالة ══════════

    [Fact]
    public async Task One_Press_Still_Prints_The_Whole_Order_Once()
    {
        // القفل مايستاهلش لو منع الطباعة العادية. ده الحارس على الحارس.
        var (vm, printer) = Build();

        printer.Release();
        await vm.ProcessCommand.ExecuteAsync();
        await Drain();

        Assert.Equal(Copies, printer.CopiesSent);
        Assert.Equal(3, printer.PrintersUsed.Count);
    }

    [Fact]
    public async Task After_An_Order_Finishes_The_Buttons_Come_Back()
    {
        // قفل مابيفتحش تاني بيبوّظ يوم الشغل كله.
        var (vm, printer) = Build();

        printer.Release();
        await vm.ProcessCommand.ExecuteAsync();
        await Drain();

        Assert.False(vm.IsPrinting);
        Assert.False(vm.IsBusy);
        Assert.True(vm.PrintCommand.CanExecute(null));
        Assert.True(vm.ProcessCommand.CanExecute(null));
        Assert.True(vm.ResetCommand.CanExecute(null));
    }

    [Fact]
    public async Task Two_Orders_Back_To_Back_Each_Print_Once()
    {
        // يوم شغل حقيقي: أوردر ورا أوردر من غير ما تقفل البرنامج.
        var (vm, printer) = Build();
        printer.Release();

        await vm.ProcessCommand.ExecuteAsync();
        await Drain();
        int afterFirst = printer.CopiesSent;

        await vm.ProcessCommand.ExecuteAsync();
        await Drain();

        Assert.Equal(Copies, afterFirst);
        Assert.Equal(Copies * 2, printer.CopiesSent);
    }

    // ══════════ التجهيز ══════════

    private (MainViewModel, HeldPrintService) Build(
        bool autoPrint = true, IPdfMergeService? merge = null)
    {
        Directory.CreateDirectory(_folder);
        string source = Path.Combine(_folder, "order-" + Guid.NewGuid().ToString("N")[..6] + ".pdf");
        File.WriteAllText(source, "%PDF-1.7");

        var repository = new SteadyPrinters(
            new Printer { Name = "PFLAB-1", IsDefault = true, Status = PrinterStatus.Ready },
            new Printer { Name = "PFLAB-2", Status = PrinterStatus.Ready },
            new Printer { Name = "PFLAB-3", Status = PrinterStatus.Ready });

        var printer = new HeldPrintService();

        var vm = new MainViewModel(
            repository, merge ?? new AlwaysMerges(), printer, pdfInfo: new FixedPageCount(19));

        vm.RefreshPrintersCommand.ExecuteAsync().GetAwaiter().GetResult();

        foreach (var item in vm.Printers)
        {
            item.IsSelected = true;
        }

        vm.Settings.TotalCopies = Copies;
        vm.Settings.DistributeCopies = true;
        vm.Settings.MergeFiles = true;
        vm.Settings.PrintDirectlyAfterProcessing = autoPrint;
        vm.AddFiles([source]);

        return (vm, printer);
    }

    private (MainViewModel, HeldPrintService, HeldMerge) BuildWithSlowMerge()
    {
        var merge = new HeldMerge();
        var (vm, printer) = Build(merge: merge);
        return (vm, printer, merge);
    }

    private static async Task Finish(HeldPrintService printer, Task running)
    {
        printer.Release();
        await running;
        await Drain();
    }

    /// <summary>بيدّي الشغل اللي اتساب ماشي فرصة يخلص قبل ما نعدّ.</summary>
    private static async Task Drain() => await Task.Delay(400);

    // ══════════ البدائل ══════════

    private sealed class SteadyPrinters(params Printer[] printers) : IPrinterRepository
    {
        public Task<List<Printer>> GetPrintersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(printers.ToList());

        public string SendTestPage(string printerName) => "ok";

        public PrinterCapabilities GetCapabilities(string printerName) => new();
    }

    private sealed class AlwaysMerges : IPdfMergeService
    {
        public MergeResult Merge(MergeRequest request)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            File.WriteAllText(request.OutputPath, "merged");
            return MergeResult.Succeeded("تم الدمج", 19);
        }
    }

    /// <summary>دمج بيقف في نص الشغل لحد ما التست يسيبه.</summary>
    private sealed class HeldMerge : IPdfMergeService
    {
        private readonly SemaphoreSlim _reached = new(0);
        private volatile bool _held;

        public void Hold() => _held = true;

        public void Release() => _held = false;

        public async Task WaitUntilMerging()
            => Assert.True(await _reached.WaitAsync(TimeSpan.FromSeconds(10)),
                "الدمج مابدأش — التست مش واقف على اللحظة الصح.");

        public MergeResult Merge(MergeRequest request)
        {
            _reached.Release();

            while (_held)
            {
                Thread.Sleep(10);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            File.WriteAllText(request.OutputPath, "merged");
            return MergeResult.Succeeded("تم الدمج", 19);
        }
    }

    private sealed class FixedPageCount(int pages) : IPdfInfoService
    {
        public int? TryGetPageCount(string filePath) => pages;

        public (double Width, double Height)? TryGetPageSize(string filePath) => null;
    }

    /// <summary>
    /// طابعة بتمسك الجوب في إيدها لحد ما التست يسيبه.
    ///
    /// من غير المسكة دي مفيش تست هنا ليه معنى: الطباعة بتخلص أسرع من ما
    /// التست يقدر يضغط الزرار التاني، فالباب اللي بنختبره مابيتفتحش أصلًا.
    /// </summary>
    private sealed class HeldPrintService : IPdfPrintService
    {
        private readonly Lock _gate = new();
        private readonly List<PrintJob> _jobs = [];
        private readonly SemaphoreSlim _firstJob = new(0);
        private volatile bool _held = true;

        public int CopiesSent
        {
            get { lock (_gate) { return _jobs.Sum(j => j.Copies); } }
        }

        public HashSet<string> PrintersUsed
        {
            get { lock (_gate) { return _jobs.Select(j => j.PrinterName).ToHashSet(); } }
        }

        public void Release() => _held = false;

        public async Task WaitForFirstJob()
            => Assert.True(await _firstJob.WaitAsync(TimeSpan.FromSeconds(10)),
                "مفيش أي جوب اتبعت — التست مش واقف على اللحظة الصح.");

        public async Task<PrintOutcome> PrintAsync(
            PrintJob job, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _jobs.Add(job);
            }

            _firstJob.Release();

            while (_held)
            {
                await Task.Delay(10, CancellationToken.None);
            }

            return PrintOutcome.Delivered($"[نجاح] {job.Copies} نسخة إلى {job.PrinterName}");
        }
    }
}
