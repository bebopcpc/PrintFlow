using PrintFlow.Domain;

namespace PrintFlow.Tests;

public class SupportedInputTests
{
    [Theory]
    [InlineData("a.pdf")]
    [InlineData("a.PDF")]
    [InlineData(@"C:\شغل\فاتورة.Pdf")]
    [InlineData("/home/x/report.pdf")]
    public void Pdf_Is_Recognised_Whatever_The_Case_Or_Path(string path)
    {
        Assert.Equal(InputKind.Pdf, SupportedInput.KindOf(path));
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPEG")]
    [InlineData("scan.png")]
    [InlineData("old.bmp")]
    public void Images_Are_Recognised(string path)
    {
        Assert.Equal(InputKind.Image, SupportedInput.KindOf(path));
    }

    [Theory]
    [InlineData("a.docx")]
    [InlineData("a.doc")]
    [InlineData("a.pptx")]
    [InlineData("a.xlsx")]
    public void Office_Is_Recognised_So_We_Can_Say_Why_It_Was_Skipped(string path)
    {
        Assert.Equal(InputKind.Office, SupportedInput.KindOf(path));
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.exe")]
    [InlineData("noextension")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Everything_Else_Is_Unsupported(string? path)
    {
        Assert.Equal(InputKind.Unsupported, SupportedInput.KindOf(path));
    }

    [Theory]
    [InlineData("x.gif")]
    [InlineData("fax.tif")]
    [InlineData("fax.TIFF")]
    [InlineData("phone.heic")]
    [InlineData("web.webp")]
    public void Known_But_Unsupported_Images_Are_Told_Apart(string path)
    {
        // PdfSharp 6.2.4 جواه تلات قارئات صور بس: JPEG و PNG و BMP.
        // الصيغ دي بتترفض، بس لازم نعرفها عشان نقول للمستخدم يعمل إيه
        // بدل ما تتجاهل في صمت وهو شايفها صورة عادية.
        Assert.Equal(InputKind.UnsupportedImage, SupportedInput.KindOf(path));
    }

    [Fact]
    public void No_Extension_Is_Claimed_Twice()
    {
        // لو صيغة اتحطت في القايمتين، السلوك بيبقى معتمد على ترتيب الفحص
        Assert.Empty(SupportedInput.ImageExtensions.Intersect(SupportedInput.UnsupportedImageExtensions));
    }

    [Fact]
    public void A_Leading_Dot_Is_Not_An_Extension()
    {
        // ".pdf" لوحده اسم ملف مخفي مش ملف PDF
        Assert.Equal(InputKind.Unsupported, SupportedInput.KindOf(".pdf"));
    }

    [Fact]
    public void A_Windows_Path_Is_Read_Correctly_On_Any_Platform()
    {
        // Path.GetExtension بيتصرف مختلف على لينكس مع الشرطة المقلوبة،
        // والتستات بتجري على لينكس والبرنامج على ويندوز
        Assert.Equal(InputKind.Image, SupportedInput.KindOf(@"C:\Users\م\Pictures\صورة.jpg"));
    }

    [Fact]
    public void A_Dot_In_The_Folder_Name_Does_Not_Confuse_It()
    {
        Assert.Equal(InputKind.Pdf, SupportedInput.KindOf(@"C:\my.folder\report.pdf"));
        Assert.Equal(InputKind.Unsupported, SupportedInput.KindOf(@"C:\my.pdf\readme"));
    }

    [Fact]
    public void The_Dialog_Filter_Covers_Every_Supported_Extension()
    {
        // الفلتر لازم يبقى مبني من نفس القوايم، مش مكتوب بالإيد
        string filter = SupportedInput.OpenDialogFilter;

        Assert.Contains("*.pdf", filter);

        foreach (string extension in SupportedInput.ImageExtensions)
        {
            Assert.Contains("*" + extension, filter);
        }
    }

    // ══════════ مكان الصورة على الورقة ══════════

    [Fact]
    public void A_Portrait_Image_Gets_A_Portrait_Sheet()
    {
        var (width, height) = ImageConvertRequest.SheetFor(1000, 1400);

        Assert.True(height > width);
    }

    [Fact]
    public void A_Landscape_Image_Gets_A_Landscape_Sheet()
    {
        var (width, height) = ImageConvertRequest.SheetFor(1400, 1000);

        Assert.True(width > height);
    }

    [Fact]
    public void A_Square_Image_Gets_A_Portrait_Sheet()
    {
        var (width, height) = ImageConvertRequest.SheetFor(1000, 1000);

        Assert.True(height > width);
    }

    [Fact]
    public void The_Image_Keeps_Its_Aspect_Ratio()
    {
        // صورة موبايل ٤:٣ على ورقة A4 — لازم تفضل ٤:٣ مش تتفرد
        var rect = ImageConvertRequest.PlaceOn(595, 842, 4032, 3024, margin: 0);

        Assert.Equal(4032.0 / 3024.0, rect.Width / rect.Height, 4);
    }

    [Fact]
    public void The_Image_Fits_Inside_The_Sheet()
    {
        var rect = ImageConvertRequest.PlaceOn(595, 842, 4032, 3024, margin: 0);

        Assert.True(rect.X >= -0.001);
        Assert.True(rect.Y >= -0.001);
        Assert.True(rect.X + rect.Width <= 595.001);
        Assert.True(rect.Y + rect.Height <= 842.001);
    }

    [Fact]
    public void The_Image_Is_Centred()
    {
        var rect = ImageConvertRequest.PlaceOn(595, 842, 4032, 3024, margin: 0);

        Assert.Equal(rect.X, 595 - (rect.X + rect.Width), 4);
        Assert.Equal(rect.Y, 842 - (rect.Y + rect.Height), 4);
    }

    [Fact]
    public void A_Silly_Margin_Cannot_Make_The_Box_Vanish()
    {
        var rect = ImageConvertRequest.PlaceOn(595, 842, 1000, 1000, margin: 99999);

        Assert.True(rect.Width > 0);
        Assert.True(rect.Height > 0);
    }
}
