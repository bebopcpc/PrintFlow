using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيحفظ الإعدادات والـ Presets كملفات JSON في مجلد المستخدم.
///
/// مبدأ ثابت هنا: **البرنامج مايفشلش عشان ملف إعدادات**. أي مشكلة في القراءة
/// (ملف تالف، صلاحيات، نسخة قديمة) بترجّع الافتراضي بدل ما ترمي استثناء —
/// مطبعة مش هتقف عشان JSON فيه فاصلة زيادة.
/// </summary>
public sealed class JsonSettingsStore : IAppSettingsStore, IPresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // من غير الحتة دي العربي بيتحفظ كـ ال... ومحدش يقدر يقرا الملف
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly string _folder;

    public JsonSettingsStore(string? folder = null)
    {
        _folder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrintFlow");
    }

    public string SettingsPath => Path.Combine(_folder, "settings.json");

    public string PresetsPath => Path.Combine(_folder, "presets.json");

    // ══════════ تفضيلات البرنامج ══════════

    public AppSettings Load() => ReadOrDefault(SettingsPath, () => new AppSettings());

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Write(SettingsPath, settings);
    }

    // ══════════ الإعدادات المسبقة ══════════

    public IReadOnlyList<Preset> LoadAll()
    {
        var presets = ReadOrDefault(PresetsPath, () => new List<Preset>());

        // بنستبعد أي مدخل بايظ بدل ما نرمي القايمة كلها
        return presets
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
            .ToList();
    }

    public void SaveAll(IEnumerable<Preset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        Write(PresetsPath, presets.ToList());
    }

    // ══════════ قراءة وكتابة ══════════

    private static T ReadOrDefault<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                return fallback();
            }

            string json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback();
            }

            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback();
        }
        catch (Exception)
        {
            // ملف تالف أو صلاحيات — نكمّل بالافتراضي
            return fallback();
        }
    }

    private void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(_folder);

        // بنكتب في ملف مؤقت وبعدين نستبدل: لو الكهربا قطعت وسط الكتابة،
        // الملف القديم بيفضل سليم بدل ما يبقى نص ملف مقطوع.
        string temporary = path + ".tmp";

        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
