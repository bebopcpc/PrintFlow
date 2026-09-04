using System.Globalization;
using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// بيحمي الرقم اللي الزبون بيدفعه.
///
/// الغلط هنا مابيبانش لحد ما الفاتورة تطلع أقل من التكلفة — أو تطلع
/// أعلى ويروح الزبون. وكل تركيبة هنا اتحسبت بالإيد الأول.
/// </summary>
public class PriceEstimateTests
{
    private static PrintSettings Plain() => new();

    /// <summary>٢٤ صفحة، وش واحد = ٢٤ وجه على ٢٤ ورقة.</summary>
    private static PaperTally Simple() => PaperCount.For([24], Plain());

    /// <summary>٢٤ صفحة على الوجهين = ٢٤ وجه على ١٢ ورقة.</summary>
    private static PaperTally Duplexed()
    {
        var settings = Plain();
        settings.Duplex = true;

        return PaperCount.For([24], settings);
    }

    // ══════════ الطريقتين ══════════

    [Fact]
    public void By_Face_Charges_For_Every_Printed_Side()
    {
        Assert.Equal(12m, PriceEstimate.Of(Simple(), 0.5m, CountingMethod.ByPage));
    }

    [Fact]
    public void By_Sheet_Charges_For_Every_Piece_Of_Paper()
    {
        Assert.Equal(24m, PriceEstimate.Of(Simple(), 1m, CountingMethod.BySheet));
    }

    /// <summary>
    /// ⚠ ده الفرق اللي الخيار موجود عشانه.
    ///
    /// نفس الأوردر بالظبط: ٢٤ وجه على ١٢ ورقة.
    ///   • بالوجه  → ٢٤ × ٠.٥ = ١٢ جنيه (الوجهين مابيوفّرش حاجة)
    ///   • بالورقة → ١٢ × ٠.٥ = ٦ جنيه  (الوجهين بيوفّر النص)
    ///
    /// لو فرضنا طريقة واحدة، نُص المطابع كانت هتشوف رقم غلط.
    /// </summary>
    [Fact]
    public void Duplex_Halves_The_Price_By_Sheet_But_Not_By_Face()
    {
        var tally = Duplexed();

        Assert.Equal(12m, PriceEstimate.Of(tally, 0.5m, CountingMethod.ByPage));
        Assert.Equal(6m, PriceEstimate.Of(tally, 0.5m, CountingMethod.BySheet));
    }

    /// <summary>٢ في الورقة + وجهين + ١٠ نسخ = ١٢٠ وجه على ٦٠ ورقة.</summary>
    [Fact]
    public void The_Whole_Chain_Reaches_The_Price()
    {
        var settings = Plain();
        settings.SlidesPerSheet = 2;
        settings.Duplex = true;
        settings.TotalCopies = 10;

        var tally = PaperCount.For([24], settings);

        Assert.Equal(120, tally.Sides);
        Assert.Equal(60, tally.Sheets);
        Assert.Equal(30m, PriceEstimate.Of(tally, 0.5m, CountingMethod.BySheet));
    }

    // ══════════ الصفر ══════════

    /// <summary>
    /// مفيش سعر متكتوب = مفيش تسعير. "٠.٠٠ جنيه" جنب أوردر حقيقي بيبان
    /// زي عطل، والمستخدم بيقعد يدوّر على السبب.
    /// </summary>
    [Fact]
    public void No_Price_Means_No_Line_At_All()
    {
        Assert.Equal(0m, PriceEstimate.Of(Simple(), 0m, CountingMethod.BySheet));
        Assert.Equal("", PriceEstimate.Describe(Simple(), 0m, CountingMethod.BySheet));
    }

    [Fact]
    public void A_Negative_Price_Is_Not_A_Discount()
    {
        Assert.Equal(0m, PriceEstimate.Of(Simple(), -5m, CountingMethod.BySheet));
        Assert.Equal("", PriceEstimate.Describe(Simple(), -5m, CountingMethod.BySheet));
    }

    [Fact]
    public void Nothing_Loaded_Costs_Nothing()
    {
        Assert.Equal(0m, PriceEstimate.Of(PaperTally.Nothing, 2m, CountingMethod.BySheet));
        Assert.Equal("", PriceEstimate.Describe(PaperTally.Nothing, 2m, CountingMethod.BySheet));
    }

    // ══════════ عدد الوحدات ══════════

    [Fact]
    public void The_Unit_Follows_The_Method()
    {
        var tally = Duplexed();

        Assert.Equal(24, PriceEstimate.UnitsIn(tally, CountingMethod.ByPage));
        Assert.Equal(12, PriceEstimate.UnitsIn(tally, CountingMethod.BySheet));
    }

    // ══════════ السطر اللي المستخدم بيقراه ══════════

    [Fact]
    public void The_Line_Shows_The_Total_And_How_It_Got_There()
    {
        string line = PriceEstimate.Describe(Duplexed(), 0.5m, CountingMethod.BySheet);

        Assert.Contains("6.00", line);
        Assert.Contains("12 ورقة", line);
        Assert.Contains("0.5", line);
    }

    [Fact]
    public void The_Line_Names_The_Right_Unit()
    {
        Assert.Contains("وجه", PriceEstimate.Describe(Simple(), 1m, CountingMethod.ByPage));
        Assert.Contains("ورقة", PriceEstimate.Describe(Simple(), 1m, CountingMethod.BySheet));
    }

    /// <summary>
    /// ⚠ الفاصلة العشرية بتختلف بين ويندوز عربي وإنجليزي.
    ///
    /// من غير الثقافة الثابتة، السطر كان هيطلع "6,00" على جهاز و"6.00"
    /// على جهاز تاني — والتست كان هيعدّي عندي ويقع عنده.
    /// </summary>
    [Fact]
    public void The_Decimal_Point_Does_Not_Follow_Windows_Language()
    {
        var previous = Thread.CurrentThread.CurrentCulture;

        try
        {
            // ثقافة بتستخدم الفاصلة العادية بدل النقطة
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            string line = PriceEstimate.Describe(Simple(), 0.5m, CountingMethod.BySheet);

            Assert.Contains("12.00", line);
            Assert.DoesNotContain("12,00", line);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// ⚠ النجوم بتظهر للمستخدم زي ما هي — السطر ده نص خام في TextBlock.
    /// </summary>
    [Fact]
    public void The_Line_Has_No_Markdown_Stars()
    {
        string line = PriceEstimate.Describe(Duplexed(), 1.25m, CountingMethod.ByPage);

        Assert.DoesNotContain("*", line);
        Assert.DoesNotContain("_", line);
    }
}
