using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// مهلة انتظار الطباعة.
///
/// الباج اللي التستات دي بتمنع رجوعه: المهلة كانت **دقيقتين ثابتة** لأي جوب،
/// مبنية على فهم غلط إن Sumatra بيسلّم الملف ويخرج فورًا. ملف ٢١٠ صفحة بياخد
/// حوالي تلات دقايق، يعني كان بيتقتل في نص الطباعة — والورق بيطلع ناقص من
/// غير ما حد يعرف إن في حاجة حصلت.
/// </summary>
public class SpoolTimeoutPolicyTests
{
    /// <summary>الحالة اللي فضحت الباج: ٢١٠ صفحة أخدت ~٣ دقايق والمهلة كانت ٢.</summary>
    [Fact]
    public void The_Two_Hundred_And_Ten_Page_Job_Gets_More_Than_Three_Minutes()
    {
        var timeout = SpoolTimeoutPolicy.For(pageCount: 210, copies: 1);

        Assert.True(timeout.TotalMinutes > 3,
            $"٢١٠ صفحة لازم تاخد أكتر من ٣ دقايق، وأخدت {timeout.TotalMinutes:0.0}");
    }

    /// <summary>
    /// ورقة واحدة مش محتاجة أكتر من ثواني، بس المهلة مابتنزلش تحت الحد الأدنى —
    /// الطابعة ممكن تكون بتصحى من السكون أو الشبكة بطيئة.
    /// </summary>
    [Fact]
    public void A_Small_Job_Still_Gets_At_Least_The_Minimum()
    {
        var timeout = SpoolTimeoutPolicy.For(1, 1);

        Assert.True(timeout >= SpoolTimeoutPolicy.Minimum);
        Assert.True(timeout < SpoolTimeoutPolicy.Minimum + TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void More_Copies_Means_More_Time()
    {
        var one = SpoolTimeoutPolicy.For(200, 1);
        var fifty = SpoolTimeoutPolicy.For(200, 50);

        Assert.True(fifty > one);
    }

    [Fact]
    public void More_Pages_Means_More_Time()
    {
        Assert.True(SpoolTimeoutPolicy.For(2000, 1) > SpoolTimeoutPolicy.For(200, 1));
    }

    /// <summary>
    /// شغل مطبعة حقيقي: ٥٠٠ صفحة × ٢٠ نسخة = ١٠ آلاف ورقة. لازم ياخد وقت
    /// محترم، مش يتقتل بعد شوية.
    /// </summary>
    [Fact]
    public void A_Real_Print_Shop_Run_Gets_A_Long_Timeout()
    {
        var timeout = SpoolTimeoutPolicy.For(pageCount: 500, copies: 20);

        Assert.True(timeout.TotalMinutes >= 60, $"لقينا {timeout.TotalMinutes:0}");
    }

    /// <summary>الجوب اتعلّق فعلًا — لازم في سقف، مننتظرش للأبد.</summary>
    [Fact]
    public void There_Is_Always_A_Ceiling()
    {
        Assert.Equal(SpoolTimeoutPolicy.Maximum, SpoolTimeoutPolicy.For(1_000_000, 1000));
    }

    [Fact]
    public void Huge_Numbers_Do_Not_Overflow_Into_A_Short_Timeout()
    {
        var timeout = SpoolTimeoutPolicy.For(int.MaxValue, int.MaxValue);

        Assert.Equal(SpoolTimeoutPolicy.Maximum, timeout);
        Assert.True(timeout > TimeSpan.Zero);
    }

    /// <summary>
    /// لما مانعرفش عدد الصفحات (وضع "من غير دمج" مثلًا)، الأأمن إننا نستنى
    /// أكتر مش أقل — قطع شغل شغال أسوأ بكتير من انتظار زيادة.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(210, 0)]
    public void An_Unknown_Page_Count_Falls_Back_To_A_Generous_Timeout(int pages, int copies)
    {
        var timeout = SpoolTimeoutPolicy.For(pages, copies);

        Assert.Equal(SpoolTimeoutPolicy.WhenPageCountIsUnknown, timeout);
        Assert.True(timeout > SpoolTimeoutPolicy.Minimum);
    }

    /// <summary>المهلة القديمة الثابتة. أي جوب حقيقي لازم ياخد أكتر منها.</summary>
    [Theory]
    [InlineData(210, 1)]
    [InlineData(50, 10)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void Nothing_Gets_Less_Than_The_Old_Two_Minute_Limit(int pages, int copies)
    {
        Assert.True(SpoolTimeoutPolicy.For(pages, copies) > TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void The_Page_Count_Reaches_The_Print_Job()
    {
        var job = PrintJob.From(new PrintSettings(), "a.pdf", "HP", copies: 3, pageCount: 210);

        Assert.Equal(210, job.PageCount);
        Assert.Equal(3, job.Copies);
    }

    /// <summary>
    /// الكود القديم بينادي From بأربع براميترات. لازم يفضل شغال،
    /// وساعتها عدد الصفحات بيبقى مش معروف مش صفر غلط.
    /// </summary>
    [Fact]
    public void An_Omitted_Page_Count_Means_Unknown_Not_Zero_Pages()
    {
        var job = PrintJob.From(new PrintSettings(), "a.pdf", "HP", copies: 1);

        Assert.Equal(0, job.PageCount);
        Assert.Equal(SpoolTimeoutPolicy.WhenPageCountIsUnknown,
            SpoolTimeoutPolicy.For(job.PageCount, job.Copies));
    }
}
