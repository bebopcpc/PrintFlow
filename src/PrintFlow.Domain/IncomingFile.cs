namespace PrintFlow.Domain;

/// <summary>
/// بيتابع ملف بيتكتب عليه دلوقتي، ويقرر إمتى يبقى خلص.
///
/// **ليه ده أهم حتة في الطابعة الوهمية:**
///
/// البورت ملف واحد بمسار ثابت. أول ما ويندوز يبدأ يكتب فيه، إحنا بنشوف
/// الملف موجود — بس هو لسه بيتكتب. لو خطفناه في اللحظة دي هنطبع نص ملزمة
/// ونفتكر إن الجوب خلص، والمستخدم مايعرفش غير لما يعدّ الورق.
///
/// والعكس خطر برضه: لو استنينا كتير، جوب جديد يبدأ يكتب فوق القديم
/// **قبل** ما نشيله، فالجوب الأول يضيع بالكامل.
///
/// الحل: بنراقب الحجم. طول ما بيزيد، الكتابة شغالة. لما يقف عند نفس
/// الرقم كذا مرة ورا بعض، يبقى خلص وناخده على طول.
///
/// حساب خالص على أرقام — متختبر من غير أي ملف ولا طابعة.
/// </summary>
public sealed record FileWatch(long LastSize, int StableTicks)
{
    /// <summary>لسه ماشفناش الملف. حجم سالب عشان أول قراءة (حتى صفر) تتحسب تغيير.</summary>
    public static FileWatch Start => new(-1, 0);

    /// <summary>
    /// قراءة جديدة للحجم. الحجم اتغيّر = الكتابة شغالة والعداد بيتصفّر.
    /// نفس الحجم = خطوة أقرب للجاهزية.
    /// </summary>
    public FileWatch Observe(long size)
        => size == LastSize ? this with { StableTicks = StableTicks + 1 } : new FileWatch(size, 0);

    /// <summary>
    /// الملف خلص كتابة؟
    ///
    /// الحجم لازم يكون أكبر من صفر: ويندوز بيعمل الملف فاضي الأول وبعدين
    /// يملاه، فملف صفر بايت ثابت معناه "لسه مابدأش" مش "خلص".
    /// </summary>
    public bool IsSettled(int ticksNeeded) => LastSize > 0 && StableTicks >= ticksNeeded;

    /// <summary>الملف موجود وبيتكتب فيه دلوقتي.</summary>
    public bool IsGrowing => LastSize >= 0 && StableTicks == 0;
}

/// <summary>
/// من فين جه الملف الوارد.
/// </summary>
public enum IncomingSource
{
    /// <summary>حد طبع على طابعة PrintFlow من أي برنامج.</summary>
    VirtualPrinter,

    /// <summary>حد رمى ملف في المجلد المراقَب.</summary>
    HotFolder
}

/// <summary>ملف وصل من بره البرنامج.</summary>
public sealed record IncomingFile(string Path, IncomingSource Source, long SizeBytes)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public string SourceLabel => Source switch
    {
        IncomingSource.VirtualPrinter => "طابعة PrintFlow",
        _ => "المجلد المراقَب"
    };
}

/// <summary>إعدادات الاستقبال — كام مرة نقرا الحجم وكل قد إيه.</summary>
public static class IncomingWatchPolicy
{
    /// <summary>كام قراءة متتالية بنفس الحجم قبل ما نعتبر الملف خلص.</summary>
    public const int StableTicksNeeded = 3;

    /// <summary>
    /// الفاصل بين القراءتين.
    ///
    /// ٤٠٠ مللي × ٣ قراءات ≈ ثانية ونص انتظار بعد آخر بايت. سريع كفاية إن
    /// اللي واقف على الماكينة مايحسّش بيه، وبطيء كفاية إن الطابعة الوهمية
    /// ماتخلصش الكتابة في نصهم على مستند تقيل.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// أقصى انتظار لملف واحد قبل ما نسيبه ونكتب في اللوج.
    ///
    /// موجود عشان ملف اتقفل عليه برنامج تاني مايوقّفش الاستقبال للأبد.
    /// </summary>
    public static readonly TimeSpan MaximumWait = TimeSpan.FromMinutes(10);
}
