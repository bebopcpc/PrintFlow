using PdfSharp.Fonts;

namespace PrintFlow.Infrastructure;

/// <summary>
/// PDFsharp 6+ بيحتاج مصدر خطوط صريح بدل الاعتماد التلقائي على ويندوز.
///
/// النسخة الأصلية كانت بتتجاهل اسم الخط المطلوب تمامًا وترجّع Arial دايمًا،
/// فاختيار Times New Roman كان بيدّي Arial من غير ما حد يعرف. دلوقتي كل خط
/// بيتحل لملفه الحقيقي من جدول WindowsFonts.
///
/// وبيشارك نفس الجدول مع WindowsFontCatalog اللي بيعرض القايمة للمستخدم،
/// فمستحيل الواجهة تعرض خط الـ Resolver مش عارف يجيبه.
/// </summary>
public class AppFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var font = WindowsFonts.Resolve(familyName);
        return new FontResolverInfo(isBold ? font.BoldFile : font.RegularFile);
    }

    public byte[] GetFont(string faceName)
    {
        string path = Path.Combine(WindowsFonts.FontsFolder, faceName + ".ttf");

        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }

        // الخط مش متسطّب على الجهاز ده — نرجع لـ Arial بدل ما الطباعة كلها تقع.
        // Arial بييجي مع كل نسخ ويندوز وفيه عربي ولاتيني.
        var fallback = WindowsFonts.Resolve(WindowsFonts.FallbackFamily);
        string fallbackPath = Path.Combine(WindowsFonts.FontsFolder, fallback.RegularFile + ".ttf");

        if (File.Exists(fallbackPath))
        {
            return File.ReadAllBytes(fallbackPath);
        }

        throw new FileNotFoundException(
            $"مالقيناش الخط '{faceName}' ولا الخط البديل Arial في {WindowsFonts.FontsFolder}.");
    }
}
