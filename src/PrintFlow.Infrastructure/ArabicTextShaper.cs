using System.Text;

namespace PrintFlow.Infrastructure;

/// <summary>
/// يحوّل نص عربي "منفصل الحروف" لشكل "مشبوك" صحيح بصريًا،
/// عشان مكتبات زي PDFsharp اللي مش بتدعم RTL/Shaping تلقائيًا ترسمه صح.
/// تغطية الحروف الأساسية فقط - كافية لنصوص قصيرة زي العلامة المائية.
/// </summary>
public static class ArabicTextShaper
{
    // كل حرف: [منفصل, بداية كلمة, وسط كلمة, نهاية كلمة]
    private static readonly Dictionary<char, char[]> Forms = new()
    {
        ['ا'] = new[] { 'ا', 'ا', 'ﺎ', 'ﺎ' },
        ['أ'] = new[] { 'أ', 'أ', 'ﺄ', 'ﺄ' },
        ['إ'] = new[] { 'إ', 'إ', 'ﺈ', 'ﺈ' },
        ['آ'] = new[] { 'آ', 'آ', 'ﺂ', 'ﺂ' },
        ['ب'] = new[] { 'ب', 'ﺑ', 'ﺒ', 'ﺐ' },
        ['ت'] = new[] { 'ت', 'ﺗ', 'ﺘ', 'ﺖ' },
        ['ث'] = new[] { 'ث', 'ﺛ', 'ﺜ', 'ﺚ' },
        ['ج'] = new[] { 'ج', 'ﺟ', 'ﺠ', 'ﺞ' },
        ['ح'] = new[] { 'ح', 'ﺣ', 'ﺤ', 'ﺢ' },
        ['خ'] = new[] { 'خ', 'ﺧ', 'ﺨ', 'ﺦ' },
        ['د'] = new[] { 'د', 'د', 'ﺪ', 'ﺪ' },
        ['ذ'] = new[] { 'ذ', 'ذ', 'ﺬ', 'ﺬ' },
        ['ر'] = new[] { 'ر', 'ر', 'ﺮ', 'ﺮ' },
        ['ز'] = new[] { 'ز', 'ز', 'ﺰ', 'ﺰ' },
        ['س'] = new[] { 'س', 'ﺳ', 'ﺴ', 'ﺲ' },
        ['ش'] = new[] { 'ش', 'ﺷ', 'ﺸ', 'ﺶ' },
        ['ص'] = new[] { 'ص', 'ﺻ', 'ﺼ', 'ﺺ' },
        ['ض'] = new[] { 'ض', 'ﺿ', 'ﻀ', 'ﺾ' },
        ['ط'] = new[] { 'ط', 'ﻃ', 'ﻄ', 'ﻂ' },
        ['ظ'] = new[] { 'ظ', 'ﻇ', 'ﻈ', 'ﻆ' },
        ['ع'] = new[] { 'ع', 'ﻋ', 'ﻌ', 'ﻊ' },
        ['غ'] = new[] { 'غ', 'ﻏ', 'ﻐ', 'ﻎ' },
        ['ف'] = new[] { 'ف', 'ﻓ', 'ﻔ', 'ﻒ' },
        ['ق'] = new[] { 'ق', 'ﻗ', 'ﻘ', 'ﻖ' },
        ['ك'] = new[] { 'ك', 'ﻛ', 'ﻜ', 'ﻚ' },
        ['ل'] = new[] { 'ل', 'ﻟ', 'ﻠ', 'ﻞ' },
        ['م'] = new[] { 'م', 'ﻣ', 'ﻤ', 'ﻢ' },
        ['ن'] = new[] { 'ن', 'ﻧ', 'ﻨ', 'ﻦ' },
        ['ه'] = new[] { 'ه', 'ﻫ', 'ﻬ', 'ﻪ' },
        ['و'] = new[] { 'و', 'و', 'ﻮ', 'ﻮ' },
        ['ي'] = new[] { 'ي', 'ﻳ', 'ﻴ', 'ﻲ' },
        ['ى'] = new[] { 'ى', 'ى', 'ﯽ', 'ﯽ' },
        ['ة'] = new[] { 'ة', 'ة', 'ﺔ', 'ﺔ' },
        ['ئ'] = new[] { 'ئ', 'ﺋ', 'ﺌ', 'ﺊ' },
        ['ؤ'] = new[] { 'ؤ', 'ؤ', 'ﺆ', 'ﺆ' },
    };

    // حروف "نص-وصل" (بتتصل من قبلها بس، مش بتوصل للي بعدها)
    private static readonly HashSet<char> RightJoiningOnly = new() { 'ا', 'أ', 'إ', 'آ', 'د', 'ذ', 'ر', 'ز', 'و', 'ى', 'ة', 'ؤ' };

    public static string Reshape(string input)
    {
        if (string.IsNullOrEmpty(input) || !ContainsArabic(input))
        {
            return input; // نص مش عربي، منسيبوش زي ما هو
        }

        var shaped = new StringBuilder();

        for (int i = 0; i < input.Length; i++)
        {
            char current = input[i];

            if (!Forms.ContainsKey(current))
            {
                shaped.Append(current); // مسافة، رقم، حرف أجنبي... نسيبه زي ما هو
                continue;
            }

            bool connectsFromPrev = i > 0 && Forms.ContainsKey(input[i - 1]) && !RightJoiningOnly.Contains(input[i - 1]);
            bool connectsToNext = i < input.Length - 1 && Forms.ContainsKey(input[i + 1]) && !RightJoiningOnly.Contains(current);

            char[] forms = Forms[current];
            char selected = (connectsFromPrev, connectsToNext) switch
            {
                (false, false) => forms[0], // منفصل
                (false, true) => forms[1],  // بداية كلمة
                (true, true) => forms[2],   // وسط كلمة
                (true, false) => forms[3],  // نهاية كلمة
            };

            shaped.Append(selected);
        }

        // عكس ترتيب الحروف عشان محرك رسم من الشمال لليمين (زي PDFsharp) يعرضها صح بصريًا
        var chars = shaped.ToString().ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    private static bool ContainsArabic(string text) => text.Any(c => c >= 0x0600 && c <= 0x06FF);
}