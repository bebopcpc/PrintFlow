using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيدمج ملفات PDF وبيحط عليها الترقيم والعلامة المائية والنص المخصص.
///
/// كل الأشكال بقت بتيجي من AppSettings عن طريق MergeRequest. قبل كده كانت
/// كل القيم مكتوبة في الكود (خط 40، رمادي، 45 درجة، الترقيم أسفل الشمال بحجم 10)
/// والإعدادات موجودة في الواجهة ومش واصلة لحتة هنا خالص.
/// </summary>
public class PdfMergeService : IPdfMergeService
{
    /// <summary>خط الترقيم والنص المخصص — Arial لأنه فيه حروف عربية ومتسطّب على أي ويندوز.</summary>
    private const string OverlayFontFamily = "Arial";

    static PdfMergeService()
    {
        // شبكة أمان: المفروض App.OnStartup هي اللي بتسجّل مصدر الخطوط،
        // بس لو حد استخدم الخدمة من غير ما يعدّي على التشغيل العادي (تست مثلًا)
        // مايبقاش عندنا XFont من غير resolver.
        PdfFonts.Register();
    }

    public MergeResult Merge(MergeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.InputFiles.Count == 0)
        {
            return MergeResult.Failed("لازم ملف واحد على الأقل.");
        }

        var warnings = new List<string>();

        try
        {
            using var output = new PdfDocument();

            // بنسجّل مدى صفحات كل ملف عشان نقدر نرقّم كل ملف من 1 لوحده
            var fileRanges = new List<PageRange>();

            foreach (string filePath in request.InputFiles)
            {
                if (!File.Exists(filePath))
                {
                    return MergeResult.Failed($"الملف مش موجود: {filePath}");
                }

                PdfDocument input;

                try
                {
                    input = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                }
                catch (Exception ex)
                {
                    // مهم: بنمسك الفشل **لكل ملف على حدة** عشان نقول اسم الملف اللي وقع.
                    // رسالة PdfSharp لوحدها إنجليزي تقني ومش بتذكر الملف، وده مش هيفيد
                    // حد في مطبعة محمّل 20 ملف.
                    return MergeResult.Failed(DescribeOpenFailure(filePath, ex));
                }

                using (input)
                {
                    int start = output.PageCount;

                    // الحذف بيتحسب **لكل ملف على حدة** — "1" معناها أول صفحة في
                    // كل ملف، مش أول صفحة في المستند المدموج. ده اللي مكتوب
                    // على الواجهة: "حذف صفحات من كل ملف".
                    var kept = PageRanges.Remaining(request.PagesToDelete, input.PageCount);

                    foreach (int pageNumber in kept)
                    {
                        output.AddPage(input.Pages[pageNumber - 1]);
                    }

                    if (kept.Count == 0 && input.PageCount > 0)
                    {
                        // الملف اتشال بالكامل. مانوقفش الشغل — يمكن يكون ده
                        // المقصود — بس لازم يتقال، عشان محدش يكتشف بعد الطباعة
                        // إن ملف اختفى في صمت.
                        warnings.Add($"الملف \"{Path.GetFileName(filePath)}\" اتشالت كل صفحاته");
                    }

                    fileRanges.Add(new PageRange(start, output.PageCount - start));
                }
            }

            if (output.PageCount == 0)
            {
                // PdfSharp مابيحفظش مستند من غير صفحات. بنقول السبب بالعربي
                // بدل ما نسيب استثناء تقني يطلع من جوه المكتبة.
                return MergeResult.Failed(
                    string.IsNullOrWhiteSpace(request.PagesToDelete)
                        ? "الملفات المحمّلة مفيهاش أي صفحات."
                        : $"حذف الصفحات \"{request.PagesToDelete}\" شال كل الصفحات — مفيش حاجة تتطبع. راجع الأرقام.");
            }

            ApplyOverlays(output, request, fileRanges, warnings);

            // لازم نقرا عدد الصفحات قبل الحفظ: PdfSharp بيقفل المستند بعد Save
            // وأي قراءة بعد كده بترمي "document was already saved".
            int pageCount = output.PageCount;

            output.Save(request.OutputPath);

            string summary = $"تم دمج {request.InputFiles.Count} ملف في {pageCount} صفحة" +
                             DescribeExtras(request) +
                             (warnings.Count > 0 ? $" — تنبيه: {string.Join("، ", warnings)}" : "");

            return MergeResult.Succeeded(summary, pageCount);
        }
        catch (Exception ex)
        {
            return MergeResult.Failed($"حصل خطأ أثناء الدمج: {ex.Message}");
        }
    }

    // ══════════ الرسم فوق الصفحات ══════════

    private static void ApplyOverlays(
        PdfDocument document,
        MergeRequest request,
        IReadOnlyList<PageRange> fileRanges,
        List<string> warnings)
    {
        if (request.PageNumbers is null && request.Watermark is null && request.CustomText is null)
        {
            return;
        }

        XImage? watermarkImage = null;
        XFont? watermarkFont = null;
        XBrush? watermarkBrush = null;

        if (request.Watermark is { } watermark)
        {
            if (watermark.IsImage)
            {
                if (File.Exists(watermark.ImagePath))
                {
                    watermarkImage = XImage.FromFile(watermark.ImagePath);
                }
                else
                {
                    warnings.Add("صورة العلامة المائية مش موجودة، فاتخطّت");
                }
            }
            else
            {
                watermarkFont = new XFont(
                    watermark.FontFamily,
                    watermark.FontSize,
                    watermark.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

                var color = HexColor.ParseOrDefault(watermark.ColorHex, new RgbColor(128, 128, 128));
                watermarkBrush = new XSolidBrush(XColor.FromArgb(watermark.Alpha, color.R, color.G, color.B));
            }
        }

        XFont? numberFont = request.PageNumbers is null
            ? null
            : new XFont(OverlayFontFamily, request.PageNumbers.FontSize);

        XBrush? numberBrush = request.PageNumbers is null
            ? null
            : SolidBrush(request.PageNumbers.ColorHex, HexColor.Black);

        XFont? customFont = request.CustomText is null
            ? null
            : new XFont(OverlayFontFamily, request.CustomText.FontSize);

        XBrush? customBrush = request.CustomText is null
            ? null
            : SolidBrush(request.CustomText.ColorHex, HexColor.Black);

        try
        {
            for (int i = 0; i < document.PageCount; i++)
            {
                var page = document.Pages[i];
                using var gfx = XGraphics.FromPdfPage(page);

                // العلامة المائية الأول عشان الترقيم والنص يفضلوا مقروءين فوقها
                if (request.Watermark is { } style)
                {
                    DrawWatermark(gfx, page, style, watermarkImage, watermarkFont, watermarkBrush);
                }

                if (request.PageNumbers is { } numbers)
                {
                    var (number, total) = ResolveNumbering(numbers, fileRanges, i, document.PageCount);
                    DrawText(
                        gfx, page,
                        $"صفحة {number} من {total}",
                        numbers.Position, numbers.EdgeMargin, numbers.FontSize,
                        numberFont!, numberBrush!,
                        numbers.Backdrop ? BackdropFor(numbers.ColorHex) : null);
                }

                if (request.CustomText is { } custom)
                {
                    DrawText(
                        gfx, page,
                        custom.Text,
                        custom.Position, custom.EdgeMargin, custom.FontSize,
                        customFont!, customBrush!,
                        backdrop: null);
                }
            }
        }
        finally
        {
            watermarkImage?.Dispose();
        }
    }

    private static void DrawWatermark(
        XGraphics gfx,
        PdfPage page,
        WatermarkStyle style,
        XImage? image,
        XFont? font,
        XBrush? brush)
    {
        if (style.IsImage ? image is null : font is null || brush is null)
        {
            return;
        }

        var visible = VisibleArea(page);
        double width = visible.Width;
        double height = visible.Height;
        double centerX = visible.X + (width / 2);
        double centerY = visible.Y + (height / 2);

        var state = gfx.Save();

        gfx.TranslateTransform(centerX, centerY);

        // بالسالب عشان الزاوية الموجبة تطلع مايلة لفوق ناحية اليمين،
        // وهو الشكل اللي الناس متعوّدة عليه في العلامة المائية.
        gfx.RotateTransform(-style.RotationDegrees);
        gfx.TranslateTransform(-centerX, -centerY);

        if (style.IsImage)
        {
            // بنخلي عرض الصورة نص عرض الصفحة ونحافظ على النسبة
            double targetWidth = width * 0.5;
            double targetHeight = image!.PixelHeight * (targetWidth / image.PixelWidth);

            gfx.DrawImage(image, centerX - (targetWidth / 2), centerY - (targetHeight / 2), targetWidth, targetHeight);
        }
        else
        {
            string text = ArabicTextShaper.Reshape(style.Text);
            var format = new XStringFormat
            {
                Alignment = XStringAlignment.Center,
                LineAlignment = XLineAlignment.Center
            };

            gfx.DrawString(text, font!, brush!, new XRect(visible.X, visible.Y, width, height), format);
        }

        gfx.Restore(state);
    }

    private static void DrawText(
        XGraphics gfx,
        PdfPage page,
        string rawText,
        ContentPosition position,
        int edgeMargin,
        int fontSize,
        XFont font,
        XBrush brush,
        XBrush? backdrop)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return;
        }

        var visible = VisibleArea(page);

        var box = OverlayPlacement.Calculate(
            position,
            visible.Width,
            visible.Height,
            edgeMargin,
            fontSize * 1.4);

        var format = new XStringFormat
        {
            Alignment = box.Horizontal switch
            {
                HorizontalAlign.Left => XStringAlignment.Near,
                HorizontalAlign.Center => XStringAlignment.Center,
                _ => XStringAlignment.Far
            },
            LineAlignment = XLineAlignment.Center
        };

        string shaped = ArabicTextShaper.Reshape(rawText);
        var target = new XRect(visible.X + box.X, visible.Y + box.Y, box.Width, box.Height);

        if (backdrop is not null)
        {
            DrawBackdrop(gfx, shaped, font, format, target, backdrop, fontSize);
        }

        gfx.DrawString(shaped, font, brush, target, format);
    }

    /// <summary>
    /// لوحة صغيرة ورا النص على قد عرضه بالظبط + هامش بسيط.
    ///
    /// بنقيس النص الأول عشان اللوحة ماتبقاش شريط على عرض الصفحة كلها،
    /// وبنحاذيها بنفس محاذاة النص (شمال/نص/يمين).
    /// </summary>
    private static void DrawBackdrop(
        XGraphics gfx,
        string shaped,
        XFont font,
        XStringFormat format,
        XRect target,
        XBrush backdrop,
        int fontSize)
    {
        var size = gfx.MeasureString(shaped, font);

        double padX = fontSize * 0.45;
        double padY = fontSize * 0.22;
        double width = Math.Min(size.Width + padX * 2, target.Width);
        double height = Math.Min(size.Height + padY * 2, target.Height + padY * 2);

        double x = format.Alignment switch
        {
            XStringAlignment.Near => target.X,
            XStringAlignment.Center => target.X + (target.Width - width) / 2,
            _ => target.X + target.Width - width
        };

        double y = target.Y + (target.Height - height) / 2;
        double radius = Math.Min(height / 2, fontSize * 0.5);

        gfx.DrawRoundedRectangle(backdrop, new XRect(x, y, width, height), new XSize(radius, radius));
    }

    /// <summary>
    /// لون اللوحة اتحسب من لون الرقم نفسه: رقم غامق → لوحة فاتحة، والعكس.
    /// شبه شفافة عشان ما تخفيش محتوى المستند اللي تحتها.
    /// </summary>
    private static XBrush BackdropFor(string numberColorHex)
    {
        var color = HexColor.ParseOrDefault(numberColorHex, HexColor.Black);

        return color.IsLight
            ? new XSolidBrush(XColor.FromArgb(190, 0, 0, 0))
            : new XSolidBrush(XColor.FromArgb(190, 255, 255, 255));
    }

    // ══════════ مساعدات ══════════

    private readonly record struct PageRange(int Start, int Count);

    /// <summary>
    /// بيحوّل صناديق صفحة PdfSharp لأرقام، والحساب نفسه في
    /// <see cref="VisiblePageArea"/> — دالة خالصة متختبرة لوحدها.
    /// </summary>
    private static VisiblePageArea VisibleArea(PdfPage page)
    {
        var media = page.MediaBox;
        var crop = page.CropBox;

        if (crop is null || media is null)
        {
            return new VisiblePageArea(0, 0, page.Width.Point, page.Height.Point);
        }

        return VisiblePageArea.Calculate(
            media.X1, media.Y2,
            crop.X1, crop.Y2, crop.Width, crop.Height,
            page.Width.Point, page.Height.Point);
    }

    /// <summary>
    /// بيحوّل فشل فتح ملف لرسالة عربية بتقول **اسم الملف** و**سبب مفهوم**.
    ///
    /// PdfSharp بيرمي رسايل زي "The StartXRef table could not be found" —
    /// صحيحة تقنيًا وملهاش أي معنى للي واقف على الماكينة.
    /// </summary>
    private static string DescribeOpenFailure(string filePath, Exception exception)
    {
        string name = Path.GetFileName(filePath);
        string reason = exception.Message;

        if (reason.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return $"الملف \"{name}\" محمي بكلمة مرور. شيل الحماية أو استبعده من القايمة.";
        }

        if (reason.Contains("not a valid PDF", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("StartXRef", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("cannot be opened", StringComparison.OrdinalIgnoreCase))
        {
            return $"الملف \"{name}\" تالف أو مش PDF سليم. جرّب تفتحه بقارئ PDF عادي عشان تتأكد.";
        }

        return $"مقدرناش نفتح الملف \"{name}\": {reason}";
    }

    /// <summary>
    /// بيرجّع رقم الصفحة وإجماليها.
    ///
    /// تلات حالات:
    ///   • ترقيم لكل ملف + دمج → كل ملف جوه المستند المدموج بيبدأ من ١
    ///   • ترقيم متصل + دمج → ١..إجمالي المستند
    ///   • من غير دمج → FirstPageNumber و TotalPages بييجوا من بره،
    ///     عشان الملفات المنفصلة تفضل مترقّمة ورا بعض
    /// </summary>
    private static (int Number, int Total) ResolveNumbering(
        PageNumberStyle style,
        IReadOnlyList<PageRange> ranges,
        int pageIndex,
        int documentPageCount)
    {
        // صفر معناها "استخدم عدد صفحات المستند ده" — السلوك الطبيعي في الدمج
        int total = style.TotalPages > 0 ? style.TotalPages : documentPageCount;

        if (!style.RestartForEachFile)
        {
            return (style.FirstPageNumber + pageIndex, total);
        }

        foreach (var range in ranges)
        {
            if (pageIndex >= range.Start && pageIndex < range.Start + range.Count)
            {
                return (pageIndex - range.Start + 1, range.Count);
            }
        }

        return (style.FirstPageNumber + pageIndex, total);
    }

    private static XBrush SolidBrush(string hex, RgbColor fallback)
    {
        var color = HexColor.ParseOrDefault(hex, fallback);
        return new XSolidBrush(XColor.FromArgb(color.R, color.G, color.B));
    }

    private static string DescribeExtras(MergeRequest request)
    {
        var extras = new List<string>();

        if (request.PageNumbers is not null)
        {
            extras.Add("ترقيم");
        }

        if (request.Watermark is not null)
        {
            extras.Add(request.Watermark.IsImage ? "علامة مائية (صورة)" : "علامة مائية");
        }

        if (request.CustomText is not null)
        {
            extras.Add("نص مخصص");
        }

        if (!string.IsNullOrWhiteSpace(request.PagesToDelete))
        {
            extras.Add($"حذف الصفحات ({request.PagesToDelete})");
        }

        return extras.Count == 0 ? "" : $" مع {string.Join(" و", extras)}";
    }
}
