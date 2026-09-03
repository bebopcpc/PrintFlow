using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// بيحمي مدى الصفحات — «اطبع من ٥ لـ ٢٠».
///
/// أخطر حاجة هنا مش المدى الغلط، لأ: **المدى اللي بيطبع ناقص من غير ما
/// حد ياخد باله**. عشان كده أغلب التستات دي على الحالات الشاذة، مش على
/// الحالة السليمة.
/// </summary>
public class PageRangeTests
{
    // ══════════ الحالة العادية ══════════

    [Fact]
    public void A_Real_Range_Resolves_As_Written()
    {
        Assert.Equal((5, 20), PageRange.Resolve(from: 5, to: 20, pageCount: 180));
    }

    [Fact]
    public void No_Range_Means_The_Whole_Document()
    {
        Assert.Equal((1, 180), PageRange.Resolve(from: 0, to: 0, pageCount: 180));
        Assert.False(PageRange.IsSubset(0, 0));
    }

    [Fact]
    public void From_Only_Runs_To_The_Last_Page()
    {
        Assert.Equal((5, 180), PageRange.Resolve(from: 5, to: 0, pageCount: 180));
    }

    [Fact]
    public void To_Only_Starts_From_The_First_Page()
    {
        Assert.Equal((1, 20), PageRange.Resolve(from: 0, to: 20, pageCount: 180));
    }

    // ══════════ الحالات اللي بتوقع الناس ══════════

    /// <summary>
    /// المستخدم كتب «لحد ٢٠٠» في مستند ٢٠ صفحة. بنقصّها على الموجود بدل
    /// ما نبعت للطابعة رقم متعرفهوش.
    /// </summary>
    [Fact]
    public void A_Range_Past_The_End_Is_Trimmed_To_The_Document()
    {
        Assert.Equal((5, 20), PageRange.Resolve(from: 5, to: 200, pageCount: 20));
    }

    /// <summary>
    /// ⚠ ده الباج اللي التست مسكه وأنا بكتب الميزة.
    ///
    /// أول نسخة كانت بتقص البداية على النهاية كمان، فـ «من ٢٠ لـ ٥» كانت
    /// بتطلع <c>(5, 5)</c> — يعني المستخدم يغلط في الأرقام ويطبع **صفحة
    /// واحدة** والبرنامج ساكت. دلوقتي بنرفض المدى كله ونطبع المستند كامل.
    /// </summary>
    [Fact]
    public void A_Backwards_Range_Is_Refused_Not_Silently_Fixed()
    {
        Assert.Equal((0, 0), PageRange.Resolve(from: 20, to: 5, pageCount: 20));
        Assert.Contains("مقلوب", PageRange.Describe(20, 5));
    }

    [Fact]
    public void A_Start_Past_The_End_Of_The_Document_Is_Refused()
    {
        Assert.Equal((0, 0), PageRange.Resolve(from: 25, to: 0, pageCount: 20));
    }

    /// <summary>
    /// عدد الصفحات مجهول + مفيش «لحد صفحة» = مانعرفش آخر صفحة. الطباعة
    /// كاملة أأمن من إننا نخمّن ونطبع ناقص.
    /// </summary>
    [Fact]
    public void Unknown_Page_Count_Without_An_End_Gives_Up()
    {
        Assert.Equal((0, 0), PageRange.Resolve(from: 5, to: 0, pageCount: 0));
    }

    [Fact]
    public void Unknown_Page_Count_With_An_End_Still_Works()
    {
        // «من ٥ لـ ١٢» مالهاش دعوة بعدد صفحات المستند
        Assert.Equal((5, 12), PageRange.Resolve(from: 5, to: 12, pageCount: 0));
    }

    // ══════════ التحذير في الواجهة ══════════

    /// <summary>
    /// المدى بيتحفظ في الإعدادات، فلو حد ظبطه ونسيه هيفضل كل أوردر
    /// بعد كده يتقص. عشان كده السطر الأحمر لازم يبان في كل حالة جزئية.
    /// </summary>
    [Theory]
    [InlineData(5, 20)]
    [InlineData(5, 0)]
    [InlineData(0, 10)]
    [InlineData(2, 0)]
    public void Any_Partial_Range_Raises_The_Warning(int from, int to)
    {
        Assert.True(PageRange.IsSubset(from, to));
        Assert.NotEmpty(PageRange.Describe(from, to));
    }

    /// <summary>
    /// وفي نفس الوقت مايطلعش تحذير على حاجة مش محتاجة تحذير — التحذير
    /// اللي بيطلع دايمًا بيتحوّل لضوضاء والناس بتبطّل تقراه.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    public void The_Whole_Document_Raises_No_Warning(int from, int to)
    {
        Assert.False(PageRange.IsSubset(from, to));
        Assert.Empty(PageRange.Describe(from, to));
    }

    [Fact]
    public void The_Warning_Says_The_Actual_Numbers()
    {
        string line = PageRange.Describe(5, 20);

        Assert.Contains("5", line);
        Assert.Contains("20", line);
    }
}
