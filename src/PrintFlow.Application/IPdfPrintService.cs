namespace PrintFlow.Application;

public interface IPdfPrintService
{
    string PrintPdf(string pdfFilePath, string printerName, string paperSize, int copies);
}