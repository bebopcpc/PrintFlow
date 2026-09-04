using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// بيحمي الرقم اللي المطبعة بتحضّر الورق عليه وبتسعّر بيه.
///
/// الغلط هنا مابيبانش لحد ما الورق يخلص في نص الأوردر، أو لحد ما
/// الفاتورة تطلع أقل من التكلفة. عشان كده كل تركيبة ليها تست.
///
/// حساب خالص على أرقام — مالوش دعوة بواجهة ولا طابعة.
/// </summary>
public class PaperCountTests
{
    private static PrintSettings Plain() => new();

    // ══════════ الحالة البسيطة ══════════

    [Fact]
    public void One_Sided_Printing_Is_One_Sheet_Per_Page()
    {
        var tally = PaperCount.For([24], Plain());

        Assert.Equal(24, tally.Sides);
        Assert.Equal(24, tally.Sheets);
    }

    [Fact]
    public void Duplex_Halves_The_Paper()
    {
        var settings = Plain();
        settings.Duplex = true;

        var tally = PaperCount.For([24], settings);

        Assert.Equal(24, tally.Sides);
        Assert.Equal(12, tally.Sheets);
    }

    /// <summary>
    /// ⚠ مستند فردي على الوجهين بياخد ورقة زيادة — آخر ورقة وشها مطبوع
    /// وضهرها فاضي. القسمة العادية كانت هتضيّع الورقة دي.
    /// </summary>
    [Fact]
    public void An_Odd_Page_Count_Still_Needs_The_Last_Sheet()
    {
        var settings = Plain();
        settings.Duplex = true;

        Assert.Equal(3, PaperCount.For([5], settings).Sheets);
    }

    /// <summary>
    /// ⚠ ده الباج اللي التست ده موجود عشانه.
    ///
    /// عشر ملفات كل واحد ٥ صفحات على الوجهين = ٣٠ ورقة (٣ لكل ملف).
    /// لو جمعنا الأوجه الأول (٥٠) وقسمنا بعدين (٢٥)، كنا هنقول ٢٥ —
    /// يعني **خمس ورقات ناقصة** من تحضير الأوردر.
    ///
    /// كل مستند بيتطبع لوحده، فالورق بيتحسب لوحده.
    /// </summary>
    [Fact]
    public void Paper_Is_Counted_Per_Document_Not_On_The_Total()
    {
        var settings = Plain();
        settings.Duplex = true;

        var ten = Enumerable.Repeat(5, 10).ToList();

        Assert.Equal(30, PaperCount.For(ten, settings).Sheets);
    }

    // ══════════ الشرائح ══════════

    [Theory]
    [InlineData(1, 24)]
    [InlineData(2, 12)]
    [InlineData(4, 6)]
    [InlineData(9, 3)]   // ٢٤ ÷ ٩ = ٢.٦٦ → ٣ أوجه
    public void Slides_Per_Sheet_Divide_The_Sides(int perSheet, int expected)
    {
        var settings = Plain();
        settings.SlidesPerSheet = perSheet;

        Assert.Equal(expected, PaperCount.For([24], settings).Sides);
    }

    /// <summary>الشرائح والوجهين مع بعض: ٢٤ صفحة، ٢ في الورقة، وجهين = ٦ ورقات.</summary>
    [Fact]
    public void Slides_And_Duplex_Multiply_The_Saving()
    {
        var settings = Plain();
        settings.SlidesPerSheet = 2;
        settings.Duplex = true;

        var tally = PaperCount.For([24], settings);

        Assert.Equal(12, tally.Sides);
        Assert.Equal(6, tally.Sheets);
    }

    // ══════════ الكتيّب ══════════

    /// <summary>
    /// كتيّب ٨ صفحات = ورقتين على الوجهين. نفس رقم
    /// <see cref="BookletImposition.SheetCount"/> بالظبط — لو الاتنين
    /// اختلفوا يبقى واحد فيهم بيكدب.
    /// </summary>
    [Fact]
    public void A_Booklet_With_Duplex_Matches_The_Imposition()
    {
        var settings = Plain();
        settings.BookletMode = true;
        settings.Duplex = true;

        Assert.Equal(BookletImposition.SheetCount(8), PaperCount.For([8], settings).Sheets);
        Assert.Equal(2, PaperCount.For([8], settings).Sheets);
    }

    /// <summary>
    /// ⚠ الرقم اللي بيدّي معنى لتحذير <see cref="BookletRules"/>.
    ///
    /// نفس الكتيّب من غير وجهين بياخد **ضِعف** الورق: كل وش بيروح على
    /// ورقة لوحده، ونُص كل ورقة بيفضل فاضي.
    /// </summary>
    [Fact]
    public void A_Booklet_Without_Duplex_Eats_Double_The_Paper()
    {
        var withDuplex = Plain();
        withDuplex.BookletMode = true;
        withDuplex.Duplex = true;

        var without = Plain();
        without.BookletMode = true;

        Assert.Equal(2, PaperCount.For([8], withDuplex).Sheets);
        Assert.Equal(4, PaperCount.For([8], without).Sheets);
    }

    /// <summary>الكتيّب بيكمّل لمضاعف ٤ — ٦ صفحات بتاخد نفس ورق الـ ٨.</summary>
    [Fact]
    public void A_Booklet_Pads_Up_To_A_Multiple_Of_Four()
    {
        var settings = Plain();
        settings.BookletMode = true;
        settings.Duplex = true;

        Assert.Equal(2, PaperCount.For([6], settings).Sheets);
    }

    /// <summary>
    /// الكتيّب بيتجاهل عدد الشرائح — ده مكتوب في <c>SlideRequest</c>،
    /// والعدّاد لازم يمشي وراه. لو اتخالفوا، الرقم بيكدب على المستخدم.
    /// </summary>
    [Fact]
    public void A_Booklet_Ignores_Slides_Per_Sheet()
    {
        var settings = Plain();
        settings.BookletMode = true;
        settings.Duplex = true;
        settings.SlidesPerSheet = 4;

        Assert.Equal(2, PaperCount.For([8], settings).Sheets);
    }

    // ══════════ الحذف والمدى ══════════

    [Fact]
    public void Deleted_Pages_Do_Not_Cost_Paper()
    {
        var settings = Plain();
        settings.DeletePages = true;
        settings.PagesToDelete = "1-4";

        Assert.Equal(20, PaperCount.For([24], settings).Sheets);
    }

    [Fact]
    public void A_Page_Range_Trims_The_Count()
    {
        var settings = Plain();
        settings.PageFrom = 5;
        settings.PageTo = 20;

        Assert.Equal(16, PaperCount.For([24], settings).Sheets);
    }

    /// <summary>
    /// ⚠ المدى بيتنفّذ **بعد** التجميع، مش قبله.
    ///
    /// المعالجة بتطلّع ملف ١٢ وش (٢٤ صفحة، ٢ في الورقة)، وبعدين الطباعة
    /// بتقول للطابعة "طلّع من ٥ لـ ٨" على الملف ده — يعني ٤ أوجه.
    ///
    /// لو قصّينا الصفحات الأصلية الأول (٤ صفحات) وجمّعنا بعدين، كنا
    /// هنقول ٢ — والرقم ده مالوش أي علاقة باللي هيطلع.
    /// </summary>
    [Fact]
    public void The_Range_Applies_After_The_Slides_Not_Before()
    {
        var settings = Plain();
        settings.SlidesPerSheet = 2;
        settings.PageFrom = 5;
        settings.PageTo = 8;

        Assert.Equal(4, PaperCount.For([24], settings).Sides);
    }

    // ══════════ النسخ والمكن ══════════

    [Fact]
    public void Copies_Multiply_The_Paper()
    {
        var settings = Plain();
        settings.TotalCopies = 10;

        Assert.Equal(240, PaperCount.For([24], settings).Sheets);
    }

    /// <summary>التوزيع بيقسّم النسخ على المكن — المجموع مايتغيّرش.</summary>
    [Fact]
    public void Distributing_Does_Not_Change_The_Total()
    {
        var settings = Plain();
        settings.TotalCopies = 10;
        settings.DistributeCopies = true;

        Assert.Equal(240, PaperCount.For([24], settings, machines: 4).Sheets);
    }

    /// <summary>
    /// ⚠ من غير توزيع، كل مكنة بتطلّع العدد كامل — الورق بيتضرب في
    /// عدد المكن. ده أخطر رقم في الملف ده: ١٠ نسخ على ٤ مكن = ٤٠ نسخة
    /// ورق، واللي حضّر ورق ١٠ نسخ هيقف في نص الأوردر.
    /// </summary>
    [Fact]
    public void Without_Distributing_Every_Machine_Prints_The_Whole_Order()
    {
        var settings = Plain();
        settings.TotalCopies = 10;
        settings.DistributeCopies = false;

        Assert.Equal(960, PaperCount.For([24], settings, machines: 4).Sheets);
    }

    // ══════════ الملفات اللي خلصت معالجة ══════════

    /// <summary>
    /// ⚠ الملف اللي خرج من المعالجة اتحذف واتجمّع خلاص.
    ///
    /// لو حسبنا عليه تاني، ٢٤ صفحة اتحوّلت لـ ١٢ وش هتتحسب ٦ — نُص
    /// الرقم الحقيقي. المدى بس هو اللي لسه ماتنفّذش عليه.
    /// </summary>
    [Fact]
    public void A_Processed_File_Is_Not_Composed_Twice()
    {
        var settings = Plain();
        settings.SlidesPerSheet = 2;

        // الملف الطالع من المعالجة فيه ١٢ وش خلاص
        Assert.Equal(12, PaperCount.For([12], settings, alreadyProcessed: true).Sides);
    }

    [Fact]
    public void A_Processed_File_Still_Respects_The_Range_And_Duplex()
    {
        var settings = Plain();
        settings.Duplex = true;
        settings.PageFrom = 1;
        settings.PageTo = 10;

        var tally = PaperCount.For([12], settings, alreadyProcessed: true);

        Assert.Equal(10, tally.Sides);
        Assert.Equal(5, tally.Sheets);
    }

    // ══════════ الحالات الفاضية ══════════

    [Fact]
    public void Nothing_Loaded_Counts_Nothing()
    {
        Assert.True(PaperCount.For([], Plain()).IsEmpty);
        Assert.Equal("", PaperCount.Describe([], Plain()));
    }

    /// <summary>الصفر معناه مقدرناش نعد الملف — مش صفر ورقة.</summary>
    [Fact]
    public void An_Uncountable_File_Is_Skipped_Not_Guessed()
    {
        Assert.True(PaperCount.For([0], Plain()).IsEmpty);
        Assert.Equal(24, PaperCount.For([0, 24], Plain()).Sheets);
    }

    /// <summary>حذف كل صفحات الملف = مفيش ورق منه.</summary>
    [Fact]
    public void Deleting_Every_Page_Costs_No_Paper()
    {
        var settings = Plain();
        settings.DeletePages = true;
        settings.PagesToDelete = "1-24";

        Assert.True(PaperCount.For([24], settings).IsEmpty);
    }

    // ══════════ السطر اللي المستخدم بيقراه ══════════

    [Fact]
    public void The_Simple_Case_Reads_As_One_Number()
    {
        Assert.Equal("الورق المتوقع: 24 ورقة.", PaperCount.Describe([24], Plain()));
    }

    /// <summary>لما الورق يختلف عن الأوجه، الاتنين بيتقالوا — عشان الفرق يبان.</summary>
    [Fact]
    public void When_Duplex_Saves_Paper_Both_Numbers_Are_Shown()
    {
        var settings = Plain();
        settings.Duplex = true;

        string line = PaperCount.Describe([24], settings);

        Assert.Contains("12 ورقة", line);
        Assert.Contains("24 وجه", line);
        Assert.Contains("وجهين", line);
    }

    /// <summary>
    /// ضرب المكن لازم يتقال بالنص. الرقم لوحده بيبان غلط للي عارف
    /// إنه طالب ١٠ نسخ بس.
    /// </summary>
    [Fact]
    public void Multiplying_By_Machines_Is_Spelled_Out()
    {
        var settings = Plain();
        settings.TotalCopies = 10;
        settings.DistributeCopies = false;

        string line = PaperCount.Describe([24], settings, machines: 4);

        Assert.Contains("960", line);
        Assert.Contains("4 مكن", line);
    }

    /// <summary>
    /// ⚠ النجوم بتظهر للمستخدم زي ما هي — السطر ده نص خام في TextBlock.
    /// وقعنا فيها قبل كده في <c>SkippedProcessing</c> و<c>PageRange</c>.
    /// </summary>
    [Fact]
    public void The_Line_Has_No_Markdown_Stars()
    {
        var settings = Plain();
        settings.Duplex = true;
        settings.BookletMode = true;
        settings.TotalCopies = 5;
        settings.PageFrom = 2;

        string line = PaperCount.Describe([24], settings, machines: 3);

        Assert.DoesNotContain("*", line);
        Assert.DoesNotContain("_", line);
    }
}
