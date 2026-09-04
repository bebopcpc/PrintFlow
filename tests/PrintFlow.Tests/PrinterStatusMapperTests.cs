using PrintFlow.Domain;
using PrintFlow.Infrastructure;
using Xunit;

namespace PrintFlow.Tests;

public class PrinterStatusMapperTests
{
    [Fact]
    public void Offline_Flag_AlwaysReturnsOffline_RegardlessOfStatusCode()
    {
        var result = PrinterStatusMapper.Map(isOffline: true, printerStatusCode: 3);
        Assert.Equal(PrinterStatus.Offline, result);
    }

    [Fact]
    public void StatusCode3_WhenOnline_ReturnsReady()
    {
        var result = PrinterStatusMapper.Map(isOffline: false, printerStatusCode: 3);
        Assert.Equal(PrinterStatus.Ready, result);
    }

    /// <summary>
    /// ⚠ ده كان بيرجّع «خطأ»، والخطأ بيشيل المكنة من الطباعة خالص.
    ///
    /// بس ٢ في جدول WMI معناه **«غير معروف»** مش «عطلانة» — فكنا
    /// بنستبعد مكنة سليمة عشان الدرايفر بتاعها مابيبلّغش حالته.
    /// </summary>
    [Fact]
    public void StatusCode2_Means_Unknown_Not_Broken()
    {
        var result = PrinterStatusMapper.Map(isOffline: false, printerStatusCode: 2);
        Assert.Equal(PrinterStatus.Unknown, result);
    }

    [Fact]
    public void StatusCode7_WhenOnline_ReturnsOffline()
    {
        var result = PrinterStatusMapper.Map(isOffline: false, printerStatusCode: 7);
        Assert.Equal(PrinterStatus.Offline, result);
    }

    [Fact]
    public void NullStatusCode_WhenOnline_ReturnsUnknown()
    {
        var result = PrinterStatusMapper.Map(isOffline: false, printerStatusCode: null);
        Assert.Equal(PrinterStatus.Unknown, result);
    }

    [Fact]
    public void UnrecognizedStatusCode_ReturnsUnknown()
    {
        var result = PrinterStatusMapper.Map(isOffline: false, printerStatusCode: 999);
        Assert.Equal(PrinterStatus.Unknown, result);
    }

    // ══════════ الإيقاف اليدوي ══════════

    /// <summary>
    /// ⚠ الأرقام دي **مقاسة على جهاز حقيقي**، مش مفترضة.
    ///
    /// Microsoft Print to PDF وهي جاهزة: PrinterStatus ٣ · PrinterState ٠
    /// ونفسها بعد Pause():        PrinterStatus ١ · PrinterState ١
    ///
    /// الرقم ١ في جدول PrinterStatus معناه «غير ذلك» — عشان كده الشاشة
    /// كانت بتقول «غير معروف» على طابعة إحنا عارفين إنها موقوفة.
    /// </summary>
    [Fact]
    public void A_Paused_Queue_Is_Called_Paused_Not_Unknown()
    {
        var result = PrinterStatusMapper.Map(
            isOffline: false, printerStatusCode: 1, printerState: 1);

        Assert.Equal(PrinterStatus.Paused, result);
    }

    /// <summary>
    /// PrinterState فيه بتّات كتير جنب بعض. لازم نقرا البت بتاعنا بس،
    /// مش نقارن الرقم كله — الطابعة الموقوفة اللي فيها ورق زنق كمان
    /// بيبقى رقمها أكبر من ١.
    /// </summary>
    [Theory]
    [InlineData(1)]      // موقوفة بس
    [InlineData(3)]      // موقوفة + خطأ
    [InlineData(0x81)]   // موقوفة + بت أعلى
    public void The_Paused_Bit_Is_Read_On_Its_Own(int state)
    {
        var result = PrinterStatusMapper.Map(
            isOffline: false, printerStatusCode: 3, printerState: state);

        Assert.Equal(PrinterStatus.Paused, result);
    }

    /// <summary>الأرقام اللي البت بتاعنا مش فيها مالهاش دعوة بالإيقاف.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(0x80)]
    public void Other_State_Bits_Do_Not_Mean_Paused(int state)
    {
        var result = PrinterStatusMapper.Map(
            isOffline: false, printerStatusCode: 3, printerState: state);

        Assert.Equal(PrinterStatus.Ready, result);
    }

    /// <summary>
    /// المفصولة أهم من الموقوفة: دي محتاجة سلك ودي محتاجة ضغطة زرار.
    /// </summary>
    [Fact]
    public void Offline_Wins_Over_Paused()
    {
        var result = PrinterStatusMapper.Map(
            isOffline: true, printerStatusCode: 1, printerState: 1);

        Assert.Equal(PrinterStatus.Offline, result);
    }

    /// <summary>
    /// مقدرناش نقرا الحقل؟ نكمّل على الأرقام التانية بدل ما ندّعي
    /// إنها مش موقوفة أو ندّعي إنها موقوفة.
    /// </summary>
    [Fact]
    public void An_Unreadable_State_Falls_Back_To_The_Old_Table()
    {
        var result = PrinterStatusMapper.Map(
            isOffline: false, printerStatusCode: 3, printerState: null);

        Assert.Equal(PrinterStatus.Ready, result);
    }
}
