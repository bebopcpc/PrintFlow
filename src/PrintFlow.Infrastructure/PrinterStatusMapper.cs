using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

public static class PrinterStatusMapper
{
    /// <summary>
    /// PRINTER_STATUS_PAUSED — البت اللي ويندوز بيعلّم بيه على طابور
    /// موقوف يدويًا. نفس الرقم اللي <c>WmiPrinterHealth</c> و
    /// <c>PdfPrintService</c> بيقروه.
    /// </summary>
    private const int PausedBit = 0x00000001;

    /// <summary>
    /// بيحوّل أرقام WMI لحالة تتعرض جنب اسم الطابعة.
    ///
    /// ═══ ليه الإيقاف بيتقرا من حقل تاني خالص ═══
    ///
    /// جدول <c>Win32_Printer.PrinterStatus</c> هو:
    /// ١ غير ذلك · ٢ غير معروف · ٣ فاضية · ٤ بتطبع · ٥ بتسخّن ·
    /// ٦ وقفت الطباعة · ٧ مفصولة.
    ///
    /// **مفيش فيه رقم للإيقاف اليدوي.** الطابعة الموقوفة بترجّع
    /// «١ = غير ذلك»، فالكود القديم كان بيوقّعها على المجهول ويعرض
    /// «غير معروف» على طابعة إحنا عارفين بالظبط مالها.
    ///
    /// اتقاس على الجهاز:
    ///
    ///   جاهزة  → PrinterStatus ٣ · PrinterState ٠
    ///   موقوفة → PrinterStatus ١ · PrinterState ١
    ///
    /// الطباعة كانت بتقرا البت ده من الأول. الشاشة هي اللي كانت
    /// بتتفرّج على حقل تاني — والاتنين المفروض يقولوا نفس الحاجة.
    ///
    /// ⚠ **٢ مابقاش «خطأ».** كان متحوّل لخطأ، والخطأ بيشيل الطابعة من
    /// الطباعة خالص. لكن ٢ معناه «غير معروف» مش «عطلانة» — يعني كنا
    /// بنستبعد مكنة سليمة عشان الدرايفر بتاعها مابيبلّغش حالته. العطل
    /// الحقيقي بيتمسك في <c>WmiPrinterHealth</c> من
    /// <c>DetectedErrorState</c>، مش من هنا.
    /// </summary>
    /// <param name="printerState">
    /// <c>Win32_Printer.PrinterState</c>. null = مقدرناش نقراه، وساعتها
    /// بنكمّل على الأرقام التانية بدل ما ندّعي إنها مش موقوفة.
    /// </param>
    public static PrinterStatus Map(bool isOffline, int? printerStatusCode, int? printerState = null)
    {
        // المفصولة قبل الموقوفة: الاتنين مش هيطلّعوا ورق، بس المفصولة
        // محتاجة سلك والموقوفة محتاجة ضغطة زرار. الأهم يتقال الأول.
        if (isOffline)
        {
            return PrinterStatus.Offline;
        }

        if (printerState is int state && (state & PausedBit) != 0)
        {
            return PrinterStatus.Paused;
        }

        return printerStatusCode switch
        {
            3 => PrinterStatus.Ready,      // فاضية
            4 => PrinterStatus.Ready,      // بتطبع
            5 => PrinterStatus.Ready,      // بتسخّن
            7 => PrinterStatus.Offline,    // مفصولة
            _ => PrinterStatus.Unknown
        };
    }
}