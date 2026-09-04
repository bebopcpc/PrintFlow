using System.Text;

namespace PrintFlow.Domain;

/// <summary>
/// شكل كود التفعيل: بايتات ← حروف يقدر واحد يكتبها أو يلزقها، والعكس.
///
/// ═══ ليه أبجدية مخصوصة ═══
///
/// الكود بيتبعت واتساب وبيتكتب بالإيد أحيانًا. الحروف اللي بتتلخبط
/// (صفر و O، واحد و I و L) **مش موجودة في الأبجدية أصلًا** — وعند
/// القراءة بنحوّلها للرقم اللي بيشبهها. يعني اللي يكتب O بدل صفر،
/// الكود بتاعه بيشتغل بدل ما يقعد يدوّر على غلطته.
///
/// وحرف U مشال كمان: عشان مايتكوّنش بالصدفة كلام مش لايق من حروف
/// عشوائية. (نفس سبب Crockford في الأبجدية دي.)
///
/// ═══ ليه الكود طويل ═══
///
/// التوقيع الرقمي لوحده ٦٤ بايت. ده مش اختيار — ده حجم توقيع
/// ECDSA P-256. الكود القصير معناه إن الحماية بسر مخبّي في البرنامج،
/// وبرنامج .NET بيتفك في دقيقة، وساعتها أي حد يطلّع أكواد بنفسه.
///
/// طول الكود تمنه إن محدش يقدر يزوّره، حتى لو معاه الكود المصدري كله.
///
/// ═══ الشُّرَط والمسافات ═══
///
/// بتتكتب للقراءة بس، وبتتشال عند القراءة. اللي يلزق الكود بسطور أو
/// مسافات زيادة (وده بيحصل مع الواتساب) الكود بتاعه بيشتغل عادي.
///
/// حساب خالص على بايتات — متختبر من غير ملفات ولا تشفير.
/// </summary>
public static class LicenseCode
{
    /// <summary>
    /// أبجدية Base32 من غير الحروف اللي بتتلخبط: I و L و O و U مش موجودين.
    /// </summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>كام حرف في المجموعة الواحدة قبل الشرطة.</summary>
    public const int GroupSize = 5;

    /// <summary>بيحوّل البايتات لكود مقروء بشُرَط كل ٥ حروف.</summary>
    public static string Format(byte[] data)
    {
        if (data is null || data.Length == 0)
        {
            return "";
        }

        var raw = new StringBuilder();

        int buffer = 0;
        int bits = 0;

        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;

            while (bits >= 5)
            {
                bits -= 5;
                raw.Append(Alphabet[(buffer >> bits) & 31]);
            }
        }

        // آخر أقل من ٥ بتّات: بنكمّلها أصفار عشان مايضيعش منها حاجة
        if (bits > 0)
        {
            raw.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        var grouped = new StringBuilder(raw.Length + (raw.Length / GroupSize));

        for (int i = 0; i < raw.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
            {
                grouped.Append('-');
            }

            grouped.Append(raw[i]);
        }

        return grouped.ToString();
    }

    /// <summary>
    /// بيقرا الكود ويرجّع البايتات. بيرجّع null لو فيه حرف مش من الأبجدية.
    /// </summary>
    /// <param name="typed">اللي المستخدم كتبه أو لزقه — بشُرَط أو من غيرها.</param>
    /// <param name="expectedLength">
    /// كام بايت متوقّعين. لازم يتقال: الحروف الأخيرة ممكن تكون حشو،
    /// ومن غير الطول مانعرفش نوقف فين.
    /// </param>
    public static byte[]? Parse(string? typed, int expectedLength)
    {
        if (string.IsNullOrWhiteSpace(typed) || expectedLength <= 0)
        {
            return null;
        }

        var bytes = new List<byte>(expectedLength);

        int buffer = 0;
        int bits = 0;

        foreach (char raw in typed)
        {
            char c = Normalize(raw);

            if (c == '\0')
            {
                continue;   // شرطة أو مسافة أو سطر جديد — بتتشال
            }

            int value = Alphabet.IndexOf(c);

            if (value < 0)
            {
                return null;   // حرف مش من الأبجدية = كود غلط
            }

            buffer = (buffer << 5) | value;
            bits += 5;

            if (bits >= 8)
            {
                bits -= 8;

                if (bytes.Count == expectedLength)
                {
                    return null;   // أطول من المتوقّع
                }

                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return bytes.Count == expectedLength ? bytes.ToArray() : null;
    }

    /// <summary>
    /// بيوحّد الحرف: كابيتال، والحروف اللي بتتلخبط بترجع لأرقامها.
    /// بيرجّع '\0' للحروف اللي بتتشال (شرطة، مسافة، سطر جديد).
    /// </summary>
    private static char Normalize(char c)
    {
        if (c is '-' or ' ' or '\t' or '\r' or '\n' or '_')
        {
            return '\0';
        }

        char upper = char.ToUpperInvariant(c);

        // ⚠ دي اللي بتخلي الكود المكتوب بالإيد يشتغل.
        // اللي بيكتب O بدل صفر مش غلطان — الاتنين شكلهم واحد في أغلب الخطوط.
        return upper switch
        {
            'O' => '0',
            'I' or 'L' => '1',
            'U' => 'V',   // U مشالة من الأبجدية، وأقرب حاجة ليها V
            _ => upper
        };
    }
}
