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

    public string MergeFiles(List<string> inputFilePaths, string outputPath, string? watermarkText = null, bool addPageNumbers = false)
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

            if (addPageNumbers)
            {
                AddPageNumbers(outputDocument);
            }

            if (!string.IsNullOrWhiteSpace(watermarkText))
            {
                AddWatermarkToAllPages(outputDocument, watermarkText);
            }

            outputDocument.Save(outputPath);

            string watermarkNote = string.IsNullOrWhiteSpace(watermarkText) ? "" : " مع علامة مائية";
            string numbersNote = addPageNumbers ? " مع ترقيم صفحات" : "";
            return $"[نجاح] تم دمج {inputFilePaths.Count} ملف{watermarkNote}{numbersNote} في: {outputPath}";
        }
        catch (Exception ex)
        {
            return $"[فشل] حصل خطأ أثناء الدمج: {ex.Message}";
        }
    }

    /// <summary>يكتب "صفحة X من Y" أسفل كل صفحة، على اليسار لتجنب التعارض مع محتوى المنتصف.</summary>
    private static void AddPageNumbers(PdfDocument document)
    {
        var font = new XFont("Arial", 10);
        var brush = XBrushes.Black;
        var format = new XStringFormat
        {
            Alignment = XStringAlignment.Near, // اصطفاف لليسار
            LineAlignment = XLineAlignment.Far // أسفل الصفحة
        };

        int totalPages = document.PageCount;

        for (int i = 0; i < totalPages; i++)
        {
            var page = document.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page);

            // عدلنا النص
            string rawText = $"صفحة {i + 1} من {totalPages}";
            string text = ArabicTextShaper.Reshape(rawText);
            
            // عملنا هامش 20 بيكسل من اليسار
            var rect = new XRect(20, page.Height.Point - 30, page.Width.Point - 40, 20);

            gfx.DrawString(text, font, brush, rect, format);
        }
    }

    private static void AddWatermarkToAllPages(PdfDocument document, string text)
    {
        // تشبيك الحروف العربية وترتيبها صح قبل الرسم (PDFsharp نفسه مش بيعمل ده تلقائيًا)
        string displayText = ArabicTextShaper.Reshape(text);
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

            gfx.DrawString(displayText, font, brush, new XRect(0, 0, page.Width, page.Height), format);
        }
    }
}