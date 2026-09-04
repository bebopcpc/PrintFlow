using PrintFlow.Application;
using PrintFlow.Domain;
using PrintFlow.Presentation;

namespace PrintFlow.Tests;

/// <summary>
/// إيقاف المعالجة — الفجوة اللي كانت بتخلّي البرنامج يبان واقف.
///
/// ═══ الحالة اللي التستات دي موجودة عشانها ═══
///
/// المستخدم بيحمّل ٥٠ ملف ويدوس «بدء معالجة الملفات». المعالجة بتاخد
/// دقايق (دمج، تجميع، ترقيم، مقياس)، والشاشة بتقول «جاري المعالجة» وخلاص.
/// لو غيّر رأيه — أو لقى إنه ظبط إعداد غلط — مكانش قدامه غير إنه يقفل
/// البرنامج من Task Manager.
///
/// «إيقاف فوري» كان شرطه <c>IsPrinting</c> بس، والتوكن بتاعه بيتولد جوّه
/// الطباعة أصلًا — يعني في مرحلة المعالجة الزرار كان مقفول، **وده كان
/// صح**: لو كان مفتوح كان هيولّع ومايعملش حاجة، وده أوحش.
///
/// دلوقتي فيه توكن للمعالجة، والزرار بيشتغل، والإيقاف بيقف عند أقرب حد
/// آمن — آخر ملف خلص، أو آخر مرحلة في السلسلة.
/// </summary>
public class ProcessCancelTests : IDisposable
{
    private readonly string _folder;

    public ProcessCancelTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PFCancel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* تنضيف */ }
    }

    // ══════════ الزرار ══════════

    /// <summary>
    /// الزرار مقفول والبرنامج فاضي — من غير كده المستخدم بيدوس على
    /// حاجة مالهاش أي أثر.
    /// </summary>
    [Fact]
    public void The_Stop_Button_Is_Closed_When_Nothing_Is_Running()
    {
        var vm = NewViewModel(out _, out _);

        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    /// <summary>ودوسة عليه وهو فاضي مالهاش أثر — مفيش سطر ولا رمية.</summary>
    [Fact]
    public void Pressing_It_With_Nothing_Running_Does_Nothing()
    {
        var vm = NewViewModel(out _, out _);

        vm.CancelCommand.Execute(null);

        Assert.Empty(vm.Log);
    }

    /// <summary>
    /// ⚠ ده الشرط اللي اتغيّر: الزرار بقى بيولّع في مرحلة المعالجة كمان.
    ///
    /// بنمسك الحالة **من جوّه** المعالجة نفسها، لأنها بتخلص قبل ما
    /// التست يقدر يبص عليها من بره.
    /// </summary>
    [Fact]
    public async Task The_Stop_Button_Opens_While_Files_Are_Being_Processed()
    {
        bool openDuringProcessing = false;

        var merge = new PausingMergeService();
        var vm = NewViewModel(out _, merge);

        merge.AfterEachFile = () => openDuringProcessing = vm.CancelCommand.CanExecute(null);

        LoadFiles(vm, "a.pdf", "b.pdf");
        NeedProcessing(vm);
        EachFileOnItsOwn(vm);
        NoAutomaticPrinting(vm);

        await vm.ProcessCommand.ExecuteAsync();

        Assert.True(openDuringProcessing, "الزرار كان لازم يبقى مفتوح والمعالجة ماشية");
    }

    // ══════════ الإيقاف في وضع "كل ملف لوحده" ══════════

    /// <summary>
    /// اللي خلص بيفضل، واللي بعده مايبدأش.
    ///
    /// ═══ ليه القاعدة مش رقم ═══
    ///
    /// أول نسخة من التست ده كانت بتقول "لازم يخلص ملف واحد بالظبط".
    /// ده اتضح إنه **توقيت مش قاعدة**: الإلغاء بيتبعت من ثريد خلفي، وأنهي
    /// ملف بيلحق يخلص قبله بيختلف من جهاز لجهاز. التست عدّى عندي وطلع
    /// فاشل على جهاز تاني — وهو نفس الكود.
    ///
    /// القاعدة الحقيقية اتنين، والاتنين مالهمش دعوة بالتوقيت:
    ///   • كل ملف وصل خدمة المعالجة، موجود في المخرج. مفيش شغل بيتعمل ويتضيّع.
    ///   • الوقفة حصلت قبل آخر ملف. يعني الإيقاف عمل حاجة فعلًا.
    /// </summary>
    [Fact]
    public async Task Stopping_Keeps_Everything_That_Was_Already_Processed()
    {
        var merge = new PausingMergeService();
        var vm = NewViewModel(out _, merge);

        merge.AfterEachFile = () =>
        {
            if (merge.Calls == 1)
            {
                vm.CancelCommand.Execute(null);
            }
        };

        LoadFiles(vm, "a.pdf", "b.pdf", "c.pdf");
        NeedProcessing(vm);
        EachFileOnItsOwn(vm);
        NoAutomaticPrinting(vm);

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(merge.Calls, vm.OutputFileCount);
        Assert.True(merge.Calls < 3, $"المفروض الإيقاف يوقف قبل التلات ملفات، اتعالج {merge.Calls}");
    }

    /// <summary>
    /// وبيقول بالأرقام إيه اللي خلص وإيه اللي لأ.
    ///
    /// «المعالجة اتوقفت» لوحدها مش كفاية — اللي واقف في المطبعة محتاج
    /// يعرف هو عنده كام ملف جاهز عشان يقرر يطبعهم ولا يعيد من الأول.
    /// </summary>
    [Fact]
    public async Task It_Says_How_Many_Finished_And_How_Many_Did_Not()
    {
        var merge = new PausingMergeService();
        var vm = NewViewModel(out _, merge);

        merge.AfterEachFile = () =>
        {
            if (merge.Calls == 1)
            {
                vm.CancelCommand.Execute(null);
            }
        };

        LoadFiles(vm, "a.pdf", "b.pdf", "c.pdf");
        NeedProcessing(vm);
        EachFileOnItsOwn(vm);
        NoAutomaticPrinting(vm);

        await vm.ProcessCommand.ExecuteAsync();

        // الرقم بيتقارن باللي حصل فعلًا، مش برقم مكتوب بالإيد
        Assert.Contains("اتوقفت", vm.StatusText);
        Assert.Contains(vm.Log, line => line.Contains($"خلص {merge.Calls} من 3")
                                     || line.Contains("قبل ما أي ملف يخلص"));
    }

    /// <summary>
    /// ⚠ الإيقاف عمره ما يتقال عنه "فشلت المعالجة".
    ///
    /// الرسالتين مختلفتين تمامًا في المعنى: الفشل بيخلّي المستخدم يدوّر
    /// على ملف تالف، والإيقاف هو اللي هو عمله بإيده.
    /// </summary>
    [Fact]
    public async Task Stopping_Is_Never_Reported_As_A_Failure()
    {
        var merge = new PausingMergeService();
        var vm = NewViewModel(out _, merge);

        merge.BeforeEachFile = () => vm.CancelCommand.Execute(null);

        LoadFiles(vm, "a.pdf", "b.pdf");
        NeedProcessing(vm);
        EachFileOnItsOwn(vm);
        NoAutomaticPrinting(vm);

        await vm.ProcessCommand.ExecuteAsync();

        Assert.DoesNotContain("فشلت", vm.StatusText);
        Assert.Contains("اتوقفت", vm.StatusText);
    }

    // ══════════ الإيقاف في وضع الدمج ══════════

    /// <summary>
    /// الدمج مستند واحد — نُصّه مالوش أي قيمة.
    ///
    /// عشان كده الإيقاف هنا بيسيب المخرج فاضي بدل ما يسلّم للمستخدم ملف
    /// ناقص هو مش عارف إنه ناقص.
    /// </summary>
    [Fact]
    public async Task Stopping_A_Merge_Leaves_No_Half_Document()
    {
        var merge = new PausingMergeService();
        var vm = NewViewModel(out _, merge);

        merge.BeforeEachFile = () => vm.CancelCommand.Execute(null);

        LoadFiles(vm, "a.pdf", "b.pdf");
        NeedProcessing(vm);
        NoAutomaticPrinting(vm);
        vm.Settings.MergeFiles = true;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.Equal(0, vm.OutputFileCount);
        Assert.Contains("اتوقفت", vm.StatusText);
    }

    // ══════════ الطباعة التلقائية ══════════

    /// <summary>
    /// ⚠ اللي وقّف المعالجة مش عايز الملفات اللي خلصت تروح للمكن لوحدها.
    ///
    /// من غير الشرط ده، «الطباعة مباشرة بعد المعالجة» كانت هتاخد اللي
    /// خلص وتطبعه — يعني الإيقاف يطلّع ورق. عكس اللي المستخدم طلبه
    /// بالظبط.
    /// </summary>
    [Fact]
    public async Task Nothing_Is_Printed_After_The_User_Stops()
    {
        var merge = new PausingMergeService();
        var print = new CountingPrintService();
        var vm = NewViewModel(out _, merge, print);

        merge.AfterEachFile = () =>
        {
            if (merge.Calls == 1)
            {
                vm.CancelCommand.Execute(null);
            }
        };

        LoadFiles(vm, "a.pdf", "b.pdf");
        NeedProcessing(vm);
        EachFileOnItsOwn(vm);
        vm.Settings.PrintDirectlyAfterProcessing = true;

        await vm.ProcessCommand.ExecuteAsync();

        // القاعدة هنا حاجة واحدة: مفيش ورق طلع بعد الإيقاف.
        // عدد الملفات اللي خلصت توقيت، مش قاعدة.
        Assert.Equal(0, print.Jobs);
    }

    /// <summary>ومن غير إيقاف، الطباعة التلقائية بتشتغل عادي زي ما كانت.</summary>
    [Fact]
    public async Task Without_Stopping_The_Automatic_Print_Still_Runs()
    {
        var print = new CountingPrintService();
        var vm = NewViewModel(out var printers, new PausingMergeService(), print);

        printers.Printers = [new Printer { Name = "HP", Status = PrinterStatus.Ready, IsDefault = true, Port = "USB001" }];
        await vm.RefreshPrintersAsync();

        LoadFiles(vm, "a.pdf");
        NeedProcessing(vm);
        EachFileOnItsOwn(vm);
        vm.Settings.PrintDirectlyAfterProcessing = true;

        await vm.ProcessCommand.ExecuteAsync();

        Assert.True(print.Jobs > 0, "المفروض الطباعة التلقائية تشتغل لما مفيش إيقاف");
    }

    // ══════════ بعد الإيقاف ══════════

    /// <summary>
    /// البرنامج بيرجع فاضي بعد الإيقاف — مش بيفضل "مشغول" للأبد.
    ///
    /// لو التوكن ماتصفّرش في الـ finally، الزرار كان هيفضل مولّع
    /// والبرنامج مقفول قدام المستخدم.
    /// </summary>
    [Fact]
    public async Task The_Program_Comes_Back_Idle_After_A_Stop()
    {
        var merge = new PausingMergeService();
        var vm = NewViewModel(out _, merge);

        merge.BeforeEachFile = () => vm.CancelCommand.Execute(null);

        LoadFiles(vm, "a.pdf", "b.pdf");
        NeedProcessing(vm);
        EachFileOnItsOwn(vm);
        NoAutomaticPrinting(vm);

        await vm.ProcessCommand.ExecuteAsync();

        Assert.True(vm.IsIdle);
        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    /// <summary>
    /// وينفع يبدأ من الأول على طول — الأوردر التاني مالوش دعوة باللي فات.
    ///
    /// التوكن الملغي بيتصفّر في الـ finally. من غير كده الجولة التانية كانت
    /// هتلاقي نفسها ملغية من قبل ما تبدأ.
    /// </summary>
    [Fact]
    public async Task A_Second_Run_Starts_Clean()
    {
        var merge = new PausingMergeService();
        var vm = NewViewModel(out _, merge);

        merge.BeforeEachFile = () => vm.CancelCommand.Execute(null);

        LoadFiles(vm, "a.pdf", "b.pdf");
        NeedProcessing(vm);
        EachFileOnItsOwn(vm);
        NoAutomaticPrinting(vm);

        await vm.ProcessCommand.ExecuteAsync();

        // الجولة التانية من غير إيقاف
        merge.BeforeEachFile = null;
        merge.AfterEachFile = null;

        await vm.ProcessCommand.ExecuteAsync();

        // الجولة التانية خلصت عادي — مش متأثرة بالإلغاء اللي فات.
        // القاعدة هي "اشتغلت"، مش رقم: عدد الملفات اللي بتلحق تخلص
        // قبل الإلغاء في الجولة الأولى بيختلف من جهاز لجهاز.
        Assert.DoesNotContain("اتوقفت", vm.StatusText);
        Assert.True(vm.OutputFileCount > 0, "الجولة التانية المفروض تطلّع ملفات");
    }

    // ══════════ مساعدات ══════════

    private MainViewModel NewViewModel(
        out FakePrinters printers,
        PausingMergeService? merge = null,
        IPdfPrintService? print = null)
    {
        printers = new FakePrinters();

        return new MainViewModel(
            printers,
            merge ?? new PausingMergeService(),
            print ?? new CountingPrintService(),
            pdfInfo: new FixedPageCount());
    }

    private MainViewModel NewViewModel(out FakePrinters printers, out PausingMergeService merge)
    {
        merge = new PausingMergeService();
        return NewViewModel(out printers, merge);
    }

    private void LoadFiles(MainViewModel vm, params string[] names)
    {
        var paths = new List<string>();

        foreach (string name in names)
        {
            string path = Path.Combine(_folder, name);
            File.WriteAllText(path, "pdf");
            paths.Add(path);
        }

        vm.AddFiles(paths);
    }

    /// <summary>
    /// من غير أي شغل مطلوب، المعالجة بتاخد الطريق المختصر وبتعدّي الملفات
    /// زي ما هي — يعني السلسلة مابتشتغلش والإيقاف مالوش حاجة يوقفها.
    /// </summary>
    private static void NeedProcessing(MainViewModel vm)
    {
        vm.Settings.DeletePages = true;
        vm.Settings.PagesToDelete = "1";
    }

    /// <summary>
    /// ⚠ «دمج وحفظ الملفات» افتراضيها **مفتوحة** في البرنامج.
    ///
    /// أول نسخة من التستات دي مافتكرتش ده، فأربعة منهم كانوا بيمشوا في
    /// طريق الدمج وهما مكتوبين لطريق «كل ملف لوحده» — وطلعوا بيفشلوا
    /// لأسباب مالهاش دعوة باللي بيختبروه.
    ///
    /// عشان كده كل تست هنا بيقول صراحة هو في أنهي طريق.
    /// </summary>
    private static void EachFileOnItsOwn(MainViewModel vm) => vm.Settings.MergeFiles = false;

    /// <summary>
    /// ⚠ «الطباعة مباشرة بعد المعالجة» افتراضيها **مفتوحة** كمان.
    ///
    /// يعني بعد أي معالجة ناجحة بتشتغل طباعة، وبتكتب فوق StatusText —
    /// فالتست اللي بيقرا الرسالة بيلاقي رسالة الطابعات مش رسالة المعالجة.
    ///
    /// كل تست مش بيختبر الطباعة بيقفلها صراحة.
    /// </summary>
    private static void NoAutomaticPrinting(MainViewModel vm) => vm.Settings.PrintDirectlyAfterProcessing = false;

    // ══════════ خدمات مزيفة ══════════

    /// <summary>
    /// دمج مزيف بيدّينا نقطتين نتدخّل فيهم: قبل كل ملف وبعده.
    /// من هناك التست بيدوس «إيقاف» زي المستخدم بالظبط.
    /// </summary>
    private sealed class PausingMergeService : IPdfMergeService
    {
        public int Calls { get; private set; }

        public Action? BeforeEachFile { get; set; }
        public Action? AfterEachFile { get; set; }

        public MergeResult Merge(MergeRequest request)
        {
            BeforeEachFile?.Invoke();

            Calls++;

            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            File.WriteAllText(request.OutputPath, "merged");

            AfterEachFile?.Invoke();

            return MergeResult.Succeeded("تم", Math.Max(1, request.InputFiles.Count));
        }
    }

    private sealed class CountingPrintService : IPdfPrintService
    {
        public int Jobs { get; private set; }

        public Task<PrintOutcome> PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
        {
            Jobs++;
            return Task.FromResult(PrintOutcome.Delivered("اتبعت"));
        }
    }

    private sealed class FakePrinters : IPrinterRepository
    {
        public List<Printer> Printers { get; set; } = new();

        public Task<List<Printer>> GetPrintersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Printers.ToList());

        public string SendTestPage(string printerName) => "ok";

        public PrinterCapabilities GetCapabilities(string printerName) => new();
    }

    private sealed class FixedPageCount : IPdfInfoService
    {
        public int? TryGetPageCount(string filePath) => 4;

        public (double Width, double Height)? TryGetPageSize(string filePath) => null;
    }
}
