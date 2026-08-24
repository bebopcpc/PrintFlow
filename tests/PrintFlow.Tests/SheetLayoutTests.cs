using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// حسابات توزيع الشرائح على الورقة.
///
/// كل ده أرقام مالهاش علاقة بـ PDF — وده مقصود. الهندسة والترتيب هما أصعب
/// حتة في الشرائح، ولو اتشابكوا مع كود الرسم مكانش ينفع نتأكد منهم غير
/// بإننا نطبع ورقة ونبص عليها.
/// </summary>
public class SheetLayoutTests
{
    // A4 بالنقطة
    private const double A4Width = 595;
    private const double A4Height = 842;
    private const double Margin = 15;

    // شريحة بوربوينت 16:9
    private const double SlideWidth = 960;
    private const double SlideHeight = 540;

    // ══════════ اختيار الشبكة ══════════

    /// <summary>
    /// القاعدة اللي المطبعة حسمتها: ٦ شرائح على ورقة طولية = ٣ صفوف × ٢ أعمدة.
    /// ده الشكل المتعارف عليه لمذكرات الطلبة والبوربوينت.
    /// </summary>
    [Fact]
    public void Six_Slides_On_A_Portrait_Sheet_Are_Three_Rows_By_Two_Columns()
    {
        var portraitSource = SheetLayout.ChooseGrid(6, A4Width, A4Height, A4Width, A4Height, Margin);
        var slideSource = SheetLayout.ChooseGrid(6, A4Width, A4Height, SlideWidth, SlideHeight, Margin);

        Assert.Equal(new SlideGrid(3, 2), portraitSource);
        Assert.Equal(new SlideGrid(3, 2), slideSource);
    }

    /// <summary>
    /// الـ handout المعروف: شرايح بوربوينت عرضية على ورقة طولية بتطلع
    /// فوق بعض — صفّين في عمود واحد، مش جنب بعض.
    /// </summary>
    [Fact]
    public void Two_Landscape_Slides_On_A_Portrait_Sheet_Stack_Vertically()
    {
        var grid = SheetLayout.ChooseGrid(2, A4Width, A4Height, SlideWidth, SlideHeight, Margin);

        Assert.Equal(new SlideGrid(2, 1), grid);
    }

    /// <summary>
    /// والعكس: صفحتين طوليتين على ورقة عرضية بتطلع جنب بعض.
    /// نفس القاعدة، نتيجتين مختلفتين — من غير أي حالة خاصة في الكود.
    /// </summary>
    [Fact]
    public void Two_Portrait_Pages_On_A_Landscape_Sheet_Sit_Side_By_Side()
    {
        var grid = SheetLayout.ChooseGrid(2, A4Height, A4Width, A4Width, A4Height, Margin);

        Assert.Equal(new SlideGrid(1, 2), grid);
    }

    [Theory]
    [InlineData(4, 2, 2)]
    [InlineData(9, 3, 3)]
    [InlineData(16, 4, 4)]
    public void Square_Counts_Give_Square_Grids(int slides, int rows, int columns)
    {
        var grid = SheetLayout.ChooseGrid(slides, A4Width, A4Height, A4Width, A4Height, Margin);

        Assert.Equal(new SlideGrid(rows, columns), grid);
    }

    [Fact]
    public void Eight_Slides_On_A_Portrait_Sheet_Are_Four_By_Two()
    {
        var grid = SheetLayout.ChooseGrid(8, A4Width, A4Height, A4Width, A4Height, Margin);

        Assert.Equal(new SlideGrid(4, 2), grid);
    }

    [Fact]
    public void One_Slide_Is_A_Single_Cell()
    {
        Assert.Equal(new SlideGrid(1, 1), SheetLayout.ChooseGrid(1, A4Width, A4Height, A4Width, A4Height, Margin));
        Assert.Equal(new SlideGrid(1, 1), SheetLayout.ChooseGrid(0, A4Width, A4Height, A4Width, A4Height, Margin));
    }

    /// <summary>الشبكة المختارة لازم تسع العدد المطلوب بالظبط، لا أكتر ولا أقل.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    public void The_Grid_Holds_Exactly_What_Was_Asked_For(int slides)
    {
        var grid = SheetLayout.ChooseGrid(slides, A4Width, A4Height, A4Width, A4Height, Margin);

        Assert.Equal(slides, grid.Capacity);
    }

    /// <summary>الشبكة المختارة لازم تبقى فعلاً الأكبر — مش تقريبًا.</summary>
    [Theory]
    [InlineData(6, 595, 842)]
    [InlineData(6, 842, 595)]
    [InlineData(8, 595, 842)]
    [InlineData(16, 842, 595)]
    public void No_Other_Grid_Would_Fit_The_Page_Bigger(int slides, double sheetW, double sheetH)
    {
        var chosen = SheetLayout.ChooseGrid(slides, sheetW, sheetH, A4Width, A4Height, Margin);
        double chosenScale = ScaleOf(chosen, sheetW, sheetH);

        for (int rows = 1; rows <= slides; rows++)
        {
            if (slides % rows != 0) continue;

            double other = ScaleOf(new SlideGrid(rows, slides / rows), sheetW, sheetH);
            Assert.True(chosenScale >= other - 1e-9,
                $"{rows}×{slides / rows} بتدي {other:0.0000} والمختارة {chosenScale:0.0000}");
        }

        static double ScaleOf(SlideGrid g, double w, double h)
        {
            var cell = SheetLayout.CellSize(g, w, h, Margin);
            return Math.Min(cell.Width / A4Width, cell.Height / A4Height);
        }
    }

    // ══════════ ترتيب التعبئة ══════════

    /// <summary>
    /// الافتراضي للعربي: الشريحة الأولى فوق على **اليمين**.
    /// ده الفرق اللي بيخلي المستند يتقرا صح.
    /// </summary>
    [Fact]
    public void The_First_Slide_Starts_Top_Right_For_Arabic()
    {
        var cell = SheetLayout.CellFor(0, new SlideGrid(2, 2), SlideOrder.Horizontal, SlideStart.Right);

        Assert.Equal(new SlideCell(0, 1), cell);
    }

    [Fact]
    public void Starting_From_The_Left_Puts_The_First_Slide_Top_Left()
    {
        var cell = SheetLayout.CellFor(0, new SlideGrid(2, 2), SlideOrder.Horizontal, SlideStart.Left);

        Assert.Equal(new SlideCell(0, 0), cell);
    }

    /// <summary>
    /// الأربع توليفات على شبكة ٢×٢، الرقم في كل خانة.
    /// دي نفس الرسومات اللي في التصور بالظبط.
    /// </summary>
    [Theory]
    // أفقي + يمين: 2 1 / 4 3
    [InlineData(SlideOrder.Horizontal, SlideStart.Right, 0, 0, 1)]
    [InlineData(SlideOrder.Horizontal, SlideStart.Right, 1, 0, 0)]
    [InlineData(SlideOrder.Horizontal, SlideStart.Right, 2, 1, 1)]
    [InlineData(SlideOrder.Horizontal, SlideStart.Right, 3, 1, 0)]
    // أفقي + شمال: 1 2 / 3 4
    [InlineData(SlideOrder.Horizontal, SlideStart.Left, 0, 0, 0)]
    [InlineData(SlideOrder.Horizontal, SlideStart.Left, 1, 0, 1)]
    [InlineData(SlideOrder.Horizontal, SlideStart.Left, 2, 1, 0)]
    [InlineData(SlideOrder.Horizontal, SlideStart.Left, 3, 1, 1)]
    // رأسي + يمين: العمود اليمين بينزل الأول
    [InlineData(SlideOrder.Vertical, SlideStart.Right, 0, 0, 1)]
    [InlineData(SlideOrder.Vertical, SlideStart.Right, 1, 1, 1)]
    [InlineData(SlideOrder.Vertical, SlideStart.Right, 2, 0, 0)]
    [InlineData(SlideOrder.Vertical, SlideStart.Right, 3, 1, 0)]
    // رأسي + شمال
    [InlineData(SlideOrder.Vertical, SlideStart.Left, 0, 0, 0)]
    [InlineData(SlideOrder.Vertical, SlideStart.Left, 1, 1, 0)]
    [InlineData(SlideOrder.Vertical, SlideStart.Left, 2, 0, 1)]
    [InlineData(SlideOrder.Vertical, SlideStart.Left, 3, 1, 1)]
    public void Fill_Order_Matches_The_Design(
        SlideOrder order, SlideStart start, int slideIndex, int expectedRow, int expectedColumn)
    {
        var cell = SheetLayout.CellFor(slideIndex, new SlideGrid(2, 2), order, start);

        Assert.Equal(new SlideCell(expectedRow, expectedColumn), cell);
    }

    /// <summary>مفيش شريحتين تقعوا في نفس الخانة، ومفيش خانة تفضل فاضية.</summary>
    [Theory]
    [InlineData(SlideOrder.Horizontal, SlideStart.Right)]
    [InlineData(SlideOrder.Horizontal, SlideStart.Left)]
    [InlineData(SlideOrder.Vertical, SlideStart.Right)]
    [InlineData(SlideOrder.Vertical, SlideStart.Left)]
    public void Every_Cell_Is_Used_Exactly_Once(SlideOrder order, SlideStart start)
    {
        foreach (var grid in new[] { new SlideGrid(2, 2), new SlideGrid(3, 2), new SlideGrid(2, 3), new SlideGrid(4, 4) })
        {
            var seen = new HashSet<SlideCell>();

            for (int i = 0; i < grid.Capacity; i++)
            {
                var cell = SheetLayout.CellFor(i, grid, order, start);

                Assert.InRange(cell.Row, 0, grid.Rows - 1);
                Assert.InRange(cell.Column, 0, grid.Columns - 1);
                Assert.True(seen.Add(cell), $"الخانة {cell} اتكررت في {grid}");
            }

            Assert.Equal(grid.Capacity, seen.Count);
        }
    }

    /// <summary>
    /// البداية من اليمين هي مرايا البداية من الشمال بالظبط — لا أكتر ولا أقل.
    /// </summary>
    [Theory]
    [InlineData(SlideOrder.Horizontal)]
    [InlineData(SlideOrder.Vertical)]
    public void Right_Start_Is_A_Mirror_Of_Left_Start(SlideOrder order)
    {
        var grid = new SlideGrid(3, 2);

        for (int i = 0; i < grid.Capacity; i++)
        {
            var left = SheetLayout.CellFor(i, grid, order, SlideStart.Left);
            var right = SheetLayout.CellFor(i, grid, order, SlideStart.Right);

            Assert.Equal(left.Row, right.Row);
            Assert.Equal(grid.Columns - 1 - left.Column, right.Column);
        }
    }

    // ══════════ الهندسة ══════════

    [Fact]
    public void Slides_Never_Overlap_And_Stay_On_The_Sheet()
    {
        var grid = SheetLayout.ChooseGrid(6, A4Width, A4Height, A4Width, A4Height, Margin);
        var slots = SheetLayout.SlotsFor(grid, A4Width, A4Height, A4Width, A4Height,
            Margin, SlideOrder.Horizontal, SlideStart.Right);

        Assert.Equal(6, slots.Count);

        foreach (var slot in slots)
        {
            Assert.True(slot.X >= 0 && slot.Y >= 0, $"طالع بره: {slot}");
            Assert.True(slot.X + slot.Width <= A4Width + 0.01, $"طالع من العرض: {slot}");
            Assert.True(slot.Y + slot.Height <= A4Height + 0.01, $"طالع من الطول: {slot}");
        }

        for (int a = 0; a < slots.Count; a++)
        {
            for (int b = a + 1; b < slots.Count; b++)
            {
                Assert.False(Overlaps(slots[a], slots[b]), $"تداخل بين {slots[a]} و {slots[b]}");
            }
        }

        static bool Overlaps(SlideRect a, SlideRect b) =>
            a.X < b.X + b.Width - 0.01 && b.X < a.X + a.Width - 0.01 &&
            a.Y < b.Y + b.Height - 0.01 && b.Y < a.Y + a.Height - 0.01;
    }

    /// <summary>الصفحة بتتصغّر، مابتتمطش — النسبة زي ما هي.</summary>
    [Theory]
    [InlineData(960, 540)]
    [InlineData(595, 842)]
    [InlineData(1000, 100)]
    public void The_Aspect_Ratio_Of_The_Page_Is_Never_Distorted(double sourceW, double sourceH)
    {
        var grid = SheetLayout.ChooseGrid(4, A4Width, A4Height, sourceW, sourceH, Margin);
        var slot = SheetLayout.SlotFor(0, grid, A4Width, A4Height, sourceW, sourceH,
            Margin, SlideOrder.Horizontal, SlideStart.Right);

        Assert.Equal(sourceW / sourceH, slot.Width / slot.Height, 6);
    }

    /// <summary>الصفحة بتتوسّط في خليتها — الفراغ بيتوزع بالتساوي.</summary>
    [Fact]
    public void The_Page_Is_Centred_Inside_Its_Cell()
    {
        var grid = new SlideGrid(2, 2);
        var cell = SheetLayout.CellSize(grid, A4Width, A4Height, Margin);
        double effective = SheetLayout.EffectiveMargin(grid, A4Width, A4Height, Margin);

        // شريحة عرضية في خلية طولية: فراغ فوق وتحت بالتساوي
        var slot = SheetLayout.SlotFor(0, grid, A4Width, A4Height, SlideWidth, SlideHeight,
            Margin, SlideOrder.Horizontal, SlideStart.Left);

        double above = slot.Y - effective;
        double below = (effective + cell.Height) - (slot.Y + slot.Height);

        Assert.Equal(above, below, 6);
    }

    /// <summary>
    /// هامش أكبر من الورقة كان ممكن يطلّع خلايا بعرض سالب والرسم يروح
    /// مكان مجهول. بنصغّر الهامش بدل ما نكسر.
    /// </summary>
    [Theory]
    [InlineData(500)]
    [InlineData(5000)]
    public void An_Absurd_Margin_Is_Reined_In_Instead_Of_Breaking(double margin)
    {
        var grid = SheetLayout.ChooseGrid(4, A4Width, A4Height, A4Width, A4Height, margin);
        var cell = SheetLayout.CellSize(grid, A4Width, A4Height, margin);

        Assert.True(cell.Width > 0, $"عرض الخلية {cell.Width}");
        Assert.True(cell.Height > 0, $"طول الخلية {cell.Height}");

        var slot = SheetLayout.SlotFor(0, grid, A4Width, A4Height, A4Width, A4Height,
            margin, SlideOrder.Horizontal, SlideStart.Right);

        Assert.True(slot.Width > 0 && slot.Height > 0);
        Assert.True(slot.X >= 0 && slot.Y >= 0);
    }

    [Fact]
    public void Zero_Margin_Fills_The_Sheet_Completely()
    {
        var grid = new SlideGrid(2, 2);
        var cell = SheetLayout.CellSize(grid, A4Width, A4Height, 0);

        Assert.Equal(A4Width / 2, cell.Width, 6);
        Assert.Equal(A4Height / 2, cell.Height, 6);
    }

    /// <summary>
    /// أول شريحة في وضع "يمين" لازم تبقى في النص الأيمن من الورقة فعليًا —
    /// مش بس رقم عمود صح في جدول.
    /// </summary>
    [Fact]
    public void The_First_Slide_Physically_Sits_On_The_Right_Half()
    {
        var grid = new SlideGrid(2, 2);
        var slot = SheetLayout.SlotFor(0, grid, A4Width, A4Height, A4Width, A4Height,
            Margin, SlideOrder.Horizontal, SlideStart.Right);

        Assert.True(slot.X > A4Width / 2, $"أول شريحة عند X={slot.X:0.0} والنص عند {A4Width / 2}");
    }
}
