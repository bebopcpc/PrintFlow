using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PrintFlow.Domain;
using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// تجميع الشرائح على الورق — الرسم الفعلي على ملفات PDF حقيقية.
///
/// الحسابات نفسها متختبرة بالأرقام في <see cref="SheetLayoutTests"/>.
/// اللي هنا بيتأكد إن الرسم بيستخدمها صح وإن الملف الطالع سليم.
/// </summary>
public class PdfSlideComposerTests : IDisposable
{
    private readonly string _folder;
    private readonly PdfSlideComposer _composer = new();

    public PdfSlideComposerTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PrintFlowSlides_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    // ══════════ عدد الورق ══════════

    [Theory]
    [InlineData(8, 8, 1)]
    [InlineData(12, 6, 2)]
    [InlineData(10, 6, 2)]    // ورقة ناقصة
    [InlineData(1, 4, 1)]
    [InlineData(210, 6, 35)]
    public void The_Right_Number_Of_Sheets_Comes_Out(int pages, int perSheet, int expectedSheets)
    {
        var result = Compose(MakePdf("src.pdf", pages), perSheet);

        Assert.True(result.Success, result.Message);
        Assert.Equal(expectedSheets, result.PageCount);
        Assert.Equal(expectedSheets, PageCountOf(Output()));
    }

    /// <summary>
    /// أخطر باج ممكن يحصل هنا: كل الشرائح تطلع **نفس الصفحة** متكررة.
    /// ده كان هيبان زي الشغل السليم في عدد الورق وفي الحجم، ومحدش هيكتشفه
    /// غير لما الورق يطلع من الماكينة.
    ///
    /// كل صفحة مصدر بتتحط ككائن منفصل في موارد الورقة، فبنعدّهم.
    /// </summary>
    [Fact]
    public void Every_Slot_Holds_A_Different_Page_Not_The_Same_One_Repeated()
    {
        Compose(MakePdf("src.pdf", 8), slidesPerSheet: 8);

        Assert.Equal(8, DistinctPagesOnSheet(Output(), sheetIndex: 0));
    }

    /// <summary>آخر ورقة ناقصة بتاخد اللي فاضل بس، والخلايا الباقية بتفضل فاضية.</summary>
    [Fact]
    public void A_Partly_Filled_Last_Sheet_Only_Holds_What_Is_Left()
    {
        Compose(MakePdf("src.pdf", 10), slidesPerSheet: 6);

        Assert.Equal(6, DistinctPagesOnSheet(Output(), sheetIndex: 0));
        Assert.Equal(4, DistinctPagesOnSheet(Output(), sheetIndex: 1));
    }

    // ══════════ مقاس الورقة ══════════

    [Fact]
    public void A_Portrait_Sheet_Keeps_The_Original_Proportions()
    {
        Compose(MakePdf("src.pdf", 4), 4, PageOrientation.Portrait);

        var sheet = FirstSheet();
        Assert.True(sheet.Height.Point > sheet.Width.Point, "المفروض طولية");
    }

    /// <summary>عرضية = نفس المقاس مقلوب، مش مقاس تاني خالص.</summary>
    [Fact]
    public void A_Landscape_Sheet_Is_The_Same_Paper_Turned_Round()
    {
        string source = MakePdf("src.pdf", 4);
        var original = PdfReader.Open(source, PdfDocumentOpenMode.Import).Pages[0];
        double longSide = Math.Max(original.Width.Point, original.Height.Point);
        double shortSide = Math.Min(original.Width.Point, original.Height.Point);

        Compose(source, 4, PageOrientation.Landscape);

        var sheet = FirstSheet();
        Assert.Equal(longSide, sheet.Width.Point, 1);
        Assert.Equal(shortSide, sheet.Height.Point, 1);
    }

    // ══════════ شريحة واحدة = عدّي زي ما هو ══════════

    /// <summary>
    /// قرار المطبعة: شريحة واحدة على الورقة معناها مفيش تجميع أصلًا،
    /// فمالوش لازمة نعيد رسم المستند ونضيّع جودته. لازم يعدّي زي ما هو
    /// **بايت بايت**.
    /// </summary>
    [Fact]
    public void One_Slide_Per_Sheet_Copies_The_File_Untouched()
    {
        string source = MakePdf("src.pdf", 5);

        var result = Compose(source, slidesPerSheet: 1);

        Assert.True(result.Success, result.Message);
        Assert.Equal(5, result.PageCount);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Output()));
    }

    [Fact]
    public void Zero_Or_Negative_Slides_Is_Treated_As_One()
    {
        string source = MakePdf("src.pdf", 3);

        Assert.True(Compose(source, slidesPerSheet: 0).Success);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Output()));
    }

    // ══════════ الإطار ══════════

    [Fact]
    public void The_Border_Does_Not_Change_How_Many_Sheets_Come_Out()
    {
        string source = MakePdf("src.pdf", 12);

        var without = Compose(source, 6, border: false);
        var with = Compose(source, 6, border: true);

        Assert.Equal(without.PageCount, with.PageCount);
        Assert.True(with.Success);
    }

    // ══════════ الملفات البايظة ══════════

    [Fact]
    public void A_Missing_File_Fails_Cleanly_And_Names_It()
    {
        var result = Compose(Path.Combine(_folder, "مش_موجود.pdf"), 4);

        Assert.False(result.Success);
        Assert.Contains("مش_موجود.pdf", result.Message);
        Assert.False(File.Exists(Output()));
    }

    [Fact]
    public void A_Corrupt_File_Fails_Cleanly_And_Names_It()
    {
        string corrupt = Path.Combine(_folder, "بايظ.pdf");
        File.WriteAllText(corrupt, "%PDF-1.7 ده مش PDF حقيقي");

        var result = Compose(corrupt, 4);

        Assert.False(result.Success);
        Assert.Contains("بايظ.pdf", result.Message);
    }

    [Fact]
    public void An_Empty_File_Fails_Cleanly()
    {
        string empty = Path.Combine(_folder, "فاضي.pdf");
        File.WriteAllBytes(empty, Array.Empty<byte>());

        var result = Compose(empty, 4);

        Assert.False(result.Success);
        Assert.Contains("فاضي.pdf", result.Message);
    }

    /// <summary>هامش غبي مايكسرش الملف — بيتصغّر والورق يطلع سليم.</summary>
    [Fact]
    public void An_Absurd_Margin_Still_Produces_A_Valid_File()
    {
        var result = Compose(MakePdf("src.pdf", 4), 4, margin: 5000);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, PageCountOf(Output()));
    }

    /// <summary>
    /// مستندات المطابع المدموجة بيبقى فيها مقاسات مختلفة (A4 مع A3 مسكانّر).
    /// المفروض كلها تتظبط في خلاياها من غير ما حاجة تتكسر.
    /// </summary>
    [Fact]
    public void Pages_Of_Different_Sizes_All_Fit_Their_Cells()
    {
        var document = new PdfDocument();
        foreach (var (w, h) in new[] { (595.0, 842.0), (842.0, 1191.0), (420.0, 595.0), (595.0, 842.0) })
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(w);
            page.Height = XUnit.FromPoint(h);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(XBrushes.LightGray, new XRect(0, 0, w, h));
        }

        string mixed = Path.Combine(_folder, "مقاسات_مختلفة.pdf");
        document.Save(mixed);

        var result = Compose(mixed, 4);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.PageCount);
        Assert.Equal(4, DistinctPagesOnSheet(Output(), 0));
    }

    // ══════════ الكتيّب ══════════

    [Theory]
    [InlineData(8, 4)]     // ورقتين × وش وضهر
    [InlineData(4, 2)]     // ورقة واحدة
    [InlineData(6, 4)]     // ورقتين، فيهم فاضي
    [InlineData(40, 20)]
    public void A_Booklet_Produces_Two_Sides_Per_Sheet(int pages, int expectedSides)
    {
        var result = Booklet(MakePdf("src.pdf", pages));

        Assert.True(result.Success, result.Message);
        Assert.Equal(expectedSides, PageCountOf(Output()));
    }

    /// <summary>الكتيّب بيتطوى من النص، فالورقة لازم تبقى عرضية مهما كان الأصل.</summary>
    [Fact]
    public void The_Booklet_Sheet_Is_Always_Landscape()
    {
        Booklet(MakePdf("src.pdf", 8));

        var sheet = FirstSheet();
        Assert.True(sheet.Width.Point > sheet.Height.Point);
    }

    /// <summary>كل وجه فيه صفحتين مختلفتين — مش نفس الصفحة مرتين.</summary>
    [Fact]
    public void Each_Side_Carries_Two_Different_Pages()
    {
        Booklet(MakePdf("src.pdf", 8));

        for (int side = 0; side < 4; side++)
        {
            Assert.Equal(2, DistinctPagesOnSheet(Output(), side));
        }
    }

    /// <summary>
    /// ٦ صفحات على ورقتين = خانتين فاضيتين. الوجه الأول لازم يبقى فيه
    /// صفحة واحدة بس — الخانة التانية فاضية فعلًا مش مرسوم فيها حاجة.
    /// </summary>
    [Fact]
    public void Blank_Slots_Are_Really_Left_Empty()
    {
        Booklet(MakePdf("src.pdf", 6));

        Assert.Equal(1, DistinctPagesOnSheet(Output(), 0));
        Assert.Equal(1, DistinctPagesOnSheet(Output(), 1));
        Assert.Equal(2, DistinctPagesOnSheet(Output(), 2));
        Assert.Equal(2, DistinctPagesOnSheet(Output(), 3));
    }

    /// <summary>الرسالة لازم تقول إن في صفحات فاضية عشان اللي بيطبع ياخد باله.</summary>
    [Fact]
    public void The_Operator_Is_Told_About_The_Blank_Pages()
    {
        var withBlanks = Booklet(MakePdf("ناقص.pdf", 6));
        var exact = Booklet(MakePdf("مظبوط.pdf", 8));

        Assert.Contains("فاضية", withBlanks.Message);
        Assert.DoesNotContain("فاضية", exact.Message);
    }

    /// <summary>الكتيّب بيتجاهل عدد الشرائح — شكله واحد مهما كان الإعداد.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(16)]
    public void The_Slide_Count_Setting_Does_Not_Affect_A_Booklet(int slidesPerSheet)
    {
        var result = _composer.Compose(new SlideRequest
        {
            InputPath = MakePdf("src.pdf", 8),
            OutputPath = Output(),
            Booklet = true,
            SlidesPerSheet = slidesPerSheet,
            Margin = 10
        });

        Assert.True(result.Success, result.Message);
        Assert.Equal(4, PageCountOf(Output()));
    }

    [Fact]
    public void A_Corrupt_File_Fails_Cleanly_In_Booklet_Mode_Too()
    {
        string corrupt = Path.Combine(_folder, "بايظ.pdf");
        File.WriteAllText(corrupt, "not a pdf");

        var result = Booklet(corrupt);

        Assert.False(result.Success);
        Assert.Contains("بايظ.pdf", result.Message);
    }

    // ══════════ مساعدات ══════════

    private MergeResult Booklet(string input, BookletStart start = BookletStart.Right)
        => _composer.Compose(new SlideRequest
        {
            InputPath = input,
            OutputPath = Output(),
            Booklet = true,
            BookletStart = start,
            Margin = 10
        });

    private string Output() => Path.Combine(_folder, "out.pdf");

    private MergeResult Compose(
        string input,
        int slidesPerSheet,
        PageOrientation orientation = PageOrientation.Portrait,
        bool border = false,
        int margin = 15)
        => _composer.Compose(new SlideRequest
        {
            InputPath = input,
            OutputPath = Output(),
            SlidesPerSheet = slidesPerSheet,
            SheetOrientation = orientation,
            Order = SlideOrder.Horizontal,
            Start = SlideStart.Right,
            Margin = margin,
            DrawBorder = border
        });

    private PdfPage FirstSheet()
        => PdfReader.Open(Output(), PdfDocumentOpenMode.Import).Pages[0];

    private static int PageCountOf(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    /// <summary>كام صفحة مصدر **مختلفة** موجودة على الورقة دي.</summary>
    private static int DistinctPagesOnSheet(string path, int sheetIndex)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var resources = document.Pages[sheetIndex].Elements.GetDictionary("/Resources");

        return resources?.Elements.GetDictionary("/XObject")?.Elements.Count ?? 0;
    }

    private string MakePdf(string name, int pages)
    {
        var document = new PdfDocument();
        var font = new XFont("Arial", 60);

        for (int i = 0; i < pages; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"{i + 1}", font, XBrushes.Black,
                new XRect(0, 0, page.Width.Point, page.Height.Point), XStringFormats.Center);
        }

        string path = Path.Combine(_folder, name);
        document.Save(path);
        return path;
    }
}
