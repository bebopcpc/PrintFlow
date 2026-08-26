namespace PrintFlow.Domain;

/// <summary>
/// طلب تجميع شرائح: مستند داخل، ومستند طالع كل ورقة فيه عليها أكتر من صفحة.
///
/// نفس فكرة MergeRequest: عقد واحد بدل براميترات بتزيد مع كل خيار جديد.
/// </summary>
public sealed record SlideRequest
{
    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    /// <summary>عدد الصفحات على الورقة الواحدة. ١ = عدّي المستند زي ما هو.</summary>
    public int SlidesPerSheet { get; init; } = 1;

    /// <summary>شكل الورقة الناتجة. بيتحسب من مقاس الصفحة الأصلية بالدوران لو لزم.</summary>
    public PageOrientation SheetOrientation { get; init; } = PageOrientation.Portrait;

    public SlideOrder Order { get; init; } = SlideOrder.Horizontal;

    /// <summary>الافتراضي يمين — المستندات العربية بتتقرا من اليمين.</summary>
    public SlideStart Start { get; init; } = SlideStart.Right;

    /// <summary>الهامش حوالين كل شريحة بالنقطة.</summary>
    public int Margin { get; init; } = 15;

    /// <summary>إطار حوالين الصفحة نفسها — مش حوالين الخلية.</summary>
    public bool DrawBorder { get; init; }

    public string BorderColorHex { get; init; } = "#808080";

    /// <summary>
    /// كتيّب: صفحتين على كل وجه، بترتيب مخصوص عشان الطي يطلع مظبوط.
    /// بيتجاهل عدد الشرائح واتجاه الورقة — الكتيّب ليه شكل واحد.
    /// </summary>
    public bool Booklet { get; init; }

    public BookletStart BookletStart { get; init; } = BookletStart.Right;

    /// <summary>
    /// مفيش تجميع أصلًا — المستند بيعدّي زي ما هو من غير إعادة رسم.
    ///
    /// **لازم ده يبقى المصدر الوحيد للقرار ده في البرنامج كله.**
    /// في 1.6.1 كان الـ ViewModel بيسأل <c>SlidesPerSheet &lt;= 1</c> بنفسه
    /// قبل ما ينده المُجمّع — فوضع الكتيّب لوحده (وعدد الشرائح ١، وهو
    /// الافتراضي) مكانش بيوصل للمُجمّع خالص وكان بيطبع عادي، والملخص في
    /// الواجهة يقول إنه شغال. أي سؤال عن "نجمّع ولا لأ" بيعدّي من هنا.
    /// </summary>
    public bool IsPassThrough => !Booklet && SlidesPerSheet <= 1;

    public static SlideRequest From(PrintSettings settings, string inputPath, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new SlideRequest
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            SlidesPerSheet = settings.SlidesPerSheet,
            SheetOrientation = settings.SlideOrientation,
            Order = settings.SlideOrder,
            Start = settings.SlideStart,
            Margin = settings.SlideMargin,
            DrawBorder = settings.DrawSlideBorder,
            Booklet = settings.BookletMode,
            BookletStart = settings.BookletStart
        };
    }
}
