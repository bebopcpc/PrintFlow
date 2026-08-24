using PrintFlow.Domain;
using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// المبدأ اللي بنختبره هنا: البرنامج مايقفش عشان ملف إعدادات.
/// أي مشكلة في القراءة بترجّع الافتراضي بدل ما ترمي استثناء.
/// </summary>
public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _folder;
    private readonly JsonSettingsStore _store;

    public JsonSettingsStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PrintFlowStore_" + Guid.NewGuid().ToString("N"));
        _store = new JsonSettingsStore(_folder);
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

    // ══════════ تفضيلات البرنامج ══════════

    [Fact]
    public void Missing_File_Gives_Defaults_Not_A_Crash()
    {
        var settings = _store.Load();

        Assert.Equal(10, settings.PrinterRefreshSeconds);
        Assert.False(settings.WatermarkEnabled);
    }

    [Fact]
    public void App_Settings_Survive_A_Save_And_Load()
    {
        var original = new AppSettings
        {
            WatermarkEnabled = true,
            WatermarkText = "مطبعة النور الحديثة",
            WatermarkColorHex = "#C0392B",
            WatermarkFontFamily = "Times New Roman",
            WatermarkOpacityPercent = 35,
            WatermarkRotationDegrees = -30,
            PageNumberPosition = ContentPosition.TopCenter,
            PageNumberFontSize = 18,
            CountingMethod = CountingMethod.BySheet,
            FileSortOrder = FileSortOrder.ByPageCount,
            DefaultPrinterName = "طابعة المكتب",
            PrinterRefreshSeconds = 25
        };

        _store.Save(original);
        var loaded = _store.Load();

        Assert.True(loaded.WatermarkEnabled);
        Assert.Equal("مطبعة النور الحديثة", loaded.WatermarkText);
        Assert.Equal("#C0392B", loaded.WatermarkColorHex);
        Assert.Equal("Times New Roman", loaded.WatermarkFontFamily);
        Assert.Equal(35, loaded.WatermarkOpacityPercent);
        Assert.Equal(-30, loaded.WatermarkRotationDegrees);
        Assert.Equal(ContentPosition.TopCenter, loaded.PageNumberPosition);
        Assert.Equal(18, loaded.PageNumberFontSize);
        Assert.Equal(CountingMethod.BySheet, loaded.CountingMethod);
        Assert.Equal(FileSortOrder.ByPageCount, loaded.FileSortOrder);
        Assert.Equal("طابعة المكتب", loaded.DefaultPrinterName);
        Assert.Equal(25, loaded.PrinterRefreshSeconds);
    }

    /// <summary>
    /// من غير Encoder مخصص، System.Text.Json بيحوّل العربي لأكواد \uXXXX
    /// والملف بيبقى مش مقروء لو المستخدم فتحه بنفسه.
    /// </summary>
    [Fact]
    public void Arabic_Is_Written_Readable_Not_Escaped()
    {
        _store.Save(new AppSettings { WatermarkText = "شركة الأمل" });

        string json = File.ReadAllText(_store.SettingsPath);

        Assert.Contains("شركة الأمل", json);
        Assert.DoesNotContain("\\u0634", json);
    }

    [Fact]
    public void Corrupt_File_Gives_Defaults_Not_A_Crash()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(_store.SettingsPath, "{ ده مش JSON خالص ,,, }");

        var settings = _store.Load();

        Assert.Equal(10, settings.PrinterRefreshSeconds);
    }

    [Fact]
    public void Empty_File_Gives_Defaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(_store.SettingsPath, "");

        Assert.False(_store.Load().WatermarkEnabled);
    }

    [Fact]
    public void No_Temp_File_Is_Left_Behind_After_Saving()
    {
        _store.Save(new AppSettings());

        Assert.False(File.Exists(_store.SettingsPath + ".tmp"));
        Assert.True(File.Exists(_store.SettingsPath));
    }

    // ══════════ الإعدادات المسبقة ══════════

    [Fact]
    public void Presets_Survive_A_Save_And_Load()
    {
        var presets = new[]
        {
            new Preset
            {
                Name = "كتالوج A3",
                Settings = new PrintSettings
                {
                    PaperSize = "A3",
                    TotalCopies = 50,
                    Duplex = true,
                    DuplexFlip = DuplexFlip.ShortEdge,
                    PageOrientation = PageOrientation.Landscape,
                    SelectedPrinters = new List<string> { "HP-1", "HP-2" }
                }
            },
            new Preset { Name = "مسودة سريعة", Settings = new PrintSettings { Grayscale = true } }
        };

        _store.SaveAll(presets);
        var loaded = _store.LoadAll();

        Assert.Equal(2, loaded.Count);

        var catalogue = loaded.First(p => p.Name == "كتالوج A3");
        Assert.Equal("A3", catalogue.Settings.PaperSize);
        Assert.Equal(50, catalogue.Settings.TotalCopies);
        Assert.True(catalogue.Settings.Duplex);
        Assert.Equal(DuplexFlip.ShortEdge, catalogue.Settings.DuplexFlip);
        Assert.Equal(PageOrientation.Landscape, catalogue.Settings.PageOrientation);
        Assert.Equal(new[] { "HP-1", "HP-2" }, catalogue.Settings.SelectedPrinters);

        Assert.True(loaded.First(p => p.Name == "مسودة سريعة").Settings.Grayscale);
    }

    [Fact]
    public void No_Presets_File_Gives_An_Empty_List()
    {
        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void Corrupt_Presets_File_Gives_An_Empty_List()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(_store.PresetsPath, "[[[ بايظ");

        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void Presets_Without_A_Name_Are_Skipped()
    {
        _store.SaveAll(new[]
        {
            new Preset { Name = "سليم" },
            new Preset { Name = "   " },
            new Preset { Name = "" }
        });

        var loaded = _store.LoadAll();

        Assert.Single(loaded);
        Assert.Equal("سليم", loaded[0].Name);
    }

    [Fact]
    public void Saving_Twice_Replaces_Rather_Than_Appends()
    {
        _store.SaveAll(new[] { new Preset { Name = "أول" }, new Preset { Name = "تاني" } });
        _store.SaveAll(new[] { new Preset { Name = "تالت" } });

        var loaded = _store.LoadAll();

        Assert.Single(loaded);
        Assert.Equal("تالت", loaded[0].Name);
    }
}
