namespace PrintFlow.Domain;

/// <summary>تقدر تاخد قطعة شغل جديدة دلوقتي؟</summary>
public enum PrinterReadiness
{
    /// <summary>آه.</summary>
    Ready,

    /// <summary>لأ دلوقتي — طابورها لسه مليان. مش عطل.</summary>
    Busy,

    /// <summary>لأ — فيها عطل محتاج يد بشرية.</summary>
    Faulted
}

/// <summary>حكم على مكنة واحدة + السبب العربي.</summary>
public sealed record PrinterVerdict(PrinterReadiness State, string? Reason)
{
    public static readonly PrinterVerdict Ready = new(PrinterReadiness.Ready, null);

    public static PrinterVerdict Busy(string reason) => new(PrinterReadiness.Busy, reason);

    public static PrinterVerdict Faulted(string reason) => new(PrinterReadiness.Faulted, reason);
}

/// <summary>
/// بيقرر هل ندّي المكنة قطعة شغل جديدة، من أرقام ويندوز الخام.
///
/// ═══ ليه فيه كابح (طابور) أصلًا ═══
///
/// دي أهم فكرة في التوزيع كله، ومش بديهية:
///
/// إرسال الجوب للطابعة بيخلص في **ثواني**، والطباعة الحقيقية بتاخد
/// **دقايق**. يعني لو سبنا كل مكنة تسحب شغل كل ما تخلّص إرسال، أسرع
/// مكنة في الإرسال (مش في الطباعة — الشبكة والدرايفر هما اللي بيحددوا)
/// هتشفط الأوردر كله في طابور ويندوز بتاعها في أول عشر ثواني.
///
/// وساعتها التوزيع بيبقى **على الورق بس**: الشغل كله اتلزق في مكنة
/// واحدة، ولو وقعت بعدها مفيش أي حاجة فاضلة في إيدينا ننقلها.
///
/// الكابح ده بيمنع كده: المكنة اللي عندها أكتر من
/// <see cref="QueueRoom"/> جوب مستني بتستنى دورها. النتيجة إن اللي
/// متلزم لمكنة في أي لحظة قطعة أو اتنين، وباقي الأوردر لسه في إيدينا.
///
/// ═══ ليه واحد مش صفر ═══
///
/// صفر معناها: استنى لحد ما الطابعة تفضى تمامًا، وبعدين ابعت. وبين
/// "فضيت" و"وصلها الجوب الجديد" فيه ثواني الطابعة بتقف فيها على
/// الفاضي. جوب واحد مستني وراها بيسد الفجوة دي — بتخلّص الحالي وتلاقي
/// اللي بعده جاهز على طول.
///
/// حساب خالص على أرقام — متختبر من غير طابعة.
/// </summary>
public static class PrinterReady
{
    /// <summary>كام جوب مستني مسموح بيهم قبل ما نبطّل نبعت.</summary>
    public const int QueueRoom = 1;

    // Win32_Printer.PrinterStatus — ١ غير ذلك · ٢ غير معروف · ٣ فاضية
    // ٤ بتطبع · ٥ بتسخّن · ٦ واقفة · ٧ مفصولة
    private const int StatusOffline = 7;

    // Win32_Printer.DetectedErrorState — ٩ هي المفصولة.
    //
    // ⚠ كانت مكتوبة ٧، و٧ في الجدول ده معناها **باب مفتوح**. الغلط ده
    // كان بيخلي الباب المفتوح يترجم "الطابعة مفصولة أو مطفية" — رسالة
    // بتبعت اللي في المطبعة يفحص الكابل بدل ما يقفل الباب.
    //
    // (لاحظ إن ٧ في PrinterStatus **فعلًا** معناها مفصولة — الرقمين
    //  في جدولين مختلفين، وده بالظبط مصدر اللخبطة.)
    private const int ErrorOffline = 9;

    /// <summary>
    /// بياخد أرقام WMI الخام ويقول: أبعت، أستنى، ولا فيها عطل.
    /// </summary>
    /// <param name="workOffline">Win32_Printer.WorkOffline.</param>
    /// <param name="printerStatus">Win32_Printer.PrinterStatus — null لو مقدرناش نقراه.</param>
    /// <param name="detectedErrorState">Win32_Printer.DetectedErrorState — null لو مقدرناش نقراه.</param>
    /// <param name="paused">الطابور موقوف يدويًا.</param>
    /// <param name="queuedJobs">
    /// عدد الجوبات المستنية في طابورها. **null معناها مقدرناش نعد** —
    /// وساعتها بنبعت عادي بدل ما نوقف الشغل بسبب فحص مش شغال.
    /// </param>
    public static PrinterVerdict Decide(
        bool workOffline,
        int? printerStatus,
        int? detectedErrorState,
        bool paused,
        int? queuedJobs,
        int queueRoom = QueueRoom)
    {
        // الأوفلاين بيتفحص هنا مش في PrinterStall عن قصد: هناك السؤال
        // "نستنى الجوب اللي ماشي ولا نلغيه"، وهنا السؤال "نبعت جوب جديد
        // ولا لأ" — ومكنة مفصولة الإجابتين مختلفتين تمامًا فيها.
        if (workOffline || printerStatus == StatusOffline || detectedErrorState == ErrorOffline)
        {
            return PrinterVerdict.Faulted("الطابعة مفصولة أو مطفية");
        }

        // ⚠ كان هنا فحص: printerStatus == 2 → "الطابعة في حالة خطأ".
        //
        // اتشال، لأن **مفيش قيمة اسمها خطأ في PrinterStatus أصلًا**.
        // الرقم ٢ في الجدول ده معناه **«غير معروف»** — يعني الفحص كان
        // بيعزل أي طابعة درايفرها مابيبلّغش حالتها كويس، ويقول عليها
        // معطلة، وهي سليمة تمامًا. مطبعة فيها ٥ مكن كانت ممكن تشتغل
        // بـ ٤ من غير ما حد يفهم ليه.
        //
        // وده كمان بيخالف قاعدة المشروع المكتوبة في أكتر من ملف:
        // «مقدرناش نقرا» مش زي «شفنا إن فيه عطل». الأعطال الحقيقية
        // بتيجي من DetectedErrorState تحت، وهي متغطية.
        var stall = PrinterStall.Diagnose(
            printerStatus,
            detectedErrorState,
            paused,
            jobsWaiting: queuedJobs is > 0);

        switch (stall)
        {
            case StallReason.Paper:
            case StallReason.Ink:
            case StallReason.Paused:
            case StallReason.NeedsAttention:
                return PrinterVerdict.Faulted(PrinterStall.Describe(stall));
        }

        // مفيش عطل. باقي سؤال واحد: طابورها فيه مكان؟
        if (queuedJobs is int waiting && waiting > queueRoom)
        {
            return PrinterVerdict.Busy($"لسه قدامها {waiting} جوب في الطابور");
        }

        return PrinterVerdict.Ready;
    }
}
