using System.Text.RegularExpressions;
using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// بيمنع تصادم أسامي أنواع الـ Domain مع أنواع .NET والمكتبات.
///
/// ═══ ليه الملف ده موجود ═══
///
/// في نسخة 1.8.0 سمّيت نوع في الـ Domain باسم <c>PrintDocument</c>. الاسم ده
/// موجود في <c>System.Drawing.Printing</c>، و<c>PrinterService.cs</c> بيعمل
/// <c>using</c> للاتنين. النتيجة:
///
///   error CS0104: 'PrintDocument' is an ambiguous reference
///
/// والبرنامج مارضيش يفتح خالص عند المستخدم.
///
/// **وليه ماصدتهاش؟** مشروع Infrastructure بيستهدف <c>net10.0-windows</c>
/// وماينفعش يتبني على لينكس، فكل البيلدات اللي بتتعمل هنا بتغطّي Domain
/// و Application و Presentation بس. الملف اللي فيه التصادم مابيتبنيش أصلًا.
/// الفجوة كانت في الأدوات مش في الانتباه.
///
/// ═══ إزاي بيشتغل ═══
///
/// بيقرا سطور الـ <c>using</c> من ملفات Infrastructure الفعلية، وبيقارن
/// أسامي أنواع الـ Domain **بس** بالمساحات اللي اتستوردت فعلًا.
///
/// التفصيلة دي مهمة: <c>PageOrientation</c> موجودة في الـ Domain وموجودة
/// في <c>PdfSharp</c> — بس Infrastructure بيستورد <c>PdfSharp.Drawing</c>
/// و<c>PdfSharp.Pdf</c> مش <c>PdfSharp</c> نفسها، فمفيش أي التباس. حارس
/// بينبّه على الحالة دي بيبقى بيصرخ من غير سبب، وأول ما حد يزهق منه هيقفله
/// — وساعتها مش هيصيد الحالة الحقيقية لما تيجي.
/// </summary>
public class DomainNameCollisionTests
{
    /// <summary>
    /// أنواع معروفة، مرتّبة تحت المساحة بتاعتها.
    ///
    /// القايمة مش شاملة كل حاجة — دي الأنواع اللي اسمها ممكن يخطر على بال
    /// حد وهو بيسمّي نوع في برنامج طباعة.
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownTypes = new(StringComparer.Ordinal)
    {
        ["System.Drawing.Printing"] =
        [
            "PrintDocument", "PrinterSettings", "PageSettings", "PrintController",
            "PrintRange", "PrinterResolution", "PaperSize", "PaperSource", "Margins",
            "PrintEventArgs", "PrintPageEventArgs", "QueryPageSettingsEventArgs",
            "PreviewPrintController", "StandardPrintController", "Duplex",
            "PrinterUnit", "InvalidPrinterException", "PrintingPermission"
        ],
        ["System.Drawing"] =
        [
            "Image", "Bitmap", "Color", "Font", "Brush", "Pen", "Point", "PointF",
            "Size", "SizeF", "Rectangle", "RectangleF", "Graphics", "Icon",
            "Region", "StringFormat", "FontStyle", "ContentAlignment"
        ],
        ["System.Management"] =
        [
            "ManagementObject", "ManagementScope", "ManagementPath", "ObjectQuery",
            "ManagementObjectSearcher", "ManagementClass", "ManagementBaseObject"
        ],
        ["System.Diagnostics"] =
        [
            "Process", "ProcessStartInfo", "Stopwatch", "Activity", "Debug", "Trace",
            "EventLog", "FileVersionInfo"
        ],
        ["PdfSharp"] =
        [
            "PageOrientation", "PageSize", "PdfFontEmbedding", "PdfFontEncoding"
        ],
        ["PdfSharp.Pdf"] =
        [
            "PdfDocument", "PdfPage", "PdfItem", "PdfDictionary", "PdfArray",
            "PdfObject", "PdfName", "PdfString", "PdfOutline", "PdfCustomValues",
            "PdfFlateEncodeMode", "PdfDocumentOptions", "PdfDocumentSettings"
        ],
        ["PdfSharp.Pdf.IO"] =
        [
            "PdfReader", "PdfWriter", "PdfDocumentOpenMode", "PdfReaderOptions"
        ],
        ["PdfSharp.Drawing"] =
        [
            "XGraphics", "XImage", "XFont", "XBrush", "XPen", "XRect", "XUnit",
            "XPdfForm", "XColor", "XPoint", "XSize", "XStringFormat", "XSolidBrush"
        ],
        // دي بتتحط في المدى لوحدها بسبب ImplicitUsings في كل المشاريع
        ["*implicit*"] =
        [
            "Path", "File", "Directory", "FileInfo", "DirectoryInfo", "Stream",
            "FileStream", "FileMode", "FileAccess", "FileAttributes", "SearchOption",
            "Console", "Environment", "Math", "Convert", "Random", "Version",
            "Type", "Array", "Tuple", "Index", "Range", "Buffer", "Task", "Timer",
            "Exception", "Action", "Func", "Comparer", "EqualityComparer"
        ]
    };

    [Fact]
    public void No_Domain_Type_Clashes_With_Anything_Infrastructure_Imports()
    {
        var imported = NamespacesInfrastructureImports();

        // اللي بيتحط في المدى لوحده دايمًا موجود
        var risky = new HashSet<string>(KnownTypes["*implicit*"], StringComparer.Ordinal);

        foreach (var (space, names) in KnownTypes)
        {
            if (space != "*implicit*" && imported.Contains(space))
            {
                risky.UnionWith(names);
            }
        }

        var clashes = PublicDomainTypes()
            .Select(t => t.Name)
            .Where(risky.Contains)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            clashes.Count == 0,
            $"أنواع في الـ Domain بأسامي موجودة في مساحات Infrastructure بيستوردها: " +
            $"{string.Join("، ", clashes)}. ده بيطلّع CS0104، و Infrastructure هو المشروع " +
            "الوحيد اللي مابيتبنيش هنا فمش هنشوف الخطأ غير على ويندوز. غيّر الاسم.");
    }

    [Fact]
    public void A_Name_Is_Only_Risky_When_Its_Namespace_Is_Actually_Imported()
    {
        // PageOrientation موجودة في الـ Domain وفي PdfSharp — بس Infrastructure
        // بيستورد PdfSharp.Drawing و PdfSharp.Pdf مش PdfSharp نفسها.
        // فمفيش التباس، والحارس ماينفعش ينبّه عليها.
        //
        // ده اللي بيفرّق بين حارس بيتصدّق وحارس بيتقفل بعد أول إنذار كاذب.
        var imported = NamespacesInfrastructureImports();

        Assert.DoesNotContain("PdfSharp", imported);
        Assert.Contains("PdfSharp.Pdf", imported);
        Assert.Contains("System.Drawing.Printing", imported);
    }

    [Fact]
    public void The_Guard_Knows_About_The_Name_That_Actually_Broke_The_Build()
    {
        Assert.Contains("PrintDocument", KnownTypes["System.Drawing.Printing"]);
    }

    [Fact]
    public void The_Guard_Can_See_The_Infrastructure_Source()
    {
        // لو الحارس مالقاش الملفات، هيطلع بقايمة فاضية ويبان إنه عدّى
        // وهو مافحصش حاجة
        Assert.NotEmpty(InfrastructureFiles());
    }

    [Fact]
    public void The_Guard_Can_See_The_Domain_Types()
    {
        Assert.True(PublicDomainTypes().Count > 20);
    }

    [Fact]
    public void Domain_Type_Names_Are_Unique_Among_Themselves()
    {
        var duplicates = PublicDomainTypes()
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    // ══════════ مساعدات ══════════

    private static HashSet<string> NamespacesInfrastructureImports()
    {
        var spaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (string file in InfrastructureFiles())
        {
            foreach (Match match in Regex.Matches(
                File.ReadAllText(file), @"^\s*using\s+([A-Za-z_][\w.]*)\s*;", RegexOptions.Multiline))
            {
                spaces.Add(match.Groups[1].Value);
            }
        }

        return spaces;
    }

    private static List<string> InfrastructureFiles()
    {
        string? folder = FindInfrastructureFolder();

        return folder is null
            ? []
            : Directory.GetFiles(folder, "*.cs", SearchOption.TopDirectoryOnly).ToList();
    }

    /// <summary>
    /// بيدوّر على مجلد Infrastructure بالطلوع من مكان التست لفوق.
    /// التست بيجري من bin/Debug/... فالمسار النسبي مش ثابت.
    /// </summary>
    private static string? FindInfrastructureFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (int depth = 0; depth < 8 && directory is not null; depth++)
        {
            string candidate = Path.Combine(directory.FullName, "src", "PrintFlow.Infrastructure");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static List<Type> PublicDomainTypes()
        => typeof(WorkloadBalancer).Assembly
            .GetTypes()
            .Where(t => t.IsPublic && !t.IsNested)
            .Where(t => !t.Name.Contains('<') && !t.Name.Contains('`'))
            .ToList();
}
