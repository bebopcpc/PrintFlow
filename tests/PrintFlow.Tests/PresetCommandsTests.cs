using PrintFlow.Application;
using PrintFlow.Domain;
using PrintFlow.Presentation;

namespace PrintFlow.Tests;

/// <summary>
/// تاب الإعدادات المسبقة. الفايدة اللي كنا بنستنّاها من كلاس PrintSettings
/// بتظهر هنا: الحفظ والتطبيق مجرد Clone و CopyFrom، من غير نسخ خصايص بالإيد.
/// </summary>
public class PresetCommandsTests
{
    private static MainViewModel CreateViewModel(FakePresetStore? store = null)
        => new(
            new StubPrinterRepository(),
            new StubMergeService(),
            new StubPrintService(),
            presetStore: store ?? new FakePresetStore());

    [Fact]
    public void Add_Saves_A_Snapshot_Of_The_Current_Settings()
    {
        var vm = CreateViewModel();
        vm.Settings.PaperSize = "A3";
        vm.Settings.TotalCopies = 40;
        vm.Settings.Duplex = true;

        vm.NewPresetName = "كتالوج";
        vm.AddPresetCommand.Execute(null);

        Assert.Single(vm.Presets);
        Assert.Equal("كتالوج", vm.Presets[0].Name);
        Assert.Equal("A3", vm.Presets[0].Settings.PaperSize);
        Assert.Equal(40, vm.Presets[0].Settings.TotalCopies);
        Assert.True(vm.Presets[0].Settings.Duplex);
    }

    [Fact]
    public void Saved_Preset_Is_A_Snapshot_Not_A_Live_Link()
    {
        var vm = CreateViewModel();
        vm.Settings.TotalCopies = 10;
        vm.NewPresetName = "عشرة";
        vm.AddPresetCommand.Execute(null);

        // نغيّر الإعدادات الحالية — الـ Preset المحفوظ مالوش دعوة
        vm.Settings.TotalCopies = 99;

        Assert.Equal(10, vm.Presets[0].Settings.TotalCopies);
    }

    [Fact]
    public void Add_Is_Blocked_Without_A_Name()
    {
        var vm = CreateViewModel();

        Assert.False(vm.AddPresetCommand.CanExecute(null));

        vm.NewPresetName = "اسم";
        Assert.True(vm.AddPresetCommand.CanExecute(null));

        vm.NewPresetName = "   ";
        Assert.False(vm.AddPresetCommand.CanExecute(null));
    }

    [Fact]
    public void Name_Box_Clears_After_Adding()
    {
        var vm = CreateViewModel();
        vm.NewPresetName = "اسم";
        vm.AddPresetCommand.Execute(null);

        Assert.Equal(string.Empty, vm.NewPresetName);
    }

    [Fact]
    public void Adding_The_Same_Name_Replaces_Instead_Of_Duplicating()
    {
        var vm = CreateViewModel();

        vm.Settings.TotalCopies = 5;
        vm.NewPresetName = "نفس الاسم";
        vm.AddPresetCommand.Execute(null);

        vm.Settings.TotalCopies = 50;
        vm.NewPresetName = "نفس الاسم";
        vm.AddPresetCommand.Execute(null);

        Assert.Single(vm.Presets);
        Assert.Equal(50, vm.Presets[0].Settings.TotalCopies);
    }

    /// <summary>
    /// التطبيق بيستخدم CopyFrom مش استبدال الكائن — لأن كل الـ Bindings
    /// في الواجهة مربوطة على نفس النسخة، فاستبدالها معناه إن الواجهة ماتتحدّثش.
    /// </summary>
    [Fact]
    public void Apply_Copies_Into_The_Same_Settings_Instance()
    {
        var vm = CreateViewModel();
        var boundInstance = vm.Settings;

        vm.Settings.PaperSize = "A3";
        vm.Settings.Grayscale = true;
        vm.NewPresetName = "محفوظ";
        vm.AddPresetCommand.Execute(null);

        vm.Settings.PaperSize = "Letter";
        vm.Settings.Grayscale = false;

        vm.SelectedPreset = vm.Presets[0];
        vm.ApplyPresetCommand.Execute(null);

        Assert.Same(boundInstance, vm.Settings);
        Assert.Equal("A3", vm.Settings.PaperSize);
        Assert.True(vm.Settings.Grayscale);
    }

    [Fact]
    public void Update_Overwrites_The_Selected_Preset()
    {
        var vm = CreateViewModel();
        vm.Settings.TotalCopies = 3;
        vm.NewPresetName = "قديم";
        vm.AddPresetCommand.Execute(null);

        vm.Settings.TotalCopies = 77;
        vm.SelectedPreset = vm.Presets[0];
        vm.UpdatePresetCommand.Execute(null);

        Assert.Single(vm.Presets);
        Assert.Equal(77, vm.Presets[0].Settings.TotalCopies);
    }

    [Fact]
    public void Delete_Removes_It_And_Clears_The_Selection()
    {
        var vm = CreateViewModel();
        vm.NewPresetName = "للحذف";
        vm.AddPresetCommand.Execute(null);
        vm.SelectedPreset = vm.Presets[0];

        vm.DeletePresetCommand.Execute(null);

        Assert.Empty(vm.Presets);
        Assert.Null(vm.SelectedPreset);
    }

    [Fact]
    public void Update_Delete_And_Apply_Need_A_Selection()
    {
        var vm = CreateViewModel();

        Assert.False(vm.UpdatePresetCommand.CanExecute(null));
        Assert.False(vm.DeletePresetCommand.CanExecute(null));
        Assert.False(vm.ApplyPresetCommand.CanExecute(null));

        vm.NewPresetName = "واحد";
        vm.AddPresetCommand.Execute(null);

        Assert.True(vm.UpdatePresetCommand.CanExecute(null));
        Assert.True(vm.DeletePresetCommand.CanExecute(null));
        Assert.True(vm.ApplyPresetCommand.CanExecute(null));
    }

    // ══════════ التخزين ══════════

    [Fact]
    public void Presets_Are_Loaded_From_Storage_At_Startup()
    {
        var store = new FakePresetStore();
        store.Saved = new List<Preset>
        {
            new() { Name = "محفوظ من قبل", Settings = new PrintSettings { PaperSize = "Legal" } }
        };

        var vm = CreateViewModel(store);

        Assert.Single(vm.Presets);
        Assert.Equal("Legal", vm.Presets[0].Settings.PaperSize);
    }

    [Fact]
    public void Every_Change_Is_Written_To_Storage_Immediately()
    {
        var store = new FakePresetStore();
        var vm = CreateViewModel(store);

        vm.NewPresetName = "أول";
        vm.AddPresetCommand.Execute(null);
        Assert.Single(store.Saved);

        vm.SelectedPreset = vm.Presets[0];
        vm.DeletePresetCommand.Execute(null);
        Assert.Empty(store.Saved);

        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public void ViewModel_Works_Without_Any_Storage_At_All()
    {
        var vm = new MainViewModel(new StubPrinterRepository(), new StubMergeService(), new StubPrintService());

        vm.NewPresetName = "من غير تخزين";
        vm.AddPresetCommand.Execute(null);

        Assert.Single(vm.Presets);
    }

    [Fact]
    public void Summary_Describes_What_The_Preset_Holds()
    {
        var preset = new Preset
        {
            Name = "كتالوج",
            Settings = new PrintSettings
            {
                PaperSize = "A3",
                TotalCopies = 25,
                Grayscale = true,
                Duplex = true,
                PageOrientation = PageOrientation.Landscape
            }
        };

        string summary = preset.Summarize();

        Assert.Contains("A3", summary);
        Assert.Contains("25 نسخة", summary);
        Assert.Contains("عرضي", summary);
        Assert.Contains("أبيض وأسود", summary);
        Assert.Contains("وجهين", summary);
    }

    // ══════════ الخطوط ══════════

    [Fact]
    public void Font_List_Comes_From_The_Catalog()
    {
        var vm = new MainViewModel(
            new StubPrinterRepository(), new StubMergeService(), new StubPrintService(),
            fontCatalog: new FakeFontCatalog("Arial", "Tahoma"));

        Assert.Equal(new[] { "Arial", "Tahoma" }, vm.WatermarkFonts);
    }

    /// <summary>
    /// إعدادات محفوظة من نسخة قديمة ممكن تكون فيها Helvetica (اللي مش موجود
    /// على ويندوز). من غير التصحيح، القايمة بتبان فاضية قدام المستخدم.
    /// </summary>
    [Fact]
    public void Saved_Font_That_Is_Not_Available_Is_Corrected()
    {
        var store = new FakeAppSettingsStore
        {
            Stored = new AppSettings { WatermarkFontFamily = "Helvetica" }
        };

        var vm = new MainViewModel(
            new StubPrinterRepository(), new StubMergeService(), new StubPrintService(),
            settingsStore: store,
            fontCatalog: new FakeFontCatalog("Arial", "Tahoma"));

        Assert.Equal("Arial", vm.App.WatermarkFontFamily);
    }

    [Fact]
    public void Available_Saved_Font_Is_Left_Alone()
    {
        var store = new FakeAppSettingsStore
        {
            Stored = new AppSettings { WatermarkFontFamily = "Tahoma" }
        };

        var vm = new MainViewModel(
            new StubPrinterRepository(), new StubMergeService(), new StubPrintService(),
            settingsStore: store,
            fontCatalog: new FakeFontCatalog("Arial", "Tahoma"));

        Assert.Equal("Tahoma", vm.App.WatermarkFontFamily);
    }

    [Fact]
    public void Default_Watermark_Font_Is_A_Real_Windows_Font()
    {
        // Helvetica مش موجود على ويندوز — الافتراضي لازم يكون خط حقيقي فيه عربي
        Assert.Equal("Arial", new AppSettings().WatermarkFontFamily);
    }

    // ══════════ فيكات ══════════

    private sealed class FakeFontCatalog : IFontCatalog
    {
        public FakeFontCatalog(params string[] fonts) => AvailableFonts = fonts;

        public IReadOnlyList<string> AvailableFonts { get; }
    }

    private sealed class FakeAppSettingsStore : IAppSettingsStore
    {
        public AppSettings Stored { get; set; } = new();

        public AppSettings Load() => Stored;

        public void Save(AppSettings settings) => Stored = settings;
    }

    private sealed class FakePresetStore : IPresetStore
    {
        public List<Preset> Saved { get; set; } = new();

        public int SaveCount { get; private set; }

        public IReadOnlyList<Preset> LoadAll() => Saved.ToList();

        public void SaveAll(IEnumerable<Preset> presets)
        {
            Saved = presets.Select(p => p.Clone()).ToList();
            SaveCount++;
        }
    }

    private sealed class StubPrinterRepository : IPrinterRepository
    {
        public Task<List<Printer>> GetPrintersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Printer>());

        public string SendTestPage(string printerName) => "ok";

        public PrinterCapabilities GetCapabilities(string printerName) => new();
    }

    private sealed class StubMergeService : IPdfMergeService
    {
        public MergeResult Merge(MergeRequest request) => MergeResult.Succeeded("ok", 1);
    }

    private sealed class StubPrintService : IPdfPrintService
    {
        public Task<PrintOutcome> PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
            => Task.FromResult(PrintOutcome.Delivered("ok"));
    }
}
