namespace PrintFlow.Domain;

/// <summary>
/// نتيجة أمر طباعة واحد — **بالمعنى اللي بيهمنا وقت التوزيع**.
///
/// ═══ ليه ده موجود ═══
///
/// الطباعة كانت بترجّع نص عربي بس. النص ده كويس للمستخدم، بس الموزّع
/// (WorkDispatcher في طبقة Application) محتاج يقرا منه حاجة واحدة محدّدة:
///
///   **هل ينفع أبعت الشغل ده لمكنة تانية، ولا لأ؟**
///
/// والإجابة مش "نجح/فشل". فيه تلات حالات فشل مختلفة تمامًا:
///
///   ١) <see cref="PrintResult.NotSent"/> — فشل **قبل** ما أي ورق يتحرك:
///      اسم الطابعة غلط، الطابعة رفضت، البروسيس ماشتغلش. الشغل ده
///      **مافيهوش أي ورق طالع**، فنقله لمكنة تانية آمن ١٠٠٪.
///
///   ٢) <see cref="PrintResult.Abandoned"/> — الجوب **اتبعت** وبعدين وقف
///      واتلغى. هنا ممكن يكون طلع نص الملازم وممكن مايكونش طلع حاجة —
///      **إحنا مش عارفين**. نقله لمكنة تانية معناه احتمال حقيقي إن
///      المطبعة تطبع نفس الملزمة مرتين وتدفع تمن الورق. فبنوقف،
///      وبنقول بالظبط أنهي ملف وكام نسخة في الشك، والبني آدم هو اللي
///      يقرر.
///
///   ٣) <see cref="PrintResult.BadJob"/> — المشكلة في الملف نفسه أو في
///      الأداة، مش في المكنة (الملف مش موجود، SumatraPDF ناقص). نقله
///      لمكنة تانية هيفشل بالظبط زي ما فشل هنا، فمفيش فايدة.
///
/// الفرق بين (١) و(٢) هو أهم سطر في الملف ده. لو خلطناهم:
///   • لو عاملنا الكل كـ NotSent → ورق مكرر ومطبعة بتخسر.
///   • لو عاملنا الكل كـ Abandoned → أي غلطة صغيرة بتوقف التوزيع.
/// </summary>
public enum PrintResult
{
    /// <summary>اتسلّمت لطابور الطباعة بنجاح.</summary>
    Delivered,

    /// <summary>مكانش فيها حاجة تتبعت أصلًا (صفر نسخة).</summary>
    Skipped,

    /// <summary>فشلت قبل ما أي ورق يتحرك — آمن تمامًا ننقلها لمكنة تانية.</summary>
    NotSent,

    /// <summary>اتبعتت وبعدين وقفت واتلغت — يحتمل طلع ورق، ممنوع نكررها لوحدنا.</summary>
    Abandoned,

    /// <summary>المشكلة في الملف أو الأداة مش في المكنة — النقل مش هيفيد.</summary>
    BadJob,

    /// <summary>المستخدم لغى.</summary>
    Cancelled
}

/// <summary>
/// نتيجة أمر طباعة: التصنيف + السطر العربي اللي بيتعرض في اللوج.
/// </summary>
public sealed record PrintOutcome(PrintResult Kind, string Message)
{
    /// <summary>
    /// ينفع نبعت الشغل ده لمكنة تانية؟
    ///
    /// **NotSent بس.** أي حاجة تانية إما نجحت، أو مش مضمون إنها ماطبعتش،
    /// أو نقلها مش هيفيد. القاعدة دي هي اللي بتمنع الورق المكرر.
    /// </summary>
    public bool SafeToMoveElsewhere => Kind == PrintResult.NotSent;

    /// <summary>المكنة هي السبب؟ (يعني نوقف نبعتلها).</summary>
    public bool PrinterIsToBlame => Kind is PrintResult.NotSent or PrintResult.Abandoned;

    /// <summary>الشغل خلص من ناحيتنا.</summary>
    public bool Finished => Kind is PrintResult.Delivered or PrintResult.Skipped;

    public static PrintOutcome Delivered(string message) => new(PrintResult.Delivered, message);

    public static PrintOutcome Skipped(string message) => new(PrintResult.Skipped, message);

    public static PrintOutcome NotSent(string message) => new(PrintResult.NotSent, message);

    public static PrintOutcome Abandoned(string message) => new(PrintResult.Abandoned, message);

    public static PrintOutcome BadJob(string message) => new(PrintResult.BadJob, message);

    public static PrintOutcome Cancelled(string message) => new(PrintResult.Cancelled, message);

    public override string ToString() => Message;
}
