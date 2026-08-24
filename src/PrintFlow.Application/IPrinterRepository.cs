using PrintFlow.Domain;

namespace PrintFlow.Application;

public interface IPrinterRepository
{
    Task<List<Printer>> GetPrintersAsync(CancellationToken cancellationToken = default);

    string SendTestPage(string printerName);

    PrinterCapabilities GetCapabilities(string printerName);

    // اتشالت SendCopies: كانت بتطبع صفحة نص فاضية مش ملف المستخدم، وكانت
    // فاضلة من التصميم القديم قبل ما الطباعة تعدّي على IPdfPrintService.
}