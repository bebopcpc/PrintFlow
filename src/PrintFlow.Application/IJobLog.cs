namespace PrintFlow.Application;

/// <summary>
/// سجل تشغيل بيتكتب على القرص.
///
/// ده أهم حاجة في التجربة الفعلية: من غيره الملاحظة اللي هترجعلك من المطبعة
/// هتبقى "البرنامج وقع مرة" من غير أي تفاصيل. مع اللوج بيبقى عندك الوقت
/// والملفات والطابعة والرسالة بالظبط.
/// </summary>
public interface IJobLog
{
    void Info(string message);

    void Error(string message, Exception? exception = null);

    /// <summary>مكان ملفات اللوج، عشان نعرضه للمستخدم يبعتهولنا.</summary>
    string LogFolder { get; }
}
