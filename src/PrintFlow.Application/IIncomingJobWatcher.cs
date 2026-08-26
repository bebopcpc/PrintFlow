using PrintFlow.Domain;

namespace PrintFlow.Application;

/// <summary>
/// بيراقب الملفات اللي بتوصل من بره البرنامج — من الطابعة الوهمية أو من
/// المجلد المراقَب — وبينادي لما يبقى في ملف جاهز.
/// </summary>
public interface IIncomingJobWatcher
{
    /// <summary>بيتنده لكل ملف خلص كتابة واتنقل للطابور بأمان.</summary>
    event Action<IncomingFile>? JobArrived;

    /// <summary>بيتنده لما يحصل حاجة تستاهل تتكتب في اللوج.</summary>
    event Action<string>? Reported;

    /// <summary>شغّال دلوقتي؟</summary>
    bool IsRunning { get; }

    /// <summary>
    /// يبدأ المراقبة. بيقرا كمان أي ملفات كانت مستنية من قبل ما البرنامج
    /// يفتح — الجوب اللي وصل والبرنامج مقفول ماينفعش يضيع.
    /// </summary>
    void Start(string spoolFolder, string queueFolder, string? hotFolder);

    void Stop();
}
