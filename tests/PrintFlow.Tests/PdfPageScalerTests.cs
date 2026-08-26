using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PrintFlow.Domain;
using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// المقياس على ملفات PDF حقيقية.
///
/// الحساب نفسه متختبر بالأرقام في <see cref="PageScalingTests"/>.
/// اللي هنا بيتأكد إن الرسم بيستخدمه صح وإن الملف الطالع سليم.
/// </summary>
public class PdfPageScalerTests : IDisposable
{
    private readonly string _folder;
    private readonly PdfPageScaler _scaler = new();

    public PdfPageScalerTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PrintFlowScale_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    // ══════════ ١٠٠٪ = مافيش شغل ══════════

    [Fact]
    public void A_Hundred_Percent_Copies_The_File_Byte_For_Byte()
    {
        // ده مش تحسين — ده قرار جودة. إعادة رسم المستند بتعيد ترميز
        // المحتوى، والمستخدم اللي ماغيّرش المقياس مايستاهلش يدفع التمن.
        string source = MakePdf("src.pdf", 3);
        string output = Path.Combine(_folder, "out.pdf");

        var result = _scaler.Scale(new ScaleRequest { InputPath = source, OutputPath = output, Percent = 100 });

        Assert.True(result.Success, result.Message);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(output));
    }

    [Fact]
    public void The_Default_Percent_Is_A_Pass_Through()
    {
        string source = MakePdf("src.pdf", 2);
        string output = Path.Combine(_folder, "out.pdf");

        var result = _scaler.Scale(new ScaleRequest { InputPath = source, OutputPath = output });

        Assert.Contains("زي ما هو", result.Message);
    }

    // ══════════ الرسم ══════════

    [Theory]
    [InlineData(50)]
    [InlineData(70)]
    [InlineData(95)]
    [InlineData(130)]
    public void The_Page_Count_Never_Changes(int percent)
    {
        string source = MakePdf("src.pdf", 5);
        string output = Path.Combine(_folder, "out.pdf");

        var result = _scaler.Scale(new ScaleRequest { InputPath = source, OutputPath = output, Percent = percent });

        Assert.True(result.Success, result.Message);
        Assert.Equal(5, result.PageCount);
        Assert.Equal(5, PageCountOf(output));
    }

    [Fact]
    public void The_Sheet_Size_Never_Changes()
    {
        // ده المعنى كله: الورقة زي ما هي والمحتوى بس هو اللي بيصغّر
        string source = MakePdf("src.pdf", 1);
        string output = Path.Combine(_folder, "out.pdf");

        _scaler.Scale(new ScaleRequest { InputPath = source, OutputPath = output, Percent = 60 });

        using var before = PdfReader.Open(source, PdfDocumentOpenMode.Import);
        using var after = PdfReader.Open(output, PdfDocumentOpenMode.Import);

        Assert.Equal(before.Pages[0].Width.Point, after.Pages[0].Width.Point, 2);
        Assert.Equal(before.Pages[0].Height.Point, after.Pages[0].Height.Point, 2);
    }

    [Fact]
    public void Every_Source_Page_Is_Drawn_Once_Not_The_First_One_Repeated()
    {
        // نفس الباج اللي اتصاد في المُجمّع: XPdfForm واحد بيتعاد استخدامه،
        // ولو PageNumber مااتغيّرتش كل الورق بيطلع نسخة من أول صفحة
        string source = MakePdf("src.pdf", 4);
        string output = Path.Combine(_folder, "out.pdf");

        _scaler.Scale(new ScaleRequest { InputPath = source, OutputPath = output, Percent = 80 });

        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);

        var drawn = new HashSet<string>();

        for (int i = 0; i < document.PageCount; i++)
        {
            var xobjects = document.Pages[i].Elements
                .GetDictionary("/Resources")?.Elements
                .GetDictionary("/XObject");

            Assert.NotNull(xobjects);

            foreach (var item in xobjects!.Elements.Values)
            {
                drawn.Add(item.ToString() ?? "");
            }
        }

        Assert.Equal(4, drawn.Count);
    }

    [Fact]
    public void Pages_Of_Different_Sizes_Each_Keep_Their_Own_Size()
    {
        // مستند فيه A4 و A5 مع بعض — كل صفحة لازم تفضل بمقاسها
        string source = Path.Combine(_folder, "mixed.pdf");

        using (var document = new PdfDocument())
        {
            var big = document.AddPage();
            big.Width = XUnit.FromPoint(595);
            big.Height = XUnit.FromPoint(842);

            var small = document.AddPage();
            small.Width = XUnit.FromPoint(420);
            small.Height = XUnit.FromPoint(595);

            document.Save(source);
        }

        string output = Path.Combine(_folder, "out.pdf");
        _scaler.Scale(new ScaleRequest { InputPath = source, OutputPath = output, Percent = 75 });

        using var result = PdfReader.Open(output, PdfDocumentOpenMode.Import);

        Assert.Equal(595, result.Pages[0].Width.Point, 1);
        Assert.Equal(420, result.Pages[1].Width.Point, 1);
    }

    // ══════════ لما الدنيا تبوظ ══════════

    [Fact]
    public void A_Missing_File_Fails_By_Name_Not_By_Exception()
    {
        var result = _scaler.Scale(new ScaleRequest
        {
            InputPath = Path.Combine(_folder, "ghost.pdf"),
            OutputPath = Path.Combine(_folder, "out.pdf")
        });

        Assert.False(result.Success);
        Assert.Contains("ghost.pdf", result.Message);
    }

    [Fact]
    public void A_Corrupt_File_Fails_By_Name_Not_By_Exception()
    {
        string source = Path.Combine(_folder, "bad.pdf");
        File.WriteAllText(source, "ده مش PDF خالص");

        var result = _scaler.Scale(new ScaleRequest
        {
            InputPath = source,
            OutputPath = Path.Combine(_folder, "out.pdf"),
            Percent = 80
        });

        Assert.False(result.Success);
        Assert.Contains("bad.pdf", result.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(99999)]
    public void A_Silly_Percentage_Is_Clamped_Not_Rejected(int percent)
    {
        string source = MakePdf("src.pdf", 1);
        string output = Path.Combine(_folder, "out.pdf");

        var result = _scaler.Scale(new ScaleRequest
        {
            InputPath = source, OutputPath = output, Percent = percent
        });

        Assert.True(result.Success, result.Message);
    }

    // ══════════ مساعدات ══════════

    private string MakePdf(string name, int pages)
    {
        string path = Path.Combine(_folder, name);

        using var document = new PdfDocument();
        var font = new XFont("Arial", 60);

        for (int i = 0; i < pages; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);

            gfx.DrawString(
                $"{i + 1}", font, XBrushes.Black,
                new XRect(0, 0, page.Width.Point, page.Height.Point),
                XStringFormats.Center);
        }

        document.Save(path);
        return path;
    }

    private static int PageCountOf(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }
}
