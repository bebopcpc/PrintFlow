using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// ترتيب صفحات الكتيّب.
///
/// أهم تست هنا هو <see cref="Folding_The_Sheets_Gives_Back_One_Two_Three"/> —
/// بيحاكي الطي بالظبط ويتأكد إن الكتيّب بيتقرا بالترتيب. الباقي حالات حدّية.
/// </summary>
public class BookletImpositionTests
{
    // ══════════ عدد الورق ══════════

    [Theory]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(1, 4)]
    [InlineData(5, 8)]
    [InlineData(6, 8)]
    [InlineData(7, 8)]
    [InlineData(9, 12)]
    public void Pages_Are_Rounded_Up_To_A_Multiple_Of_Four(int pages, int expected)
    {
        Assert.Equal(expected, BookletImposition.PaddedPageCount(pages));
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(8, 2)]
    [InlineData(5, 2)]
    [InlineData(20, 5)]
    [InlineData(210, 53)]
    public void Each_Sheet_Carries_Four_Pages(int pages, int sheets)
    {
        Assert.Equal(sheets, BookletImposition.SheetCount(pages));
    }

    [Fact]
    public void An_Empty_Document_Needs_No_Paper()
    {
        Assert.Equal(0, BookletImposition.SheetCount(0));
        Assert.Empty(BookletImposition.Order(0, BookletStart.Right));
    }

    // ══════════ الترتيب ══════════

    /// <summary>
    /// الحالة المرجعية: ٨ صفحات على ورقتين.
    /// كل عنصرين = وجه واحد، بترتيب: وش١، ضهر١، وش٢، ضهر٢.
    /// </summary>
    [Fact]
    public void Eight_Pages_Land_In_The_Classic_Booklet_Order()
    {
        var order = BookletImposition.Order(8, BookletStart.Right);

        Assert.Equal([8, 1, 2, 7, 6, 3, 4, 5], order);
    }

    [Fact]
    public void Four_Pages_Fit_On_One_Sheet()
    {
        var order = BookletImposition.Order(4, BookletStart.Right);

        Assert.Equal([4, 1, 2, 3], order);
    }

    /// <summary>البدء من الشمال هو نفس الترتيب بس كل وجه مقلوب.</summary>
    [Fact]
    public void Starting_From_The_Left_Mirrors_Each_Side()
    {
        var right = BookletImposition.Order(8, BookletStart.Right);
        var left = BookletImposition.Order(8, BookletStart.Left);

        Assert.Equal(right.Count, left.Count);

        for (int i = 0; i < right.Count; i += 2)
        {
            Assert.Equal(right[i], left[i + 1]);
            Assert.Equal(right[i + 1], left[i]);
        }
    }

    // ══════════ محاكاة الطي ══════════

    /// <summary>
    /// ده التست اللي بيثبت إن الحساب صح فعلًا: بنحاكي طي الورق ودبّسه،
    /// وبنقرا الكتيّب صفحة صفحة. لازم يطلع ١، ٢، ٣… بالترتيب.
    ///
    /// من غير التست ده، أي غلطة في الترتيب مكانتش هتبان غير لما حد
    /// يطبع ٥٠ ورقة ويطويهم ويلاقي الصفحات مبعثرة.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(40)]
    public void Folding_The_Sheets_Gives_Back_One_Two_Three(int pageCount)
    {
        var order = BookletImposition.Order(pageCount, BookletStart.Right);
        int sheets = BookletImposition.SheetCount(pageCount);

        // بنطوي الورق جوه بعض: الورقة الأولى بره والأخيرة في النص.
        // القراية بتمشي: غلاف، وبعدين كل فرشة من بره لجوه، وبعدين رجوع.
        var read = new List<int>();

        // الوش الأيمن للورقة الأولى = الغلاف
        read.Add(order[1]);

        // الفرشات: ضهر الورقة i (اليمين) + وش الورقة i+1 (الشمال)
        for (int sheet = 0; sheet < sheets; sheet++)
        {
            int back = sheet * 4 + 2;      // بداية وجه الضهر
            read.Add(order[back]);          // يمين الضهر

            if (sheet + 1 < sheets)
            {
                read.Add(order[(sheet + 1) * 4 + 1]);   // يمين وش الورقة اللي بعدها
            }
            else
            {
                read.Add(order[back + 1]);  // أعمق فرشة: شمال نفس الضهر
            }
        }

        // الرجوع من جوه لبره: شمال الأوجه
        for (int sheet = sheets - 1; sheet >= 0; sheet--)
        {
            if (sheet < sheets - 1)
            {
                read.Add(order[sheet * 4 + 3]);   // شمال الضهر
            }

            read.Add(order[sheet * 4]);           // شمال الوش
        }

        var expected = Enumerable.Range(1, pageCount).ToList();
        var actual = read.Where(p => p != BookletImposition.Blank).ToList();

        Assert.Equal(expected, actual);
    }

    // ══════════ الصفحات الفاضية ══════════

    /// <summary>
    /// ٦ صفحات على ورقتين معناها خانتين فاضيتين. لازم يبقوا في الآخر
    /// (ورا آخر صفحة حقيقية) مش في النص.
    /// </summary>
    [Fact]
    public void Padding_Blanks_Never_Sit_In_The_Middle_Of_The_Reading()
    {
        var order = BookletImposition.Order(6, BookletStart.Right);

        Assert.Equal(8, order.Count);
        Assert.Equal(2, order.Count(p => p == BookletImposition.Blank));

        // كل الصفحات الحقيقية موجودة مرة واحدة بالظبط
        var real = order.Where(p => p != BookletImposition.Blank).OrderBy(p => p);
        Assert.Equal(Enumerable.Range(1, 6), real);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(13)]
    public void Every_Real_Page_Appears_Exactly_Once(int pageCount)
    {
        var order = BookletImposition.Order(pageCount, BookletStart.Right);

        var real = order.Where(p => p != BookletImposition.Blank).OrderBy(p => p).ToList();

        Assert.Equal(Enumerable.Range(1, pageCount), real);
    }

    /// <summary>مفيش رقم صفحة أكبر من عدد الصفحات الحقيقي بيتسرّب للورق.</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(22)]
    public void No_Page_Beyond_The_Document_Is_Ever_Requested(int pageCount)
    {
        var order = BookletImposition.Order(pageCount, BookletStart.Right);

        Assert.All(order, p => Assert.True(p <= pageCount, $"طلب صفحة {p} والمستند {pageCount}"));
        Assert.All(order, p => Assert.True(p >= 0));
    }

    [Fact]
    public void The_Order_Always_Has_Two_Slots_Per_Side()
    {
        foreach (int pages in new[] { 1, 4, 6, 8, 15, 40 })
        {
            var order = BookletImposition.Order(pages, BookletStart.Right);

            Assert.Equal(BookletImposition.SheetCount(pages) * 4, order.Count);
            Assert.Equal(0, order.Count % 2);
        }
    }

    /// <summary>
    /// الغلاف الأول لازم يبقى صفحة ١، والغلاف الأخير آخر صفحة —
    /// دي أول حاجة أي حد هيشوفها لو الترتيب غلط.
    /// </summary>
    [Fact]
    public void The_Cover_Is_Page_One_And_The_Back_Is_The_Last_Page()
    {
        var order = BookletImposition.Order(12, BookletStart.Right);

        Assert.Equal(1, order[1]);    // يمين وش الورقة الأولى
        Assert.Equal(12, order[0]);   // شمالها = الغلاف الخلفي
    }
}
