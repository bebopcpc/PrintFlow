using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيرسم أكتر من صفحة على ورقة واحدة.
///
/// كل الحسابات (اختيار الشبكة، ترتيب الخلايا، التوسيط) عايشة في
/// <see cref="SheetLayout"/> في الـ Domain ومتختبرة بأرقام لوحدها.
/// الكلاس ده مسؤول عن حاجة واحدة بس: ياخد الأرقام دي ويرسم بيها.
/// </summary>
public class PdfSlideComposer : IPdfSlideComposer
{
    static PdfSlideComposer() => PdfFonts.Register();

    public MergeResult Compose(SlideRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.InputPath))
        {
            return MergeResult.Failed($"الملف مش موجود: {Path.GetFileName(request.InputPath)}");
        }

        try
        {
            // شريحة واحدة = مفيش تجميع. بننسخ الملف زي ما هو بدل ما نعيد رسمه —
            // إعادة الرسم من غير داعي بتضيّع جودة الأصل من غير أي مقابل.
            if (request.IsPassThrough)
            {
                int passThroughPages = PageCountOf(request.InputPath);

                if (!string.Equals(
                        Path.GetFullPath(request.InputPath),
                        Path.GetFullPath(request.OutputPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(request.InputPath, request.OutputPath, overwrite: true);
                }

                return MergeResult.Succeeded(
                    $"شريحة واحدة على الورقة — المستند عدّى زي ما هو ({passThroughPages} صفحة).",
                    passThroughPages);
            }

            return request.Booklet ? ComposeBooklet(request) : ComposeSheets(request);
        }
        catch (Exception ex)
        {
            return MergeResult.Failed(
                $"مقدرناش نجمّع الشرائح لملف \"{Path.GetFileName(request.InputPath)}\": {ex.Message}");
        }
    }

    private static MergeResult ComposeSheets(SlideRequest request)
    {
        using var source = PdfReader.Open(request.InputPath, PdfDocumentOpenMode.Import);

        if (source.PageCount == 0)
        {
            return MergeResult.Failed($"الملف \"{Path.GetFileName(request.InputPath)}\" مفيهوش صفحات.");
        }

        // مقاس الورقة الناتجة بيتاخد من أول صفحة، وبيتدوّر حسب الاتجاه المطلوب
        var (sheetWidth, sheetHeight) = SheetSizeFor(source.Pages[0], request.SheetOrientation);

        // الشبكة بتتحدد مرة واحدة من أول صفحة وبتفضل ثابتة على المستند كله،
        // عشان الورق كله يطلع بنفس التقسيم حتى لو المقاسات جوه مختلفة
        var grid = SheetLayout.ChooseGrid(
            request.SlidesPerSheet,
            sheetWidth, sheetHeight,
            source.Pages[0].Width.Point, source.Pages[0].Height.Point,
            request.Margin);

        using var output = new PdfDocument();
        var borderPen = BorderPen(request);

        // XPdfForm واحد لكل المستند وبنغيّر PageNumber بس — فتح الملف من
        // الأول لكل شريحة كان معناه ٢١٠ فتحة على مستند ٢١٠ صفحة
        using var form = XPdfForm.FromFile(request.InputPath);

        for (int firstSlide = 0; firstSlide < source.PageCount; firstSlide += grid.Capacity)
        {
            var sheet = output.AddPage();
            sheet.Width = XUnit.FromPoint(sheetWidth);
            sheet.Height = XUnit.FromPoint(sheetHeight);

            using var gfx = XGraphics.FromPdfPage(sheet);

            for (int slot = 0; slot < grid.Capacity; slot++)
            {
                int pageIndex = firstSlide + slot;

                // آخر ورقة ممكن تبقى ناقصة — بنسيب باقي الخلايا فاضية
                if (pageIndex >= source.PageCount)
                {
                    break;
                }

                var sourcePage = source.Pages[pageIndex];

                var cell = SheetLayout.CellBoundsFor(
                    slot, grid, sheetWidth, sheetHeight, request.Margin, request.Order, request.Start);

                var target = SheetLayout.FitInto(
                    cell, sourcePage.Width.Point, sourcePage.Height.Point);

                form.PageNumber = pageIndex + 1;
                gfx.DrawImage(form, target.X, target.Y, target.Width, target.Height);

                // الإطار حوالين الصفحة نفسها مش حوالين الخلية — قرار المطبعة:
                // كده بيحدد المحتوى، ولو حوالين الخلية كان هيحوّط فراغ
                if (borderPen is not null)
                {
                    gfx.DrawRectangle(borderPen, target.X, target.Y, target.Width, target.Height);
                }
            }
        }

        int sheetCount = output.PageCount;
        output.Save(request.OutputPath);

        return MergeResult.Succeeded(
            $"{source.PageCount} صفحة على {sheetCount} ورقة ({grid.Rows}×{grid.Columns} لكل ورقة).",
            sheetCount);
    }

    /// <summary>
    /// الكتيّب: صفحتين على كل وجه، والورقة عرضية عشان تتطوى من النص.
    ///
    /// الفرق الوحيد عن التجميع العادي إن ترتيب الصفحات مش ١، ٢، ٣ — بيجي
    /// من <see cref="BookletImposition"/> اللي متختبر بمحاكاة الطي نفسها.
    /// الرسم نفسه هو هو.
    /// </summary>
    private static MergeResult ComposeBooklet(SlideRequest request)
    {
        using var source = PdfReader.Open(request.InputPath, PdfDocumentOpenMode.Import);

        if (source.PageCount == 0)
        {
            return MergeResult.Failed($"الملف \"{Path.GetFileName(request.InputPath)}\" مفيهوش صفحات.");
        }

        // الورقة عرضية دايمًا: صفحتين جنب بعض والطي في النص
        var (sheetWidth, sheetHeight) = SheetSizeFor(source.Pages[0], PageOrientation.Landscape);

        // صف واحد وعمودين — شكل الكتيّب الوحيد
        var grid = new SlideGrid(1, 2);

        // الترتيب هو اللي بيتحكم في اتجاه الكتيّب، فالتوزيع على الخانات ثابت:
        // العنصر الأول دايمًا على اليمين. لو خلّينا الاتجاه هنا كمان،
        // كان هيتقلب مرتين ويطلع الكتيّب معكوس.
        var pageOrder = BookletImposition.Order(source.PageCount, request.BookletStart);

        using var output = new PdfDocument();
        var borderPen = BorderPen(request);
        using var form = XPdfForm.FromFile(request.InputPath);

        for (int side = 0; side * 2 < pageOrder.Count; side++)
        {
            var sheet = output.AddPage();
            sheet.Width = XUnit.FromPoint(sheetWidth);
            sheet.Height = XUnit.FromPoint(sheetHeight);

            using var gfx = XGraphics.FromPdfPage(sheet);

            for (int slot = 0; slot < 2; slot++)
            {
                int pageNumber = pageOrder[(side * 2) + slot];

                // خانة فاضية — بتحصل لما عدد الصفحات مش من مضاعفات ٤
                if (pageNumber == BookletImposition.Blank)
                {
                    continue;
                }

                var sourcePage = source.Pages[pageNumber - 1];

                var cell = SheetLayout.CellBoundsFor(
                    slot, grid, sheetWidth, sheetHeight, request.Margin,
                    SlideOrder.Horizontal, SlideStart.Right);

                var target = SheetLayout.FitInto(cell, sourcePage.Width.Point, sourcePage.Height.Point);

                form.PageNumber = pageNumber;
                gfx.DrawImage(form, target.X, target.Y, target.Width, target.Height);

                if (borderPen is not null)
                {
                    gfx.DrawRectangle(borderPen, target.X, target.Y, target.Width, target.Height);
                }
            }
        }

        int sideCount = output.PageCount;
        output.Save(request.OutputPath);

        int sheets = BookletImposition.SheetCount(source.PageCount);
        int blanks = pageOrder.Count(p => p == BookletImposition.Blank);

        string note = blanks > 0 ? $" ({blanks} صفحة فاضية في الآخر)" : "";

        return MergeResult.Succeeded(
            $"كتيّب: {source.PageCount} صفحة على {sheets} ورقة بوش وضهر{note}. " +
            "اطبع على الوجهين واطوي من النص.",
            sideCount);
    }

    /// <summary>
    /// مقاس الورقة الناتجة: نفس مقاس الصفحة الأصلية، بس مدوّر للاتجاه المطلوب.
    /// يعني A4 طولية + اتجاه عرضي = A4 عرضية، مش مقاس تاني خالص.
    /// </summary>
    private static (double Width, double Height) SheetSizeFor(PdfPage page, PageOrientation orientation)
    {
        double width = page.Width.Point;
        double height = page.Height.Point;

        double longSide = Math.Max(width, height);
        double shortSide = Math.Min(width, height);

        return orientation == PageOrientation.Landscape
            ? (longSide, shortSide)
            : (shortSide, longSide);
    }

    private static XPen? BorderPen(SlideRequest request)
    {
        if (!request.DrawBorder)
        {
            return null;
        }

        var color = HexColor.ParseOrDefault(request.BorderColorHex, new RgbColor(128, 128, 128));

        return new XPen(XColor.FromArgb(color.R, color.G, color.B), 0.75);
    }

    private static int PageCountOf(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }
}
