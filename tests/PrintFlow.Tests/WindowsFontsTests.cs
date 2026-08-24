using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// الخطوط. الباج اللي بيوجع هنا هادي: الـ PDF بيطلع مربعات فاضية مكان العربي
/// من غير أي رسالة خطأ، والمستخدم بيكتشفها بعد ما يطبع الورق.
///
/// خط الدفاع: القايمة اللي المستخدم بيختار منها مافيهاش غير خطوط بتغطي
/// العربي واللاتيني مع بعض — فالاختيار الغلط مش ممكن أصلًا.
/// </summary>
public class WindowsFontsTests
{
    [Fact]
    public void Every_Offered_Font_Covers_Arabic_And_Latin()
    {
        // الجدول ده هو العقد. أي إضافة ليه لازم تعدّي على الشرط ده يدويًا
        // قبل ما تتحط: الخط بييجي مع ويندوز وفيه أشكال العرض العربية.
        string[] expected = ["Arial", "Tahoma", "Times New Roman", "Courier New", "Segoe UI"];

        Assert.Equal(expected, WindowsFonts.ArabicCapable.Select(f => f.DisplayName));
    }

    [Fact]
    public void Arial_Is_The_First_Choice_Because_Every_Windows_Has_It()
    {
        Assert.Equal("Arial", WindowsFonts.ArabicCapable[0].DisplayName);
        Assert.Equal("Arial", WindowsFonts.FallbackFamily);
    }

    [Theory]
    [InlineData("Arial", "arial", "arialbd")]
    [InlineData("Tahoma", "tahoma", "tahomabd")]
    [InlineData("Times New Roman", "times", "timesbd")]
    [InlineData("Courier New", "cour", "courbd")]
    [InlineData("Segoe UI", "segoeui", "segoeuib")]
    public void Font_Names_Map_To_The_Right_Windows_Files(string family, string regular, string bold)
    {
        var font = WindowsFonts.Resolve(family);

        Assert.Equal(regular, font.RegularFile);
        Assert.Equal(bold, font.BoldFile);
    }

    /// <summary>
    /// Helvetica كان الخط الافتراضي القديم وهو مش موجود على ويندوز خالص.
    /// أي إعدادات محفوظة من نسخة قديمة لازم تفضل شغالة.
    /// </summary>
    [Theory]
    [InlineData("Helvetica", "arial")]
    [InlineData("Times", "times")]
    [InlineData("Courier", "cour")]
    public void Legacy_Font_Names_Still_Resolve(string oldName, string expectedFile)
    {
        Assert.Equal(expectedFile, WindowsFonts.Resolve(oldName).RegularFile);
    }

    [Theory]
    [InlineData("خط مش موجود")]
    [InlineData("Comic Sans MS")]
    [InlineData("")]
    [InlineData((string?)null)]
    public void Unknown_Font_Falls_Back_To_Arial_Not_A_Crash(string? family)
    {
        Assert.Equal("arial", WindowsFonts.Resolve(family).RegularFile);
    }

    [Fact]
    public void Font_Name_Matching_Ignores_Case()
    {
        Assert.Equal("times", WindowsFonts.Resolve("TIMES NEW ROMAN").RegularFile);
        Assert.Equal("arial", WindowsFonts.Resolve("arial").RegularFile);
    }

    [Fact]
    public void Bold_And_Regular_Files_Are_Never_The_Same()
    {
        foreach (var font in WindowsFonts.ArabicCapable)
        {
            Assert.NotEqual(font.RegularFile, font.BoldFile);
        }
    }

    /// <summary>
    /// على جهاز مالوش خطوط ويندوز (زي سيرفر البناء)، القايمة لازم ترجّع
    /// حاجة بدل ما تبقى فاضية وتسيب المستخدم قدام ComboBox فاضي.
    /// </summary>
    [Fact]
    public void Installed_List_Is_Never_Empty()
    {
        Assert.NotEmpty(WindowsFonts.InstalledNames());
    }

    [Fact]
    public void Installed_List_Only_Contains_Known_Fonts()
    {
        var known = WindowsFonts.ArabicCapable.Select(f => f.DisplayName).ToHashSet();

        foreach (string name in WindowsFonts.InstalledNames())
        {
            Assert.Contains(name, known);
        }
    }
}
