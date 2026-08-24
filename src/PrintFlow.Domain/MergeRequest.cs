namespace PrintFlow.Domain;

/// <summary>شكل الترقيم على الورقة.</summary>
public sealed record PageNumberStyle
{
    public ContentPosition Position { get; init; } = ContentPosition.BottomLeft;

    public string ColorHex { get; init; } = "#000000";

    public int FontSize { get; init; } = 10;

    public int EdgeMargin { get; init; } = 16;

    /// <summary>
    /// false (الافتراضي) = ترقيم متصل على المستند المدموج كله.
    /// true = كل ملف يبدأ من 1 لوحده.
    /// </summary>
    public bool RestartForEachFile { get; init; }

    /// <summary>
    /// لوحة خفيفة ورا الرقم عشان يبان على أي خلفية.
    /// من غيرها الرقم الأسود بيختفي على المستندات اللي خلفيتها غامقة —
    /// وده اللي حصل فعلًا في التجربة على مذكرة بخلفية كحلي كاملة.
    /// </summary>
    public bool Backdrop { get; init; } = true;

    /// <summary>
    /// الرقم اللي هيتكتب على أول صفحة في المستند ده.
    ///
    /// موجود عشان وضع "من غير دمج": الملفات بتفضل منفصلة بس الترقيم يفضل
    /// متصل عبرها — الملف الأول ١..٥ والتاني بيكمّل من ٦.
    /// في وضع الدمج بيفضل ١ ومالوش أي أثر.
    /// </summary>
    public int FirstPageNumber { get; init; } = 1;

    /// <summary>
    /// إجمالي الصفحات اللي بيتكتب بعد "من". صفر = استخدم عدد صفحات
    /// المستند ده نفسه (وده السلوك الطبيعي في وضع الدمج).
    ///
    /// في وضع "من غير دمج" بنمرّر هنا **إجمالي كل الملفات** عشان يطلع
    /// "صفحة ٦ من ٤٠" مش "صفحة ١ من ١٢".
    /// </summary>
    public int TotalPages { get; init; }

    public static PageNumberStyle From(AppSettings app) => new()
    {
        Position = app.PageNumberPosition,
        ColorHex = app.PageNumberColorHex,
        FontSize = app.PageNumberFontSize,
        EdgeMargin = app.PageNumberEdgeMargin,
        RestartForEachFile = app.RestartNumberingForEachFile,
        Backdrop = app.PageNumberBackdrop
    };

    /// <summary>
    /// نسخة من نفس الشكل بس بترقيم بيبدأ من رقم معيّن وإجمالي محدد.
    /// بتتستخدم في وضع "من غير دمج" لكل ملف على حدة.
    /// </summary>
    public PageNumberStyle ContinuingFrom(int firstPageNumber, int totalPages) => this with
    {
        FirstPageNumber = firstPageNumber,
        TotalPages = totalPages
    };
}

/// <summary>النص المخصص اللي بيتحط على كل ورقة.</summary>
public sealed record OverlayTextStyle
{
    public required string Text { get; init; }

    public ContentPosition Position { get; init; } = ContentPosition.BottomRight;

    public string ColorHex { get; init; } = "#000000";

    public int FontSize { get; init; } = 10;

    public int EdgeMargin { get; init; } = 16;

    public static OverlayTextStyle? From(AppSettings app)
    {
        if (!app.CustomTextEnabled || string.IsNullOrWhiteSpace(app.CustomText))
        {
            return null;
        }

        return new OverlayTextStyle
        {
            Text = app.CustomText,
            Position = app.CustomTextPosition,
            ColorHex = app.PageNumberColorHex,
            FontSize = app.PageNumberFontSize,
            EdgeMargin = app.PageNumberEdgeMargin
        };
    }
}

/// <summary>شكل العلامة المائية — نص أو صورة.</summary>
public sealed record WatermarkStyle
{
    public bool IsImage { get; init; }

    public string Text { get; init; } = string.Empty;

    public string ImagePath { get; init; } = string.Empty;

    public string ColorHex { get; init; } = "#1B2A4A";

    public string FontFamily { get; init; } = "Arial";

    public bool Bold { get; init; }

    public int FontSize { get; init; } = 40;

    public int OpacityPercent { get; init; } = 50;

    /// <summary>موجب = مايل لفوق ناحية اليمين، وهو الشكل المتعارف عليه للعلامة المائية.</summary>
    public int RotationDegrees { get; init; } = 45;

    public byte Alpha => (byte)Math.Clamp(OpacityPercent * 255 / 100, 0, 255);

    public static WatermarkStyle? From(AppSettings app)
    {
        if (!app.WatermarkEnabled)
        {
            return null;
        }

        bool hasContent = app.WatermarkIsImage
            ? !string.IsNullOrWhiteSpace(app.WatermarkImagePath)
            : !string.IsNullOrWhiteSpace(app.WatermarkText);

        if (!hasContent)
        {
            return null;
        }

        return new WatermarkStyle
        {
            IsImage = app.WatermarkIsImage,
            Text = app.WatermarkText,
            ImagePath = app.WatermarkImagePath,
            ColorHex = app.WatermarkColorHex,
            FontFamily = app.WatermarkFontFamily,
            Bold = app.WatermarkBold,
            FontSize = app.WatermarkFontSize,
            OpacityPercent = app.WatermarkOpacityPercent,
            RotationDegrees = app.WatermarkRotationDegrees
        };
    }
}

/// <summary>
/// طلب دمج كامل. نفس فكرة PrintJob: عقد واحد بدل براميترات بتزيد كل ما نضيف خيار.
/// </summary>
public sealed record MergeRequest
{
    public required IReadOnlyList<string> InputFiles { get; init; }

    public required string OutputPath { get; init; }

    public PageNumberStyle? PageNumbers { get; init; }

    public WatermarkStyle? Watermark { get; init; }

    public OverlayTextStyle? CustomText { get; init; }

    /// <summary>
    /// بيجمّع خيارات الجوب (PrintSettings) مع تفضيلات البرنامج (AppSettings).
    /// الجوب بيقرر "نرقّم ولا لأ"، والتفضيلات بتقرر "الترقيم شكله إيه".
    /// </summary>
    public static MergeRequest From(
        PrintSettings print,
        AppSettings app,
        IReadOnlyList<string> inputFiles,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(print);
        ArgumentNullException.ThrowIfNull(app);

        return new MergeRequest
        {
            InputFiles = inputFiles,
            OutputPath = outputPath,
            PageNumbers = print.NumberPagesPerFile ? PageNumberStyle.From(app) : null,
            Watermark = WatermarkStyle.From(app),
            CustomText = OverlayTextStyle.From(app)
        };
    }

    /// <summary>في حاجة أصلًا هتترسم فوق الورق؟</summary>
    public bool HasAnyOverlay => PageNumbers is not null || Watermark is not null || CustomText is not null;

    /// <summary>
    /// نفس الطلب بس بالإضافات المطلوبة بس — الباقي بيتشال.
    ///
    /// بتتستخدم في تقسيم الإضافات على مرحلتين حوالين تجميع الشرائح:
    /// اللي على الصفحة الأصلية بيتحط الأول، واللي على الورقة بيتحط بعدين.
    /// </summary>
    public MergeRequest KeepOnly(OverlayStage stage) => this with
    {
        PageNumbers = stage.PageNumbers ? PageNumbers : null,
        Watermark = stage.Watermark ? Watermark : null,
        CustomText = stage.CustomText ? CustomText : null
    };
}

/// <summary>
/// نتيجة الدمج. بدل ما نرجّع سترينج ونفتّش فيه على كلمة "[نجاح]"،
/// بقى في فلاج صريح — والـ ViewModel مبقاش محتاج يخمّن.
/// </summary>
public sealed record MergeResult(bool Success, string Message, int PageCount = 0)
{
    public static MergeResult Failed(string reason) => new(false, $"[فشل] {reason}");

    public static MergeResult Succeeded(string message, int pageCount) => new(true, $"[نجاح] {message}", pageCount);
}
