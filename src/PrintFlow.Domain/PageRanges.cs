namespace PrintFlow.Domain;

/// <summary>
/// بيقرا مدى صفحات مكتوب بالإيد زي <c>1,3,5-8</c>.
///
/// اللي بيكتب ده واقف على ماكينة ومستعجل، فالقاعدة: **اقبل أي حاجة معقولة،
/// وتجاهل اللي مش مفهوم، وعمرك ما ترمي استثناء**. لو كتب فاصلة زيادة أو
/// مسافات أو مدى مقلوب، ده مش سبب إن الشغل كله يقف.
///
/// حساب على نصوص وأرقام بس — بيتختبر لوحده من غير أي PDF.
/// </summary>
public static class PageRanges
{
    /// <summary>
    /// بيرجّع أرقام الصفحات المطلوبة، مرتبة ومن غير تكرار.
    /// </summary>
    /// <param name="text">النص اللي المستخدم كتبه.</param>
    /// <param name="pageCount">عدد صفحات المستند — أي رقم بره ده بيتشال.</param>
    public static IReadOnlyList<int> Parse(string? text, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(text) || pageCount <= 0)
        {
            return [];
        }

        var pages = new SortedSet<int>();

        foreach (string part in text.Split([',', '،', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            AddPart(part.Trim(), pageCount, pages);
        }

        return pages.ToList();
    }

    /// <summary>
    /// نفس القراءة بس بترجّع الصفحات اللي **هتفضل** — وده اللي الدمج محتاجه.
    /// </summary>
    public static IReadOnlyList<int> Remaining(string? text, int pageCount)
    {
        var removed = Parse(text, pageCount).ToHashSet();

        var kept = new List<int>(pageCount);

        for (int page = 1; page <= pageCount; page++)
        {
            if (!removed.Contains(page))
            {
                kept.Add(page);
            }
        }

        return kept;
    }

    /// <summary>
    /// وصف بالعربي لللي المستخدم كتبه — عشان يتأكد قبل ما يضغط معالجة.
    /// النص الغلط بيتقال إنه غلط بدل ما يعدي في صمت.
    /// </summary>
    public static string Describe(string? text, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        if (pageCount <= 0)
        {
            return "حمّل ملفات الأول عشان نتأكد من الأرقام.";
        }

        var pages = Parse(text, pageCount);

        if (pages.Count == 0)
        {
            return "مفيش أرقام صفحات مفهومة في النص ده — مش هيتشال حاجة.";
        }

        if (pages.Count >= pageCount)
        {
            return "ده هيشيل كل صفحات الملف! راجع الأرقام.";
        }

        return $"هيتشال {pages.Count} صفحة من كل ملف، ويفضل {pageCount - pages.Count}.";
    }

    private static void AddPart(string part, int pageCount, SortedSet<int> pages)
    {
        if (part.Length == 0)
        {
            return;
        }

        // مدى: 5-8، وكمان 5–8 بالشرطة الطويلة اللي وورد بيحطها
        int dash = part.IndexOfAny(['-', '–', '—']);

        if (dash > 0 && dash < part.Length - 1)
        {
            if (TryReadNumber(part[..dash], out int from) &&
                TryReadNumber(part[(dash + 1)..], out int to))
            {
                // مدى مقلوب (8-5) نعتبره زي 5-8 بدل ما نتجاهله
                if (from > to)
                {
                    (from, to) = (to, from);
                }

                for (int page = Math.Max(1, from); page <= Math.Min(pageCount, to); page++)
                {
                    pages.Add(page);
                }
            }

            return;
        }

        if (TryReadNumber(part, out int single) && single >= 1 && single <= pageCount)
        {
            pages.Add(single);
        }
    }

    /// <summary>بيقرا رقم عربي أو إنجليزي. أي حاجة تانية بتترفض بهدوء.</summary>
    private static bool TryReadNumber(string text, out int value)
    {
        value = 0;
        var digits = new System.Text.StringBuilder();

        foreach (char c in text.Trim())
        {
            if (char.IsDigit(c))
            {
                // الأرقام العربية (٠-٩) بتتحول لنظيرها الإنجليزي
                digits.Append((char)('0' + char.GetNumericValue(c)));
            }
            else if (!char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        // مافيش حد على الطول عن قصد: TryParse بيرجّع false لوحده لو الرقم أكبر
        // من int بدل ما يرمي، فأي حد إضافي هيبقى كود ميّت. (اتأكدت بالتخريب:
        // شيل الحد ومفيش تست بيقع — يعني مكانش بيعمل حاجة.)
        return digits.Length > 0 && int.TryParse(digits.ToString(), out value);
    }
}
