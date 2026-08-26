namespace PrintFlow.Domain;

/// <summary>
/// حساب مقياس الصفحة بالنسبة المئوية.
///
/// المعنى المقصود هنا بالظبط — وده مهم لأن كلمة "مقياس" ليها أكتر من معنى
/// في برامج الطباعة:
///
///   • **مقاس الورقة مابيتغيّرش.** A4 داخلة A4 طالعة.
///   • **المحتوى بس هو اللي بيصغّر أو يكبر**، حوالين مركز الورقة.
///   • ٩٠٪ يعني هامش أبيض حوالين المحتوى — وده الاستخدام الأكتر في المطبعة،
///     لما الطابعة بتقص من الحواف والمستخدم عايز يبعد المحتوى عنها.
///   • ١١٠٪ يعني المحتوى بيكبر وبيخرج بره الورقة (بيتقص). ده مقصود —
///     نفس سلوك أي بوكس طباعة.
///
/// حساب على أرقام بس، متختبر من غير أي PDF.
/// </summary>
public static class PageScaling
{
    /// <summary>أقل نسبة مسموح بيها — تحتها المحتوى بيبقى نقطة.</summary>
    public const int Minimum = 10;

    /// <summary>أكبر نسبة مسموح بيها.</summary>
    public const int Maximum = 400;

    public static int Clamp(int percent) => Math.Clamp(percent, Minimum, Maximum);

    /// <summary>
    /// ١٠٠٪ يعني مفيش شغل أصلًا.
    ///
    /// ده مش تحسين اختياري — ده **قرار جودة**. إعادة رسم المستند بتعيد ترميز
    /// المحتوى من غير أي داعي؛ لما المستخدم مايطلبش مقياس، الملف لازم يعدّي
    /// بالبايت زي ما هو.
    /// </summary>
    public static bool IsIdentity(int percent) => Clamp(percent) == 100;

    /// <summary>
    /// مستطيل المحتوى بعد المقياس، متوسّط في الورقة.
    /// بيرجّع نفس نوع <see cref="SlideRect"/> اللي التجميع بيستخدمه عشان
    /// الرسم يبقى بنفس الطريقة في الحالتين.
    /// </summary>
    public static SlideRect Place(double pageWidth, double pageHeight, int percent)
    {
        double factor = Clamp(percent) / 100.0;

        double width = pageWidth * factor;
        double height = pageHeight * factor;

        return new SlideRect(
            (pageWidth - width) / 2,
            (pageHeight - height) / 2,
            width,
            height);
    }

    /// <summary>وصف بالعربي للي هيحصل — بيظهر جنب الخانة في الواجهة.</summary>
    public static string Describe(int percent)
    {
        int value = Clamp(percent);

        if (value == 100)
        {
            return "المحتوى هيتطبع بمقاسه الطبيعي.";
        }

        return value < 100
            ? $"المحتوى هيصغّر لـ {value}% ويتوسّط في الورقة — هامش أبيض حواليه."
            : $"المحتوى هيكبر لـ {value}% — اللي هيخرج بره حدود الورقة هيتقص.";
    }
}
