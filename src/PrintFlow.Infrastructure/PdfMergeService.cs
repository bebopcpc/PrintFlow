using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Fonts;
using PrintFlow.Application;

namespace PrintFlow.Infrastructure;

public class PdfMergeService : IPdfMergeService
{
    static PdfMergeService()
    {
        // نسجّل مصدر الخطوط مرة واحدة بس، أول ما الكلاس يتستخدم لأول مرة
        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new AppFontResolver();
        }
    }

    public string MergeFiles(List<string> inputFilePaths, string outputPath, string? watermarkText = null)
    {
        if (inputFilePaths == null || inputFilePaths.Count == 0)
        {
            return "[فشل] لازم ملف واحد على الأقل.";
        }
        try
        {
            using var outputDocument = new PdfDocument();
            foreach (var filePath in inputFilePaths)
            {
                if (!File.Exists(filePath))
                {
                    return $"[فشل] الملف مش موجود: {filePath}";
                }
                using var inputDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                foreach (var page in inputDocument.Pages)
                {
                    outputDocument.AddPage(page);
                }
            }
            if (!string.IsNullOrWhiteSpace(watermarkText))
            {
                AddWatermarkToAllPages(outputDocument, watermarkText);
            }
            outputDocument.Save(outputPath);
            string watermarkNote = string.IsNullOrWhiteSpace(watermarkText) ? "" : " مع علامة مائية";
            return $"[نجاح] تم دمج {inputFilePaths.Count} ملف{watermarkNote} في: {outputPath}";
        }
        catch (Exception ex)
        {
            return $"[فشل] حصل خطأ أثناء الدمج: {ex.Message}";
        }
    }

    private static void AddWatermarkToAllPages(PdfDocument document, string text)
    {
        var font = new XFont("Arial", 40, XFontStyleEx.Bold);
        var brush = new XSolidBrush(XColor.FromArgb(70, 128, 128, 128));
        var format = new XStringFormat
        {
            Alignment = XStringAlignment.Center,
            LineAlignment = XLineAlignment.Center
        };
        foreach (var page in document.Pages)
        {
            using var gfx = XGraphics.FromPdfPage(page);
            double centerX = page.Width / 2;
            double centerY = page.Height / 2;
            gfx.TranslateTransform(centerX, centerY);
            gfx.RotateTransform(-45);
            gfx.TranslateTransform(-centerX, -centerY);
            gfx.DrawString(text, font, brush, new XRect(0, 0, page.Width, page.Height), format);
        }
    }
}