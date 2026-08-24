using PdfSharp.Pdf.IO;
using PrintFlow.Application;

namespace PrintFlow.Infrastructure;

/// <summary>
/// قراءة عدد الصفحات. عمره ما يرمي استثناء — ملف تالف أو محمي بيرجّع null
/// والقايمة بتعرض الحجم بس بدل ما البرنامج يقف.
/// </summary>
public sealed class PdfInfoService : IPdfInfoService
{
    public int? TryGetPageCount(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            // Import مش Modify: مابنعدّلش حاجة، بنعد الصفحات وبس.
            //
            // كان هنا InformationOnly — الاسم كان مغري لأنه بيوحي بقراية أخف،
            // بس PdfSharp 6 معلّم عليه Obsolete وبيقول "مش متنفّذ، استخدم Import".
            // قِسنا الاتنين على ملف ٢١٠ صفحة: نفس النتيجة ونفس السرعة بالظبط
            // (~١٠ م.ث للملف)، فهو مجرد اسم تاني لنفس الحاجة.
            using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            return document.PageCount;
        }
        catch
        {
            return null;
        }
    }
}
