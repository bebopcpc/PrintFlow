namespace PrintFlow.Domain;

/// <summary>تقسيم الورقة: كام صف وكام عمود.</summary>
public readonly record struct SlideGrid(int Rows, int Columns)
{
    public int Capacity => Rows * Columns;
}

/// <summary>مكان شريحة في الشبكة. الصفوف بتنزل من فوق لتحت دايمًا.</summary>
public readonly record struct SlideCell(int Row, int Column);

/// <summary>مستطيل بإحداثيات الرسم (الأصل فوق-شمال، Y نازلة) بالنقطة.</summary>
public readonly record struct SlideRect(double X, double Y, double Width, double Height);

/// <summary>
/// حسابات توزيع الشرائح على الورقة.
///
/// كل اللي هنا **حساب على أرقام** — مفيش PDF ولا رسم ولا ملفات. ده مقصود:
/// أصعب حتة في الشرائح هي الهندسة والترتيب، ولو كانت متشابكة مع كود الرسم
/// مكانش ينفع نختبرها غير بإننا نطبع ونبص. كده بنختبرها كلها بأرقام.
/// </summary>
public static class SheetLayout
{
    /// <summary>
    /// بيختار تقسيم الورقة اللي بيدّي **أكبر حجم للصفحة الأصلية**.
    ///
    /// ليه مش جدول ثابت: الشكل الصح بيعتمد على شكل الصفحة الأصلية مش الورقة
    /// بس. شرايح بوربوينت (عرضية) على ورقة طولية بتطلع فوق بعض، وصفحات
    /// مذكرة (طولية) على نفس الورقة بتطلع جنب بعض. القاعدة الواحدة دي
    /// بتطلّع الشكلين صح لوحدها من غير حالات خاصة مكتوبة بالإيد.
    ///
    /// عند التعادل بنمشي مع شكل الورقة: الطولية تاخد صفوف أكتر.
    /// </summary>
    public static SlideGrid ChooseGrid(
        int slidesPerSheet,
        double sheetWidth,
        double sheetHeight,
        double sourceWidth,
        double sourceHeight,
        double margin)
    {
        if (slidesPerSheet <= 1)
        {
            return new SlideGrid(1, 1);
        }

        bool sheetIsPortrait = sheetHeight >= sheetWidth;

        SlideGrid best = default;
        double bestScale = -1;

        for (int rows = 1; rows <= slidesPerSheet; rows++)
        {
            if (slidesPerSheet % rows != 0)
            {
                continue;
            }

            int columns = slidesPerSheet / rows;
            var cell = CellSize(new SlideGrid(rows, columns), sheetWidth, sheetHeight, margin);

            if (cell.Width <= 0 || cell.Height <= 0)
            {
                continue;
            }

            double scale = FitScale(cell.Width, cell.Height, sourceWidth, sourceHeight);

            if (scale > bestScale + Tolerance)
            {
                best = new SlideGrid(rows, columns);
                bestScale = scale;
                continue;
            }

            // تعادل: نمشي مع شكل الورقة
            if (Math.Abs(scale - bestScale) <= Tolerance)
            {
                bool candidateFitsSheet = sheetIsPortrait ? rows >= columns : columns >= rows;
                bool currentFitsSheet = sheetIsPortrait
                    ? best.Rows >= best.Columns
                    : best.Columns >= best.Rows;

                if (candidateFitsSheet && !currentFitsSheet)
                {
                    best = new SlideGrid(rows, columns);
                    bestScale = scale;
                }
            }
        }

        // مفيش تقسيم صالح (هامش أكبر من الورقة مثلًا) — صف واحد وخلاص
        return best.Capacity > 0 ? best : new SlideGrid(1, slidesPerSheet);
    }

    /// <summary>
    /// مكان الشريحة رقم <paramref name="slideIndex"/> (بيبدأ من صفر) في الشبكة.
    ///
    /// الصفوف بتنزل من فوق لتحت في كل الحالات — ده مش إعداد لأن العربي
    /// بيقرا من فوق لتحت زي الإنجليزي. اللي بيتقلب هو اتجاه الأعمدة بس.
    /// </summary>
    public static SlideCell CellFor(int slideIndex, SlideGrid grid, SlideOrder order, SlideStart start)
    {
        int row;
        int column;

        if (order == SlideOrder.Horizontal)
        {
            row = slideIndex / grid.Columns;
            column = slideIndex % grid.Columns;
        }
        else
        {
            column = slideIndex / grid.Rows;
            row = slideIndex % grid.Rows;
        }

        if (start == SlideStart.Right)
        {
            column = grid.Columns - 1 - column;
        }

        return new SlideCell(row, column);
    }

    /// <summary>
    /// المكان النهائي اللي الصفحة الأصلية هترسم فيه — **بعد** الحفاظ على
    /// نسبتها وتوسيطها في خليتها.
    ///
    /// ده كمان هو المستطيل اللي الإطار بيترسم عليه: المطبعة اختارت إن
    /// الإطار يلزق على الصفحة نفسها مش على حدود الخلية، عشان يحدد
    /// المحتوى مش الفراغ.
    /// </summary>
    public static SlideRect SlotFor(
        int slideIndex,
        SlideGrid grid,
        double sheetWidth,
        double sheetHeight,
        double sourceWidth,
        double sourceHeight,
        double margin,
        SlideOrder order,
        SlideStart start)
    {
        var cell = CellBoundsFor(slideIndex, grid, sheetWidth, sheetHeight, margin, order, start);

        return FitInto(cell, sourceWidth, sourceHeight);
    }

    /// <summary>
    /// حدود الخلية نفسها على الورقة — من غير أي علاقة بالصفحة اللي هتتحط فيها.
    ///
    /// مفصولة عن <see cref="FitInto"/> عن قصد: المستندات المدموجة في المطابع
    /// بيبقى فيها مقاسات مختلفة (A4 مع A3 مسكانّر). الشبكة والخلايا لازم
    /// تفضل ثابتة على الورقة كلها عشان الشكل يبقى منتظم، وكل صفحة تتظبط
    /// جوه خليتها بمقاسها هي.
    /// </summary>
    public static SlideRect CellBoundsFor(
        int slideIndex,
        SlideGrid grid,
        double sheetWidth,
        double sheetHeight,
        double margin,
        SlideOrder order,
        SlideStart start)
    {
        var size = CellSize(grid, sheetWidth, sheetHeight, margin);
        var cell = CellFor(slideIndex, grid, order, start);
        double effective = EffectiveMargin(grid, sheetWidth, sheetHeight, margin);

        return new SlideRect(
            effective + cell.Column * (size.Width + effective),
            effective + cell.Row * (size.Height + effective),
            size.Width,
            size.Height);
    }

    /// <summary>
    /// بيحط صفحة بمقاس معيّن جوه خلية: محافظ على النسبة، متوسّط، ومش بيكبّرها
    /// أكبر من الخلية.
    /// </summary>
    public static SlideRect FitInto(SlideRect cell, double sourceWidth, double sourceHeight)
    {
        double scale = FitScale(cell.Width, cell.Height, sourceWidth, sourceHeight);
        double width = sourceWidth * scale;
        double height = sourceHeight * scale;

        return new SlideRect(
            cell.X + (cell.Width - width) / 2,
            cell.Y + (cell.Height - height) / 2,
            width,
            height);
    }

    /// <summary>
    /// كل أماكن الشرائح على ورقة واحدة **بترتيب التعبئة** — العنصر رقم صفر
    /// هو مكان الشريحة الأولى. اللي بيرسم مايحتاجش يعرف أي حاجة عن الترتيب.
    /// </summary>
    public static IReadOnlyList<SlideRect> SlotsFor(
        SlideGrid grid,
        double sheetWidth,
        double sheetHeight,
        double sourceWidth,
        double sourceHeight,
        double margin,
        SlideOrder order,
        SlideStart start)
    {
        var slots = new List<SlideRect>(grid.Capacity);

        for (int i = 0; i < grid.Capacity; i++)
        {
            slots.Add(SlotFor(i, grid, sheetWidth, sheetHeight,
                sourceWidth, sourceHeight, margin, order, start));
        }

        return slots;
    }

    // ══════════ مساعدات ══════════

    /// <summary>فرق أقل من ده بين نسبتين بنعتبره تعادل.</summary>
    private const double Tolerance = 1e-9;

    /// <summary>
    /// الهامش المستخدم فعلًا. لو المستخدم طلب هامش أكبر من الورقة، بنصغّره
    /// بدل ما نطلّع خلايا بعرض سالب ونرسم في مكان مجهول.
    /// </summary>
    public static double EffectiveMargin(SlideGrid grid, double sheetWidth, double sheetHeight, double margin)
    {
        if (margin <= 0)
        {
            return 0;
        }

        // بنسيب على الأقل نص الورقة للمحتوى
        double maxHorizontal = sheetWidth / 2 / (grid.Columns + 1);
        double maxVertical = sheetHeight / 2 / (grid.Rows + 1);

        return Math.Min(margin, Math.Min(maxHorizontal, maxVertical));
    }

    /// <summary>مقاس الخلية الواحدة بعد خصم الهوامش.</summary>
    public static SlideRect CellSize(SlideGrid grid, double sheetWidth, double sheetHeight, double margin)
    {
        double effective = EffectiveMargin(grid, sheetWidth, sheetHeight, margin);

        return new SlideRect(
            0, 0,
            (sheetWidth - effective * (grid.Columns + 1)) / grid.Columns,
            (sheetHeight - effective * (grid.Rows + 1)) / grid.Rows);
    }

    /// <summary>نسبة التصغير اللي بتخلي الصفحة تدخل الخلية من غير ما تتمط.</summary>
    private static double FitScale(double cellWidth, double cellHeight, double sourceWidth, double sourceHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return 0;
        }

        return Math.Min(cellWidth / sourceWidth, cellHeight / sourceHeight);
    }
}
