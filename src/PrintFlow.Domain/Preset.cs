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

    /// <summary>
    /// وصف مختصر يتعرض جنب الاسم عشان المستخدم يعرف الـ Preset ده بيعمل إيه.
    ///
    /// **لازم يذكر أي إعداد بيغيّر شكل الورق.** ده الكلام اللي بيقراه قبل
    /// ما يضغط تحميل؛ لو كتم إن الإعداد فيه كتيّب أو ٤ شرائح أو حذف صفحات،
    /// هو هيحمّله فاكر إنه إعداد طباعة عادي والورق هيطلع حاجة تانية خالص.
    /// </summary>
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

        // ══ اللي بيغيّر شكل الورق ══

        if (Settings.BookletMode)
        {
            // الكتيّب بيتجاهل عدد الشرائح، فماينفعش نقول الاتنين
            parts.Add("كتيّب");
        }
        else if (Settings.SlidesPerSheet > 1)
        {
            parts.Add($"{Settings.SlidesPerSheet} شرائح");
        }

        if (Settings.DeletePages && !string.IsNullOrWhiteSpace(Settings.PagesToDelete))
        {
            parts.Add($"حذف ({Settings.PagesToDelete})");
        }

        if (Settings.ScalePercent != 100)
        {
            parts.Add($"مقياس {Settings.ScalePercent}%");
        }

        if (!Settings.MergeFiles)
        {
            parts.Add("من غير دمج");
        }

        if (Settings.UseMultiplePrinters)
        {
            parts.Add(Settings.DistributeCopies ? "توزيع على الطابعات" : "كل الطابعات");
        }

        return string.Join(" • ", parts);
    }

    public Preset Clone() => new() { Name = Name, Settings = Settings.Clone() };
}
