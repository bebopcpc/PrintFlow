using PrintFlow.Domain;

namespace PrintFlow.Tests;

public class PageScalingTests
{
    [Fact]
    public void Half_Scale_Halves_Both_Sides()
    {
        var rect = PageScaling.Place(1000, 800, 50);

        Assert.Equal(500, rect.Width, 6);
        Assert.Equal(400, rect.Height, 6);
    }

    [Fact]
    public void Shrunk_Content_Is_Centred()
    {
        var rect = PageScaling.Place(1000, 800, 50);

        Assert.Equal(250, rect.X, 6);
        Assert.Equal(200, rect.Y, 6);
    }

    [Fact]
    public void Margins_Are_Equal_On_Both_Sides()
    {
        var rect = PageScaling.Place(595, 842, 73);

        double left = rect.X;
        double right = 595 - (rect.X + rect.Width);

        Assert.Equal(left, right, 6);
    }

    [Fact]
    public void Hundred_Percent_Covers_The_Whole_Page()
    {
        var rect = PageScaling.Place(595, 842, 100);

        Assert.Equal(0, rect.X, 6);
        Assert.Equal(0, rect.Y, 6);
        Assert.Equal(595, rect.Width, 6);
        Assert.Equal(842, rect.Height, 6);
    }

    [Fact]
    public void Enlarging_Overflows_Equally_On_Both_Sides()
    {
        // ٢٠٠٪ لازم يخرج بره الورقة من الجهتين بالتساوي — مش من جهة واحدة
        var rect = PageScaling.Place(1000, 800, 200);

        Assert.Equal(-500, rect.X, 6);
        Assert.Equal(2000, rect.Width, 6);
    }

    [Fact]
    public void Aspect_Ratio_Never_Changes()
    {
        foreach (int percent in new[] { 10, 45, 99, 101, 250, 400 })
        {
            var rect = PageScaling.Place(595, 842, percent);

            Assert.Equal(595.0 / 842.0, rect.Width / rect.Height, 6);
        }
    }

    [Theory]
    [InlineData(0, PageScaling.Minimum)]
    [InlineData(-50, PageScaling.Minimum)]
    [InlineData(5, PageScaling.Minimum)]
    [InlineData(99999, PageScaling.Maximum)]
    [InlineData(401, PageScaling.Maximum)]
    [InlineData(100, 100)]
    public void Percentages_Are_Clamped_Not_Rejected(int input, int expected)
    {
        Assert.Equal(expected, PageScaling.Clamp(input));
    }

    [Fact]
    public void Only_A_Hundred_Is_A_No_Op()
    {
        Assert.True(PageScaling.IsIdentity(100));
        Assert.False(PageScaling.IsIdentity(99));
        Assert.False(PageScaling.IsIdentity(101));
    }

    [Fact]
    public void A_Clamped_Value_That_Lands_On_A_Hundred_Is_Still_A_No_Op()
    {
        // مافيش قيمة بتتقصّ لـ ١٠٠، بس القاعدة لازم تمشي على المقصوص
        Assert.False(PageScaling.IsIdentity(0));
        Assert.False(PageScaling.IsIdentity(99999));
    }

    [Fact]
    public void Description_Tells_Which_Way_It_Goes()
    {
        Assert.Contains("الطبيعي", PageScaling.Describe(100));
        Assert.Contains("هامش أبيض", PageScaling.Describe(80));
        Assert.Contains("هيتقص", PageScaling.Describe(120));
    }

    [Fact]
    public void Request_Knows_When_There_Is_Nothing_To_Do()
    {
        Assert.True(new ScaleRequest { InputPath = "a", OutputPath = "b" }.IsPassThrough);
        Assert.True(new ScaleRequest { InputPath = "a", OutputPath = "b", Percent = 100 }.IsPassThrough);
        Assert.False(new ScaleRequest { InputPath = "a", OutputPath = "b", Percent = 99 }.IsPassThrough);
    }
}
