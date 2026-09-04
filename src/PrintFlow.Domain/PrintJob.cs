namespace PrintFlow.Domain;

/// <summary>
/// أمر طباعة واحد: ملف واحد على طابعة واحدة بعدد نسخ محدد.
///
/// قبل كده كانت الطباعة بتاخد ٦ براميترات سايبة (مسار، طابعة، مقاس، نسخ، رمادي، وجهين)،
/// وكل خيار جديد كان معناه براميتر زيادة في التوقيع وتعديل في كل مكان بينادي عليه.
/// الريكورد ده بيخلي التوقيع ثابت مهما زادت الخيارات.
///
/// ملاحظة: النسخ مش بتتاخد من Settings.TotalCopies مباشرة، لأن في وضع
/// "توزيع النسخ" كل طابعة بتاخد نصيب مختلف.
/// </summary>
public sealed record PrintJob
{
    public required string FilePath { get; init; }

    public required string PrinterName { get; init; }

    public int Copies { get; init; } = 1;

    public string PaperSize { get; init; } = "A4";

    public bool Grayscale { get; init; }

    public bool Duplex { get; init; }

    public DuplexFlip DuplexFlip { get; init; } = DuplexFlip.LongEdge;

    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;

    /// <summary>
    /// عدد صفحات المستند. مابيتبعتش للطابعة في أمر الطباعة نفسه.
    ///
    /// ⚠ بس مالوش «أثر صفر» زي ما التعليق ده كان بيقول — بيتحسب عليه
    /// حاجتين:
    ///
    ///   • مهلة انتظار الجوب (<c>SpoolTimeoutPolicy</c>)
    ///   • <see cref="PagesPerCopy"/> تحت على طول — وهي اللي التقسيم
    ///     لدفعات بيتبني عليها
    ///
    /// صفر معناها مش معروف.
    /// </summary>
    public int PageCount { get; init; }

    /// <summary>
    /// أول صفحة تتطبع. صفر = من أول المستند. شوف <see cref="PageRange"/>.
    /// </summary>
    public int FirstPage { get; init; }

    /// <summary>آخر صفحة تتطبع. صفر = لآخر المستند.</summary>
    public int LastPage { get; init; }
        /// <summary>
    /// صفحات النسخة الواحدة فعلًا — بعد حساب مدى الصفحات.
    ///
    /// مستند ١٨٠ صفحة بمدى «من ٥ لـ ٢٠» نسخته ١٦ صفحة مش ١٨٠. التقسيم
    /// بيتحسب على الرقم ده، فلو أخدنا <see cref="PageCount"/> على طول كنا
    /// هنقسّم أوردر صغير لجوبات كتير من غير أي داعي.
    ///
    /// صفر = مش عارفين.
    /// </summary>
    public int PagesPerCopy => PageRange.CountIn(FirstPage, LastPage, PageCount);

    /// <summary>بيبني أمر طباعة من إعدادات الجوب الحالية.</summary>
    public static PrintJob From(
        PrintSettings settings,
        string filePath,
        string printerName,
        int copies,
        int pageCount = 0)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new PrintJob
        {
            FilePath = filePath,
            PrinterName = printerName,
            Copies = copies,
            PaperSize = settings.PaperSize,
            Grayscale = settings.Grayscale,
            Duplex = settings.Duplex,
            DuplexFlip = settings.DuplexFlip,
            Orientation = settings.PageOrientation,
            PageCount = pageCount,
            FirstPage = settings.PageFrom,
            LastPage = settings.PageTo
        };
    }
}
