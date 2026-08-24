namespace PrintFlow.Domain;

public enum HorizontalAlign
{
    Left,
    Center,
    Right
}

public enum VerticalAlign
{
    Top,
    Bottom
}

/// <summary>
/// المستطيل اللي هيترسم جواه العنصر، بالنقطة (point)، بنظام إحداثيات الـ PDF:
/// الأصل فوق-شمال و Y بتزيد وإحنا نازلين.
/// </summary>
public readonly record struct PlacementBox(
    double X,
    double Y,
    double Width,
    double Height,
    HorizontalAlign Horizontal,
    VerticalAlign Vertical);

/// <summary>
/// بيحسب مكان رقم الصفحة أو النص المخصص على الورقة.
///
/// حسابات نقية من غير أي اعتماد على PdfSharp — عشان نقدر نتأكد إن "أسفل يمين"
/// بيطلع فعلًا أسفل يمين، من غير ما نولّد ملف PDF ونبصّ فيه بعنينا.
/// </summary>
public static class OverlayPlacement
{
    public static PlacementBox Calculate(
        ContentPosition position,
        double pageWidth,
        double pageHeight,
        double margin,
        double lineHeight)
    {
        // لو الهوامش أكبر من الصفحة نفسها، بنصغّرها بدل ما نطلع عرض بالسالب
        double safeMargin = Math.Max(0, Math.Min(margin, Math.Min(pageWidth, pageHeight) / 3));
        double width = Math.Max(1, pageWidth - (safeMargin * 2));
        double height = Math.Max(1, lineHeight);

        var vertical = position is ContentPosition.TopLeft or ContentPosition.TopCenter or ContentPosition.TopRight
            ? VerticalAlign.Top
            : VerticalAlign.Bottom;

        var horizontal = position switch
        {
            ContentPosition.TopLeft or ContentPosition.BottomLeft => HorizontalAlign.Left,
            ContentPosition.TopCenter or ContentPosition.BottomCenter => HorizontalAlign.Center,
            _ => HorizontalAlign.Right
        };

        double y = vertical == VerticalAlign.Top
            ? safeMargin
            : Math.Max(0, pageHeight - safeMargin - height);

        return new PlacementBox(safeMargin, y, width, height, horizontal, vertical);
    }
}
