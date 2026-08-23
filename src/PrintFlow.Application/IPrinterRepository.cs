using PrintFlow.Domain;

namespace PrintFlow.Application;

public interface IPrinterRepository
{
    Task<List<Printer>> GetPrintersAsync(CancellationToken cancellationToken = default);
    string SendTestPage(string printerName);
    PrinterCapabilities GetCapabilities(string printerName);
    string SendCopies(string printerName, int copies);
}