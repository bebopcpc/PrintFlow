namespace PrintFlow.Domain;

/// <summary>نوع الملف اللي المستخدم حمّله.</summary>
public enum InputKind
{
    /// <summary>PDF — بيدخل السلسلة على طول.</summary>
    Pdf,

    /// <summary>صورة بصيغة مدعومة — بتتحوّل لـ PDF الأول.</summary>
    Image,

    /// <summary>
    /// صورة بصيغة إحنا **مش** بندعمها. متعرّفة عن قصد عشان نقول للمستخدم
    /// "حوّلها لـ JPEG" بدل ما نتجاهلها في صمت وهو شايفها صورة عادية.
    /// </summary>
    UnsupportedImage,

    /// <summary>وورد أو بوربوينت — محتاج أوفيس متسطّب.</summary>
    Office,

    /// <summary>حاجة تانية — بتترفض.</summary>
    Unsupported
}

/// <summary>
/// بيقرر نوع الملف من امتداده.
///
/// حساب على نصوص بس، فمتختبر من غير ما نلمس قرص. والأهم إنه **مكان واحد**
/// بيعرف الامتدادات: الواجهة (فلتر مربع الفتح)، والتحميل، والتحويل كلهم
/// بيسألوه. لو كل واحد فيهم كان عنده قايمته، أول ما نضيف صيغة هتشتغل في
/// مكان وتقع في التاني.
/// </summary>
public static class SupportedInput
{
    /// <summary>
    /// الصيغ اللي PdfSharp بيقدر يقراها فعلًا.
    ///
    /// القايمة دي **متقاسة مش متخمّنة**: PdfSharp 6.2.4 جواه تلات قارئات صور
    /// بس — ImageImporterJpeg و ImageImporterPng و ImageImporterBmp — ومفيش
    /// فيه أي مسار بيعدّي على تصوير ويندوز (لا GDI ولا WPF)، يعني نفس التلاتة
    /// على كل نظام. اتجربوا واحدة واحدة والباقي رجّع "Unsupported image format".
    ///
    /// GIF و TIFF مش هنا عن قصد رغم إنهم شائعين — الادعاء بيهم كان هيطلع
    /// رسالة فشل لكل صورة، وده أسوأ من إننا نقول من الأول إنهم مش مدعومين.
    /// </summary>
    public static readonly IReadOnlyList<string> ImageExtensions =
        [".jpg", ".jpeg", ".png", ".bmp"];

    /// <summary>
    /// صيغ صور معروفة بس مش مدعومة. بنتعرّف عليها عشان الرسالة تبقى مفيدة.
    /// </summary>
    public static readonly IReadOnlyList<string> UnsupportedImageExtensions =
        [".gif", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif", ".svg"];

    /// <summary>صيغ أوفيس — لسه محتاجة قرار عن طريقة التحويل.</summary>
    public static readonly IReadOnlyList<string> OfficeExtensions =
        [".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx"];

    public static InputKind KindOf(string? path)
    {
        string extension = ExtensionOf(path);

        if (extension.Length == 0)
        {
            return InputKind.Unsupported;
        }

        if (extension == ".pdf")
        {
            return InputKind.Pdf;
        }

        if (ImageExtensions.Contains(extension))
        {
            return InputKind.Image;
        }

        if (UnsupportedImageExtensions.Contains(extension))
        {
            return InputKind.UnsupportedImage;
        }

        return OfficeExtensions.Contains(extension) ? InputKind.Office : InputKind.Unsupported;
    }

    /// <summary>
    /// الامتداد بحروف صغيرة، أو نص فاضي.
    ///
    /// بنقراه بالإيد مش بـ <c>Path.GetExtension</c> عشان ده بيختلف بين
    /// ويندوز ولينكس في التعامل مع الشرطة المقلوبة — ولو التستات بتجري
    /// على لينكس والبرنامج شغال على ويندوز، الفرق ده بيخبّي أخطاء.
    /// </summary>
    private static string ExtensionOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        int lastSeparator = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        string name = lastSeparator >= 0 ? path[(lastSeparator + 1)..] : path;

        int dot = name.LastIndexOf('.');

        // نقطة في الأول (ملف مخفي) مش امتداد
        return dot > 0 ? name[dot..].ToLowerInvariant() : "";
    }

    /// <summary>فلتر مربع "فتح ملف" — بيتبني من نفس القوايم فوق.</summary>
    public static string OpenDialogFilter
    {
        get
        {
            string images = string.Join(";", ImageExtensions.Select(e => "*" + e));

            return $"ملفات مدعومة|*.pdf;{images}|" +
                   $"ملفات PDF|*.pdf|" +
                   $"صور|{images}|" +
                   "كل الملفات|*.*";
        }
    }
}
