using System.Text.Json;
using PrintFlow.Domain;
using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// على جهاز المستخدم في settings.json متحفوظ من نسخة قديمة، ومفيهوش
/// المفاتيح الجديدة. لازم الخصائص الجديدة تاخد قيمتها الافتراضية
/// مش false/صفر.
/// </summary>
public class MigrationTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "PFMig_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Old_Settings_File_Gets_The_New_Defaults()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "settings.json");

        // ملف من نسخة 1.1: مفيهوش PageNumberBackdrop ولا RestartNumberingForEachFile
        File.WriteAllText(path, """
        {
          "DefaultPrinterName": "HP LaserJet",
          "PageNumberFontSize": 14,
          "WatermarkEnabled": true,
          "WatermarkText": "سري"
        }
        """);

        var store = new JsonSettingsStore(_folder);
        var loaded = store.Load();

        // القديم اتقرا زي ما هو
        Assert.Equal("HP LaserJet", loaded.DefaultPrinterName);
        Assert.Equal(14, loaded.PageNumberFontSize);
        Assert.True(loaded.WatermarkEnabled);

        // والجديد خد الافتراضي الصح
        Assert.True(loaded.PageNumberBackdrop);
        Assert.False(loaded.RestartNumberingForEachFile);
    }

    [Fact]
    public void A_Missing_File_Gives_Plain_Defaults()
    {
        var store = new JsonSettingsStore(Path.Combine(_folder, "لا-يوجد"));
        var loaded = store.Load();

        Assert.True(loaded.PageNumberBackdrop);
        Assert.False(loaded.RestartNumberingForEachFile);
    }

    [Fact]
    public void New_Settings_Survive_A_Save_And_Load_Round_Trip()
    {
        var store = new JsonSettingsStore(_folder);
        store.Save(new AppSettings
        {
            PageNumberBackdrop = false,
            RestartNumberingForEachFile = true
        });

        var loaded = store.Load();

        Assert.False(loaded.PageNumberBackdrop);
        Assert.True(loaded.RestartNumberingForEachFile);
    }
}
