using PdfSharp.Fonts;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيسجّل مصدر الخطوط لـ PdfSharp.
///
/// ليه كلاس لوحده: التسجيل كان جوه الـ static constructor بتاع PdfMergeService،
/// يعني مكان بيحصل بالصدفة أول ما حد يلمس الكلاس ده. لو أي كود عمل XFont قبلها
/// (معاينة، صفحة اختبار، أي حاجة)، PdfSharp بيرمي:
///     "No appropriate font found for family name '...'"
///
/// دلوقتي التسجيل بقى خطوة صريحة بتتنادى عند تشغيل البرنامج، والـ static
/// constructor فاضل كشبكة أمان لو حد نسي.
/// </summary>
public static class PdfFonts
{
    private static readonly Lock Gate = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            GlobalFontSettings.FontResolver ??= new AppFontResolver();
            _registered = true;
        }
    }
}
