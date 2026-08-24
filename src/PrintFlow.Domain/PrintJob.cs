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
    /// عدد صفحات المستند. مالوش أي أثر على أمر الطباعة نفسه —
    /// بنستخدمه بس عشان نحسب مهلة انتظار معقولة للجوب (SpoolTimeoutPolicy).
    /// صفر معناها مش معروف.
    /// </summary>
    public int PageCount { get; init; }

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
            PageCount = pageCount
        };
    }
}
