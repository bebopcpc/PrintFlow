using System.Security.Cryptography;
using System.Text;

namespace PrintFlow.Domain;

/// <summary>
/// رقم الجهاز — اللي العميل بيبعتهولك عشان تطلّعله كود.
///
/// ═══ ليه مش رقم ويندوز على طول ═══
///
/// ويندوز عنده معرّف ثابت لكل تركيبة (MachineGuid). ينفع نبعته زي ما
/// هو، بس فيه مشكلتين:
///
///   • طوله ٣٦ حرف بشُرَط — العميل هيغلط وهو بيكتبه في الواتساب.
///   • هو معرّف بيتستخدم في حاجات تانية في ويندوز. مفيش داعي نخليه
///     يتنقّل في محادثات ويتخزّن في تليفونات الناس.
///
/// فبناخد بصمة منه: تجزئة، وناخد أول ١٠ بايت. ده بيدّي ١٦ حرف في
/// مجموعتين تلاتة وواحدة — سهل يتكتب ويتقرا في التليفون.
///
/// ⚠ التجزئة في اتجاه واحد: من الرقم ده مفيش طريقة ترجع للمعرّف الأصلي.
///
/// ═══ إيه اللي بيغيّر رقم الجهاز ═══
///
///   • تسطيب ويندوز من الأول → رقم جديد، العميل محتاج كود جديد.
///   • تغيير الهارد أو المازربورد → **الرقم مابيتغيرش**، لأنه مربوط
///     بنسخة ويندوز مش بالحديد. ده مقصود: الهارد بيتغيّر أكتر ما
///     الويندوز بيتسطّب، ومش عايزين العميل يتصل بيك كل ما يركّب هارد.
///
/// ⚠ والنتيجة الطبيعية للقرار ده: نسخة الهارد المصوّرة (clone) بتشيل
/// نفس الرقم. يعني اللي ينسخ الهارد على جهاز تاني، الكود هيشتغل على
/// الاتنين. ده تمن الاستقرار — والبديل (ربط بالحديد) كان هيوقف عملاء
/// شرفاء كل شوية.
///
/// ═══ التلميح ═══
///
/// أول بايتين من الرقم بيتحطوا **جوّه الكود** نفسه. فايدتهم إن البرنامج
/// يعرف يفرّق بين "الكود ده لجهاز تاني" و"الكود ده مزوّر" — الرسالتين
/// مختلفتين تمامًا للي واقف قدام الشاشة.
///
/// حساب خالص — متختبر من غير ويندوز.
/// </summary>
public static class MachineCode
{
    /// <summary>كام بايت في رقم الجهاز. ١٠ = ١٦ حرف.</summary>
    public const int Bytes = 10;

    /// <summary>كام بايت منه بيتحطوا في الكود كتلميح.</summary>
    public const int HintBytes = 2;

    /// <summary>
    /// نص ثابت بيتخلط مع المعرّف قبل التجزئة.
    ///
    /// مش سر — موجود في الكود المصدري. فايدته إن البصمة دي مالهاش أي
    /// معنى برّه البرنامج ده، حتى لو برنامج تاني استخدم نفس المعرّف.
    /// </summary>
    private const string Salt = "PrintFlow.Machine.v1";

    /// <summary>
    /// بصمة الجهاز من معرّف ويندوز. بترجّع صفر بايت لو المعرّف فاضي.
    /// </summary>
    public static byte[] From(string? windowsMachineId)
    {
        if (string.IsNullOrWhiteSpace(windowsMachineId))
        {
            return [];
        }

        // Trim و ToLower: نفس المعرّف ممكن يترجع بمسافة أو بحروف كابيتال
        // حسب اللي قراه. من غيرهم نفس الجهاز كان ممكن يدّي رقمين.
        string normalized = Salt + "|" + windowsMachineId.Trim().ToLowerInvariant();

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return hash[..Bytes];
    }

    /// <summary>الشكل اللي بيتعرض للعميل ويتبعت في الواتساب.</summary>
    public static string Display(byte[] machineId) => LicenseCode.Format(machineId);

    /// <summary>أول بايتين — دول اللي بيتحطوا في الكود.</summary>
    public static byte[] HintOf(byte[] machineId)
    {
        if (machineId is null || machineId.Length < HintBytes)
        {
            return [];
        }

        return machineId[..HintBytes];
    }

    /// <summary>التلميح اللي في الكود بتاع الجهاز ده؟</summary>
    public static bool HintMatches(byte[] machineId, byte[] hint)
    {
        var mine = HintOf(machineId);

        return mine.Length == HintBytes
               && hint is { Length: HintBytes }
               && CryptographicOperations.FixedTimeEquals(mine, hint);
    }
}
