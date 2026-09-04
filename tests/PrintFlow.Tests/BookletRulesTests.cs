using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// بيحمي التحذير اللي بيمنع أوردر كتيّب كامل يروح في الزبالة.
///
/// الحالة اللي التستات دي موجودة عشانها: المستخدم بيفتح «طباعة الملفات
/// بنظام بوكليت»، والوجهين مقفول (وده الافتراضي)، فبيطبع أربع ورقات
/// نُصّهم فاضي بدل ورقتين — والبرنامج مابيقولش حاجة.
///
/// حساب على قيمتين bool، فمالوش دعوة بواجهة ولا طابعة.
/// </summary>
public class BookletRulesTests
{
    // ══════════ الشرط نفسه ══════════

    /// <summary>
    /// الحالة الوحيدة اللي فيها مشكلة: الكتيّب فاتح والوجهين مقفول.
    /// أي تركيبة تانية سليمة — بما فيها الوجهين لوحده (ده طلب عادي جدًا).
    /// </summary>
    [Theory]
    [InlineData(true, false, true)]    // كتيّب + وش واحد ← المشكلة
    [InlineData(true, true, false)]    // كتيّب + وجهين ← تمام
    [InlineData(false, true, false)]   // وجهين لوحده ← تمام
    [InlineData(false, false, false)]  // طباعة عادية ← تمام
    public void Only_Booklet_Without_Duplex_Is_A_Problem(bool booklet, bool duplex, bool expected)
    {
        Assert.Equal(expected, BookletRules.NeedsDuplex(booklet, duplex));
    }

    // ══════════ السطر اللي المستخدم بيقراه ══════════

    /// <summary>
    /// التحذير لازم يقول حاجتين: إيه اللي هيحصل، وإيه اللي يعمله.
    /// من غير التانية، المستخدم بيقرا إن في مشكلة ومايعرفش يصلّحها فين.
    /// </summary>
    [Fact]
    public void The_Warning_Says_What_Goes_Wrong_And_What_To_Do()
    {
        string warning = BookletRules.Describe(bookletMode: true, duplex: false);

        Assert.NotEmpty(warning);
        Assert.Contains("الوجهين", warning);
    }

    /// <summary>مفيش مشكلة = مفيش سطر. الواجهة بتخبّيه على أساس الفاضي ده.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void No_Problem_Means_No_Line(bool booklet, bool duplex)
    {
        Assert.Equal("", BookletRules.Describe(booklet, duplex));
    }

    /// <summary>
    /// ⚠ النجوم بتظهر للمستخدم زي ما هي.
    ///
    /// السطر ده بيتعرض في TextBlock — نص خام، مفيش ماركداون. وقعنا في
    /// دي مرتين قبل كده (في <c>SkippedProcessing</c> و<c>PageRange.Describe</c>)،
    /// والمستخدم شاف نجوم في نص الرسالة.
    /// </summary>
    [Fact]
    public void The_Warning_Has_No_Markdown_Stars()
    {
        string warning = BookletRules.Describe(bookletMode: true, duplex: false);

        Assert.DoesNotContain("*", warning);
        Assert.DoesNotContain("_", warning);
    }

    // ══════════ مصدر واحد للقرار ══════════

    /// <summary>
    /// الواجهة بتنده النسخة اللي بتاخد <c>PrintSettings</c>، والطباعة
    /// بتقرا نفس الخيارات. لازم الاتنين يقولوا نفس الكلام دايمًا.
    ///
    /// من غير التست ده، حد ممكن يعدّل شرط ويسيب التاني — والواجهة
    /// تقول "تمام" والطباعة تطلّع ورق ضايع.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void The_Settings_Overload_Agrees_With_The_Raw_One(bool booklet, bool duplex)
    {
        var settings = new PrintSettings { BookletMode = booklet, Duplex = duplex };

        Assert.Equal(BookletRules.NeedsDuplex(booklet, duplex), BookletRules.NeedsDuplex(settings));
        Assert.Equal(BookletRules.Describe(booklet, duplex), BookletRules.Describe(settings));
    }

    /// <summary>
    /// الافتراضي في البرنامج: الكتيّب مقفول والوجهين مقفول. أول ما
    /// المستخدم يفتح الكتيّب لوحده، التحذير لازم يظهر فورًا.
    /// </summary>
    [Fact]
    public void Turning_Booklet_On_Alone_Raises_The_Warning()
    {
        var settings = new PrintSettings();

        Assert.False(BookletRules.NeedsDuplex(settings));

        settings.BookletMode = true;

        Assert.True(BookletRules.NeedsDuplex(settings));

        settings.Duplex = true;

        Assert.False(BookletRules.NeedsDuplex(settings));
    }
}
