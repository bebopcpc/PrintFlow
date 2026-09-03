namespace PrintFlow.Domain;

/// <summary>
/// ليه المهلة عدّت والجوب لسه مامشيش.
/// </summary>
public enum StallReason
{
    /// <summary>مالقيناش سبب معروف — يبقى عطل حقيقي.</summary>
    Unknown,

    /// <summary>الورق خلص أو اتزنق أو الدرج مقفول.</summary>
    Paper,

    /// <summary>الحبر/التونر خلص.</summary>
    Ink,

    /// <summary>الطابعة موقوفة يدويًا.</summary>
    Paused,

    /// <summary>باب مفتوح أو تدخّل بشري مطلوب.</summary>
    NeedsAttention,

    /// <summary>الطابعة شغالة فعلًا وبتطبع — إحنا اللي استعجلنا.</summary>
    StillPrinting
}

/// <summary>
/// بيقرر: نستنى أكتر ولا نلغي الجوب؟
///
/// ليه ده موجود أصلًا:
///
/// المهلة موجودة عشان جوب معلّق مايقعدش يستنى للأبد. بس فيه فرق جوهري بين
/// "الطابعة ماتت" و"الورق خلص". في المطبعة الورق بيخلص كل شوية، وده **سلوك
/// طبيعي مش عطل** — بتحط ورق والجوب بيكمّل من مكانه. لو المهلة قتلت الجوب
/// في اللحظة دي، اللي بيحصل إن نص الملزمة بتطلع والباقي بيضيع، والمستخدم
/// مايعرفش غير لما يعدّ الورق.
///
/// فالقاعدة: **مانلغيش جوب بسبب حاجة اليد البشرية بتحلها.** بنستنى، وبنقول
/// في اللوج إيه اللي واقف، والمهلة بتتمدّد لحد السقف الأقصى.
///
/// حساب خالص على أرقام — متختبر من غير طابعة.
/// </summary>
public static class PrinterStall
{
    // ═══ أكواد Win32_Printer.DetectedErrorState (CIM) ═══
    //
    // ⚠ الأرقام دي كانت **مزحلقة** واتصلّحت من توثيق مايكروسوفت الرسمي.
    // الغلط كان بيعمل حاجتين وحشين:
    //
    //   • ٩ (مفصولة) كان مكتوب عليه "باب مفتوح" → الطابعة المفصولة كان
    //     ردنا عليها "استنى"، فالجوب يفضل مستني لحد أربع ساعات على مكنة
    //     مش موصولة أصلًا.
    //
    //   • ٧ (باب مفتوح) كان مكتوب عليه "مفصولة" → الباب المفتوح كان
    //     بيلغي الجوب، وهو أسهل حاجة اليد البشرية بتحلها. عكس فلسفة
    //     الملف ده بالظبط.
    //
    // وكمان ١١ (درج الخارج ملان) مكانش متعالج خالص، و٥ (حبر قليل) كان
    // بيطلّع رسالة "الورق خلص".
    //
    // القايمة الصح: ٣ ورق قليل · ٤ ورق خلص · ٥ حبر قليل · ٦ حبر خلص
    // ٧ باب مفتوح · ٨ ورق مزنوق · ٩ مفصولة · ١٠ صيانة · ١١ درج الخارج ملان
    private const int ErrorLowPaper = 3;
    private const int ErrorNoPaper = 4;
    private const int ErrorLowToner = 5;
    private const int ErrorNoToner = 6;
    private const int ErrorDoorOpen = 7;
    private const int ErrorPaperJam = 8;
    private const int ErrorOffline = 9;
    private const int ErrorServiceRequested = 10;
    private const int ErrorOutputBinFull = 11;

    // أكواد Win32_Printer.PrinterStatus
    private const int StatusPrinting = 4;
    private const int StatusWarmingUp = 5;
    private const int StatusPaused = 1;
    private const int StatusStopped = 6;

    /// <summary>
    /// بيقرا حالة الطابعة ويقول السبب.
    /// </summary>
    /// <param name="printerStatus">Win32_Printer.PrinterStatus — null لو مقدرناش نقراه.</param>
    /// <param name="detectedErrorState">Win32_Printer.DetectedErrorState — null لو مقدرناش نقراه.</param>
    /// <param name="isPaused">الطابور موقوف يدويًا.</param>
    /// <param name="jobsWaiting">في جوبات في الطابور دلوقتي.</param>
    public static StallReason Diagnose(
        int? printerStatus,
        int? detectedErrorState,
        bool isPaused,
        bool jobsWaiting)
    {
        // حالة العطل أدق حاجة عندنا، فبنبص عليها الأول
        switch (detectedErrorState)
        {
            case ErrorLowPaper:
            case ErrorNoPaper:
            case ErrorPaperJam:
            case ErrorOutputBinFull:
                return StallReason.Paper;

            case ErrorLowToner:
            case ErrorNoToner:
                return StallReason.Ink;

            case ErrorDoorOpen:
            case ErrorServiceRequested:
                return StallReason.NeedsAttention;
        }

        if (isPaused || printerStatus is StatusPaused or StatusStopped)
        {
            return StallReason.Paused;
        }

        if (printerStatus is StatusPrinting or StatusWarmingUp)
        {
            return StallReason.StillPrinting;
        }

        // مفيش عطل معروف، بس في ورق مستني في الطابور — يبقى الطابعة
        // شغالة على حاجة، مش ميتة. بنديها فرصة.
        //
        // مهم: ده بيتحسب **بعد** فحص الأوفلاين فوق، عشان طابعة مفصولة
        // وعندها جوبات عالقة ما تتحسبش "بتشتغل".
        if (jobsWaiting && detectedErrorState != ErrorOffline)
        {
            return StallReason.StillPrinting;
        }

        return StallReason.Unknown;
    }

    /// <summary>
    /// نستنى أكتر ولا نلغي؟
    ///
    /// كل الأسباب اللي اليد البشرية بتحلها = استنى. المجهول بس = الغي.
    /// </summary>
    public static bool ShouldKeepWaiting(StallReason reason) => reason != StallReason.Unknown;

    /// <summary>شرح عربي يتكتب في اللوج وقت الانتظار.</summary>
    public static string Describe(StallReason reason) => reason switch
    {
        StallReason.Paper => "الورق خلص أو اتزنق — حط ورق والجوب هيكمّل لوحده",
        StallReason.Ink => "الحبر خلص — غيّر الخرطوشة والجوب هيكمّل لوحده",
        StallReason.Paused => "الطابعة موقوفة — شغّلها من طابور الطباعة",
        StallReason.NeedsAttention => "الطابعة محتاجة تدخّل (باب مفتوح أو صيانة)",
        StallReason.StillPrinting => "الطابعة لسه بتطبع",
        _ => "مفيش سبب واضح"
    };
}
