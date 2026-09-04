using PrintFlow.Application;
using PrintFlow.Domain;
using PrintFlow.Presentation;

namespace PrintFlow.Tests;

/// <summary>
/// الإعدادات العامة لازم تعيش على القرص من غير ما نستنى إغلاق نضيف،
/// والاستقبال لازم يقول عن نفسه لما يشتغل ولما يقف.
///
/// ═══ البلاغ اللي التستات دي اتكتبت بسببه ═══
///
/// المستخدم علّم على "استقبال من طابعة PrintFlow"، اشتغل قدامه على طول،
/// وبعدين لقاه مقفول تاني — وافتكر إن زرار التصفير الأحمر هو اللي قفله.
///
/// لما اتتبّعنا الكود طلع الزرار الأحمر مالوش دعوة خالص. المشكلة كانت في
/// حتة تانية تمامًا: <c>SaveAppSettings()</c> كانت بتتنده من مكان **واحد**
/// بس في البرنامج كله — <c>Window.Closed</c>.
///
/// يعني أي حاجة تمنع الإغلاق النضيف بتضيّع كل إعداد اتغيّر في الجلسة:
///
///   • الكهربا تقطع في المطبعة
///   • الجهاز يعمل ريستارت لتحديث ويندوز
///   • حد يقفل البرنامج من Task Manager
///   • البرنامج يقع لأي سبب
///
/// ومعظم الإعدادات لو ضاعت المستخدم بيشوفها (لون، حجم، هامش). الاستقبال
/// لأ — بيضيع في صمت تام والبرنامج شكله سليم.
/// </summary>
public class AppSettingsPersistenceTests
{
    /// <summary>ViewModel بحفظ فوري (من غير تأجيل) عشان التستات تفضل متزامنة.</summary>
    private static MainViewModel CreateViewModel(
        RecordingSettingsStore store,
        FakeWatcher? watcher = null)
    {
        var vm = new MainViewModel(
            new StubPrinters(),
            new StubMerge(),
            new StubPrint(),
            settingsStore: store,
            incomingWatcher: watcher);

        vm.SaveDelayMilliseconds = 0;

        // ما نحسبش الحفظ اللي حصل وقت التركيب
        store.Saves.Clear();

        return vm;
    }

    // ══════════ الحفظ اللحظي ══════════

    [Fact]
    public void Ticking_Reception_Is_Written_To_Disk_Immediately()
    {
        var store = new RecordingSettingsStore();
        var vm = CreateViewModel(store);

        vm.App.ReceiveFromVirtualPrinter = true;

        // من غير الحفظ اللحظي، القرص هيفضل فاكر القيمة القديمة لحد ما
        // النافذة تتقفل بالراحة — واللي مش دايمًا بيحصل
        Assert.NotEmpty(store.Saves);
        Assert.True(store.Saves[^1].ReceiveFromVirtualPrinter);
    }

    [Fact]
    public void The_Hot_Folder_Path_Survives_Without_A_Clean_Close()
    {
        var store = new RecordingSettingsStore();
        var vm = CreateViewModel(store);

        vm.App.HotFolder = @"\\SERVER\شغل";

        Assert.Equal(@"\\SERVER\شغل", store.Saves[^1].HotFolder);
    }

    [Fact]
    public void Every_Writable_Preference_Gets_Persisted_On_Change()
    {
        // مش بنعدّد الخصايص بالإيد: أي حاجة تتضاف بكرة تتفحص لوحدها
        var defaults = new AppSettings();

        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            var store = new RecordingSettingsStore();
            var vm = CreateViewModel(store);

            object? changed = Different(property.GetValue(defaults), property.PropertyType);
            property.SetValue(vm.App, changed);

            Assert.True(store.Saves.Count > 0, $"الخاصية {property.Name} اتغيّرت ومحدش حفظ");
            Assert.Equal(changed, property.GetValue(store.Saves[^1]));
        }
    }

    [Fact]
    public async Task Rapid_Changes_Collapse_Into_One_Write()
    {
        // سحب مؤشر "درجة الظهور" بيبعت عشرات الإشعارات في الثانية.
        // من غير التأجيل ده بقى عشرات الكتابات على القرص في الثانية.
        var store = new RecordingSettingsStore();

        var vm = new MainViewModel(
            new StubPrinters(), new StubMerge(), new StubPrint(), settingsStore: store);

        vm.SaveDelayMilliseconds = 60;
        store.Saves.Clear();

        for (int value = 10; value <= 60; value++)
        {
            vm.App.WatermarkOpacityPercent = value;
        }

        await vm.PendingSave;

        Assert.Single(store.Saves);
        Assert.Equal(60, store.Saves[0].WatermarkOpacityPercent);
    }

    [Fact]
    public async Task A_Change_After_A_Quiet_Save_Starts_A_Fresh_One()
    {
        // الحلقة بتخلص بعد ما تكتب. لو مانزّلناش العلم بتاعها صح، أي
        // تغيير بعد كده هيلاقي حلقة "شغّالة" وهمية ويستنى للأبد — يعني
        // كل حاجة تتغيّر بعد أول حفظ تضيع.
        var store = new RecordingSettingsStore();

        var vm = new MainViewModel(
            new StubPrinters(), new StubMerge(), new StubPrint(), settingsStore: store);

        vm.SaveDelayMilliseconds = 30;
        store.Saves.Clear();

        vm.App.WatermarkFontSize = 22;
        await vm.PendingSave;

        vm.App.WatermarkFontSize = 33;
        await vm.PendingSave;

        Assert.Equal(2, store.Saves.Count);
        Assert.Equal(33, store.Saves[^1].WatermarkFontSize);
    }

    [Fact]
    public void A_Broken_Settings_File_Does_Not_Crash_The_Program()
    {
        // المبدأ الثابت في المشروع: مطبعة ماتقفش عشان ملف إعدادات.
        // من غير الحماية دي، قرص مليان وقت الإغلاق = البرنامج يقع وهو
        // بيتقفل، والمستخدم ماياخدش باله إن إعداداته ضاعت أصلًا.
        var store = new ThrowingSettingsStore();

        var vm = new MainViewModel(
            new StubPrinters(), new StubMerge(), new StubPrint(), settingsStore: store);

        vm.SaveDelayMilliseconds = 0;

        vm.App.WatermarkFontSize = 33;
        vm.SaveAppSettings();
        vm.RestoreDefaultAppSettingsCommand.Execute(null);

        Assert.True(store.Attempts > 0);
    }

    // ══════════ الاستقبال بيقول عن نفسه ══════════

    [Fact]
    public void Turning_Reception_On_Is_Announced_In_The_Results_Bar()
    {
        var watcher = new FakeWatcher();
        var vm = CreateViewModel(new RecordingSettingsStore(), watcher);

        vm.App.ReceiveFromVirtualPrinter = true;

        Assert.Contains(vm.Log, line => line.Contains("استقبال") && line.Contains("شغّال"));
        Assert.True(vm.ReceptionIsRunning);
    }

    [Fact]
    public void Turning_Reception_Off_Is_Announced_Too()
    {
        // دي أهم واحدة: لما الاستقبال يقف مفيش أي حاجة في الواجهة بتتغيّر.
        // البرنامج شكله سليم تمامًا والملفات الجاية من بره بتروح في الهوا.
        var watcher = new FakeWatcher();
        var vm = CreateViewModel(new RecordingSettingsStore(), watcher);

        vm.App.ReceiveFromVirtualPrinter = true;
        vm.Log.Clear();

        vm.App.ReceiveFromVirtualPrinter = false;

        Assert.Contains(vm.Log, line => line.Contains("اتقفل"));
        Assert.False(vm.ReceptionIsRunning);
    }

    [Fact]
    public void Starting_Up_With_Reception_Off_Says_Nothing()
    {
        // ده الوضع الطبيعي لمعظم الناس — مش تغيير ومحتاجش سطر
        var watcher = new FakeWatcher();
        var vm = CreateViewModel(new RecordingSettingsStore(), watcher);

        vm.ApplyReceptionSettings();

        Assert.Empty(vm.Log);
        Assert.Contains("مقفول", vm.ReceptionStatus);
    }

    [Fact]
    public void Applying_The_Same_Settings_Twice_Does_Not_Repeat_The_Line()
    {
        var watcher = new FakeWatcher();
        var vm = CreateViewModel(new RecordingSettingsStore(), watcher);

        vm.App.ReceiveFromVirtualPrinter = true;
        int after = vm.Log.Count;

        vm.ApplyReceptionSettings();
        vm.ApplyReceptionSettings();

        Assert.Equal(after, vm.Log.Count);
    }

    [Fact]
    public void Changing_The_Watched_Folder_Is_Announced()
    {
        var watcher = new FakeWatcher();
        var vm = CreateViewModel(new RecordingSettingsStore(), watcher);

        vm.App.HotFolder = @"C:\وارد";
        vm.Log.Clear();

        vm.App.HotFolder = @"\\SERVER\وارد";

        Assert.Contains(vm.Log, line => line.Contains("SERVER"));
    }

    // ══════════ التصفير الأحمر والاستقبال ══════════

    [Fact]
    public void Reset_Keeps_The_Reception_Line_Visible_After_Clearing_The_Log()
    {
        // ده اللي خلّى المستخدم يفتكر إن الزرار قفل الاستقبال: الزرار
        // بيمسح اللوج، وسطر "الاستقبال شغّال" بيتمسح معاه، فالشاشة
        // بتبقى ساكتة تمامًا زي ما يكون مفيش استقبال.
        var watcher = new FakeWatcher();
        var vm = CreateViewModel(new RecordingSettingsStore(), watcher);

        vm.App.ReceiveFromVirtualPrinter = true;

        vm.ResetCommand.Execute(null);

        Assert.Contains(vm.Log, line => line.Contains("استقبال") && line.Contains("شغّال"));
        Assert.True(watcher.Running);
    }

    [Fact]
    public void Reset_Does_Not_Stop_The_Watcher()
    {
        var watcher = new FakeWatcher();
        var vm = CreateViewModel(new RecordingSettingsStore(), watcher);

        vm.App.ReceiveFromVirtualPrinter = true;

        vm.ResetCommand.Execute(null);

        Assert.True(watcher.Running);
        Assert.True(vm.App.ReceiveFromVirtualPrinter);
    }

    [Fact]
    public void Reset_With_Reception_Off_Leaves_The_Log_Empty()
    {
        var watcher = new FakeWatcher();
        var vm = CreateViewModel(new RecordingSettingsStore(), watcher);

        vm.ResetCommand.Execute(null);

        Assert.Empty(vm.Log);
    }

    // ══════════ مساعدات ══════════

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

    private sealed class RecordingSettingsStore : IAppSettingsStore
    {
        public List<AppSettings> Saves { get; } = new();

        public AppSettings Load() => new();

        public void Save(AppSettings settings)
        {
            // لقطة مستقلة، عشان التست يشوف القيمة وقت الحفظ مش دلوقتي
            var copy = new AppSettings();

            foreach (var property in typeof(AppSettings).GetProperties())
            {
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                {
                    property.SetValue(copy, property.GetValue(settings));
                }
            }

            Saves.Add(copy);
        }
    }

    private sealed class ThrowingSettingsStore : IAppSettingsStore
    {
        public int Attempts { get; private set; }

        public AppSettings Load() => new();

        public void Save(AppSettings settings)
        {
            Attempts++;
            throw new IOException("القرص مليان");
        }
    }

    private sealed class FakeWatcher : IIncomingJobWatcher
    {
        public event Action<IncomingFile>? JobArrived;
        public event Action<string>? Reported;

        public bool IsRunning => Running;
        public bool Running { get; private set; }

        public void Start(string spoolFolder, string queueFolder, string? hotFolder) => Running = true;

        public void Stop() => Running = false;

        public void Deliver(IncomingFile file) => JobArrived?.Invoke(file);

        public void Say(string line) => Reported?.Invoke(line);
    }

    private sealed class StubPrinters : IPrinterRepository
    {
        public Task<List<Printer>> GetPrintersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Printer>());

        public string SendTestPage(string printerName) => "ok";

        public PrinterCapabilities GetCapabilities(string printerName) => new();
    }

    private sealed class StubMerge : IPdfMergeService
    {
        public MergeResult Merge(MergeRequest request) => MergeResult.Succeeded("تم", 1);

        public Task<MergeResult> MergeAsync(MergeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(MergeResult.Succeeded("تم", 1));
    }

    private sealed class StubPrint : IPdfPrintService
    {
        public Task<PrintOutcome> PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
            => Task.FromResult(PrintOutcome.Delivered("تم"));
    }
}
