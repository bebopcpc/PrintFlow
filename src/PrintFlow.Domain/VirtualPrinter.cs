namespace PrintFlow.Domain;

/// <summary>
/// أسامي ومسارات الطابعة الوهمية.
///
/// الفكرة اللي اتجربت واتأكدت على ويندوز حقيقي: بناخد درايفر **موجود أصلًا**
/// في ويندوز (Microsoft Print To PDF) ونربطه بـ Local Port اسمه مسار ملف.
/// النتيجة: أي برنامج بيطبع على "PrintFlow" بيطلّع PDF كامل في المسار ده
/// **من غير أي شاشة حفظ** — من غير درايفر نكتبه، ومن غير شهادة توقيع.
///
/// كل حاجة هنا نصوص ثابتة وحسابات — متختبرة من غير ويندوز.
/// </summary>
public static class VirtualPrinter
{
    /// <summary>الاسم اللي هيظهر في قايمة الطابعات.</summary>
    public const string PrinterName = "PrintFlow";

    /// <summary>درايفر مدمج في ويندوز — إحنا مابنسطّبش حاجة.</summary>
    public const string DriverName = "Microsoft Print To PDF";

    /// <summary>
    /// المجلد الأساسي في ProgramData مش في TEMP.
    ///
    /// ليه: خدمة الطباعة (Spooler) بتشتغل بحساب SYSTEM مش بحساب المستخدم،
    /// فمجلد TEMP بتاع المستخدم ممكن ماتكونش ليها صلاحية عليه أصلًا.
    /// ProgramData مشترك على الجهاز كله و SYSTEM ليها فيه صلاحية كاملة.
    /// </summary>
    public const string RootFolderName = "PrintFlow";

    /// <summary>اسم الملف اللي البورت بيكتب فيه.</summary>
    public const string PortFileName = "incoming.pdf";

    public static string RootFolder(string programData)
        => Path.Combine(programData, RootFolderName);

    /// <summary>المكان اللي ويندوز بيكتب فيه — ملف واحد ثابت.</summary>
    public static string SpoolFolder(string programData)
        => Path.Combine(RootFolder(programData), "spool");

    /// <summary>مسار البورت نفسه. ده اللي بيتسجّل في ويندوز كاسم بورت.</summary>
    public static string PortPath(string programData)
        => Path.Combine(SpoolFolder(programData), PortFileName);

    /// <summary>
    /// المكان اللي بننقل له الجوبات بعد ما تخلص كتابة.
    ///
    /// ليه بننقل أصلًا: البورت ملف **واحد بمسار ثابت**. طالما الجوب قاعد
    /// مكانه، أي جوب جديد هيكتب فوقه. فأول ما الملف يخلص، بنشيله من هناك
    /// فورًا باسم فريد، ونسيب المكان فاضي للجوب اللي بعده.
    /// </summary>
    public static string QueueFolder(string programData)
        => Path.Combine(RootFolder(programData), "queue");

    /// <summary>
    /// اسم فريد للجوب في الطابور: <c>job_20260824_145203_001.pdf</c>.
    ///
    /// التوقيت في الاسم عشان الترتيب يبان بالعين، والرقم عشان جوبين في
    /// نفس الثانية مايدهسوش بعض.
    /// </summary>
    public static string QueueNameFor(DateTime moment, int sequence)
        => $"job_{moment:yyyyMMdd_HHmmss}_{Math.Clamp(sequence, 0, 999):000}.pdf";

    /// <summary>
    /// أوامر PowerShell اللي بتنشئ الطابعة. بتترجّع كنص عشان الواجهة
    /// تقدر تعرضها للمستخدم قبل ما تشغّلها — محدش المفروض يشغّل أوامر
    /// إدارية على جهازه من غير ما يشوفها.
    /// </summary>
    public static IReadOnlyList<string> InstallCommands(string programData) =>
    [
        $"New-Item -ItemType Directory -Force -Path '{SpoolFolder(programData)}'",
        $"New-Item -ItemType Directory -Force -Path '{QueueFolder(programData)}'",
        $"Add-PrinterPort -Name '{PortPath(programData)}'",
        $"Add-Printer -Name '{PrinterName}' -DriverName '{DriverName}' -PortName '{PortPath(programData)}'"
    ];

    public static IReadOnlyList<string> UninstallCommands(string programData) =>
    [
        $"Remove-Printer -Name '{PrinterName}'",
        $"Remove-PrinterPort -Name '{PortPath(programData)}'"
    ];
}
