namespace PrintFlow.Domain;

/// <summary>نتيجة العدّ: أوجه الطباعة، الورق، وعدد النسخ اللي اتحسبت.</summary>
public readonly record struct PaperTally(int Sides, int Sheets, int Copies)
{
    public static readonly PaperTally Nothing = new(0, 0, 0);

    public bool IsEmpty => Sheets <= 0;
}

/// <summary>
/// كام ورقة هتخرج من الأوردر ده — قبل ما تضغط طباعة.
///
/// ═══ ليه الرقم ده مهم أكتر من عدد الصفحات ═══
///
/// اللي بيتباع في المطبعة **ورق**، مش صفحات. الفرق مش تفصيلة:
/// أوردر ٢٤٠ صفحة على الوجهين + اتنين في الورقة = ٦٠ ورقة. اللي بيقرا
/// "٢٤٠" وبيحضّر ٢٤٠ ورقة بيحضّر أربع أضعاف اللزوم، واللي بيسعّر على
/// ٢٤٠ بيسعّر غلط.
///
/// والعكس أخطر: كتيّب من غير وجهين بياخد **ضِعف** الورق (شوف
/// <see cref="BookletRules"/>) — والرقم ده بيخلّي الفرق يبان بالأرقام
/// مش بالكلام.
///
/// ═══ الترتيب مقصود، وهو نفس ترتيب البرنامج ═══
///
///   ١) حذف الصفحات  — بيقلّل الصفحات (وقت المعالجة)
///   ٢) كتيّب أو شرائح — بيحوّل الصفحات لأوجه (وقت المعالجة)
///   ٣) مدى الصفحات  — بيتقص على الأوجه (وقت الطباعة، مش المعالجة)
///   ٤) الوجهين      — بيحوّل الأوجه لورق
///   ٥) النسخ والمكن — بيضرب
///
/// ⚠ المدى بعد التجميع مش قبله، لأنه بيتنفّذ وقت الطباعة على الملف
/// اللي طلع من المعالجة. لو حسبناه على الصفحات الأصلية، أوردر شرائح
/// بمدى كان هيطلع برقم غلط تمامًا.
///
/// ═══ الورق بيتحسب لكل مستند لوحده ═══
///
/// مستند ٥ أوجه على الوجهين = ٣ ورقات مش ٢.٥. لو جمعنا الأوجه الأول
/// وقسمنا بعدين، عشر ملفات فردية كانوا هيضيّعوا خمس ورقات من الحساب.
///
/// حساب خالص على أرقام — متختبر من غير طابعة ولا ملف.
/// </summary>
public static class PaperCount
{
    /// <summary>
    /// أوجه الطباعة من مستند واحد **خام** — بعد الحذف والتجميع والمدى.
    /// </summary>
    public static int SidesIn(int pageCount, PrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (pageCount <= 0)
        {
            return 0;
        }

        // ١) الحذف بيشيل صفحات قبل أي حاجة تانية
        int pages = settings.DeletePages
            ? PageRanges.Remaining(settings.PagesToDelete, pageCount).Count
            : pageCount;

        if (pages <= 0)
        {
            return 0;
        }

        // ٢) الكتيّب بيتجاهل عدد الشرائح — ده مكتوب في SlideRequest وهو
        //    المصدر الوحيد للقرار ده، فبنمشي وراه هنا بالحرف.
        int sides;

        if (settings.BookletMode)
        {
            // كل ورقة كتيّب فيها ٤ صفحات على وشين — يعني وشين لكل ورقة.
            sides = BookletImposition.SheetCount(pages) * 2;
        }
        else
        {
            int perSheet = Math.Max(1, settings.SlidesPerSheet);

            // القسمة لفوق: ٥ صفحات على ٢ في الورقة = ٣ أوجه، مش ٢.
            sides = (pages + perSheet - 1) / perSheet;
        }

        // ٣) المدى بيتقص على الأوجه اللي طلعت من المعالجة
        return PageRange.CountIn(settings.PageFrom, settings.PageTo, sides);
    }

    /// <summary>
    /// أوجه مستند **خلص معالجة** — المدى بس هو اللي لسه ماتنفّذش عليه.
    ///
    /// لازم تبقى منفصلة: لو نادينا <see cref="SidesIn"/> على ملف اتعالج
    /// خلاص، الحذف والتجميع هيتحسبوا **مرتين** والرقم هيطلع نُصّه.
    /// </summary>
    public static int SidesInProcessed(int pageCount, PrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return pageCount <= 0
            ? 0
            : PageRange.CountIn(settings.PageFrom, settings.PageTo, pageCount);
    }

    /// <summary>الورق من الأوجه. القسمة لفوق: ٥ أوجه على الوجهين = ٣ ورقات.</summary>
    public static int SheetsFrom(int sides, bool duplex)
    {
        if (sides <= 0)
        {
            return 0;
        }

        return duplex ? (sides + 1) / 2 : sides;
    }

    /// <summary>
    /// العدّ الكامل للأوردر.
    /// </summary>
    /// <param name="pageCounts">صفحات كل مستند. الصفر معناه مقدرناش نعده.</param>
    /// <param name="machines">عدد المكن المؤهلة اللي هتشتغل. أقلها واحدة.</param>
    /// <param name="alreadyProcessed">
    /// الملفات دي خرجت من المعالجة خلاص؟ لو أه، الحذف والتجميع اتنفّذوا
    /// عليها بالفعل ومايتحسبوش تاني.
    /// </param>
    public static PaperTally For(
        IReadOnlyList<int> pageCounts,
        PrintSettings settings,
        int machines = 1,
        bool alreadyProcessed = false)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (pageCounts is null || pageCounts.Count == 0)
        {
            return PaperTally.Nothing;
        }

        int sides = 0;
        int sheets = 0;

        foreach (int pageCount in pageCounts)
        {
            int documentSides = alreadyProcessed
                ? SidesInProcessed(pageCount, settings)
                : SidesIn(pageCount, settings);

            sides += documentSides;

            // ⚠ الورق لكل مستند لوحده — مش على المجموع. الشرح فوق.
            sheets += SheetsFrom(documentSides, settings.Duplex);
        }

        if (sheets <= 0)
        {
            return PaperTally.Nothing;
        }

        // التوزيع بيقسّم النسخ على المكن، فالمجموع بيفضل زي ما هو.
        // من غير توزيع، كل مكنة بتطلّع العدد كامل — يعني الورق بيتضرب.
        int copies = settings.TotalCopies * (settings.DistributeCopies ? 1 : Math.Max(1, machines));

        return new PaperTally(sides * copies, sheets * copies, copies);
    }

    /// <summary>
    /// سطر عربي للواجهة. بيرجّع "" لما مفيش أرقام نقدر نعد بيها.
    /// </summary>
    public static string Describe(
        IReadOnlyList<int> pageCounts,
        PrintSettings settings,
        int machines = 1,
        bool alreadyProcessed = false)
    {
        var tally = For(pageCounts, settings, machines, alreadyProcessed);

        if (tally.IsEmpty)
        {
            return "";
        }

        var parts = new List<string>();

        if (tally.Copies > 1)
        {
            parts.Add(settings.DistributeCopies || machines <= 1
                ? $"{tally.Copies} نسخة"
                : $"{settings.TotalCopies} نسخة على {machines} مكن");
        }

        if (!alreadyProcessed)
        {
            if (settings.BookletMode)
            {
                parts.Add("كتيّب");
            }
            else if (settings.SlidesPerSheet > 1)
            {
                parts.Add($"{settings.SlidesPerSheet} في الورقة");
            }

            if (settings.DeletePages && !string.IsNullOrWhiteSpace(settings.PagesToDelete))
            {
                parts.Add("بعد الحذف");
            }
        }

        if (settings.Duplex)
        {
            parts.Add("وجهين");
        }

        if (PageRange.IsSubset(settings.PageFrom, settings.PageTo))
        {
            parts.Add("بعد المدى");
        }

        // من غير نجوم ماركداون — السطر ده بيتعرض نص خام في الواجهة.
        string head = tally.Sides == tally.Sheets
            ? $"الورق المتوقع: {tally.Sheets} ورقة"
            : $"الورق المتوقع: {tally.Sheets} ورقة — {tally.Sides} وجه";

        return parts.Count == 0
            ? head + "."
            : $"{head} ({string.Join("، ", parts)}).";
    }
}
