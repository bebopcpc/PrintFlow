namespace PrintFlow.Domain;

/// <summary>
/// المنطقة **المرئية** من الورقة بإحداثيات الرسم (الأصل فوق-شمال، Y نازلة).
///
/// ليه دي موجودة أصلًا: كتير من ملفات المطابع والكتب فيها MediaBox أكبر من
/// CropBox (هوامش قص / bleed). قارئ الـ PDF بيعرض الـ CropBox بس، فأي حاجة
/// بنرسمها على حافة الـ MediaBox بتقع **بره الجزء الظاهر** وتختفي تمامًا.
///
/// ده بالظبط اللي كان بيحصل في التجربة: العلامة المائية بتبان (لأنها في نص
/// الصفحة) والترقيم بيختفي (لأنه على الحافة).
///
/// الحساب متعمد يكون هنا في الـ Domain مش جوه PdfMergeService: كده هو دالة
/// خالصة على أرقام، تتختبر لوحدها من غير ما نحتاج نبني ملف PDF ونرندره.
/// </summary>
public readonly record struct VisiblePageArea(double X, double Y, double Width, double Height)
{
    /// <param name="mediaX1">حافة الـ MediaBox الشمال.</param>
    /// <param name="mediaY2">حافة الـ MediaBox العليا (إحداثيات PDF بتعد من تحت لفوق).</param>
    /// <param name="cropX1">حافة الـ CropBox الشمال.</param>
    /// <param name="cropY2">حافة الـ CropBox العليا.</param>
    /// <param name="cropWidth">عرض الـ CropBox — صفر أو أقل معناها مفيش CropBox.</param>
    /// <param name="cropHeight">ارتفاع الـ CropBox.</param>
    /// <param name="pageWidth">عرض الورقة الكامل بالنقطة.</param>
    /// <param name="pageHeight">ارتفاع الورقة الكامل بالنقطة.</param>
    public static VisiblePageArea Calculate(
        double mediaX1,
        double mediaY2,
        double cropX1,
        double cropY2,
        double cropWidth,
        double cropHeight,
        double pageWidth,
        double pageHeight)
    {
        var whole = new VisiblePageArea(0, 0, pageWidth, pageHeight);

        // مفيش CropBox معرّف؟ المنطقة المرئية هي الورقة كلها
        if (cropWidth <= 0 || cropHeight <= 0)
        {
            return whole;
        }

        double x = cropX1 - mediaX1;

        // إحداثيات PDF بتعد من تحت لفوق، وإحداثيات الرسم من فوق لتحت — بنقلب
        double y = mediaY2 - cropY2;

        // أي حساب غريب (CropBox بره MediaBox مثلًا) → نرجع للورقة كاملة
        // بدل ما نرسم في مكان مجهول
        bool sane = x >= 0 && y >= 0 &&
                    x + cropWidth <= pageWidth + 1 &&
                    y + cropHeight <= pageHeight + 1;

        return sane ? new VisiblePageArea(x, y, cropWidth, cropHeight) : whole;
    }
}
