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
    /// أطول سطر أوامر ويندوز بيقبله ٣٢٧٦٧ حرف. بنقف عند حد آمن قبله بكتير
    /// عشان لسه فاضل اسم الملف واسم الطابعة وباقي الخيارات.
    /// </summary>
    private const int MaxSettingsLength = 8000;

    /// <summary>
    /// بيبني قيمة print-settings.
    ///
    /// أهم حتة هنا: النسخ بتتبعت في أمر واحد بدل ما نفتح بروسيس لكل نسخة —
    /// يعني ٥٠ نسخة = بروسيس واحد مش ٥٠.
    /// </summary>
    public static string BuildPrintSettings(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var parts = new List<string>();

        string? pages = BuildPages(job);

        if (pages is not null)
        {
            parts.Add(pages);
        }

        parts.Add($"paper={MapPaperSize(job.PaperSize)}");
        parts.Add(job.Orientation == PageOrientation.Landscape ? "landscape" : "portrait");

        // SumatraPDF بيدعم noscale/shrink/fit/stretch بس — مفيش نسبة مئوية.
        // عشان كده ScalePercent لسه مش موصّلة؛ التحجيم بنسبة لازم يتعمل على الـ PDF نفسه.
        parts.Add("shrink");

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
        /// <summary>
    /// بيرجّع الجزء اللي بيقول لسوماترا **أنهي صفحات** تتطبع، **كام نسخة**،
    /// و**بأي ترتيب**. بيرجّع null لما مفيش حاجة تتقال (نسخة واحدة من
    /// المستند كامل) — وساعتها الأمر بيطلع مطابق للقديم بالحرف.
    ///
    /// ═══ المشكلة ═══
    ///
    /// <c>{n}x</c> بتعمل حاجة واحدة بس: بتحط العدد في <c>dmCopies</c> جوه
    /// إعدادات الدرايفر، **وماتقولش ولا كلمة عن الترتيب**. يعني اللي بيقرر
    /// يطلّع ١·٢·٣·١·٢·٣ ولا ١·١·٢·٢·٣·٣ هو إعداد الطابعة نفسها، والافتراضي
    /// في أغلب الدرايفرات هو التاني.
    ///
    /// اتجرّب على HP LaserJet P1102: نفس الملف ونفس الأمر — بخانة Collate
    /// مقفولة طلع ١·١·٢·٢·٣·٣، وبعد ما اتفتحت طلع ١·٢·٣·١·٢·٣. يعني كنا
    /// سايبين أهم حاجة في المطبعة لخانة مدفونة في شباك الدرايفر.
    /// (سوماترا 3.6.1 مافيهاش خيار <c>collate</c> أصلًا — اتضاف في 3.7 التجريبية.)
    ///
    /// ═══ الحل: نبعت الترتيب بنفسنا ═══
    ///
    /// بدل "اطبعها ٣ مرات"، بنقوله الصفحات بالترتيب: <c>1-20,1-20,1-20</c>.
    /// سوماترا بتطلّعها بالترتيب ده في **جوب واحد**، والدرايفر مابقاش عنده
    /// نسخ يلخبطها — فالترتيب مضمون على أي طابعة.
    ///
    /// ═══ إمتى بنرجع لـ {n}x ═══
    ///
    ///   • **عدد الصفحات مجهول**: مانقدرش نكتب مدى من غير آخر صفحة، ولو
    ///     خمّنا غلط هنطبع **ناقص** — وده أسوأ بكتير من ترتيب غلط.
    ///
    ///   • **وشين + صفحات فردية**: مستند ٣ صفحات مكرر مرتين = ٦ صفحات ورا
    ///     بعض، فالورقة التانية هتبقى صفحة ٣ من النسخة الأولى على وش وصفحة ١
    ///     من النسخة التانية على ضهرها. الدرايفر بيبدأ كل نسخة في ورقة جديدة،
    ///     فسايبينه يعملها هو في الحالة دي.
    ///
    ///   • **عدد نسخ ضخم**: ويندوز بيرفض أمر أطول من ٣٢٧٦٧ حرف، فبنوقف
    ///     عند <see cref="MaxSettingsLength"/> بأمان.
    ///
    /// كل التستات القديمة بتبني الجوب من غير <c>pageCount</c>، يعني بتقع في
    /// الحالة الأولى وبتاخد <c>{n}x</c> زي الأول — عشان كده ولا تست اتغيّر.
    /// </summary>
        private static string? BuildPages(PrintJob job)
    {
        var (first, last) = PageRange.Resolve(job.FirstPage, job.LastPage, job.PageCount);

        bool knownRange = last >= first && first > 0;
        bool userAskedForPart = PageRange.IsSubset(job.FirstPage, job.LastPage);

        // نسخة واحدة: مفيش ترتيب نتحكم فيه أصلًا. بنكتب المدى بس لو
        // المستخدم طلب جزء — وإلا مانكتبش حاجة والمستند بيتطبع كامل
        // زي ما كان بالحرف.
        if (job.Copies <= 1)
        {
            return userAskedForPart && knownRange ? $"{first}-{last}" : null;
        }

        string range = $"{first}-{last}";
        int perCopy = last - first + 1;

        // +1 للفاصلة اللي بين كل مدى والتاني.
        long length = (long)(range.Length + 1) * job.Copies;

        bool canOrderThemOurselves =
            knownRange &&
            (!job.Duplex || perCopy % 2 == 0) &&
            length <= MaxSettingsLength;

        if (canOrderThemOurselves)
        {
            return string.Join(",", Enumerable.Repeat(range, job.Copies));
        }

        // رجعنا للدرايفر. لو المستخدم طالب جزء، بنبعت المدى **مع** عدد
        // النسخ — سوماترا بتفهم الاتنين مع بعض: المدى بيحدد الصفحات
        // و{n}x بيحدد العدد. اللي بنخسره هنا هو التحكم في الترتيب بس،
        // مش الصفحات.
        return userAskedForPart && knownRange
            ? $"{range},{job.Copies}x"
            : $"{job.Copies}x";
    }

    private static string MapPaperSize(string paperSize) =>
        PaperNames.TryGetValue(paperSize, out string? mapped) ? mapped : "A4";
}
