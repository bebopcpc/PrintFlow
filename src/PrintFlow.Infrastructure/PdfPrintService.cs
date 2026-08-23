using System.Diagnostics;
using PrintFlow.Application;

namespace PrintFlow.Infrastructure;

public class PdfPrintService : IPdfPrintService
{
    private static readonly string SumatraPath =
        Path.Combine(AppContext.BaseDirectory, "tools", "SumatraPDF.exe");

    public string PrintPdf(string pdfFilePath, string printerName, string paperSize, int copies, bool grayscale = false, bool duplex = false)
    {
        if (!File.Exists(SumatraPath))
        {
            return "[فشل] SumatraPDF.exe مش موجود في مجلد tools. تأكد إنك حطيته صح.";
        }

        if (!File.Exists(pdfFilePath))
        {
            return $"[فشل] الملف مش موجود: {pdfFilePath}";
        }

        try
        {
            var settingsParts = new List<string> { $"paper={paperSize}", "noscale" };

            if (grayscale)
            {
                settingsParts.Add("monochrome");
            }

            if (duplex)
            {
                settingsParts.Add("duplex");
            }

            string printSettings = string.Join(",", settingsParts);

            for (int i = 0; i < copies; i++)
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = SumatraPath,
                    Arguments = $"-print-to \"{printerName}\" -print-settings \"{printSettings}\" -silent \"{pdfFilePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                process?.WaitForExit(30000);
            }

            return $"[نجاح] تم إرسال {copies} نسخة إلى '{printerName}' بمقاس {paperSize}{(grayscale ? " (أبيض وأسود)" : "")}{(duplex ? " (وجهين)" : "")}.";
        }
        catch (Exception ex)
        {
            return $"[فشل] لم يتم الطباعة إلى '{printerName}'. السبب: {ex.Message}";
        }
    }
}