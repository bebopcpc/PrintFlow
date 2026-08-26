using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// الوصف اللي بيظهر جنب اسم الإعداد المسبق.
///
/// ليه ده يستاهل تستات: الإعداد المسبق بيتحفظ كامل ويترجع كامل — دي شغالة
/// من الأول. بس الوصف كان بيقول "A4 • طولي • ٥ نسخة" لإعداد فيه كتيّب
/// وحذف صفحات ومقياس ٩٠٪. المستخدم بيقرا الوصف مش الـ JSON، فالإعداد كان
/// بيبان حاجة وهو حاجة تانية.
/// </summary>
public class PresetSummaryTests
{
    private static string Summary(Action<PrintSettings> configure)
    {
        var settings = new PrintSettings();
        configure(settings);
        return new Preset { Name = "تجربة", Settings = settings }.Summarize();
    }

    [Fact]
    public void The_Basics_Are_Always_There()
    {
        string text = Summary(s => { s.PaperSize = "A3"; s.TotalCopies = 5; });

        Assert.Contains("A3", text);
        Assert.Contains("5 نسخة", text);
        Assert.Contains("طولي", text);
    }

    [Fact]
    public void Booklet_Is_Announced()
    {
        Assert.Contains("كتيّب", Summary(s => s.BookletMode = true));
    }

    [Fact]
    public void Slides_Are_Announced()
    {
        Assert.Contains("4 شرائح", Summary(s => s.SlidesPerSheet = 4));
    }

    [Fact]
    public void One_Slide_Is_Not_Worth_Mentioning()
    {
        Assert.DoesNotContain("شرائح", Summary(s => s.SlidesPerSheet = 1));
    }

    [Fact]
    public void Booklet_Wins_Over_Slides_Because_It_Ignores_Them()
    {
        // الكتيّب بيتجاهل عدد الشرائح فعلًا، فذكر الاتنين هيكون كذب
        string text = Summary(s => { s.BookletMode = true; s.SlidesPerSheet = 6; });

        Assert.Contains("كتيّب", text);
        Assert.DoesNotContain("شرائح", text);
    }

    [Fact]
    public void Page_Deletion_Is_Announced_With_The_Numbers()
    {
        string text = Summary(s => { s.DeletePages = true; s.PagesToDelete = "1,5-7"; });

        Assert.Contains("1,5-7", text);
    }

    [Fact]
    public void Deletion_Text_Left_Behind_With_The_Box_Off_Is_Not_Announced()
    {
        string text = Summary(s => { s.DeletePages = false; s.PagesToDelete = "1,5-7"; });

        Assert.DoesNotContain("حذف", text);
    }

    [Fact]
    public void Scale_Is_Announced_Only_When_It_Does_Something()
    {
        Assert.Contains("مقياس 85%", Summary(s => s.ScalePercent = 85));
        Assert.DoesNotContain("مقياس", Summary(s => s.ScalePercent = 100));
    }

    [Fact]
    public void Not_Merging_Is_Announced()
    {
        Assert.Contains("من غير دمج", Summary(s => s.MergeFiles = false));
        Assert.DoesNotContain("من غير دمج", Summary(s => s.MergeFiles = true));
    }

    [Fact]
    public void A_Loaded_Preset_Describes_Itself_The_Same_Way()
    {
        // نسخ الإعداد ماينفعش يغيّر وصفه — وده اللي بيحصل لما المستخدم
        // يحمّل إعداد محفوظ
        var settings = new PrintSettings
        {
            BookletMode = true,
            DeletePages = true,
            PagesToDelete = "1",
            ScalePercent = 92,
            MergeFiles = false
        };

        var original = new Preset { Name = "أ", Settings = settings };

        Assert.Equal(original.Summarize(), original.Clone().Summarize());
    }

    [Fact]
    public void Everything_At_Once_Still_Reads_As_One_Line()
    {
        string text = Summary(s =>
        {
            s.BookletMode = true;
            s.DeletePages = true;
            s.PagesToDelete = "1";
            s.ScalePercent = 90;
            s.MergeFiles = false;
            s.Duplex = true;
            s.Grayscale = true;
            s.NumberPagesPerFile = true;
            s.UseMultiplePrinters = true;
        });

        Assert.DoesNotContain("\n", text);
        Assert.Contains("كتيّب", text);
        Assert.Contains("حذف", text);
        Assert.Contains("مقياس", text);
    }
}
