using PrintFlow.Domain;
using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// بيتأكد إن كل خيار في الواجهة بيوصل لـ SumatraPDF بالصيغة الصح —
/// من غير ما نطبع ورقة واحدة. ده اللي بيدينا ثقة إن الطباعة هتطلع زي المتوقع.
/// </summary>
public class SumatraArgumentsTests
{
    private static PrintJob Job(Action<PrintSettings>? configure = null, int copies = 1)
    {
        var settings = new PrintSettings();
        configure?.Invoke(settings);
        return PrintJob.From(settings, @"C:\temp\doc.pdf", "HP LaserJet", copies);
    }

    // ══════════ عدد النسخ ══════════

    /// <summary>
    /// دي الحتة اللي كانت بتفتح بروسيس لكل نسخة. دلوقتي رقم واحد جوه الأوامر.
    /// </summary>
    [Fact]
    public void Multiple_Copies_Become_One_Nx_Token()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 25));
        Assert.Contains("25x", settings);
    }

    [Fact]
    public void Single_Copy_Does_Not_Add_A_Copies_Token()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 1));
        Assert.DoesNotContain("1x", settings);
    }

    [Fact]
    public void Copies_Token_Comes_First()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 3));
        Assert.StartsWith("3x,", settings);
    }

    // ══════════ مقاس الورق ══════════

    [Theory]
    [InlineData("A4", "paper=A4")]
    [InlineData("A3", "paper=A3")]
    [InlineData("Letter", "paper=letter")]
    [InlineData("Legal", "paper=legal")]
    [InlineData("Tabloid", "paper=tabloid")]
    public void Paper_Size_Maps_To_Sumatra_Name(string uiValue, string expected)
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(s => s.PaperSize = uiValue));
        Assert.Contains(expected, settings);
    }

    [Fact]
    public void Unknown_Paper_Size_Falls_Back_To_A4()
    {
        // A0 مش مدعوم في SumatraPDF — الأأمن نرجع لـ A4 بدل ما الأمر يتلغي
        string settings = SumatraArguments.BuildPrintSettings(Job(s => s.PaperSize = "A0"));
        Assert.Contains("paper=A4", settings);
    }

    // ══════════ الاتجاه والألوان والوجهين ══════════

    [Fact]
    public void Portrait_Is_The_Default()
    {
        Assert.Contains("portrait", SumatraArguments.BuildPrintSettings(Job()));
    }

    [Fact]
    public void Landscape_Is_Passed_Through()
    {
        string settings = SumatraArguments.BuildPrintSettings(
            Job(s => s.PageOrientation = PageOrientation.Landscape));

        Assert.Contains("landscape", settings);
        Assert.DoesNotContain("portrait", settings);
    }

    [Fact]
    public void Grayscale_Becomes_Monochrome()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(s => s.Grayscale = true));

        Assert.Contains("monochrome", settings);
        Assert.DoesNotContain("color", settings);
    }

    [Fact]
    public void Colour_Is_The_Default()
    {
        Assert.Contains("color", SumatraArguments.BuildPrintSettings(Job()));
    }

    [Fact]
    public void Duplex_Off_Sends_Simplex()
    {
        Assert.Contains("simplex", SumatraArguments.BuildPrintSettings(Job()));
    }

    [Fact]
    public void Duplex_Long_Edge_Is_The_Default_Flip()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(s => s.Duplex = true));
        Assert.Contains("duplexlong", settings);
    }

    [Fact]
    public void Duplex_Short_Edge_Is_Respected()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(s =>
        {
            s.Duplex = true;
            s.DuplexFlip = DuplexFlip.ShortEdge;
        }));

        Assert.Contains("duplexshort", settings);
    }

    // ══════════ الأوامر الكاملة ══════════

    [Fact]
    public void Arguments_Are_Separate_Tokens_Not_One_String()
    {
        // مهم للأمان: اسم طابعة فيه مسافة أو علامة تنصيص لازم يعدّي كعنصر واحد
        var arguments = SumatraArguments.BuildArguments(Job());

        Assert.Contains("-print-to", arguments);
        Assert.Contains("HP LaserJet", arguments);
        Assert.Contains("-silent", arguments);
        Assert.Contains(@"C:\temp\doc.pdf", arguments);
    }

    [Fact]
    public void Printer_Name_Follows_The_PrintTo_Switch()
    {
        var arguments = SumatraArguments.BuildArguments(Job()).ToList();

        int index = arguments.IndexOf("-print-to");
        Assert.Equal("HP LaserJet", arguments[index + 1]);
    }

    [Fact]
    public void File_Path_Is_The_Last_Argument()
    {
        var arguments = SumatraArguments.BuildArguments(Job()).ToList();
        Assert.Equal(@"C:\temp\doc.pdf", arguments[^1]);
    }

    [Fact]
    public void A_Realistic_Job_Produces_The_Expected_Settings()
    {
        var job = Job(s =>
        {
            s.PaperSize = "A3";
            s.Grayscale = true;
            s.Duplex = true;
            s.DuplexFlip = DuplexFlip.ShortEdge;
            s.PageOrientation = PageOrientation.Landscape;
        }, copies: 10);

        Assert.Equal(
            "10x,paper=A3,landscape,shrink,monochrome,duplexshort",
            SumatraArguments.BuildPrintSettings(job));
    }

    // ══════════ بناء الجوب من الإعدادات ══════════

    [Fact]
    public void PrintJob_Takes_Copies_From_The_Caller_Not_From_Settings()
    {
        // في وضع التوزيع كل طابعة بتاخد نصيب مختلف عن الإجمالي
        var settings = new PrintSettings { TotalCopies = 100 };
        var job = PrintJob.From(settings, "a.pdf", "HP", 7);

        Assert.Equal(7, job.Copies);
    }

    [Fact]
    public void PrintJob_Carries_Every_Print_Option_From_Settings()
    {
        var settings = new PrintSettings
        {
            PaperSize = "Legal",
            Grayscale = true,
            Duplex = true,
            DuplexFlip = DuplexFlip.ShortEdge,
            PageOrientation = PageOrientation.Landscape
        };

        var job = PrintJob.From(settings, "a.pdf", "HP", 2);

        Assert.Equal("Legal", job.PaperSize);
        Assert.True(job.Grayscale);
        Assert.True(job.Duplex);
        Assert.Equal(DuplexFlip.ShortEdge, job.DuplexFlip);
        Assert.Equal(PageOrientation.Landscape, job.Orientation);
    }
}
