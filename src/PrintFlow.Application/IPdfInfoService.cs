namespace PrintFlow.Application;

/// <summary>
/// بيقرا معلومات سريعة عن ملف PDF من غير ما يعالجه.
/// بنستخدمه عشان نعرض عدد صفحات كل ملف في القايمة قبل المعالجة.
/// </summary>
public interface IPdfInfoService
{
    /// <summary>بيرجّع عدد الصفحات، أو null لو الملف تالف أو محمي أو مش موجود.</summary>
    int? TryGetPageCount(string filePath);

    /// <summary>
    /// مقاس أول صفحة بالنقطة، أو null لو مقدرناش نقراه.
    ///
    /// المعاينة الحية بتستخدمه: تقسيم الورقة بيعتمد على شكل الصفحة الأصلية،
    /// فمن غيره المعاينة هتفترض A4 طولية وتوري المستخدم شكل غلط لو شغله
    /// شرايح بوربوينت عرضية.
    /// </summary>
    (double Width, double Height)? TryGetPageSize(string filePath);
}
