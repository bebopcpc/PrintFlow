using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// القرار اللي بيحمي الجوب من الإلغاء.
///
/// القاعدة اللي التستات دي بتحرسها: **الورق لما يخلص ده مش عطل.** في
/// المطبعة ده بيحصل كل شوية، وبتحط ورق والجوب بيكمّل. لو المهلة قتلت
/// الجوب في اللحظة دي، نص الملزمة بيطلع والباقي بيضيع — وده أسوأ من إن
/// الجوب يستنى ساعة.
/// </summary>
public class PrinterStallTests
{
    // ══════════ حاجات اليد بتحلها = استنى ══════════

    [Theory]
    [InlineData(3)]    // ورق قليل
    [InlineData(4)]    // ورق خلص
    [InlineData(8)]    // ورق اتزنق
    [InlineData(11)]   // درج الخروج مليان
    public void Paper_Problems_Never_Cancel_The_Job(int errorState)
    {
        var reason = PrinterStall.Diagnose(
            printerStatus: null, detectedErrorState: errorState, isPaused: false, jobsWaiting: false);

        Assert.Equal(StallReason.Paper, reason);
        Assert.True(PrinterStall.ShouldKeepWaiting(reason));
    }

    [Fact]
    public void Out_Of_Ink_Never_Cancels_The_Job()
    {
        var reason = PrinterStall.Diagnose(null, 6, false, false);

        Assert.Equal(StallReason.Ink, reason);
        Assert.True(PrinterStall.ShouldKeepWaiting(reason));
    }

    [Theory]
    [InlineData(7)]    // باب مفتوح
    [InlineData(10)]   // صيانة مطلوبة
    public void Doors_And_Service_Never_Cancel_The_Job(int errorState)
    {
        var reason = PrinterStall.Diagnose(null, errorState, false, false);

        Assert.Equal(StallReason.NeedsAttention, reason);
        Assert.True(PrinterStall.ShouldKeepWaiting(reason));
    }

    [Fact]
    public void A_Paused_Queue_Never_Cancels_The_Job()
    {
        Assert.Equal(StallReason.Paused, PrinterStall.Diagnose(null, null, isPaused: true, jobsWaiting: false));
        Assert.Equal(StallReason.Paused, PrinterStall.Diagnose(1, null, false, false));
        Assert.Equal(StallReason.Paused, PrinterStall.Diagnose(6, null, false, false));
    }

    [Theory]
    [InlineData(4)]    // بتطبع
    [InlineData(5)]    // بتسخّن
    public void A_Working_Printer_Never_Cancels_The_Job(int status)
    {
        var reason = PrinterStall.Diagnose(status, null, false, false);

        Assert.Equal(StallReason.StillPrinting, reason);
        Assert.True(PrinterStall.ShouldKeepWaiting(reason));
    }

    [Fact]
    public void Jobs_Sitting_In_The_Queue_Mean_Something_Is_Happening()
    {
        // مفيش عطل معروف بس في ورق مستني — يبقى فيه شغل ماشي
        var reason = PrinterStall.Diagnose(null, null, false, jobsWaiting: true);

        Assert.Equal(StallReason.StillPrinting, reason);
    }

    // ══════════ العطل الحقيقي = الغي ══════════

    [Fact]
    public void No_Known_Reason_Is_The_Only_Thing_That_Cancels()
    {
        var reason = PrinterStall.Diagnose(null, null, false, false);

        Assert.Equal(StallReason.Unknown, reason);
        Assert.False(PrinterStall.ShouldKeepWaiting(reason));
    }

    [Fact]
    public void An_Offline_Printer_With_Stuck_Jobs_Is_Not_Working()
    {
        // فخّ حقيقي: طابعة مفصولة وعندها جوبات عالقة من امبارح. لو
        // "في جوبات" لوحدها كانت كفاية، كنا هنفضل مستنيين طابعة ميتة
        // للأبد.
        var reason = PrinterStall.Diagnose(
            printerStatus: null, detectedErrorState: 9, isPaused: false, jobsWaiting: true);

        Assert.Equal(StallReason.Unknown, reason);
        Assert.False(PrinterStall.ShouldKeepWaiting(reason));
    }

    [Fact]
    public void Unreadable_Status_Behaves_Exactly_Like_Before_The_Feature()
    {
        // درايفر مش بيدعم الحقول دي → مجهول → نفس السلوك القديم بالظبط.
        // مهم: الميزة دي ماينفعش تخلي جوب يستنى للأبد على جهاز مابنعرفش
        // نقرا حالته.
        Assert.False(PrinterStall.ShouldKeepWaiting(PrinterStall.Diagnose(null, null, false, false)));
    }

    // ══════════ الأولويات ══════════

    [Fact]
    public void The_Real_Error_Beats_The_Printing_Status()
    {
        // الطابعة بتقول "بطبع" وفي نفس الوقت الورق خلص — الورق أدق
        var reason = PrinterStall.Diagnose(printerStatus: 4, detectedErrorState: 4, isPaused: false, jobsWaiting: true);

        Assert.Equal(StallReason.Paper, reason);
    }

    [Fact]
    public void The_Real_Error_Beats_The_Paused_Flag()
    {
        var reason = PrinterStall.Diagnose(null, 4, isPaused: true, jobsWaiting: false);

        Assert.Equal(StallReason.Paper, reason);
    }

    // ══════════ الرسايل ══════════

    [Fact]
    public void Every_Reason_Says_What_To_Do_About_It()
    {
        foreach (StallReason reason in Enum.GetValues<StallReason>())
        {
            string text = PrinterStall.Describe(reason);

            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Fact]
    public void The_Paper_Message_Says_It_Will_Carry_On_By_Itself()
    {
        // أهم جملة في الميزة كلها — اللي بيقرا اللوج لازم يعرف إنه
        // يحط ورق وخلاص، مش يعيد الجوب من الأول
        Assert.Contains("هيكمّل لوحده", PrinterStall.Describe(StallReason.Paper));
    }
}
