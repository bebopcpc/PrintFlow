using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// حساب المنطقة المرئية من الورقة.
///
/// الباج اللي التستات دي بتمنع رجوعه: في التجربة الفعلية، مذكرة بخلفية ملونة
/// كاملة اتحطّ عليها ترقيم و**الترقيم اختفى**. السبب إن الملف MediaBox أكبر من
/// الـ CropBox، والترقيم كان بيترسم على حافة الـ MediaBox — يعني في الجزء
/// اللي القارئ أصلًا مش بيعرضه.
/// </summary>
public class VisiblePageAreaTests
{
    // A4 بالنقطة
    private const double Width = 595;
    private const double Height = 842;

    [Fact]
    public void No_CropBox_Means_The_Whole_Page_Is_Visible()
    {
        var area = VisiblePageArea.Calculate(
            mediaX1: 0, mediaY2: Height,
            cropX1: 0, cropY2: 0, cropWidth: 0, cropHeight: 0,
            pageWidth: Width, pageHeight: Height);

        Assert.Equal(new VisiblePageArea(0, 0, Width, Height), area);
    }

    /// <summary>
    /// الحالة الحقيقية: MediaBox 720×900 و CropBox داخلها بـ 36 نقطة من كل جهة.
    /// دي بالظبط أرقام الملف اللي الترقيم اختفى فيه.
    /// </summary>
    [Fact]
    public void Inset_CropBox_Shifts_The_Drawing_Origin()
    {
        var area = VisiblePageArea.Calculate(
            mediaX1: 0, mediaY2: 900,
            cropX1: 36, cropY2: 864, cropWidth: 648, cropHeight: 828,
            pageWidth: 720, pageHeight: 900);

        Assert.Equal(36, area.X);
        Assert.Equal(36, area.Y);       // 900 − 864، القلب من Y-طالعة لـ Y-نازلة
        Assert.Equal(648, area.Width);
        Assert.Equal(828, area.Height);
    }

    /// <summary>
    /// القلب في محور Y هو أكتر حتة سهل تتعمل غلط. CropBox ملزوقة في **أعلى**
    /// الورقة لازم تدّي Y = 0 في إحداثيات الرسم، مش Y = المسافة من تحت.
    /// </summary>
    [Fact]
    public void CropBox_At_The_Top_Of_The_Page_Starts_At_Y_Zero()
    {
        var area = VisiblePageArea.Calculate(
            mediaX1: 0, mediaY2: 842,
            cropX1: 0, cropY2: 842, cropWidth: 595, cropHeight: 400,
            pageWidth: Width, pageHeight: Height);

        Assert.Equal(0, area.Y);
        Assert.Equal(400, area.Height);
    }

    [Fact]
    public void CropBox_At_The_Bottom_Of_The_Page_Starts_Below_The_Top()
    {
        var area = VisiblePageArea.Calculate(
            mediaX1: 0, mediaY2: 842,
            cropX1: 0, cropY2: 400, cropWidth: 595, cropHeight: 400,
            pageWidth: Width, pageHeight: Height);

        Assert.Equal(442, area.Y);      // 842 − 400
    }

    /// <summary>
    /// MediaBox مش لازم تبدأ من الصفر. لو بدأت من 20، CropBox عند 56
    /// معناها إزاحة 36 نقطة مش 56.
    /// </summary>
    [Fact]
    public void Offset_MediaBox_Is_Subtracted_From_The_CropBox()
    {
        var area = VisiblePageArea.Calculate(
            mediaX1: 20, mediaY2: 862,
            cropX1: 56, cropY2: 826, cropWidth: 500, cropHeight: 700,
            pageWidth: Width, pageHeight: Height);

        Assert.Equal(36, area.X);
        Assert.Equal(36, area.Y);
    }

    [Fact]
    public void CropBox_Equal_To_The_MediaBox_Changes_Nothing()
    {
        var area = VisiblePageArea.Calculate(
            mediaX1: 0, mediaY2: Height,
            cropX1: 0, cropY2: Height, cropWidth: Width, cropHeight: Height,
            pageWidth: Width, pageHeight: Height);

        Assert.Equal(new VisiblePageArea(0, 0, Width, Height), area);
    }

    // ══════════ الملفات البايظة ══════════
    // القاعدة: أي حساب مايطلعش منطقي → نرجع للورقة كاملة.
    // أسوأ نتيجة تبقى إن الترقيم يرجع مكانه القديم، مش إنه يروح مكان مجهول.

    [Fact]
    public void CropBox_Sticking_Out_To_The_Left_Falls_Back_To_The_Whole_Page()
    {
        var area = VisiblePageArea.Calculate(
            mediaX1: 50, mediaY2: Height,
            cropX1: 0, cropY2: Height, cropWidth: 500, cropHeight: 700,
            pageWidth: Width, pageHeight: Height);

        Assert.Equal(new VisiblePageArea(0, 0, Width, Height), area);
    }

    [Fact]
    public void CropBox_Wider_Than_The_Page_Falls_Back_To_The_Whole_Page()
    {
        var area = VisiblePageArea.Calculate(
            mediaX1: 0, mediaY2: Height,
            cropX1: 100, cropY2: Height, cropWidth: Width, cropHeight: Height,
            pageWidth: Width, pageHeight: Height);

        Assert.Equal(new VisiblePageArea(0, 0, Width, Height), area);
    }

    [Fact]
    public void CropBox_Above_The_MediaBox_Falls_Back_To_The_Whole_Page()
    {
        var area = VisiblePageArea.Calculate(
            mediaX1: 0, mediaY2: 800,
            cropX1: 0, cropY2: 900, cropWidth: 500, cropHeight: 700,
            pageWidth: Width, pageHeight: Height);

        Assert.Equal(new VisiblePageArea(0, 0, Width, Height), area);
    }

    [Fact]
    public void Negative_CropBox_Size_Falls_Back_To_The_Whole_Page()
    {
        var area = VisiblePageArea.Calculate(
            mediaX1: 0, mediaY2: Height,
            cropX1: 0, cropY2: Height, cropWidth: -10, cropHeight: -10,
            pageWidth: Width, pageHeight: Height);

        Assert.Equal(new VisiblePageArea(0, 0, Width, Height), area);
    }

    /// <summary>
    /// فرق كسور بسيط (نص نقطة) مش خطأ — الملفات الحقيقية مليانة أرقام زي دي،
    /// ولو رفضناها هنرجع للورقة كاملة من غير داعي.
    /// </summary>
    [Fact]
    public void Sub_Point_Rounding_Is_Tolerated()
    {
        var area = VisiblePageArea.Calculate(
            mediaX1: 0, mediaY2: Height,
            cropX1: 0, cropY2: Height, cropWidth: Width + 0.5, cropHeight: Height + 0.5,
            pageWidth: Width, pageHeight: Height);

        Assert.Equal(Width + 0.5, area.Width);
    }
}
