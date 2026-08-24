using System.Text;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيحوّل نص عربي منطقي (زي ما اتكتب) لنص "بصري" جاهز للرسم بمحرك بيرسم من الشمال
/// لليمين وبس — زي PDFsharp، اللي مش بيدعم RTL ولا تشبيك الحروف تلقائيًا.
///
/// بيشتغل على مرحلتين:
///   1) التشبيك (Join): كل حرف بياخد شكله الصح (منفصل/بداية/وسط/نهاية) + ligature لام-ألف.
///   2) الترتيب البصري (ToVisualOrder): بنقسم النص لمقاطع (runs) عربي / لاتيني-أرقام / محايد،
///      بنعكس **ترتيب المقاطع**، وبنعكس **حروف المقاطع العربية بس**.
///
/// المرحلة التانية دي هي اللي اتصلحت. النسخة القديمة كانت بتعمل Array.Reverse على
/// النص كله، فالأرقام والحروف اللاتيني كانوا بيتقلبوا معاه:
///     "صفحة 3 من 12"    كانت بتطلع الصفحة رقم 21
///     "نسخة PrintFlow"  كانت بتطلع wolFtnirP
///
/// حدود معروفة (مقبولة للاستخدام ده — علامة مائية وترقيم ونص قصير):
///   • مش تطبيق كامل لخوارزمية BiDi (UAX #9)؛ ده mini-bidi باتجاه أساسي RTL.
///   • مفيش ligatures اختيارية غير لام-ألف (اللي هي إجبارية في العربي).
///   • لو احتجنا يومًا نص عربي طويل ومعقّد، البديل هو محرك shaping حقيقي زي HarfBuzzSharp.
/// </summary>
public static class ArabicTextShaper
{
    // كل حرف: [منفصل, بداية كلمة, وسط كلمة, نهاية كلمة]
    private static readonly Dictionary<char, char[]> Forms = new()
    {
        ['ا'] = ['ا', 'ا', 'ﺎ', 'ﺎ'],
        ['أ'] = ['أ', 'أ', 'ﺄ', 'ﺄ'],
        ['إ'] = ['إ', 'إ', 'ﺈ', 'ﺈ'],
        ['آ'] = ['آ', 'آ', 'ﺂ', 'ﺂ'],
        ['ب'] = ['ب', 'ﺑ', 'ﺒ', 'ﺐ'],
        ['ت'] = ['ت', 'ﺗ', 'ﺘ', 'ﺖ'],
        ['ث'] = ['ث', 'ﺛ', 'ﺜ', 'ﺚ'],
        ['ج'] = ['ج', 'ﺟ', 'ﺠ', 'ﺞ'],
        ['ح'] = ['ح', 'ﺣ', 'ﺤ', 'ﺢ'],
        ['خ'] = ['خ', 'ﺧ', 'ﺨ', 'ﺦ'],
        ['د'] = ['د', 'د', 'ﺪ', 'ﺪ'],
        ['ذ'] = ['ذ', 'ذ', 'ﺬ', 'ﺬ'],
        ['ر'] = ['ر', 'ر', 'ﺮ', 'ﺮ'],
        ['ز'] = ['ز', 'ز', 'ﺰ', 'ﺰ'],
        ['س'] = ['س', 'ﺳ', 'ﺴ', 'ﺲ'],
        ['ش'] = ['ش', 'ﺷ', 'ﺸ', 'ﺶ'],
        ['ص'] = ['ص', 'ﺻ', 'ﺼ', 'ﺺ'],
        ['ض'] = ['ض', 'ﺿ', 'ﻀ', 'ﺾ'],
        ['ط'] = ['ط', 'ﻃ', 'ﻄ', 'ﻂ'],
        ['ظ'] = ['ظ', 'ﻇ', 'ﻈ', 'ﻆ'],
        ['ع'] = ['ع', 'ﻋ', 'ﻌ', 'ﻊ'],
        ['غ'] = ['غ', 'ﻏ', 'ﻐ', 'ﻎ'],
        ['ف'] = ['ف', 'ﻓ', 'ﻔ', 'ﻒ'],
        ['ق'] = ['ق', 'ﻗ', 'ﻘ', 'ﻖ'],
        ['ك'] = ['ك', 'ﻛ', 'ﻜ', 'ﻚ'],
        ['ل'] = ['ل', 'ﻟ', 'ﻠ', 'ﻞ'],
        ['م'] = ['م', 'ﻣ', 'ﻤ', 'ﻢ'],
        ['ن'] = ['ن', 'ﻧ', 'ﻨ', 'ﻦ'],
        ['ه'] = ['ه', 'ﻫ', 'ﻬ', 'ﻪ'],
        ['و'] = ['و', 'و', 'ﻮ', 'ﻮ'],
        ['ي'] = ['ي', 'ﻳ', 'ﻴ', 'ﻲ'],
        ['ى'] = ['ى', 'ى', 'ﯽ', 'ﯽ'],
        ['ة'] = ['ة', 'ة', 'ﺔ', 'ﺔ'],
        ['ئ'] = ['ئ', 'ﺋ', 'ﺌ', 'ﺊ'],
        ['ؤ'] = ['ؤ', 'ؤ', 'ﺆ', 'ﺆ'],
        ['ء'] = ['ء', 'ء', 'ء', 'ء'],
    };

    /// <summary>حروف بتوصل باللي قبلها بس، ومبتوصلش باللي بعدها.</summary>
    private static readonly HashSet<char> NoForwardJoin =
        ['ا', 'أ', 'إ', 'آ', 'د', 'ذ', 'ر', 'ز', 'و', 'ى', 'ة', 'ؤ', 'ء'];

    /// <summary>ligature لام-ألف: [منفصل, نهاية]. الألف عمرها ما بتوصل لقدام، فمفيش شكل بداية أو وسط.</summary>
    private static readonly Dictionary<char, char[]> LamAlef = new()
    {
        ['ا'] = ['ﻻ', 'ﻼ'],
        ['أ'] = ['ﻷ', 'ﻸ'],
        ['إ'] = ['ﻹ', 'ﻺ'],
        ['آ'] = ['ﻵ', 'ﻶ'],
    };

    /// <summary>الأقواس بيتعكس شكلها في السياق العربي، عشان "(ملاحظة)" تفضل بنفس الشكل.</summary>
    private static readonly Dictionary<char, char> Mirrored = new()
    {
        ['('] = ')', [')'] = '(',
        ['['] = ']', [']'] = '[',
        ['{'] = '}', ['}'] = '{',
        ['<'] = '>', ['>'] = '<',
    };

    private const char Tatweel = 'ـ';

    public static string Reshape(string input)
    {
        if (string.IsNullOrEmpty(input) || !ContainsArabic(input))
        {
            return input; // نص مش عربي — نسيبه زي ما هو
        }

        return ToVisualOrder(Join(input));
    }

    // ══════════ المرحلة 1: التشبيك ══════════

    private static string Join(string input)
    {
        var output = new StringBuilder(input.Length);

        for (int i = 0; i < input.Length; i++)
        {
            char current = input[i];

            if (IsMark(current) || current == Tatweel)
            {
                output.Append(current); // التشكيل والتطويل بيعدّوا زي ما هما
                continue;
            }

            if (!Forms.ContainsKey(current))
            {
                output.Append(current); // مسافة، رقم، حرف أجنبي...
                continue;
            }

            bool joinsBackward = JoinsBackward(input, i);

            // لام + ألف = حرف واحد مدموج. ده إجباري في العربي مش اختياري،
            // ومن غيره "لا" بتترسم حرفين منفصلين وشكلها غلط.
            if (current == 'ل')
            {
                int next = NextLetter(input, i);
                if (next >= 0 && LamAlef.TryGetValue(input[next], out var ligature))
                {
                    output.Append(ligature[joinsBackward ? 1 : 0]);
                    i = next; // بنتخطى الألف لأنها اتدمجت
                    continue;
                }
            }

            bool joinsForward = JoinsForward(input, i);
            char[] forms = Forms[current];

            output.Append((joinsBackward, joinsForward) switch
            {
                (false, false) => forms[0], // منفصل
                (false, true) => forms[1],  // بداية كلمة
                (true, true) => forms[2],   // وسط كلمة
                (true, false) => forms[3],  // نهاية كلمة
            });
        }

        return output.ToString();
    }

    /// <summary>هل الحرف ده بيتصل باللي قبله؟ (بنتخطى التشكيل — التشكيل شفاف للتشبيك)</summary>
    private static bool JoinsBackward(string text, int index)
    {
        int previous = PreviousLetter(text, index);
        if (previous < 0)
        {
            return false;
        }

        char letter = text[previous];
        return letter == Tatweel || (Forms.ContainsKey(letter) && !NoForwardJoin.Contains(letter));
    }

    private static bool JoinsForward(string text, int index)
    {
        if (NoForwardJoin.Contains(text[index]))
        {
            return false;
        }

        int next = NextLetter(text, index);
        return next >= 0 && (text[next] == Tatweel || Forms.ContainsKey(text[next]));
    }

    private static int PreviousLetter(string text, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            if (!IsMark(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int NextLetter(string text, int index)
    {
        for (int i = index + 1; i < text.Length; i++)
        {
            if (!IsMark(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    // ══════════ المرحلة 2: الترتيب البصري ══════════

    private enum RunKind
    {
        Rtl,
        Ltr,
        Neutral
    }

    /// <summary>
    /// بيرتّب النص للعرض على محرك LTR، باتجاه أساسي RTL.
    /// بنعكس ترتيب المقاطع، وبنعكس حروف المقاطع العربية بس — الأرقام والكلام
    /// الإنجليزي بيفضلوا بترتيبهم الطبيعي جوه مقاطعهم.
    /// </summary>
    private static string ToVisualOrder(string text)
    {
        var runs = new List<(RunKind Kind, string Text)>();
        var buffer = new StringBuilder();
        RunKind? currentKind = null;

        foreach (char c in text)
        {
            RunKind kind = Classify(c);

            if (currentKind is not null && kind != currentKind)
            {
                runs.Add((currentKind.Value, buffer.ToString()));
                buffer.Clear();
            }

            currentKind = kind;
            buffer.Append(c);
        }

        if (buffer.Length > 0)
        {
            runs.Add((currentKind!.Value, buffer.ToString()));
        }

        runs.Reverse();

        var result = new StringBuilder(text.Length);
        foreach (var (kind, runText) in runs)
        {
            result.Append(kind switch
            {
                RunKind.Rtl => ReverseKeepingMarks(runText),
                RunKind.Neutral => MirrorBrackets(runText),
                _ => runText
            });
        }

        return result.ToString();
    }

    /// <summary>
    /// بيعكس حروف مقطع عربي، بس بيحافظ على التشكيل ملزوق بحرفه وبعده.
    /// لو عكسنا حرف بحرف، التشكيل هيسبق حرفه ويترسم غلط.
    /// </summary>
    private static string ReverseKeepingMarks(string run)
    {
        var clusters = new List<string>();
        var cluster = new StringBuilder();

        foreach (char c in run)
        {
            if (IsMark(c) && cluster.Length > 0)
            {
                cluster.Append(c); // التشكيل بيلحق بالحرف اللي قبله
                continue;
            }

            if (cluster.Length > 0)
            {
                clusters.Add(cluster.ToString());
                cluster.Clear();
            }

            cluster.Append(c);
        }

        if (cluster.Length > 0)
        {
            clusters.Add(cluster.ToString());
        }

        clusters.Reverse();
        return string.Concat(clusters);
    }

    private static string MirrorBrackets(string run)
    {
        if (!run.Any(Mirrored.ContainsKey))
        {
            return run;
        }

        return string.Concat(run.Select(c => Mirrored.TryGetValue(c, out char mirror) ? mirror : c));
    }

    // ══════════ تصنيف الحروف ══════════

    private static RunKind Classify(char c)
    {
        if (IsArabic(c))
        {
            return RunKind.Rtl;
        }

        // الأرقام والحروف اللاتيني بتتقرا من الشمال لليمين حتى وهي جوه نص عربي
        if (char.IsAsciiDigit(c) || char.IsAsciiLetter(c) || (c >= 0x00C0 && c <= 0x024F))
        {
            return RunKind.Ltr;
        }

        return RunKind.Neutral;
    }

    private static bool IsArabic(char c) =>
        (c >= 0x0600 && c <= 0x06FF) ||   // العربي الأساسي (شامل الأرقام العربية-الهندية والتشكيل)
        (c >= 0x0750 && c <= 0x077F) ||   // ملحق العربي
        (c >= 0xFB50 && c <= 0xFDFF) ||   // أشكال العرض A (فيها ligatures لام-ألف)
        (c >= 0xFE70 && c <= 0xFEFF);     // أشكال العرض B (الأشكال المشبوكة)

    /// <summary>التشكيل وعلامات التركيب — بتترسم فوق أو تحت الحرف ومش بتاخد مساحة لوحدها.</summary>
    private static bool IsMark(char c) =>
        (c >= 0x064B && c <= 0x065F) ||   // فتحتين .. علامات قرآنية
        c == 0x0670 ||                    // ألف خنجرية
        (c >= 0x06D6 && c <= 0x06ED);     // علامات قرآنية إضافية

    private static bool ContainsArabic(string text) => text.Any(IsArabic);
}
