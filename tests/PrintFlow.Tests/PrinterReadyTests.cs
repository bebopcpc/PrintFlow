using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// "أدّي المكنة دي قطعة جديدة ولا لأ؟"
///
/// الفكرة اللي التستات دي بتحرسها إن فيه **تلات** إجابات مش اتنين:
/// آه، لأ دلوقتي (مشغولة)، لأ فيها عطل. خلط المشغولة بالواقفة بيكسر
/// التوزيع في الاتجاهين:
///
///   • لو المشغولة اتحسبت عطل → بنشيل شغلها ونديه لغيرها وهي كانت
///     هتخلصه، والمكن بتفضل تتبادل الشغل على الفاضي.
///   • لو الواقفة اتحسبت عادية → بنكوّم عليها شغل وهي مش بتطبع، وده
///     بالظبط الباج اللي المطبعة اشتكت منه.
/// </summary>
public class PrinterReadyTests
{
    // ═══ أكواد ويندوز اللي بنقراها ═══
    //
    // ⚠ الأرقام دي كانت مزحلقة، والتستات كانت بتحرس الغلط. اتصلّحت من
    // توثيق مايكروسوفت. لاحظ إن الرقم ٧ معناه حاجتين مختلفتين حسب
    // الحقل — وده كان مصدر اللخبطة كلها.
    //
    // PrinterStatus: ١ غير ذلك · ٢ غير معروف · ٣ فاضية · ٤ بتطبع
    //                ٥ بتسخّن · ٦ واقفة · ٧ مفصولة
    private const int Idle = 3;
    private const int Printing = 4;
    private const int UnknownStatus = 2;
    private const int OfflineStatus = 7;

    // DetectedErrorState: ٣ ورق قليل · ٤ ورق خلص · ٥ حبر قليل · ٦ حبر خلص
    //                     ٧ باب مفتوح · ٨ ورق مزنوق · ٩ مفصولة
    //                     ١٠ صيانة · ١١ درج الخارج مليان
    private const int NoPaper = 4;
    private const int PaperJam = 8;
    private const int NoToner = 6;
    private const int DoorOpen = 7;
    private const int OfflineError = 9;

    private static PrinterVerdict Ask(
        bool offline = false,
        int? status = Idle,
        int? error = null,
        bool paused = false,
        int? queued = 0)
        => PrinterReady.Decide(offline, status, error, paused, queued);

    // ══════════ الأعطال ══════════

    [Fact]
    public void A_Machine_Marked_Offline_Is_A_Fault()
    {
        Assert.Equal(PrinterReadiness.Faulted, Ask(offline: true).State);
    }

    [Fact]
    public void Offline_In_The_Status_Code_Counts_Too()
    {
        Assert.Equal(PrinterReadiness.Faulted, Ask(status: OfflineStatus).State);
    }

    [Fact]
    public void Offline_In_The_Error_Code_Counts_Too()
    {
        // نفس العطل بيتبلّغ من تلات حقول مختلفة حسب الدرايفر. لو
        // فحصنا واحد بس، نص الأجهزة مش هتتمسك.
        Assert.Equal(PrinterReadiness.Faulted, Ask(error: OfflineError).State);
    }

    /// <summary>
    /// ⚠ التست ده كان اسمه An_Error_Status_Is_A_Fault وكان بيتأكد إن
    /// PrinterStatus = 2 معناه عطل.
    ///
    /// **ومفيش قيمة اسمها «خطأ» في PrinterStatus أصلًا** — الرقم ٢
    /// معناه «غير معروف». يعني الفحص كان بيعزل أي طابعة درايفرها
    /// مابيبلّغش حالتها كويس ويقول عليها معطلة وهي سليمة: مطبعة فيها
    /// ٥ مكن ممكن تشتغل بـ ٤ من غير ما حد يفهم ليه.
    ///
    /// اتقلب لعكسه: الحالة المجهولة **مش** عطل. الأعطال الحقيقية
    /// بتيجي من DetectedErrorState، وهي متغطية في التستات التانية.
    /// </summary>
    [Fact]
    public void An_Unknown_Status_Is_Not_A_Fault()
    {
        Assert.Equal(PrinterReadiness.Ready, Ask(status: UnknownStatus).State);
    }

    [Fact]
    public void Paper_Running_Out_Stops_New_Work()
    {
        // ═══ الحالة اللي الميزة اتعملت عشانها ═══
        //
        // على ويندوز، الطابعة اللي الورق خلص منها **بتفضل تقبل جوبات**
        // وتكوّمها في طابورها. من غير الفحص ده، الموزّع بيفضل يرمي
        // عليها شغل وهي واقفة، والمكن التانية بتخلص وتقف.
        var verdict = Ask(error: NoPaper);

        Assert.Equal(PrinterReadiness.Faulted, verdict.State);
        Assert.Contains("ورق", verdict.Reason!);
    }

    [Fact]
    public void A_Paper_Jam_Stops_New_Work()
    {
        Assert.Equal(PrinterReadiness.Faulted, Ask(error: PaperJam).State);
    }

    [Fact]
    public void Ink_Running_Out_Stops_New_Work()
    {
        Assert.Equal(PrinterReadiness.Faulted, Ask(error: NoToner).State);
    }

    [Fact]
    public void An_Open_Door_Stops_New_Work()
    {
        Assert.Equal(PrinterReadiness.Faulted, Ask(error: DoorOpen).State);
    }

    [Fact]
    public void A_Paused_Queue_Stops_New_Work()
    {
        Assert.Equal(PrinterReadiness.Faulted, Ask(paused: true).State);
    }

    [Fact]
    public void Every_Fault_Comes_With_A_Reason_Someone_Can_Act_On()
    {
        // "المكنة واقفة" مابيساعدش حد. "الورق خلص" بيخلي اللي في
        // المطبعة يعرف يعمل إيه من غير ما يفتح اللوج.
        foreach (var verdict in new[]
        {
            Ask(error: NoPaper),
            Ask(error: NoToner),
            Ask(paused: true),
            Ask(offline: true),
            Ask(error: DoorOpen)
        })
        {
            Assert.Equal(PrinterReadiness.Faulted, verdict.State);
            Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));
        }
    }

    // ══════════ الكابح ══════════

    [Fact]
    public void A_Machine_With_A_Backlog_Is_Busy_Not_Broken()
    {
        var verdict = Ask(queued: 5);

        Assert.Equal(PrinterReadiness.Busy, verdict.State);
    }

    [Fact]
    public void One_Job_Waiting_Still_Leaves_Room_For_The_Next()
    {
        // ═══ ليه واحد مش صفر ═══
        //
        // لو استنينا الطابور يفضى تمامًا، الطابعة بتقف ثواني بين قطعة
        // والتانية وهي مستنية الجوب الجديد يوصل. جوب واحد وراها بيسد
        // الفجوة دي.
        Assert.Equal(PrinterReadiness.Ready, Ask(queued: 1).State);
        Assert.Equal(PrinterReadiness.Ready, Ask(queued: 0).State);
        Assert.Equal(PrinterReadiness.Busy, Ask(queued: 2).State);
    }

    [Fact]
    public void A_Machine_That_Is_Printing_Can_Still_Take_The_Next_Piece()
    {
        // "بتطبع" مش عطل ومش زحمة. دي مكنة شغالة صح.
        Assert.Equal(PrinterReadiness.Ready, Ask(status: Printing, queued: 1).State);
    }

    [Fact]
    public void A_Fault_Wins_Over_A_Full_Queue()
    {
        // مكنة الورق خلص منها وعندها طابور طويل = **عطل**، مش "مشغولة".
        // لو قلنا مشغولة، الموزّع هيستنى للأبد ومش هيقول لحد إن فيه
        // مشكلة محتاجة يد.
        var verdict = Ask(error: NoPaper, queued: 9);

        Assert.Equal(PrinterReadiness.Faulted, verdict.State);
    }

    [Fact]
    public void A_Queue_We_Could_Not_Read_Never_Blocks_The_Work()
    {
        // مبدأ ثابت في المشروع: منع الطباعة بسبب **فحص** فشل أوحش من
        // العطل اللي الفحص موجود عشانه.
        Assert.Equal(PrinterReadiness.Ready, Ask(queued: null).State);
    }

    [Fact]
    public void An_Unknown_Machine_Is_Given_The_Benefit_Of_The_Doubt()
    {
        // درايفر مش بيدعم الحقول دي = كل حاجة null. البرنامج لازم
        // يشتغل عليه زي ما كان قبل الميزة بالظبط.
        Assert.Equal(
            PrinterReadiness.Ready,
            PrinterReady.Decide(false, null, null, false, null).State);
    }

    [Fact]
    public void A_Healthy_Idle_Machine_Is_Ready()
    {
        var verdict = Ask();

        Assert.Equal(PrinterReadiness.Ready, verdict.State);
        Assert.Null(verdict.Reason);
    }
}
