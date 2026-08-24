using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// الكلاس ده كان مغطّى بصفر تستات، وهو بالظبط المكان اللي كانت فيه التلات باجات.
/// كل تست هنا مربوط بحالة حقيقية بتحصل في الطباعة.
/// </summary>
public class ArabicTextShaperTests
{
    private const char LamAlefIsolated = 'ﻻ'; // ﻻ
    private const char LamAlefFinal = 'ﻼ';    // ﻼ
    private const char Fatha = 'َ';

    // ══════════ الباج الأول: الأرقام كانت بتتقلب ══════════

    [Theory]
    [InlineData("صفحة 3 من 12", "12")]
    [InlineData("صفحة 25 من 100", "100")]
    [InlineData("صفحة 7 من 1250", "1250")]
    public void Page_Total_Keeps_Digit_Order(string input, string expectedNumber)
    {
        string result = ArabicTextShaper.Reshape(input);
        Assert.Contains(expectedNumber, result);
    }

    [Fact]
    public void Page_Number_Twelve_Does_Not_Become_TwentyOne()
    {
        string result = ArabicTextShaper.Reshape("صفحة 3 من 12");

        Assert.Contains("12", result);
        Assert.DoesNotContain("21", result);
    }

    /// <summary>
    /// الرقم الأخير منطقيًا هو أول حاجة على الشمال بصريًا — لأن الاتجاه الأساسي RTL.
    /// </summary>
    [Fact]
    public void Rightmost_Logical_Word_Ends_Up_Leftmost_Visually()
    {
        string result = ArabicTextShaper.Reshape("صفحة 3 من 12");
        Assert.StartsWith("12", result);
    }

    [Fact]
    public void Both_Numbers_In_Page_Label_Stay_Correct()
    {
        string result = ArabicTextShaper.Reshape("صفحة 25 من 100");

        Assert.Contains("25", result);
        Assert.Contains("100", result);
        Assert.DoesNotContain("52", result);
        Assert.DoesNotContain("001", result);
    }

    // ══════════ الباج التاني: الإنجليزي كان بيتقلب ══════════

    [Theory]
    [InlineData("نسخة PrintFlow", "PrintFlow")]
    [InlineData("شركة النور Ltd", "Ltd")]
    [InlineData("مطبعة Alpha للطباعة", "Alpha")]
    public void Latin_Words_Keep_Their_Order(string input, string expectedWord)
    {
        string result = ArabicTextShaper.Reshape(input);
        Assert.Contains(expectedWord, result);
    }

    [Fact]
    public void PrintFlow_Does_Not_Become_Backwards()
    {
        string result = ArabicTextShaper.Reshape("نسخة PrintFlow");

        Assert.Contains("PrintFlow", result);
        Assert.DoesNotContain("wolFtnirP", result);
    }

    [Fact]
    public void Mixed_Watermark_Keeps_Latin_And_Digits()
    {
        string result = ArabicTextShaper.Reshape("مطبعة النور 2026 Cairo");

        Assert.Contains("2026", result);
        Assert.Contains("Cairo", result);
    }

    // ══════════ الباج التالت: لام-ألف مش متشبكة ══════════

    [Fact]
    public void Lam_Alef_Becomes_One_Ligature_Not_Two_Letters()
    {
        string result = ArabicTextShaper.Reshape("لا");

        Assert.Equal(1, result.Length);
        Assert.Equal(LamAlefIsolated, result[0]);
    }

    [Fact]
    public void Lam_Alef_Uses_Final_Form_When_Connected_To_Previous_Letter()
    {
        // في "بلا" اللام موصولة بالباء، فالـ ligature لازم يبقى الشكل النهائي
        string result = ArabicTextShaper.Reshape("بلا");
        Assert.Contains(LamAlefFinal, result);
    }

    [Fact]
    public void Lam_Alef_Uses_Isolated_Form_After_Non_Connecting_Letter()
    {
        // الواو مبتوصلش لقدام، فاللام بتبدأ من جديد
        string result = ArabicTextShaper.Reshape("ولا");
        Assert.Contains(LamAlefIsolated, result);
    }

    [Theory]
    [InlineData("لأ", 'ﻷ')]
    [InlineData("لإ", 'ﻹ')]
    [InlineData("لآ", 'ﻵ')]
    public void All_Lam_Alef_Variants_Are_Handled(string input, char expected)
    {
        Assert.Contains(expected, ArabicTextShaper.Reshape(input));
    }

    [Fact]
    public void Lam_Alef_Inside_A_Word_Shortens_The_Result()
    {
        // "ملاحظة" فيها لام-ألف، فالناتج لازم يبقى أقصر بحرف من المدخل
        string result = ArabicTextShaper.Reshape("ملاحظة");
        Assert.Equal("ملاحظة".Length - 1, result.Length);
    }

    // ══════════ التشكيل ══════════

    [Fact]
    public void Diacritics_Do_Not_Break_Letter_Joining()
    {
        string withMark = ArabicTextShaper.Reshape("بَاب");
        string withoutMark = ArabicTextShaper.Reshape("باب");

        string stripped = new(withMark.Where(c => c != Fatha).ToArray());
        Assert.Equal(withoutMark, stripped);
    }

    [Fact]
    public void Diacritic_Stays_Attached_After_Its_Letter()
    {
        string result = ArabicTextShaper.Reshape("بَاب");

        int markIndex = result.IndexOf(Fatha);
        Assert.True(markIndex > 0, "التشكيل مالوش حرف قبله — يبقى اتفصل عن حرفه");
    }

    // ══════════ الأقواس ══════════

    [Fact]
    public void Brackets_Are_Mirrored_So_They_Look_Right()
    {
        string result = ArabicTextShaper.Reshape("(ملاحظة)");

        Assert.StartsWith("(", result);
        Assert.EndsWith(")", result);
    }

    // ══════════ التشبيك الأساسي ══════════

    [Fact]
    public void Letters_Get_Their_Connected_Forms()
    {
        string result = ArabicTextShaper.Reshape("بيت");

        // مفيش أي حرف فاضل بشكله المنفصل الأصلي
        Assert.DoesNotContain('ب', result);
        Assert.DoesNotContain('ي', result);
        Assert.DoesNotContain('ت', result);
    }

    [Fact]
    public void Word_Length_Is_Preserved_When_No_Ligature()
    {
        Assert.Equal(3, ArabicTextShaper.Reshape("بيت").Length);
        Assert.Equal(4, ArabicTextShaper.Reshape("كتاب").Length);
    }

    // ══════════ حالات حدّية ══════════

    [Theory]
    [InlineData("")]
    [InlineData("Hello World")]
    [InlineData("12345")]
    [InlineData("Page 3 of 12")]
    public void Text_Without_Arabic_Is_Returned_Unchanged(string input)
    {
        Assert.Equal(input, ArabicTextShaper.Reshape(input));
    }

    [Fact]
    public void Null_Is_Returned_As_Is()
    {
        Assert.Null(ArabicTextShaper.Reshape(null!));
    }

    [Fact]
    public void Single_Arabic_Letter_Stays_Isolated()
    {
        Assert.Equal("ب", ArabicTextShaper.Reshape("ب"));
    }

    [Fact]
    public void Spaces_Between_Words_Are_Preserved()
    {
        string result = ArabicTextShaper.Reshape("مطبعة النور الحديثة");
        Assert.Equal(2, result.Count(c => c == ' '));
    }

    [Fact]
    public void Reshaping_Never_Loses_Characters_Except_Ligatures()
    {
        const string input = "شركة الأمل للطباعة 2026";
        string result = ArabicTextShaper.Reshape(input);

        int ligatures = result.Count(c => c is >= 'ﻵ' and <= 'ﻼ');
        Assert.Equal(input.Length - ligatures, result.Length);
    }
}
