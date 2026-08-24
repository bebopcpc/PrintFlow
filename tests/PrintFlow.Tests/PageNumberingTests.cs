using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// وضع الترقيم ولون اللوحة اللي وراه.
///
/// خلفية التستات دي: المستخدم بلّغ إن كل صفحة بتكتب "صفحة 1 من 1". ده مكانش
/// عطل في العداد — كان الترقيم مضبوط على "كل ملف من الأول" **مكتوب في الكود**،
/// وهو حمّل ملفين كل واحد صفحة. القرار ده بقى خيار صريح، والافتراضي بقى
/// الترقيم المتصل لأنه اللي حد بيدمج ملفات بيتوقعه.
/// </summary>
public class PageNumberingTests
{
    [Fact]
    public void Numbering_Is_Continuous_By_Default()
    {
        Assert.False(new AppSettings().RestartNumberingForEachFile);
        Assert.False(new PageNumberStyle().RestartForEachFile);
    }

    [Fact]
    public void The_Setting_Reaches_The_Merge_Request()
    {
        var app = new AppSettings { RestartNumberingForEachFile = true };

        Assert.True(PageNumberStyle.From(app).RestartForEachFile);
    }

    [Fact]
    public void Continuous_Numbering_Is_Carried_Through_As_False()
    {
        var app = new AppSettings { RestartNumberingForEachFile = false };

        Assert.False(PageNumberStyle.From(app).RestartForEachFile);
    }

    /// <summary>
    /// الطلب المبني من الإعدادات لازم ينقل الوضع — لو الوصلة دي اتقطعت،
    /// المستخدم هيغيّر الخيار في الواجهة ومحصلش حاجة في الـ PDF.
    /// </summary>
    [Fact]
    public void Merge_Request_Carries_The_Numbering_Mode()
    {
        var request = MergeRequest.From(
            new PrintSettings { NumberPagesPerFile = true },
            new AppSettings { RestartNumberingForEachFile = true },
            new[] { "a.pdf" },
            "out.pdf");

        Assert.NotNull(request.PageNumbers);
        Assert.True(request.PageNumbers!.RestartForEachFile);
    }

    [Fact]
    public void No_Numbering_Requested_Means_No_Page_Number_Style()
    {
        var request = MergeRequest.From(
            new PrintSettings { NumberPagesPerFile = false },
            new AppSettings(),
            new[] { "a.pdf" },
            "out.pdf");

        Assert.Null(request.PageNumbers);
    }

    // ══════════ اللوحة اللي ورا الرقم ══════════

    /// <summary>
    /// الباج التاني من التجربة: على مذكرة بخلفية كحلي كاملة، الترقيم الأسود
    /// كان بيترسم فوق الخلفية فعلًا بس محدش يقدر يقراه. اللوحة دي هي الحل،
    /// وهي شغالة افتراضيًا لأن اللي واقف على الماكينة مش هيفتح الإعدادات
    /// عشان يكتشفها.
    /// </summary>
    [Fact]
    public void Backdrop_Is_On_By_Default()
    {
        Assert.True(new AppSettings().PageNumberBackdrop);
        Assert.True(new PageNumberStyle().Backdrop);
        Assert.True(PageNumberStyle.From(new AppSettings()).Backdrop);
    }

    [Fact]
    public void Backdrop_Can_Be_Switched_Off()
    {
        var app = new AppSettings { PageNumberBackdrop = false };

        Assert.False(PageNumberStyle.From(app).Backdrop);
    }

    [Theory]
    [InlineData(0, 0, 0)]           // أسود
    [InlineData(27, 42, 74)]        // كحلي — لون البرنامج
    [InlineData(192, 57, 43)]       // أحمر غامق
    public void Dark_Numbers_Are_Reported_As_Dark(byte r, byte g, byte b)
    {
        Assert.False(new RgbColor(r, g, b).IsLight);
    }

    [Theory]
    [InlineData(255, 255, 255)]     // أبيض
    [InlineData(255, 255, 0)]       // أصفر
    [InlineData(200, 200, 200)]     // رمادي فاتح
    public void Light_Numbers_Are_Reported_As_Light(byte r, byte g, byte b)
    {
        Assert.True(new RgbColor(r, g, b).IsLight);
    }

    /// <summary>
    /// السطوع بالوزن الإدراكي مش متوسط حسابي: الأخضر الصافي بيتحس فاتح
    /// والأزرق الصافي بيتحس غامق، رغم إن الاتنين قيمتهم 255 في قناة واحدة.
    /// لو استخدمنا متوسط عادي، الاتنين هيبقوا غامقين والأخضر هياخد لوحة غلط.
    /// </summary>
    [Fact]
    public void Luminance_Is_Perceptual_Not_A_Plain_Average()
    {
        Assert.True(new RgbColor(0, 255, 0).IsLight);
        Assert.False(new RgbColor(0, 0, 255).IsLight);
    }

    [Fact]
    public void Black_And_White_Sit_At_The_Ends_Of_The_Range()
    {
        Assert.Equal(0d, new RgbColor(0, 0, 0).Luminance, 3);
        Assert.Equal(1d, new RgbColor(255, 255, 255).Luminance, 3);
    }
}
