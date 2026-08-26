using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PrintFlow.Domain;
using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// تحويل الصور لـ PDF على صور حقيقية.
///
/// الحساب (مقاس الورقة ومكان الصورة) متختبر بالأرقام في
/// <see cref="SupportedInputTests"/>. اللي هنا بيتأكد إن PdfSharp فعلًا
/// بيقرا الصيغ اللي إحنا مدّعينها.
///
/// الصور بتتعمل هنا بـ PdfSharp نفسه؟ لأ — بنكتب بايتات BMP بالإيد.
/// ده مقصود: التست ماينفعش يعتمد على أي مكتبة صور تانية، عشان لو التحويل
/// وقع نعرف إن العيب في التحويل مش في اللي عمل الصورة.
/// </summary>
public class ImageToPdfConverterTests : IDisposable
{
    private readonly string _folder;
    private readonly ImageToPdfConverter _converter = new();

    public ImageToPdfConverterTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PrintFlowImages_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    // ══════════ التحويل ══════════

    [Fact]
    public void An_Image_Becomes_A_One_Page_Pdf()
    {
        string image = MakeBmp("a.bmp", 400, 300);
        string output = Path.Combine(_folder, "a.pdf");

        var result = _converter.Convert(new ImageConvertRequest { InputPath = image, OutputPath = output });

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.PageCount);
        Assert.Equal(1, PageCountOf(output));
    }

    [Fact]
    public void A_Portrait_Image_Gets_A_Portrait_Page()
    {
        string image = MakeBmp("p.bmp", 300, 400);
        string output = Path.Combine(_folder, "p.pdf");

        _converter.Convert(new ImageConvertRequest { InputPath = image, OutputPath = output });

        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);

        Assert.True(document.Pages[0].Height.Point > document.Pages[0].Width.Point);
    }

    [Fact]
    public void A_Landscape_Image_Gets_A_Landscape_Page()
    {
        string image = MakeBmp("l.bmp", 400, 300);
        string output = Path.Combine(_folder, "l.pdf");

        _converter.Convert(new ImageConvertRequest { InputPath = image, OutputPath = output });

        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);

        Assert.True(document.Pages[0].Width.Point > document.Pages[0].Height.Point);
    }

    [Fact]
    public void The_Page_Is_Always_A4_Not_The_Image_Size()
    {
        // صورة موبايل بمقاس كبير ماينفعش تطلع ورقة بمقاس خرافي —
        // الطابعة عندها A4 وخلاص
        string image = MakeBmp("huge.bmp", 4032, 3024);
        string output = Path.Combine(_folder, "huge.pdf");

        _converter.Convert(new ImageConvertRequest { InputPath = image, OutputPath = output });

        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);

        Assert.Equal(ImageConvertRequest.A4Long, document.Pages[0].Width.Point, 1);
        Assert.Equal(ImageConvertRequest.A4Short, document.Pages[0].Height.Point, 1);
    }

    [Fact]
    public void The_Image_Actually_Lands_On_The_Page()
    {
        // من غير ده التست كان هيعدّي على ورقة بيضا فاضية
        string image = MakeBmp("a.bmp", 400, 300);
        string output = Path.Combine(_folder, "a.pdf");

        _converter.Convert(new ImageConvertRequest { InputPath = image, OutputPath = output });

        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);

        var xobjects = document.Pages[0].Elements
            .GetDictionary("/Resources")?.Elements
            .GetDictionary("/XObject");

        Assert.NotNull(xobjects);
        Assert.NotEmpty(xobjects!.Elements);
    }

    // ══════════ الصيغ اللي بندّعيها ══════════

    [Fact]
    public void Every_Extension_We_Claim_Is_A_Real_Image_Kind()
    {
        // الحاجز اللي بيمنع إننا نضيف صيغة للقايمة ونكتشف بعدين إن
        // PdfSharp مابيقراهاش. PdfSharp 6.2.4 جواه تلات قارئات بس.
        Assert.Equal(4, SupportedInput.ImageExtensions.Count);

        foreach (string extension in SupportedInput.ImageExtensions)
        {
            Assert.Equal(InputKind.Image, SupportedInput.KindOf("x" + extension));
        }
    }

    // ══════════ لما الدنيا تبوظ ══════════

    [Fact]
    public void A_Missing_Image_Fails_By_Name()
    {
        var result = _converter.Convert(new ImageConvertRequest
        {
            InputPath = Path.Combine(_folder, "ghost.jpg"),
            OutputPath = Path.Combine(_folder, "out.pdf")
        });

        Assert.False(result.Success);
        Assert.Contains("ghost.jpg", result.Message);
    }

    [Fact]
    public void A_Corrupt_Image_Gives_Arabic_Advice_Not_An_English_Exception()
    {
        // رسالة PdfSharp هنا "Unsupported image format" — صحيحة تقنيًا
        // وملهاش أي معنى للي واقف على الماكينة
        string image = Path.Combine(_folder, "bad.jpg");
        File.WriteAllText(image, "دي مش صورة خالص");

        var result = _converter.Convert(new ImageConvertRequest
        {
            InputPath = image,
            OutputPath = Path.Combine(_folder, "out.pdf")
        });

        Assert.False(result.Success);
        Assert.Contains("bad.jpg", result.Message);
        Assert.DoesNotContain("Unsupported image format", result.Message);
    }

    // ══════════ مساعدات ══════════

    /// <summary>
    /// بيكتب ملف BMP بايت بايت: هيدر ١٤ بايت + هيدر معلومات ٤٠ بايت +
    /// بيكسلات ٢٤ بت. صفوف الـ BMP مرصوصة من تحت لفوق وكل صف بيتكمّل
    /// لمضاعفات ٤ بايت.
    /// </summary>
    private string MakeBmp(string name, int width, int height)
    {
        string path = Path.Combine(_folder, name);

        int rowSize = ((width * 3) + 3) / 4 * 4;
        int pixelBytes = rowSize * height;
        int fileSize = 54 + pixelBytes;

        var bytes = new byte[fileSize];

        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BitConverter.GetBytes(fileSize).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);

        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(width).CopyTo(bytes, 18);
        BitConverter.GetBytes(height).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        BitConverter.GetBytes(pixelBytes).CopyTo(bytes, 34);

        // تدرّج بسيط عشان الصورة تبقى فيها حاجة فعلًا
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = 54 + (y * rowSize) + (x * 3);
                bytes[offset] = (byte)(x % 256);
                bytes[offset + 1] = (byte)(y % 256);
                bytes[offset + 2] = 128;
            }
        }

        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static int PageCountOf(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }
}
