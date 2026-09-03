using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// بيثبّت **أرقام** أكواد الأعطال في ويندوز، مش سلوكها بس.
///
/// ═══ ليه ملف لوحده ═══
///
/// الأرقام دي كانت مزحلقة في <see cref="PrinterStall"/> و<see cref="PrinterReady"/>،
/// **والتستات القديمة كانت بتحرس الغلط بدل ما تمسكه**: كان مكتوب فيها
/// `[InlineData(9)] // باب مفتوح` — و٩ في جدول مايكروسوفت معناها
/// **مفصولة** مش باب مفتوح. يعني ٩١١ تست خضرا وهما بيأكدوا حاجة غلط.
///
/// الملف ده بيربط كل رقم باسمه الرسمي في تست مستقل. لو حد زحلقهم تاني،
/// **الاسم في التست هيبان غلط بالعين المجردة** قبل حتى ما التست يقع.
///
/// المرجع: Win32_Printer.DetectedErrorState (CIM)
///   ٠ مجهول · ١ غير ذلك · ٢ مفيش عطل · ٣ ورق قليل · ٤ ورق خلص
///   ٥ حبر قليل · ٦ حبر خلص · ٧ باب مفتوح · ٨ ورق مزنوق · ٩ مفصولة
///   ١٠ صيانة مطلوبة · ١١ درج الخارج ملان
/// </summary>
public class PrinterErrorCodeTests
{
    // ══════════ الورق ══════════

    [Theory]
    [InlineData(3)]    // ورق قليل
    [InlineData(4)]    // ورق خلص
    [InlineData(8)]    // ورق مزنوق
    [InlineData(11)]   // درج الخارج ملان
    public void Paper_Problems_Are_Paper_And_Never_Cancel(int code)
    {
        var reason = PrinterStall.Diagnose(null, code, false, false);

        Assert.Equal(StallReason.Paper, reason);
        Assert.True(PrinterStall.ShouldKeepWaiting(reason));
    }

    /// <summary>
    /// ⚠ ١١ (درج الخارج ملان) مكانش متعالج خالص قبل كده — كان بيقع في
    /// «مجهول» ويلغي الجوب. وهو أسهل حاجة: بتفضّي الدرج والشغل بيكمّل.
    /// </summary>
    [Fact]
    public void A_Full_Output_Bin_Does_Not_Kill_The_Job()
    {
        Assert.True(PrinterStall.ShouldKeepWaiting(PrinterStall.Diagnose(null, 11, false, false)));
    }

    // ══════════ الحبر ══════════

    [Theory]
    [InlineData(5)]    // حبر قليل
    [InlineData(6)]    // حبر خلص
    public void Toner_Problems_Are_Ink_Not_Paper(int code)
    {
        var reason = PrinterStall.Diagnose(null, code, false, false);

        Assert.Equal(StallReason.Ink, reason);
        Assert.Contains("الحبر", PrinterStall.Describe(reason));
    }

    /// <summary>
    /// ⚠ ٥ (حبر قليل) كان مصنّف «ورق» — فالرسالة كانت بتقول للي واقف
    /// «حط ورق» وهو محتاج يغيّر خرطوشة.
    /// </summary>
    [Fact]
    public void Low_Toner_Does_Not_Tell_The_Operator_To_Add_Paper()
    {
        string message = PrinterStall.Describe(PrinterStall.Diagnose(null, 5, false, false));

        Assert.Contains("الحبر", message);
        Assert.DoesNotContain("الورق", message);
    }

    // ══════════ الباب والصيانة — حاجات اليد بتحلها ══════════

    /// <summary>
    /// ⚠ ٧ (باب مفتوح) كان مصنّف «مفصولة» → الجوب كان **بيتلغي**.
    /// وهو أسهل حاجة في المطبعة: بتقفل الباب والشغل بيكمّل.
    /// </summary>
    [Fact]
    public void An_Open_Door_Waits_Instead_Of_Cancelling()
    {
        var reason = PrinterStall.Diagnose(null, 7, false, false);

        Assert.Equal(StallReason.NeedsAttention, reason);
        Assert.True(PrinterStall.ShouldKeepWaiting(reason));
    }

    [Fact]
    public void Service_Requested_Waits_Too()
    {
        Assert.True(PrinterStall.ShouldKeepWaiting(PrinterStall.Diagnose(null, 10, false, false)));
    }

    // ══════════ المفصولة — دي اللي مانستناهاش ══════════

    /// <summary>
    /// ⚠ ٩ (مفصولة) كان مصنّف «باب مفتوح» → الجوب كان بيفضل مستني
    /// لحد **أربع ساعات** على مكنة مش موصولة أصلًا، والشغل معلّق عليها.
    /// </summary>
    [Fact]
    public void An_Offline_Printer_Is_Not_Waited_For()
    {
        var reason = PrinterStall.Diagnose(null, 9, false, jobsWaiting: true);

        Assert.Equal(StallReason.Unknown, reason);
        Assert.False(PrinterStall.ShouldKeepWaiting(reason));
    }

    // ══════════ نفس الأرقام في بوابة توزيع الشغل ══════════

    [Fact]
    public void An_Offline_Printer_Gets_No_New_Work()
    {
        var verdict = PrinterReady.Decide(
            workOffline: false, printerStatus: null, detectedErrorState: 9,
            paused: false, queuedJobs: 0);

        Assert.Equal(PrinterReadiness.Faulted, verdict.State);
    }

    /// <summary>
    /// الباب المفتوح لازم يتقال عنه إنه باب مفتوح — مش «مفصولة أو مطفية».
    /// الرسالة الغلط بتبعت اللي في المطبعة يفحص الكابل بدل ما يقفل الباب.
    /// </summary>
    [Fact]
    public void An_Open_Door_Is_Not_Reported_As_Disconnected()
    {
        var verdict = PrinterReady.Decide(
            workOffline: false, printerStatus: null, detectedErrorState: 7,
            paused: false, queuedJobs: 0);

        Assert.Equal(PrinterReadiness.Faulted, verdict.State);
        Assert.DoesNotContain("مفصولة", verdict.Reason ?? "");
    }

    /// <summary>
    /// ⚠ أخطر واحد: كان فيه فحص `printerStatus == 2 → عطل`، و٢ في
    /// جدول PrinterStatus معناها **«غير معروف»** مش «خطأ» (مفيش قيمة
    /// اسمها خطأ في الجدول ده أصلًا).
    ///
    /// يعني أي طابعة درايفرها مابيبلّغش حالتها كويس كانت بتتعزل وتتقال
    /// عليها معطلة وهي سليمة — مطبعة فيها ٥ مكن ممكن تشتغل بـ ٤ من غير
    /// ما حد يفهم ليه.
    /// </summary>
    [Fact]
    public void A_Printer_With_An_Unknown_Status_Still_Gets_Work()
    {
        var verdict = PrinterReady.Decide(
            workOffline: false, printerStatus: 2, detectedErrorState: null,
            paused: false, queuedJobs: 0);

        Assert.Equal(PrinterReadiness.Ready, verdict.State);
    }

    /// <summary>الحاجات الحقيقية لسه بتتمسك — الفحص اتشال مش الحماية.</summary>
    [Theory]
    [InlineData(4)]    // ورق خلص
    [InlineData(6)]    // حبر خلص
    [InlineData(7)]    // باب مفتوح
    [InlineData(9)]    // مفصولة
    public void Real_Faults_Still_Stop_New_Work(int code)
    {
        var verdict = PrinterReady.Decide(
            workOffline: false, printerStatus: null, detectedErrorState: code,
            paused: false, queuedJobs: 0);

        Assert.Equal(PrinterReadiness.Faulted, verdict.State);
    }
}
