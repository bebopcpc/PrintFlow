using PrintFlow.Application;

namespace PrintFlow.Infrastructure;

/// <summary>خط واحد: الاسم اللي المستخدم بيشوفه + ملف العادي والعريض.</summary>
public sealed record FontDefinition(string DisplayName, string RegularFile, string BoldFile);

/// <summary>
/// جدول واحد لخطوط ويندوز، بيستخدمه AppFontResolver (اللي بيقرا الملفات)
/// و WindowsFontCatalog (اللي بيعرض القايمة للمستخدم) — عشان الاتنين
/// مايختلفوش أبدًا عن بعض.
///
/// شرط الدخول للجدول ده حاجتين:
///   1) الخط بييجي مع ويندوز (مش محتاج تسطيب).
///   2) بيغطي **العربي واللاتيني مع بعض**.
///
/// الشرط التاني ده هو بيت القصيد. الشيبر بتاعنا بيولّد أشكال العرض العربية
/// (U+FB50–U+FEFF)، فالخط لازم يكون فيه الأشكال دي. خطوط زي Liberation Sans
/// مفيهاش عربي خالص، وخطوط زي Noto Naskh Arabic مفيهاش لاتيني —
/// وفي الحالتين الناقص بيترسم مربعات فاضية من غير أي تحذير.
/// </summary>
public static class WindowsFonts
{
    public const string FontsFolder = @"C:\Windows\Fonts";

    public const string FallbackFamily = "Arial";

    public static readonly IReadOnlyList<FontDefinition> ArabicCapable =
    [
        new("Arial", "arial", "arialbd"),
        new("Tahoma", "tahoma", "tahomabd"),
        new("Times New Roman", "times", "timesbd"),
        new("Courier New", "cour", "courbd"),
        new("Segoe UI", "segoeui", "segoeuib"),
    ];

    /// <summary>أسامي قديمة أو بديلة → الاسم الحقيقي على ويندوز.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Helvetica مش موجود على ويندوز أصلًا. كان هو الافتراضي القديم،
        // فبنحوّله لـ Arial عشان الإعدادات المحفوظة القديمة تفضل شغالة.
        ["Helvetica"] = "Arial",
        ["Times"] = "Times New Roman",
        ["Courier"] = "Courier New",
        ["Segoe"] = "Segoe UI",
    };

    public static FontDefinition Resolve(string? familyName)
    {
        string name = familyName ?? string.Empty;

        if (Aliases.TryGetValue(name, out string? aliased))
        {
            name = aliased;
        }

        return ArabicCapable.FirstOrDefault(
                   f => f.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? ArabicCapable[0];
    }

    public static bool IsInstalled(FontDefinition font) =>
        File.Exists(Path.Combine(FontsFolder, font.RegularFile + ".ttf"));

    /// <summary>أسماء الخطوط المتاحة فعلًا على الجهاز ده.</summary>
    public static IReadOnlyList<string> InstalledNames()
    {
        var installed = ArabicCapable.Where(IsInstalled).Select(f => f.DisplayName).ToList();

        // لو مالقيناش ولا خط (بيئة غريبة أو اختبار)، نرجّع Arial على الأقل
        // عشان القايمة ماتبقاش فاضية قدام المستخدم.
        return installed.Count > 0 ? installed : [FallbackFamily];
    }
}

/// <summary>تنفيذ الكتالوج على ويندوز.</summary>
public sealed class WindowsFontCatalog : IFontCatalog
{
    public IReadOnlyList<string> AvailableFonts { get; } = WindowsFonts.InstalledNames();
}
