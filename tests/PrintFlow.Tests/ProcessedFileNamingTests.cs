using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// أسامي الملفات الناتجة من وضع "من غير دمج".
///
/// الحالة اللي كل ده موجود عشانها: المطبعة بتحمّل ملفات من مجلدات مختلفة،
/// وفيهم اتنين اسمهم "فاتورة.pdf". من غير الرقم في الأول، التاني هيدهس
/// الأول ويطلع للزبون نسخة ناقصة من غير ما حد ياخد باله.
/// </summary>
public class ProcessedFileNamingTests
{
    [Fact]
    public void The_Order_Number_Comes_First_So_Sorting_Matches_Print_Order()
    {
        Assert.Equal("01_فاتورة.pdf", ProcessedFileNaming.NameFor(1, @"C:\a\فاتورة.pdf"));
        Assert.Equal("07_تقرير.pdf", ProcessedFileNaming.NameFor(7, @"D:\b\تقرير.pdf"));
    }

    /// <summary>ترتيب ٢ رقم عشان ١٠ ماتجيش قبل ٢ في ترتيب المجلد.</summary>
    [Fact]
    public void Single_Digits_Are_Padded()
    {
        var names = new[] { 1, 2, 10, 11 }
            .Select(i => ProcessedFileNaming.NameFor(i, "x.pdf"))
            .ToList();

        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
    }

    [Fact]
    public void Two_Files_With_The_Same_Name_Do_Not_Collide()
    {
        string first = ProcessedFileNaming.NameFor(1, @"C:\مجلد1\فاتورة.pdf");
        string second = ProcessedFileNaming.NameFor(2, @"C:\مجلد2\فاتورة.pdf");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void The_Original_Name_Is_Kept_So_The_Operator_Recognises_It()
    {
        Assert.Contains("كشف حساب مارس", ProcessedFileNaming.NameFor(3, @"C:\x\كشف حساب مارس.pdf"));
    }

    // ══════════ الأسامي اللي ويندوز بيرفضها ══════════

    [Theory]
    [InlineData("تقرير:2024")]
    [InlineData("ملف|مهم")]
    [InlineData("أ/ب")]
    [InlineData("سؤال?")]
    [InlineData("نجمة*")]
    public void Characters_Windows_Rejects_Are_Replaced(string stem)
    {
        string name = ProcessedFileNaming.NameFor(1, stem + ".pdf");

        Assert.DoesNotContain(':', name[2..]);   // بنتخطى "01" نفسها
        Assert.False(name.Contains('|') || name.Contains('?') || name.Contains('*') || name.Contains('/'));
        Assert.EndsWith(".pdf", name);
    }

    [Fact]
    public void A_Very_Long_Name_Is_Trimmed_To_Survive_The_Path_Limit()
    {
        string name = ProcessedFileNaming.NameFor(1, new string('ط', 300) + ".pdf");

        Assert.True(name.Length < 100, $"الطول {name.Length}");
        Assert.EndsWith(".pdf", name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_Empty_Name_Still_Produces_A_Usable_File(string path)
    {
        string name = ProcessedFileNaming.NameFor(1, path);

        Assert.StartsWith("01_", name);
        Assert.EndsWith(".pdf", name);
        Assert.True(name.Length > 7);
    }

    [Fact]
    public void A_Trailing_Dot_Is_Removed_Because_Windows_Rejects_It()
    {
        string name = ProcessedFileNaming.NameFor(1, "اسم منقوط...pdf");

        Assert.DoesNotContain("..pdf", name);
    }

    // ══════════ عدم الدهس ══════════

    [Fact]
    public void A_Free_Name_Is_Used_As_Is()
    {
        Assert.Equal("01_أ.pdf", ProcessedFileNaming.MakeUnique("01_أ.pdf", _ => false));
    }

    /// <summary>
    /// المستخدم اختار مجلد دائم وشغّل نفس الشغل مرتين. الملف القديم
    /// **ماينفعش** يتدهس — ممكن يكون اتبعت للزبون خلاص.
    /// </summary>
    [Fact]
    public void An_Existing_File_Is_Never_Overwritten()
    {
        var onDisk = new HashSet<string> { "01_أ.pdf" };

        string name = ProcessedFileNaming.MakeUnique("01_أ.pdf", onDisk.Contains);

        Assert.Equal("01_أ (2).pdf", name);
        Assert.DoesNotContain(name, onDisk);
    }

    [Fact]
    public void The_Counter_Keeps_Climbing_Past_Several_Copies()
    {
        var onDisk = new HashSet<string> { "01_أ.pdf", "01_أ (2).pdf", "01_أ (3).pdf" };

        Assert.Equal("01_أ (4).pdf", ProcessedFileNaming.MakeUnique("01_أ.pdf", onDisk.Contains));
    }

    /// <summary>لو حاجة غريبة خلّت كل اسم "موجود"، لازم نخرج مش نعلّق للأبد.</summary>
    [Fact]
    public void It_Gives_Up_Gracefully_Instead_Of_Hanging()
    {
        string name = ProcessedFileNaming.MakeUnique("01_أ.pdf", _ => true);

        Assert.EndsWith(".pdf", name);
        Assert.StartsWith("01_أ", name);
    }
}
