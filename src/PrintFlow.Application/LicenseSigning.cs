using System.Security.Cryptography;
using PrintFlow.Domain;

namespace PrintFlow.Application;

/// <summary>اللي طلع من قراية الكود، قبل ما القواعد تحكم عليه.</summary>
public readonly record struct ReadLicense(
    bool Parsed,
    bool HintMatches,
    bool SignatureOk,
    DateOnly? ExpiresOn);

/// <summary>
/// توقيع أكواد التفعيل والتحقق منها.
///
/// ═══ ليه توقيع مش كلمة سر ═══
///
/// برنامج .NET بيتفك بأدوات مجانية في دقيقة. أي سر مخبّي جوّه البرنامج
/// هو سر معروف — واللي يلاقيه يقدر يطلّع أكواد لنفسه ولغيره.
///
/// التوقيع بيقلب المعادلة: **مفتاحين**، سري وعام.
///
///   • السري يقعد على جهازك انت ومايتحطش في البرنامج أبدًا. بيه بس
///     تقدر تطلّع أكواد.
///   • العام بيتحط في البرنامج. بيه بس تقدر **تتأكد** من الكود، مش
///     تطلّعه.
///
/// يعني حتى لو حد فك البرنامج كله وقرا كل سطر فيه، مش هيقدر يطلّع كود
/// واحد. أقصى اللي يقدر يعمله إنه يعدّل البرنامج نفسه عشان مايسألش —
/// ودي حاجة تانية خالص، ومحدش بيعملها عشان برنامج مطبعة.
///
/// ⚠ **المفتاح السري هو المنتج.** لو ضاع، مش هتقدر تجدد لأي عميل ولا
/// تطلّع كود لعميل جديد — وكل اللي عندهم أكواد هيقفوا لما مدتهم تخلص.
/// ولو اتسرب، أي حد يطلّع أكواد. خد منه نسخة احتياطية في مكانين من
/// أول يوم، وماتحطهوش على أي جهاز بتوزّع منه البرنامج.
///
/// ═══ شكل الكود ═══
///
///   [تاريخ الانتهاء ٢ بايت][تلميح الجهاز ٢][التوقيع ٦٤] = ٦٨ بايت
///
/// التوقيع بيتعمل على: **رقم الجهاز كامل + تاريخ الانتهاء**. يعني نفس
/// التاريخ على جهاز تاني بيدّي توقيع مختلف تمامًا، ومفيش نقل أكواد.
///
/// ٦٤ بايت مش اختيار — ده حجم توقيع ECDSA P-256. وهو اللي بيخلي الكود
/// ١٠٩ حرف. الكود القصير معناه سر مخبّي، وده اللي بنهرب منه أصلًا.
///
/// التشفير هنا من مكتبة .NET القياسية، فالملف ده شغّال على أي نظام
/// ومتختبر بالكامل.
/// </summary>
public static class LicenseSigning
{
    /// <summary>حجم توقيع P-256 بصيغة IEEE P1363.</summary>
    public const int SignatureBytes = 64;

    /// <summary>التاريخ بيتخزّن كعدد أيام من نقطة البداية، في بايتين.</summary>
    public const int ExpiryBytes = 2;

    /// <summary>حجم الكود كله بالبايت.</summary>
    public const int CodeBytes = ExpiryBytes + MachineCode.HintBytes + SignatureBytes;

    /// <summary>
    /// نقطة بداية العد. التاريخ بيتخزّن كعدد أيام من هنا في بايتين،
    /// يعني بيوصل لسنة ٢١٩٩ — أكتر من كفاية.
    /// </summary>
    private static readonly DateOnly Epoch = new(2020, 1, 1);

    /// <summary>
    /// بيطلّع مفتاحين جداد. **بيتنده مرة واحدة في عمر المنتج.**
    ///
    /// بيرجّع الاتنين Base64: السري تحفظه عندك، والعام تحطه في البرنامج.
    /// </summary>
    public static (string PrivateKey, string PublicKey) NewKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return (
            Convert.ToBase64String(ecdsa.ExportECPrivateKey()),
            Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>
    /// بيطلّع كود لعميل. **دي بتتنده عندك انت بس** — محتاجة المفتاح السري.
    /// </summary>
    /// <param name="privateKeyBase64">المفتاح السري بتاعك.</param>
    /// <param name="machineId">رقم جهاز العميل (من <see cref="MachineCode"/>).</param>
    /// <param name="expiresOn">آخر يوم شغل — اليوم ده نفسه بيشتغل.</param>
    public static string Issue(string privateKeyBase64, byte[] machineId, DateOnly expiresOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyBase64);

        if (machineId is not { Length: MachineCode.Bytes })
        {
            throw new ArgumentException("رقم الجهاز مش بالطول الصح.", nameof(machineId));
        }

        byte[] expiry = PackExpiry(expiresOn);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportECPrivateKey(Convert.FromBase64String(privateKeyBase64), out _);

        byte[] signature = ecdsa.SignData(
            SignedPart(machineId, expiry),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var code = new byte[CodeBytes];

        expiry.CopyTo(code, 0);
        MachineCode.HintOf(machineId).CopyTo(code, ExpiryBytes);
        signature.CopyTo(code, ExpiryBytes + MachineCode.HintBytes);

        return LicenseCode.Format(code);
    }

    /// <summary>
    /// بيقرا كود العميل ويتأكد منه. دي اللي بتشتغل جوّه البرنامج.
    ///
    /// مابترميش أبدًا — أي حاجة غلط بترجع في النتيجة. الكود اللي المستخدم
    /// لزقه ممكن يكون أي حاجة، ومينفعش البرنامج يقع عشان لزقة غلط.
    /// </summary>
    public static ReadLicense Read(string publicKeyBase64, string? typedCode, byte[] machineId)
    {
        byte[]? code = LicenseCode.Parse(typedCode, CodeBytes);

        if (code is null || machineId is not { Length: MachineCode.Bytes })
        {
            return new ReadLicense(Parsed: false, false, false, null);
        }

        byte[] expiry = code[..ExpiryBytes];
        byte[] hint = code[ExpiryBytes..(ExpiryBytes + MachineCode.HintBytes)];
        byte[] signature = code[(ExpiryBytes + MachineCode.HintBytes)..];

        // التلميح الأول: بيفرّق بين "لجهاز تاني" و"مزوّر". لو سبناه،
        // الاتنين كانوا هيطلعوا "توقيع غلط" والمستخدم مش عارف يعمل إيه.
        bool hintOk = MachineCode.HintMatches(machineId, hint);

        bool signatureOk = false;

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);

            signatureOk = ecdsa.VerifyData(
                SignedPart(machineId, expiry),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch
        {
            // مفتاح عام بايظ أو ناقص = مفيش تحقق. البرنامج مايقعش —
            // بيقول الكود مش مظبوط، وده أوضح للي قدام الشاشة.
        }

        return new ReadLicense(Parsed: true, hintOk, signatureOk, UnpackExpiry(expiry));
    }

    /// <summary>
    /// اللي بيتوقّع عليه: **رقم الجهاز كامل** + التاريخ.
    ///
    /// رقم الجهاز كامل مش التلميح — عشان الكود مايتنقلش. لو وقّعنا على
    /// التلميح بس (بايتين)، كان في جهاز من كل ٦٥ ألف يقبل نفس الكود.
    /// </summary>
    private static byte[] SignedPart(byte[] machineId, byte[] expiry)
    {
        var buffer = new byte[machineId.Length + expiry.Length];

        machineId.CopyTo(buffer, 0);
        expiry.CopyTo(buffer, machineId.Length);

        return buffer;
    }

    private static byte[] PackExpiry(DateOnly expiresOn)
    {
        int days = expiresOn.DayNumber - Epoch.DayNumber;

        if (days is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresOn), "التاريخ لازم يكون بين 2020 و 2199.");
        }

        return [(byte)(days >> 8), (byte)(days & 0xFF)];
    }

    private static DateOnly? UnpackExpiry(byte[] expiry)
    {
        if (expiry is not { Length: ExpiryBytes })
        {
            return null;
        }

        int days = (expiry[0] << 8) | expiry[1];

        return Epoch.AddDays(days);
    }
}
