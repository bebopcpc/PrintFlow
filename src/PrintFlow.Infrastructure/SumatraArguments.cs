using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيحوّل PrintJob لأوامر سطر أوامر SumatraPDF.
///
/// اتفصل عن PdfPrintService عن قصد: ده منطق نصي خالص من غير أي تشغيل بروسيس
/// ولا اعتماد على ويندوز، فينفع نعمله Unit Tests ونتأكد إن كل خيار في الواجهة
/// بيتحول للأمر الصح — من غير ما نطبع ورقة واحدة.
///
/// المرجع: https://www.sumatrapdfreader.org/docs/Command-line-arguments
/// </summary>
public static class SumatraArguments
{
    /// <summary>
    /// المقاسات اللي SumatraPDF بيفهمها في paper=. لاحظ إن A0 و A1 مش مدعومين،
    /// وإن letter/legal/tabloid بحروف صغيرة زي ما هي في التوثيق.
    /// </summary>
    private static readonly Dictionary<string, string> PaperNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A2"] = "A2",
        ["A3"] = "A3",
        ["A4"] = "A4",
        ["A5"] = "A5",
        ["A6"] = "A6",
        ["Letter"] = "letter",
        ["Legal"] = "legal",
        ["Tabloid"] = "tabloid",
        ["Statement"] = "statement",
    };

    /// <summary>
    /// بيبني قيمة print-settings.
    ///
    /// أهم حتة هنا: <c>{n}x</c> بتخلي SumatraPDF نفسه يطبع العدد المطلوب.
    /// الكود القديم كان بيفتح بروسيس منفصل لكل نسخة — يعني ٥٠ نسخة = ٥٠ بروسيس،
    /// وكمان الترتيب (collation) كان بيبوظ لأن كل بروسيس جوب مستقل عند الطابعة.
    /// </summary>
    public static string BuildPrintSettings(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var parts = new List<string>();

        if (job.Copies > 1)
        {
            parts.Add($"{job.Copies}x");
        }

        parts.Add($"paper={MapPaperSize(job.PaperSize)}");
        parts.Add(job.Orientation == PageOrientation.Landscape ? "landscape" : "portrait");

        // SumatraPDF بيدعم noscale/shrink/fit/stretch بس — مفيش نسبة مئوية.
        // عشان كده ScalePercent لسه مش موصّلة؛ التحجيم بنسبة لازم يتعمل على الـ PDF نفسه.
        parts.Add("noscale");

        parts.Add(job.Grayscale ? "monochrome" : "color");
        parts.Add(job.Duplex
            ? (job.DuplexFlip == DuplexFlip.ShortEdge ? "duplexshort" : "duplexlong")
            : "simplex");

        return string.Join(",", parts);
    }

    /// <summary>
    /// بيرجّع الأوامر كـ لستة منفصلة عشان تتحط في ProcessStartInfo.ArgumentList.
    ///
    /// ليه لستة مش سترينج واحد: الويندوز هو اللي بيتولى تهريب علامات التنصيص.
    /// اسم طابعة فيه علامة تنصيص كان هيكسر السترينج المبني بالإيد.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return
        [
            "-print-to", job.PrinterName,
            "-print-settings", BuildPrintSettings(job),
            "-silent",
            job.FilePath
        ];
    }

    private static string MapPaperSize(string paperSize) =>
        PaperNames.TryGetValue(paperSize, out string? mapped) ? mapped : "A4";
}
