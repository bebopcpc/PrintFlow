namespace PrintFlow.Domain;

/// <summary>ليه فشلنا في أخذ الملف من مكان الاستقبال.</summary>
public enum ClaimFailure
{
    /// <summary>نجح.</summary>
    None,

    /// <summary>برنامج تاني ماسك الملف دلوقتي — بيتحل لوحده بعد ثواني.</summary>
    Locked,

    /// <summary>
    /// مفيش صلاحية. ده **مابيتحلش بالانتظار** — لازم تدخّل.
    /// </summary>
    NoPermission,

    /// <summary>الملف اختفى (حد شاله أو جوب تاني دهسه).</summary>
    Missing,

    /// <summary>حاجة تانية.</summary>
    Unknown
}

/// <summary>
/// بيقرا سبب فشل نقل الملف الوارد، ويقرر: نعيد المحاولة ولا نقول للمستخدم؟
///
/// ═══ ليه ده موجود ═══
///
/// في نسخة 1.9.0 كان الاستقبال بيفشل على أول جوب حقيقي برسالة:
///
///   [تنبيه] مقدرناش ناخد "incoming.pdf": Access to the path is denied.
///
/// السبب: خدمة الطباعة بتشتغل بحساب SYSTEM وهي اللي **بتعمل** الملف، فبتبقى
/// مالكته. وسكربت التسطيب كان بيدي الصلاحية لـ SYSTEM بس — واللي فاته إن
/// البرنامج شغّال بحساب المستخدم العادي، و<c>File.Move</c> محتاج صلاحية
/// **حذف** على الملف الأصلي، والمستخدم مكانش عنده.
///
/// والأسوأ إن الرسالة كانت بتطلع كل نص ثانية بنص إنجليزي تقني، وماكانتش
/// بتقول للمستخدم يعمل إيه. الفرق بين السببين مهم:
///
///   • قفل ملف     → استنى، بيتحل لوحده
///   • صلاحيات     → عمرك ما هيتحل بالانتظار، قول للمستخدم يشغّل السكربت
///
/// حساب خالص على أنواع الاستثناءات — متختبر من غير ويندوز.
/// </summary>
public static class FileClaim
{
    /// <summary>
    /// كام محاولة صامتة قبل ما نزعج المستخدم — لما المشكلة **مش** بتتحل
    /// بالانتظار (الصلاحيات).
    ///
    /// القراءة كل ٤٠٠ مللي، يعني ٥ محاولات ≈ ثانيتين. سريعة عن قصد: ده
    /// عطل حقيقي محتاج تدخّل، ومحدش يستفيد من إننا نسكت عليه.
    /// </summary>
    public const int QuietAttempts = 5;

    /// <summary>
    /// وكام محاولة لما المشكلة **بتتحل لوحدها** (قفل ملف).
    ///
    /// ٧٥ محاولة ≈ ٣٠ ثانية. الرقم كبير عن قصد:
    ///
    /// أول تجربة حقيقية كانت ملزمة ١٧٦ صفحة / ١٢ ميجا من Foxit. الملف
    /// فضل مقفول شوية والبرنامج طلّع تحذير **بعد ثانيتين** — وبعد لحظات
    /// الملف وصل سليم. يعني التحذير كان صح تقنيًا وغلط عمليًا: خوّف
    /// المستخدم من حاجة اتحلّت لوحدها.
    ///
    /// وتحذير بيطلع وبعدين يبان إنه مالوش لازمة أسوأ من مفيش تحذير —
    /// لأنه بيعلّم اللي في المطبعة إنه يتجاهل السطور الصفرا.
    /// </summary>
    public const int QuietAttemptsWhenLocked = 75;

    /// <summary>كام محاولة صامتة حسب نوع الفشل.</summary>
    public static int QuietAttemptsFor(ClaimFailure failure)
        => failure == ClaimFailure.Locked ? QuietAttemptsWhenLocked : QuietAttempts;

    /// <summary>
    /// السطر اللي بيتقال لما ملف كنا حذّرنا منه يوصل في الآخر.
    ///
    /// من غيره، آخر حاجة المستخدم شايفها في اللوج بتفضل تحذير — حتى بعد
    /// ما المشكلة خلصت.
    /// </summary>
    public static string Resolved(string fileName)
        => $"[استقبال] \"{fileName}\" اتحل وخلاص — الجوب وصل.";

    /// <summary>
    /// بيصنّف الاستثناء.
    ///
    /// مهم: <c>UnauthorizedAccessException</c> مش نوع من <c>IOException</c>
    /// في .NET — دي وراثة من <c>SystemException</c> مباشرة. فالكود اللي
    /// بيمسك IOException بس بيسيب مشكلة الصلاحيات تعدّي للـ catch العام.
    /// وده اللي حصل بالظبط.
    /// </summary>
    public static ClaimFailure Classify(Exception? exception) => exception switch
    {
        null => ClaimFailure.None,
        UnauthorizedAccessException => ClaimFailure.NoPermission,
        FileNotFoundException => ClaimFailure.Missing,
        DirectoryNotFoundException => ClaimFailure.Missing,
        IOException => ClaimFailure.Locked,
        _ => ClaimFailure.Unknown
    };

    /// <summary>
    /// نعيد المحاولة ولا لأ؟
    ///
    /// الصلاحيات بتتعاد كمان — بس مرات قليلة، لأنها ممكن تكون لحظية
    /// (الملف لسه بيتقفل من السبولر). بعد كده بنبطّل ونتكلم.
    /// </summary>
    public static bool WorthRetrying(ClaimFailure failure)
        => failure is ClaimFailure.Locked or ClaimFailure.NoPermission;

    /// <summary>الملف اختفى؟ مفيش حاجة نعملها ومفيش داعي نزعج حد.</summary>
    public static bool IsSilent(ClaimFailure failure)
        => failure is ClaimFailure.None or ClaimFailure.Missing;

    /// <summary>
    /// الرسالة اللي بتتقال بعد ما المحاولات الصامتة تخلص.
    ///
    /// كل رسالة لازم تقول **يعمل إيه**، مش بس إن في مشكلة.
    /// </summary>
    public static string Explain(ClaimFailure failure, string fileName) => failure switch
    {
        ClaimFailure.NoPermission =>
            $"[فشل] مافيش صلاحية على \"{fileName}\". الملف عمله ويندوز بحساب النظام " +
            "والبرنامج مش قادر ينقله. الحل: افتح PowerShell كمسؤول في مجلد البرنامج " +
            "واكتب:  .\\install-printer.ps1 -FixPermissions",

        ClaimFailure.Locked =>
            $"[تنبيه] \"{fileName}\" لسه مقفول من برنامج تاني. البرنامج هيفضل يحاول، " +
            "ولو استمر اقفل البرنامج اللي طبع منه.",

        ClaimFailure.Unknown =>
            $"[تنبيه] مقدرناش ناخد \"{fileName}\". شوف التفاصيل في سجل التشغيل.",

        _ => ""
    };
}
