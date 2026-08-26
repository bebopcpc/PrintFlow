namespace PrintFlow.Domain;

/// <summary>
/// طلب تحويل صورة لملف PDF من صفحة واحدة.
///
/// قرار المقاس: الورقة **A4 باتجاه الصورة**، والصورة بتتحط جواها متوسّطة
/// ومحافظة على نسبتها.
///
/// ليه مش مقاس الصورة نفسه: صورة من موبايل ٤٠٣٢×٣٠٢٤ بكسل كانت هتطلع ورقة
/// بمقاس خرافي، والطابعة هتحاول تقصّها أو تصغّرها بطريقة مالهاش تحكّم.
/// A4 هو اللي هيتطبع عليه فعلًا، فخلي الملف يقول كده من الأول.
/// </summary>
public sealed record ImageConvertRequest
{
    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    /// <summary>هامش أبيض حوالين الصورة بالنقطة. صفر = لحد حرف الورقة.</summary>
    public int Margin { get; init; }

    /// <summary>A4 بالنقطة — الضلع القصير والطويل.</summary>
    public const double A4Short = 595;
    public const double A4Long = 842;

    /// <summary>
    /// مقاس الورقة المناسب لصورة بالأبعاد دي: A4 طولية للصورة الطولية،
    /// وعرضية للعرضية. الصورة المربعة بتاخد طولية (الافتراضي الشائع).
    /// </summary>
    public static (double Width, double Height) SheetFor(double imageWidth, double imageHeight)
        => imageWidth > imageHeight ? (A4Long, A4Short) : (A4Short, A4Long);

    /// <summary>
    /// مكان الصورة على الورقة: متوسّطة، محافظة على نسبتها، جوه الهامش.
    /// بيستخدم نفس <see cref="SheetLayout.FitInto"/> اللي التجميع بيستخدمه،
    /// فالسلوك واحد في كل حتة في البرنامج.
    /// </summary>
    public static SlideRect PlaceOn(
        double sheetWidth, double sheetHeight,
        double imageWidth, double imageHeight,
        int margin)
    {
        double safe = Math.Max(0, Math.Min(margin, Math.Min(sheetWidth, sheetHeight) / 2 - 1));

        var box = new SlideRect(safe, safe, sheetWidth - safe * 2, sheetHeight - safe * 2);

        return SheetLayout.FitInto(box, imageWidth, imageHeight);
    }
}
