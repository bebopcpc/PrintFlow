namespace PrintFlow.Domain;

/// <summary>
/// بيجمّع الملفات اللي فشلت لنفس السبب في سطر واحد، وبيقول أسماءها.
///
/// ═══ المشكلة اللي بيحلها ═══
///
/// وضع "كل ملف لوحده" بيكتب سطر فشل لكل ملف. في تجربة حقيقية اتحمّل
/// فيها ٤٤ ملف، فشل منهم ٢٠ لنفس السبب بالظبط — فطلعوا **عشرين سطر
/// متطابق** في شاشة النتايج، غرقوا سطر النجاح اللي فوقهم.
///
/// وأسوأ من التكرار: مافيش سطر فيهم بيقول **أنهي ملف**. الرسالة بتقول
/// "حذف الصفحات شال كل الصفحات" وخلاص. فاللي واقف في المطبعة عارف إن
/// حاجة فشلت، ومش عارف يروح لأنهي ملف يصلّحه.
///
/// ═══ الشكل الجديد ═══
///
///   [فشل] ٢٠ ملف: حذف الصفحات "1" شال كل الصفحات — مفيش حاجة تتطبع.
///         الملفات: a.pdf، b.pdf، c.pdf و١٧ غيرهم
///
/// سطرين بدل عشرين، وفيهم الأسامي.
///
/// ═══ ليه بس ٣ أسامي ═══
///
/// عشان السطر مايطولش ويخرج بره الشاشة. التلاتة الأولانيين كفاية إن
/// المستخدم يفتح واحد منهم يشوف المشكلة إيه — والباقي نفس الحكاية.
/// السجل على القرص فيه الملفات كلها بالاسم، عشان أي مراجعة بعدين.
///
/// حساب خالص على نصوص — متختبر من غير ملفات ولا طابعة.
/// </summary>
public static class FailureSummary
{
    /// <summary>كام اسم نكتبه قبل ما نقول "وكذا غيرهم".</summary>
    public const int NamesShown = 3;

    /// <summary>
    /// بيرجّع سطر (أو سطرين) لكل سبب فشل مختلف، بالترتيب اللي حصل بيه.
    /// بيرجّع لستة فاضية لما مفيش فشل.
    /// </summary>
    /// <param name="failures">اسم الملف والرسالة اللي رجعت منه.</param>
    public static IReadOnlyList<string> Describe(IEnumerable<(string File, string Message)> failures)
    {
        if (failures is null)
        {
            return [];
        }

        // ترتيب الظهور مقصود: أول سبب حصل يفضل أول سطر. Dictionary عادي
        // في .NET بيحافظ على ترتيب الإضافة طالما مفيش حذف، بس مابنعتمدش
        // على ده — بنمسك الترتيب بإيدنا.
        var order = new List<string>();
        var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (file, message) in failures)
        {
            string reason = message ?? "";

            if (!grouped.TryGetValue(reason, out var names))
            {
                names = [];
                grouped[reason] = names;
                order.Add(reason);
            }

            if (!string.IsNullOrWhiteSpace(file))
            {
                names.Add(file);
            }
        }

        var lines = new List<string>();

        foreach (string reason in order)
        {
            var names = grouped[reason];

            lines.Add(Headline(reason, names.Count));

            if (names.Count > 0)
            {
                lines.Add("      الملفات: " + NameList(names));
            }
        }

        return lines;
    }

    /// <summary>
    /// السطر الأول: العدد والسبب.
    ///
    /// الملف الواحد مابيتقالش عنه "١ ملف" — بيتقال السبب على طول، زي
    /// ما كان بالظبط قبل التجميع.
    /// </summary>
    private static string Headline(string reason, int count)
    {
        if (count <= 1)
        {
            return reason;
        }

        // الرسالة جاية بالشكل "[فشل] كذا". العدد بيتحط بعد الوسم عشان
        // السطر يفضل يبدأ بـ [فشل] زي باقي سطور النتايج.
        const string tag = "[فشل] ";

        return reason.StartsWith(tag, StringComparison.Ordinal)
            ? $"{tag}{count} ملف: {reason[tag.Length..]}"
            : $"{count} ملف: {reason}";
    }

    /// <summary>
    /// أول تلات أسامي وبعدين "وكذا غيرهم" — عشان السطر مايطولش.
    ///
    /// عامة عن قصد: خدمة الدمج بتحتاجها كمان. هناك الأربعين تنبيه كانوا
    /// بيتلموا في **سطر واحد عملاق** يملا مربع النتايج كله — نفس المشكلة
    /// بشكل تاني، فنفس الحل.
    /// </summary>
    public static string NameList(IReadOnlyList<string> names)
    {
        string head = string.Join("، ", names.Take(NamesShown));

        return names.Count > NamesShown
            ? $"{head} و{names.Count - NamesShown} غيرهم"
            : head;
    }
}
