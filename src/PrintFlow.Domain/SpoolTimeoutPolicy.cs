namespace PrintFlow.Domain;

/// <summary>
/// المهلة اللي بنستنى فيها SumatraPDF لحد ما يسلّم الجوب لطابور الطباعة.
///
/// ليه دي موجودة أصلًا: المهلة كانت **دقيقتين ثابتة** لأي جوب. الكلام ده
/// كان مبني على فهم غلط — إن Sumatra بيسلّم الملف ويخرج على طول. الحقيقة إنه
/// بيرندر كل صفحة وبيبعتها للسبولر، والبروسيس مابيخرجش غير لما يخلّص الصفحات
/// كلها. يعني ٢١٠ صفحة ممكن تاخد تلات دقايق بسهولة، وساعتها الجوب كان
/// **بيتقتل في نص الطباعة** والورق بيطلع ناقص.
///
/// دي أسوأ حاجة ممكن تحصل في مطبعة: مش رسالة خطأ واضحة، لأ — ورق ناقص
/// من غير ما حد ياخد باله.
///
/// فالمهلة بقت بتكبر مع حجم الشغل الفعلي: عدد الصفحات × عدد النسخ.
/// </summary>
public static class SpoolTimeoutPolicy
{
    /// <summary>أقل مهلة مهما كان الجوب صغير.</summary>
    public static readonly TimeSpan Minimum = TimeSpan.FromMinutes(5);

    /// <summary>سقف نهائي — بعد كده الجوب اتعلّق فعلًا مش بس بطيء.</summary>
    public static readonly TimeSpan Maximum = TimeSpan.FromMinutes(120);

    /// <summary>لما مانعرفش عدد الصفحات، بنسيب مهلة واسعة بدل ما نقطع شغل شغال.</summary>
    public static readonly TimeSpan WhenPageCountIsUnknown = TimeSpan.FromMinutes(15);

    /// <summary>كام صفحة مطبوعة بنحسبلها دقيقة. متحسوبة على ماكينة بطيئة عن قصد.</summary>
    private const int PagesPerMinute = 100;

    public static TimeSpan For(int pageCount, int copies)
    {
        if (pageCount <= 0 || copies <= 0)
        {
            return WhenPageCountIsUnknown;
        }

        // long عشان ٢٠٠٠ صفحة × ١٠٠٠ نسخة ماتقلبش الرقم لسالب
        long sheets = (long)pageCount * copies;
        double minutes = Minimum.TotalMinutes + (double)sheets / PagesPerMinute;

        if (minutes >= Maximum.TotalMinutes)
        {
            return Maximum;
        }

        return TimeSpan.FromMinutes(minutes);
    }
}
