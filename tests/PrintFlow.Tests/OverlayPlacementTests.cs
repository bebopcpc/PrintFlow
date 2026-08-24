using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// حساب مكان الترقيم والنص المخصص على الورقة. الحسابات دي نقية،
/// فبنتأكد إن "أسفل يمين" بتطلع فعلًا أسفل يمين من غير ما نولّد PDF ونبصّ فيه.
///
/// إحداثيات PDF: الأصل فوق-شمال، و Y بتزيد وإحنا نازلين.
/// </summary>
public class OverlayPlacementTests
{
    private const double A4Width = 595;   // نقطة
    private const double A4Height = 842;
    private const double Margin = 20;
    private const double LineHeight = 14;

    private static PlacementBox Box(ContentPosition position) =>
        OverlayPlacement.Calculate(position, A4Width, A4Height, Margin, LineHeight);

    [Theory]
    [InlineData(ContentPosition.TopLeft)]
    [InlineData(ContentPosition.TopCenter)]
    [InlineData(ContentPosition.TopRight)]
    public void Top_Positions_Sit_At_The_Top_Margin(ContentPosition position)
    {
        var box = Box(position);

        Assert.Equal(VerticalAlign.Top, box.Vertical);
        Assert.Equal(Margin, box.Y);
    }

    [Theory]
    [InlineData(ContentPosition.BottomLeft)]
    [InlineData(ContentPosition.BottomCenter)]
    [InlineData(ContentPosition.BottomRight)]
    public void Bottom_Positions_Sit_Above_The_Bottom_Margin(ContentPosition position)
    {
        var box = Box(position);

        Assert.Equal(VerticalAlign.Bottom, box.Vertical);
        Assert.Equal(A4Height - Margin - LineHeight, box.Y);
    }

    [Theory]
    [InlineData(ContentPosition.TopLeft, HorizontalAlign.Left)]
    [InlineData(ContentPosition.BottomLeft, HorizontalAlign.Left)]
    [InlineData(ContentPosition.TopCenter, HorizontalAlign.Center)]
    [InlineData(ContentPosition.BottomCenter, HorizontalAlign.Center)]
    [InlineData(ContentPosition.TopRight, HorizontalAlign.Right)]
    [InlineData(ContentPosition.BottomRight, HorizontalAlign.Right)]
    public void Horizontal_Alignment_Follows_The_Position(ContentPosition position, HorizontalAlign expected)
    {
        Assert.Equal(expected, Box(position).Horizontal);
    }

    [Fact]
    public void Box_Spans_The_Page_Minus_Both_Margins()
    {
        var box = Box(ContentPosition.BottomLeft);

        Assert.Equal(Margin, box.X);
        Assert.Equal(A4Width - (Margin * 2), box.Width);
    }

    [Fact]
    public void Top_And_Bottom_Are_Never_The_Same_Place()
    {
        Assert.NotEqual(Box(ContentPosition.TopLeft).Y, Box(ContentPosition.BottomLeft).Y);
    }

    [Fact]
    public void Box_Always_Stays_Inside_The_Page()
    {
        foreach (ContentPosition position in Enum.GetValues<ContentPosition>())
        {
            var box = Box(position);

            Assert.True(box.X >= 0, $"{position}: X بالسالب");
            Assert.True(box.Y >= 0, $"{position}: Y بالسالب");
            Assert.True(box.X + box.Width <= A4Width, $"{position}: خرج من عرض الصفحة");
            Assert.True(box.Y + box.Height <= A4Height, $"{position}: خرج من طول الصفحة");
        }
    }

    /// <summary>
    /// المستخدم ممكن يكتب هامش كبير أوي، ولازم مانطلعش عرض بالسالب
    /// يخلي الرسم يرمي استثناء وسط الطباعة.
    /// </summary>
    [Fact]
    public void Absurd_Margin_Does_Not_Produce_Negative_Width()
    {
        var box = OverlayPlacement.Calculate(ContentPosition.BottomLeft, A4Width, A4Height, 5000, LineHeight);

        Assert.True(box.Width > 0);
        Assert.True(box.X >= 0);
        Assert.True(box.Y >= 0);
    }

    [Fact]
    public void Larger_Margin_Pushes_Content_Further_From_The_Edge()
    {
        var small = OverlayPlacement.Calculate(ContentPosition.TopLeft, A4Width, A4Height, 10, LineHeight);
        var large = OverlayPlacement.Calculate(ContentPosition.TopLeft, A4Width, A4Height, 40, LineHeight);

        Assert.True(large.Y > small.Y);
        Assert.True(large.Width < small.Width);
    }
}

public class HexColorTests
{
    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("#FF0000", 255, 0, 0)]
    [InlineData("#1B2A4A", 27, 42, 74)]
    [InlineData("1B2A4A", 27, 42, 74)]
    public void Parses_Six_Digit_Hex(string hex, byte r, byte g, byte b)
    {
        Assert.True(HexColor.TryParse(hex, out var color));
        Assert.Equal(new RgbColor(r, g, b), color);
    }

    [Fact]
    public void Parses_Three_Digit_Shorthand()
    {
        Assert.True(HexColor.TryParse("#C30", out var color));
        Assert.Equal(new RgbColor(0xCC, 0x33, 0x00), color);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("أحمر")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void Rejects_Bad_Input_Instead_Of_Throwing(string? hex)
    {
        Assert.False(HexColor.TryParse(hex, out _));
    }

    [Fact]
    public void Falls_Back_Quietly_So_Printing_Never_Crashes()
    {
        var fallback = new RgbColor(128, 128, 128);
        Assert.Equal(fallback, HexColor.ParseOrDefault("مش لون", fallback));
    }
}

public class WatermarkStyleTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 127)]
    [InlineData(100, 255)]
    public void Opacity_Percent_Maps_To_Alpha(int percent, byte expected)
    {
        var style = new WatermarkStyle { OpacityPercent = percent };
        Assert.Equal(expected, style.Alpha);
    }

    [Fact]
    public void Disabled_Watermark_Produces_Nothing()
    {
        var app = new AppSettings { WatermarkEnabled = false, WatermarkText = "أي كلام" };
        Assert.Null(WatermarkStyle.From(app));
    }

    [Fact]
    public void Enabled_But_Empty_Text_Produces_Nothing()
    {
        var app = new AppSettings { WatermarkEnabled = true, WatermarkText = "   " };
        Assert.Null(WatermarkStyle.From(app));
    }

    [Fact]
    public void Enabled_Image_Without_A_Path_Produces_Nothing()
    {
        var app = new AppSettings
        {
            WatermarkEnabled = true,
            WatermarkIsImage = true,
            WatermarkImagePath = "",
            WatermarkText = "نص موجود بس المختار صورة"
        };

        Assert.Null(WatermarkStyle.From(app));
    }

    [Fact]
    public void Custom_Text_Only_Appears_When_Enabled_And_Filled()
    {
        Assert.Null(OverlayTextStyle.From(new AppSettings { CustomTextEnabled = false, CustomText = "كلام" }));
        Assert.Null(OverlayTextStyle.From(new AppSettings { CustomTextEnabled = true, CustomText = "  " }));
        Assert.NotNull(OverlayTextStyle.From(new AppSettings { CustomTextEnabled = true, CustomText = "كلام" }));
    }

    [Fact]
    public void Merge_Request_Combines_Job_And_App_Settings()
    {
        var print = new PrintSettings { NumberPagesPerFile = true };
        var app = new AppSettings
        {
            WatermarkEnabled = true,
            WatermarkText = "سري",
            PageNumberPosition = ContentPosition.TopCenter
        };

        var request = MergeRequest.From(print, app, new[] { "a.pdf" }, "out.pdf");

        Assert.NotNull(request.PageNumbers);
        Assert.Equal(ContentPosition.TopCenter, request.PageNumbers.Position);
        Assert.NotNull(request.Watermark);
        Assert.Equal("سري", request.Watermark.Text);
        Assert.Null(request.CustomText);
    }
}
