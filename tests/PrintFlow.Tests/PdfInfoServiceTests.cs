using PdfSharp.Pdf;
using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// قراءة عدد صفحات كل ملف في القايمة.
///
/// القاعدة هنا زي مخزن الإعدادات واللوج بالظبط: **عمره ما يرمي استثناء**.
/// الميثود دي بتتنادى على كل ملف المستخدم بيرميه، ومن ضمنهم أكيد هيبقى في
/// ملف تالف أو محمي. لو رمت، القايمة كلها بتقع بسبب ملف واحد.
/// </summary>
public class PdfInfoServiceTests : IDisposable
{
    private readonly string _folder;
    private readonly PdfInfoService _service = new();

    public PdfInfoServiceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PrintFlowInfo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(210)]
    public void Counts_The_Pages_Of_A_Healthy_File(int pages)
    {
        Assert.Equal(pages, _service.TryGetPageCount(MakePdf("سليم.pdf", pages)));
    }

    /// <summary>ماينفعش يقفل الملف مقفول ورا كده — الدمج بعدها هيفشل بـ "الملف مستخدم".</summary>
    [Fact]
    public void The_File_Is_Not_Left_Locked()
    {
        string path = MakePdf("مقفول.pdf", 2);

        _service.TryGetPageCount(path);

        // لو الملف لسه مفتوح، السطر ده هيرمي
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    // ══════════ الحالات اللي بتحصل فعلًا في مطبعة ══════════

    [Fact]
    public void A_Missing_File_Returns_Null_Not_An_Exception()
    {
        Assert.Null(_service.TryGetPageCount(Path.Combine(_folder, "مش_موجود.pdf")));
    }

    [Fact]
    public void A_Corrupt_File_Returns_Null()
    {
        string path = Path.Combine(_folder, "بايظ.pdf");
        File.WriteAllText(path, "%PDF-1.7 ده مش PDF حقيقي");

        Assert.Null(_service.TryGetPageCount(path));
    }

    [Fact]
    public void An_Empty_File_Returns_Null()
    {
        string path = Path.Combine(_folder, "فاضي.pdf");
        File.WriteAllBytes(path, Array.Empty<byte>());

        Assert.Null(_service.TryGetPageCount(path));
    }

    [Fact]
    public void A_Password_Protected_File_Returns_Null()
    {
        var document = new PdfDocument();
        document.AddPage();
        document.AddPage();
        document.SecuritySettings.UserPassword = "1234";

        string path = Path.Combine(_folder, "مقفول_بباسورد.pdf");
        document.Save(path);

        Assert.Null(_service.TryGetPageCount(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\0مسار::غلط<>|")]
    public void A_Nonsense_Path_Returns_Null(string path)
    {
        Assert.Null(_service.TryGetPageCount(path));
    }

    private string MakePdf(string name, int pages)
    {
        var document = new PdfDocument();

        for (int i = 0; i < pages; i++)
        {
            document.AddPage();
        }

        string path = Path.Combine(_folder, name);
        document.Save(path);
        return path;
    }
}
