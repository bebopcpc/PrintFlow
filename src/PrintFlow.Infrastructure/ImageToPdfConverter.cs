using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيحوّل صورة لملف PDF من صفحة واحدة.
///
/// ملاحظة عن الجودة: PdfSharp بيحطّ ملف الـ JPEG **بالبايت زي ما هو** جوه
/// الـ PDF من غير ما يفكّه ويعيد ترميزه. يعني تحويل صورة JPEG مفيهوش أي
/// ضياع جودة، والحجم بيفضل قريب من الأصل. الصيغ التانية (PNG/BMP) بتتحوّل،
/// وده طبيعي لأن الـ PDF مابيعرفش يخزّنها بصيغتها.
/// </summary>
public class ImageToPdfConverter : IImageToPdfConverter
{
    static ImageToPdfConverter() => PdfFonts.Register();

    public MergeResult Convert(ImageConvertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.InputPath))
        {
            return MergeResult.Failed($"الصورة مش موجودة: {Path.GetFileName(request.InputPath)}");
        }

        try
        {
            using var image = XImage.FromFile(request.InputPath);

            if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
            {
                return MergeResult.Failed($"الصورة \"{Path.GetFileName(request.InputPath)}\" مقاسها صفر.");
            }

            var (sheetWidth, sheetHeight) = ImageConvertRequest.SheetFor(image.PixelWidth, image.PixelHeight);

            using var document = new PdfDocument();

            var page = document.AddPage();
            page.Width = XUnit.FromPoint(sheetWidth);
            page.Height = XUnit.FromPoint(sheetHeight);

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var target = ImageConvertRequest.PlaceOn(
                    sheetWidth, sheetHeight, image.PixelWidth, image.PixelHeight, request.Margin);

                gfx.DrawImage(image, target.X, target.Y, target.Width, target.Height);
            }

            document.Save(request.OutputPath);

            return MergeResult.Succeeded(
                $"اتحوّلت \"{Path.GetFileName(request.InputPath)}\" لصفحة PDF " +
                $"({(sheetWidth > sheetHeight ? "عرضية" : "طولية")}).",
                1);
        }
        catch (Exception ex)
        {
            return MergeResult.Failed(
                $"مقدرناش نحوّل \"{Path.GetFileName(request.InputPath)}\": {Explain(ex.Message)}");
        }
    }

    /// <summary>
    /// بيحوّل رسايل PdfSharp الإنجليزية لكلام مفيد.
    ///
    /// "Unsupported image format" بتطلع في حالتين مختلفتين تمامًا: صورة تالفة،
    /// أو صورة سليمة بس بصيغة داخلية مش مدعومة (أشهرها PNG بعمق ١٦ بت —
    /// اتجرب واتأكد إنه بيترفض، والـ ٨ بت بكل أنواعه بيعدّي). الرسالة
    /// الإنجليزية مابتفرّقش، فبنقول للمستخدم الحل اللي بينفع في الحالتين.
    /// </summary>
    private static string Explain(string message)
    {
        if (message.Contains("Unsupported image format", StringComparison.OrdinalIgnoreCase))
        {
            return "الصيغة الداخلية للصورة مش مدعومة (أو الملف تالف). " +
                   "افتحها في الرسام أو أي برنامج صور واحفظها JPEG، وهتشتغل.";
        }

        return message;
    }
}
