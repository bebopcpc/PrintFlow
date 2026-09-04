using System.Drawing;
using System.Drawing.Printing;
using System.Management;
using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

public class PrinterService : IPrinterRepository
{
    public async Task<List<Printer>> GetPrintersAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            string defaultName = new PrinterSettings().PrinterName;
            var result = new List<Printer>();

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
                foreach (ManagementObject printer in searcher.Get())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string name = printer["Name"]?.ToString() ?? "Unknown";
                    bool isOffline = Convert.ToBoolean(printer["WorkOffline"] ?? false);
                    int? printerStatus = printer["PrinterStatus"] != null ? Convert.ToInt32(printer["PrinterStatus"]) : null;

                    // الحقل ده كان بيتسحب مع SELECT * ومحدش بيقراه — وهو
                    // اللي فيه بت الإيقاف اليدوي.
                    int? printerState = printer["PrinterState"] != null ? Convert.ToInt32(printer["PrinterState"]) : null;

                    result.Add(new Printer
                    {
                        Name = name,
                        IsDefault = name == defaultName,
                        Status = PrinterStatusMapper.Map(isOffline, printerStatus, printerState),
                        Port = printer["PortName"]?.ToString(),
                        DriverName = printer["DriverName"]?.ToString()
                    });
                }
            }
            catch (OperationCanceledException)
            {
                throw; 
            }
            catch (Exception)
            {
                foreach (string name in PrinterSettings.InstalledPrinters)
                {
                    result.Add(new Printer { Name = name, IsDefault = name == defaultName, Status = PrinterStatus.Unknown });
                }
            }

            return result;
        }, cancellationToken);
    }

    public string SendTestPage(string printerName)
    {
        try
        {
            using var document = new PrintDocument();
            document.PrinterSettings.PrinterName = printerName;

            if (!document.PrinterSettings.IsValid)
            {
                return $"[فشل] البرنتر '{printerName}' غير صالحة للطباعة حاليًا.";
            }

            document.PrintPage += (sender, e) =>
            {
                using var font = new Font("Arial", 14);
                string text = $"PrintFlow - Test Page\nPrinter: {printerName}\nTime: {DateTime.Now}";
                e.Graphics?.DrawString(text, font, Brushes.Black, new PointF(50, 50));
            };

            document.Print();
            return $"[نجاح] تم إرسال صفحة اختبار إلى '{printerName}'.";
        }
        catch (Exception ex)
        {
            return $"[فشل] لم يتم إرسال الصفحة إلى '{printerName}'. السبب: {ex.Message}";
        }
    }

    public PrinterCapabilities GetCapabilities(string printerName)
    {
        var settings = new PrinterSettings { PrinterName = printerName };

        if (!settings.IsValid)
        {
            return new PrinterCapabilities();
        }

        var paperSizes = new List<string>();
        foreach (PaperSize size in settings.PaperSizes)
        {
            paperSizes.Add(size.PaperName);
        }

        return new PrinterCapabilities
        {
            SupportsColor = settings.SupportsColor,
            SupportsDuplex = settings.CanDuplex,
            PaperSizes = paperSizes,
            DefaultPaperSize = settings.DefaultPageSettings?.PaperSize?.PaperName
        };
    }
}
