namespace PrintFlow.Domain;

/// <summary>
/// إعداد مسبق: اسم + نسخة كاملة من خيارات الجوب.
///
/// كل الشغل اللي عملناه في PrintSettings بيثمر هنا — الـ Preset مجرد
/// Serialize/Deserialize للكلاس ده، من غير أي نسخ خصايص بالإيد.
/// </summary>
public sealed class Preset
{
    public string Name { get; set; } = string.Empty;

    public PrintSettings Settings { get; set; } = new();

    /// <summary>وصف مختصر يتعرض جنب الاسم عشان المستخدم يعرف الـ Preset ده بيعمل إيه.</summary>
    public string Summarize()
    {
        var parts = new List<string>
        {
            Settings.PaperSize,
            Settings.PageOrientation == PageOrientation.Landscape ? "عرضي" : "طولي",
            $"{Settings.TotalCopies} نسخة"
        };

        if (Settings.Grayscale)
        {
            parts.Add("أبيض وأسود");
        }

        if (Settings.Duplex)
        {
            parts.Add("وجهين");
        }

        if (Settings.NumberPagesPerFile)
        {
            parts.Add("ترقيم");
        }

        if (Settings.UseMultiplePrinters)
        {
            parts.Add(Settings.DistributeCopies ? "توزيع على الطابعات" : "كل الطابعات");
        }

        return string.Join(" • ", parts);
    }

    public Preset Clone() => new() { Name = Name, Settings = Settings.Clone() };
}
