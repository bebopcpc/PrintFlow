using PrintFlow.Domain;
using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// بيحمي **ترتيب النسخ** و**مدى الصفحات** في سطر أوامر SumatraPDF.
///
/// ═══ ليه ملف لوحده جنب SumatraArgumentsTests ═══
///
/// الملف التاني بيتأكد إن كل خيار في الواجهة بيوصل صح. الملف ده بيحمي
/// **قرار** — قرار إن البرنامج يرتّب النسخ بنفسه بدل ما يسيبها للدرايفر.
///
/// ═══ القصة ═══
///
/// أوردر ٣٠ نسخة من ملزمة طلع **مش مترتب**: ١·١·٢·٢·٣·٣ بدل
/// ١·٢·٣·١·٢·٣ — يعني ترصيص بالإيد لأوردر كامل.
///
/// السبب: <c>{n}x</c> بتحط العدد في <c>dmCopies</c> جوّه إعدادات
/// الدرايفر **وماتقولش ولا كلمة عن الترتيب**. فاللي بيقرر هو إعداد
/// الطابعة، والافتراضي في أغلب الدرايفرات هو «مش مترتب».
/// (وسوماترا 3.6.1 مافيهاش خيار <c>collate</c> أصلًا — اتفحص الملف
/// الثنائي؛ اتضاف في 3.7 اللي لسه تجريبية.)
///
/// الحل: بدل «اطبعها ٣٠ مرة»، بنقول الصفحات بالترتيب:
/// <c>1-20,1-20,1-20…</c> في **جوب واحد**. الدرايفر مابقاش عنده نسخ
/// يلخبطها.
///
/// أي تعديل يرجّع <c>{n}x</c> في الحالة العادية بيكسر التستات دي —
/// وده المقصود بالظبط.
/// </summary>
public class SumatraArgumentsCollationTests
{
    private static PrintJob Job(
        int copies,
        int pages,
        int from = 0,
        int to = 0,
        bool duplex = false)
    {
        var settings = new PrintSettings
        {
            PageFrom = from,
            PageTo = to,
            Duplex = duplex
        };

        return PrintJob.From(settings, @"C:\temp\doc.pdf", "HP LaserJet", copies, pages);
    }

    // ══════════ الترتيب ══════════

    [Fact]
    public void Copies_Are_Sent_As_A_Repeated_Range_Not_As_Nx()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 3, pages: 20));

        Assert.StartsWith("1-20,1-20,1-20,", settings);
        Assert.DoesNotContain("3x", settings);
    }

    [Fact]
    public void One_Copy_Adds_No_Token_At_All()
    {
        // نسخة واحدة مفيهاش ترتيب نتحكم فيه — الأمر لازم يفضل زي القديم بالحرف
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 1, pages: 20));

        Assert.StartsWith("paper=", settings);
    }

    [Fact]
    public void A_Single_Page_Document_Repeats_Correctly()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 4, pages: 1));

        Assert.StartsWith("1-1,1-1,1-1,1-1,", settings);
    }

    // ══════════ إمتى بنرجع للدرايفر ══════════

    /// <summary>
    /// مانعرفش آخر صفحة = مانقدرش نكتب مدى. لو خمّنا غلط هنطبع **ناقص**،
    /// وده أسوأ بكتير من ترتيب غلط.
    /// </summary>
    [Fact]
    public void An_Unknown_Page_Count_Falls_Back_To_Nx()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 7, pages: 0));

        Assert.StartsWith("7x,", settings);
    }

    /// <summary>
    /// ⚠ الحالة دي دقيقة: مستند ٣ صفحات مكرر مرتين = ٦ صفحات ورا بعض،
    /// فالورقة التانية هتبقى صفحة ٣ من النسخة الأولى على وش وصفحة ١ من
    /// النسخة التانية على ضهرها. الدرايفر لما بيعمل النسخ بنفسه بيبدأ
    /// كل نسخة في ورقة جديدة — فسايبينه يعملها هو هنا.
    /// </summary>
    [Fact]
    public void Duplex_With_An_Odd_Page_Count_Falls_Back_To_The_Driver()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 4, pages: 3, duplex: true));

        Assert.StartsWith("4x,", settings);
    }

    [Fact]
    public void Duplex_With_An_Even_Page_Count_Still_Orders_Them_Ourselves()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 4, pages: 4, duplex: true));

        Assert.StartsWith("1-4,1-4,1-4,1-4,", settings);
    }

    /// <summary>ويندوز بيرفض أمر أطول من ٣٢٧٦٧ حرف.</summary>
    [Fact]
    public void A_Huge_Copy_Count_Falls_Back_Instead_Of_Building_A_Giant_Command()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 5000, pages: 20));

        Assert.StartsWith("5000x,", settings);
    }

    [Fact]
    public void A_Big_But_Sane_Copy_Count_Still_Fits()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 300, pages: 20));

        Assert.StartsWith("1-20,1-20,", settings);
        Assert.True(settings.Length < 8000, $"طول الأمر {settings.Length}");
    }

    // ══════════ مدى الصفحات ══════════

    [Fact]
    public void A_Page_Range_Is_Sent_On_Its_Own_For_One_Copy()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 1, pages: 20, from: 5, to: 20));

        Assert.StartsWith("5-20,paper=", settings);
    }

    [Fact]
    public void A_Page_Range_Repeats_With_The_Copies()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 3, pages: 20, from: 5, to: 20));

        Assert.StartsWith("5-20,5-20,5-20,paper=", settings);
    }

    [Fact]
    public void From_Only_Runs_To_The_End_Of_The_Document()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 2, pages: 20, from: 5));

        Assert.StartsWith("5-20,5-20,paper=", settings);
    }

    /// <summary>
    /// لما نرجع للدرايفر ولسه فيه مدى، **الصفحات لازم تفضل مظبوطة**.
    /// اللي بنخسره هو التحكم في الترتيب بس. سوماترا بتفهم الاتنين مع بعض.
    /// </summary>
    [Fact]
    public void Falling_Back_Still_Keeps_The_Page_Range()
    {
        string settings = SumatraArguments.BuildPrintSettings(
            Job(copies: 4, pages: 20, from: 5, to: 9, duplex: true));

        Assert.StartsWith("5-9,4x,paper=", settings);
    }

    [Fact]
    public void A_Backwards_Range_Prints_The_Whole_Document()
    {
        string settings = SumatraArguments.BuildPrintSettings(Job(copies: 1, pages: 20, from: 20, to: 5));

        Assert.StartsWith("paper=", settings);
    }

    // ══════════ باقي الخيارات ما اتلمستش ══════════

    /// <summary>
    /// الترتيب اتضاف **قدّام** باقي الخيارات، مش مكانهم. لو واحد منهم
    /// وقع في الطريق، الأوردر بيطلع بمقاس أو لون غلط والترتيب سليم —
    /// وده أسوأ من الترتيب الغلط نفسه.
    /// </summary>
    [Fact]
    public void Everything_Else_Still_Rides_Along()
    {
        var settings = new PrintSettings
        {
            PaperSize = "A3",
            Grayscale = true,
            Duplex = true,
            DuplexFlip = DuplexFlip.ShortEdge,
            PageOrientation = PageOrientation.Landscape
        };

        var job = PrintJob.From(settings, "a.pdf", "HP", copies: 3, pageCount: 10);
        string built = SumatraArguments.BuildPrintSettings(job);

        Assert.StartsWith("1-10,1-10,1-10,", built);
        Assert.Contains("paper=A3", built);
        Assert.Contains("landscape", built);
        Assert.Contains("monochrome", built);
        Assert.Contains("duplexshort", built);
    }
}
