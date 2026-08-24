using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PrintFlow.Domain;
using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// الحالات اللي بتحصل فعلًا في مطبعة: ملف محمي بباسورد، ملف تالف، ملف فاضي.
///
/// المطلوب حاجتين: البرنامج **مايقعش**، والرسالة **تقول اسم الملف** —
/// لأن اللي واقف على الماكينة محمّل 20 ملف ومحتاج يعرف أنهي واحد فيهم.
/// </summary>
public class PdfMergeServiceTests : IDisposable
{
    private readonly string _folder;
    private readonly PdfMergeService _service = new();

    public PdfMergeServiceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PrintFlowMerge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    // ══════════ المسار السليم ══════════

    [Fact]
    public void Merges_Two_Files_And_Reports_The_Page_Count()
    {
        string a = MakePdf("a.pdf", 3);
        string b = MakePdf("b.pdf", 5);

        var result = Merge(new[] { a, b });

        Assert.True(result.Success);
        Assert.Equal(8, result.PageCount);
        Assert.True(File.Exists(Output()));
    }

    /// <summary>
    /// PdfSharp بيقفل المستند بعد Save، فقراية PageCount بعده بترمي استثناء.
    /// التست ده بيمنع رجوع الباج ده — كان بيخلي **كل** عملية دمج تفشل.
    /// </summary>
    [Fact]
    public void Page_Count_Is_Read_Before_Saving()
    {
        var result = Merge(new[] { MakePdf("a.pdf", 4) });

        Assert.True(result.Success);
        Assert.Equal(4, result.PageCount);
        Assert.DoesNotContain("already saved", result.Message);
    }

    /// <summary>
    /// الترقيم المتصل هو الافتراضي: ٣ صفحات + ٥ صفحات = "١ من ٨" لحد "٨ من ٨".
    /// قبل كده كان الوضع "كل ملف من الأول" مكتوب في الكود، فملفين كل واحد صفحة
    /// كانوا بيطلعوا "صفحة 1 من 1" مرتين — وده اللي المستخدم بلّغه كباج.
    /// </summary>
    [Fact]
    public void Continuous_Numbering_Is_The_Default()
    {
        var request = MergeRequest.From(
            new PrintSettings { NumberPagesPerFile = true },
            new AppSettings(),
            new[] { MakePdf("a.pdf", 3), MakePdf("b.pdf", 5) },
            Output());

        Assert.False(request.PageNumbers!.RestartForEachFile);
        Assert.True(_service.Merge(request).Success);
    }

    /// <summary>
    /// ملف MediaBox أكبر من CropBox — شكل شائع جدًا في ملفات المطابع (هوامش قص).
    /// الترقيم كان بيترسم على حافة الـ MediaBox، يعني في جزء القارئ أصلًا مش
    /// بيعرضه، فكان بيختفي خالص. المفروض دلوقتي يترسم جوه الـ CropBox.
    /// </summary>
    [Fact]
    public void A_Page_With_A_Smaller_CropBox_Is_Merged_And_Numbered()
    {
        string cropped = MakeCroppedPdf("مقصوص.pdf", pages: 2);

        var result = Merge(new[] { cropped });

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.PageCount);

        // الصناديق نفسها لازم تعدي زي ما هي للمخرج، وإلا القارئ هيعرض الورقة غلط
        var output = PdfReader.Open(Output(), PdfDocumentOpenMode.Import);
        var page = output.Pages[0];

        Assert.Equal(720, page.MediaBox.Width);
        Assert.Equal(648, page.CropBox.Width);
    }

    /// <summary>
    /// الحساب نفسه متختبر بالتفصيل في VisiblePageAreaTests. اللي هنا بيتأكد إن
    /// PdfSharp فعلًا بيرجّع الـ MediaBox في Width/Height مش الـ CropBox —
    /// لأن الحساب كله مبني على الافتراض ده.
    /// </summary>
    [Fact]
    public void Page_Width_Reports_The_MediaBox_Not_The_CropBox()
    {
        string cropped = MakeCroppedPdf("مقصوص.pdf", pages: 1);
        var document = PdfReader.Open(cropped, PdfDocumentOpenMode.Import);
        var page = document.Pages[0];

        Assert.Equal(720, page.Width.Point);
        Assert.Equal(900, page.Height.Point);
        Assert.Equal(648, page.CropBox.Width);
    }

    // ══════════ الحالات الحدّية ══════════

    [Fact]
    public void Password_Protected_File_Fails_Cleanly_And_Names_The_File()
    {
        string locked = MakeProtectedPdf("مستند_سري.pdf");

        var result = Merge(new[] { locked });

        Assert.False(result.Success);
        Assert.Contains("مستند_سري.pdf", result.Message);
        Assert.Contains("كلمة مرور", result.Message);
    }

    [Fact]
    public void Corrupt_File_Fails_Cleanly_And_Names_The_File()
    {
        string corrupt = Path.Combine(_folder, "بايظ.pdf");
        File.WriteAllText(corrupt, "%PDF-1.7 ده مش PDF حقيقي");

        var result = Merge(new[] { corrupt });

        Assert.False(result.Success);
        Assert.Contains("بايظ.pdf", result.Message);
        Assert.Contains("تالف", result.Message);
    }

    [Fact]
    public void Empty_File_Fails_Cleanly()
    {
        string empty = Path.Combine(_folder, "فاضي.pdf");
        File.WriteAllBytes(empty, Array.Empty<byte>());

        var result = Merge(new[] { empty });

        Assert.False(result.Success);
        Assert.Contains("فاضي.pdf", result.Message);
    }

    /// <summary>
    /// أهم حالة عمليًا: 20 ملف سليم وواحد بايظ. لازم الرسالة تشاور على البايظ بالاسم.
    /// </summary>
    [Fact]
    public void One_Bad_File_Among_Good_Ones_Is_Named_Explicitly()
    {
        string good1 = MakePdf("سليم1.pdf", 2);
        string good2 = MakePdf("سليم2.pdf", 2);
        string bad = Path.Combine(_folder, "المشكلة.pdf");
        File.WriteAllText(bad, "not a pdf");

        var result = Merge(new[] { good1, good2, bad });

        Assert.False(result.Success);
        Assert.Contains("المشكلة.pdf", result.Message);
        Assert.DoesNotContain("سليم1.pdf", result.Message);
    }

    [Fact]
    public void Missing_File_Is_Named_Too()
    {
        var result = Merge(new[] { Path.Combine(_folder, "مش_موجود.pdf") });

        Assert.False(result.Success);
        Assert.Contains("مش_موجود.pdf", result.Message);
    }

    [Fact]
    public void Empty_Input_List_Fails_Without_Touching_The_Disk()
    {
        var result = Merge(Array.Empty<string>());

        Assert.False(result.Success);
        Assert.False(File.Exists(Output()));
    }

    // ══════════ مساعدات ══════════

    private string Output() => Path.Combine(_folder, "out.pdf");

    private MergeResult Merge(IReadOnlyList<string> inputs) =>
        _service.Merge(MergeRequest.From(
            new PrintSettings { NumberPagesPerFile = true },
            new AppSettings { WatermarkEnabled = true, WatermarkText = "تجربة" },
            inputs,
            Output()));

    private string MakePdf(string name, int pages)
    {
        var document = new PdfDocument();

        for (int i = 0; i < pages; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"page {i + 1}", new XFont("Arial", 12), XBrushes.Black,
                new XRect(0, 50, page.Width.Point, 20), XStringFormats.Center);
        }

        string path = Path.Combine(_folder, name);
        document.Save(path);
        return path;
    }

    /// <summary>ملف MediaBox 720×900 و CropBox داخلها بـ 36 نقطة من كل جهة، بخلفية ملونة كاملة.</summary>
    private string MakeCroppedPdf(string name, int pages)
    {
        var document = new PdfDocument();

        for (int i = 0; i < pages; i++)
        {
            var page = document.AddPage();
            page.MediaBox = new PdfRectangle(new XRect(0, 0, 720, 900));
            page.CropBox = new PdfRectangle(new XRect(36, 36, 648, 828));

            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(XBrushes.DarkBlue, new XRect(0, 0, 720, 900));
        }

        string path = Path.Combine(_folder, name);
        document.Save(path);
        return path;
    }

    private string MakeProtectedPdf(string name)
    {
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawString("secret", new XFont("Arial", 12), XBrushes.Black,
                new XRect(0, 50, page.Width.Point, 20), XStringFormats.Center);
        }

        document.SecuritySettings.UserPassword = "1234";

        string path = Path.Combine(_folder, name);
        document.Save(path);
        return path;
    }
}
