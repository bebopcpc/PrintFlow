using PrintFlow.Domain;

namespace PrintFlow.Application;

/// <summary>تخزين تفضيلات البرنامج (تاب الإعدادات العامة).</summary>
public interface IAppSettingsStore
{
    /// <summary>بيرجّع الإعدادات المحفوظة، أو إعدادات افتراضية لو مفيش ملف أو الملف تالف.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>تخزين الإعدادات المسبقة (تاب الإعدادات المسبقة).</summary>
public interface IPresetStore
{
    IReadOnlyList<Preset> LoadAll();

    void SaveAll(IEnumerable<Preset> presets);
}
