using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيصغّر أو يكبّر محتوى الصفحات، ومقاس الورقة زي ما هو.
///
/// نفس تقنية <see cref="PdfSlideComposer"/> بالظبط: ورقة جديدة بنفس المقاس،
/// والصفحة القديمة بتترسم جواها كـ <see cref="XPdfForm"/> في مستطيل محسوب.
/// الحساب نفسه في <see cref="PageScaling"/> — دالة خالصة متختبرة بأرقام.
///
/// ليه مرحلة لوحدها ومش جوه المُجمّع: التجميع بيغيّر مقاس الورقة واتجاهها
/// وبيحط هوامش بين الشرائح. المقياس بيعمل حاجة واحدة بس — بيغيّر حجم
/// المحتوى على نفس الورقة. لو خلطناهم، "٩٠٪" كانت هتبقى معناها مختلف حسب
/// إعدادات الشرائح، والمستخدم مش هيفهم النتيجة.
/// </summary>
public class PdfPageScaler : IPdfPageScaler
{
    public MergeResult Scale(ScaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.InputPath))
        {
            return MergeResult.Failed($"الملف مش موجود: {Path.GetFileName(request.InputPath)}");
        }

        try
        {
            // ١٠٠٪ = نسخ بالبايت. إعادة الرسم من غير داعي بتضيّع من جودة
            // الأصل، والمستخدم اللي ماغيّرش المقياس مايستاهلش يدفع التمن ده.
            if (request.IsPassThrough)
            {
                return PassThrough(request);
            }

            using var source = PdfReader.Open(request.InputPath, PdfDocumentOpenMode.Import);

            if (source.PageCount == 0)
            {
                return MergeResult.Failed($"الملف \"{Path.GetFileName(request.InputPath)}\" مفيهوش صفحات.");
            }

            using var output = new PdfDocument();

            // فورم واحد للمستند كله وبنغيّر رقم الصفحة بس — فتحه لكل صفحة
            // على مستند ٢١٠ صفحة كان معناه ٢١٠ فتحة.
            using var form = XPdfForm.FromFile(request.InputPath);

            for (int i = 0; i < source.PageCount; i++)
            {
                var sourcePage = source.Pages[i];

                double width = sourcePage.Width.Point;
                double height = sourcePage.Height.Point;

                var sheet = output.AddPage();
                sheet.Width = XUnit.FromPoint(width);
                sheet.Height = XUnit.FromPoint(height);

                using var gfx = XGraphics.FromPdfPage(sheet);

                var target = PageScaling.Place(width, height, request.Percent);

                form.PageNumber = i + 1;
                gfx.DrawImage(form, target.X, target.Y, target.Width, target.Height);
            }

            int pageCount = output.PageCount;
            output.Save(request.OutputPath);

            return MergeResult.Succeeded(
                $"المقياس {PageScaling.Clamp(request.Percent)}% على {pageCount} صفحة.",
                pageCount);
        }
        catch (Exception ex)
        {
            return MergeResult.Failed(
                $"مقدرناش نغيّر مقياس \"{Path.GetFileName(request.InputPath)}\": {ex.Message}");
        }
    }

    private static MergeResult PassThrough(ScaleRequest request)
    {
        int pageCount;

        // Import مش InformationOnly — التانية اتشالت من PdfSharp 6 (CS0618)،
        // واتقاس الاتنين وطلعوا نفس السرعة على مستند ٢١٠ صفحة.
        using (var document = PdfReader.Open(request.InputPath, PdfDocumentOpenMode.Import))
        {
            pageCount = document.PageCount;
        }

        if (!string.Equals(
                Path.GetFullPath(request.InputPath),
                Path.GetFullPath(request.OutputPath),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(request.InputPath, request.OutputPath, overwrite: true);
        }

        return MergeResult.Succeeded($"المقياس ١٠٠٪ — المستند عدّى زي ما هو ({pageCount} صفحة).", pageCount);
    }
}
