namespace PrintFlow.Application;

/// <summary>
/// بيقرا معلومات سريعة عن ملف PDF من غير ما يعالجه.
/// بنستخدمه عشان نعرض عدد صفحات كل ملف في القايمة قبل المعالجة.
/// </summary>
public interface IPdfInfoService
{
    /// <summary>بيرجّع عدد الصفحات، أو null لو الملف تالف أو محمي أو مش موجود.</summary>
    int? TryGetPageCount(string filePath);
}
