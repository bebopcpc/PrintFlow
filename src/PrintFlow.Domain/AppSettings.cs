namespace PrintFlow.Domain;

/// <summary>
/// إعدادات البرنامج نفسه — اللي بتتحفظ مرة وتفضل ثابتة لكل الجوبات
/// (تاب "الإعدادات العامة"). دي مش جزء من الـ Preset.
///
/// الفرق المهم عن PrintSettings:
///   PrintSettings = خيارات الجوب  →  بتتحفظ كـ Preset، بتتغير كل شغلانة.
///   AppSettings   = تفضيلات البرنامج →  بتتحفظ مرة في %AppData%\PrintFlow\settings.json.
/// شكل العلامة المائية والترقيم بيعيشوا هنا لأنهم مظهر ثابت للمطبعة، مش خيار جوب.
/// </summary>
public sealed class AppSettings : ObservableObject
{
    // ══════════ إعدادات عامة ══════════

    private string _defaultPrinterName = string.Empty;
    /// <summary>الطابعة الافتراضية للبرنامج. فاضية = استخدم افتراضية الويندوز.</summary>
    public string DefaultPrinterName
    {
        get => _defaultPrinterName;
        set => SetProperty(ref _defaultPrinterName, value, v => v ?? string.Empty);
    }

    private FileSortOrder _fileSortOrder = FileSortOrder.Default;
    public FileSortOrder FileSortOrder
    {
        get => _fileSortOrder;
        set => SetProperty(ref _fileSortOrder, value);
    }

    private CountingMethod _countingMethod = CountingMethod.ByPage;
    public CountingMethod CountingMethod
    {
        get => _countingMethod;
        set => SetProperty(ref _countingMethod, value);
    }

    private AppTheme _theme = AppTheme.Light;
    public AppTheme Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }

    private AppLanguage _language = AppLanguage.Arabic;
    public AppLanguage Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    private string _defaultOutputFolder = string.Empty;
    /// <summary>مجلد افتراضي لحفظ الملفات بعد المعالجة.</summary>
    public string DefaultOutputFolder
    {
        get => _defaultOutputFolder;
        set => SetProperty(ref _defaultOutputFolder, value, v => v ?? string.Empty);
    }

    private int _printerRefreshSeconds = 10;
    /// <summary>
    /// كل كام ثانية نعيد قراءة حالة الطابعات. استعلام WMI ممكن ياخد ثانية أو اتنين
    /// مع طابعات الشبكة، فـ 5 ثواني كانت ضيقة أوي — 10 أهدى على الجهاز.
    /// </summary>
    public int PrinterRefreshSeconds
    {
        get => _printerRefreshSeconds;
        set => SetProperty(ref _printerRefreshSeconds, value, v => Math.Clamp(v, 3, 120));
    }

    private int _tempFileRetentionDays = 2;
    /// <summary>الملفات المدموجة المؤقتة الأقدم من كده بتتمسح عند تشغيل البرنامج.</summary>
    public int TempFileRetentionDays
    {
        get => _tempFileRetentionDays;
        set => SetProperty(ref _tempFileRetentionDays, value, v => Math.Clamp(v, 0, 365));
    }

    // ══════════ إعدادات الترقيم ══════════

    private bool _numberWholeSheetInsteadOfSlide = true;
    /// <summary>ترقيم الورقة كاملة بدل ترقيم كل شريحة فيها لوحدها.</summary>
    public bool NumberWholeSheetInsteadOfSlide
    {
        get => _numberWholeSheetInsteadOfSlide;
        set => SetProperty(ref _numberWholeSheetInsteadOfSlide, value);
    }

    private bool _restartNumberingForEachFile;
    /// <summary>
    /// مقفولة (الافتراضي) = ترقيم متصل على المستند المدموج كله: 1 من 40، 2 من 40…
    /// مفتوحة = كل ملف يبدأ من 1 لوحده: 1 من 3، ثم 1 من 12…
    ///
    /// كانت متشفّرة على "كل ملف لوحده" وده لخبط أول مستخدم حقيقي —
    /// دمج ملفين كل واحد صفحة طلّع "صفحة 1 من 1" مرتين، وهو متوقع "1 من 2".
    /// </summary>
    public bool RestartNumberingForEachFile
    {
        get => _restartNumberingForEachFile;
        set => SetProperty(ref _restartNumberingForEachFile, value);
    }

    private ContentPosition _pageNumberPosition = ContentPosition.BottomLeft;
    public ContentPosition PageNumberPosition
    {
        get => _pageNumberPosition;
        set => SetProperty(ref _pageNumberPosition, value);
    }

    private string _pageNumberColorHex = "#000000";
    public string PageNumberColorHex
    {
        get => _pageNumberColorHex;
        set => SetProperty(ref _pageNumberColorHex, value, v => string.IsNullOrWhiteSpace(v) ? "#000000" : v);
    }

    private int _pageNumberFontSize = 10;
    public int PageNumberFontSize
    {
        get => _pageNumberFontSize;
        set => SetProperty(ref _pageNumberFontSize, value, v => Math.Clamp(v, 4, 96));
    }

    private int _pageNumberEdgeMargin = 16;
    /// <summary>المسافة من حافة الورقة بالنقطة.</summary>
    public int PageNumberEdgeMargin
    {
        get => _pageNumberEdgeMargin;
        set => SetProperty(ref _pageNumberEdgeMargin, value, v => Math.Clamp(v, 0, 200));
    }

    private bool _pageNumberBackdrop = true;
    /// <summary>
    /// لوحة خفيفة ورا الرقم. لونها بيتحسب من لون الرقم نفسه:
    /// رقم غامق → لوحة فاتحة، ورقم فاتح → لوحة غامقة.
    ///
    /// على ورقة بيضا عادية اللوحة البيضا مش بتبان أصلًا، فمفيش خسارة —
    /// وعلى مستند بخلفية ملونة كاملة دي الحاجة الوحيدة اللي بتخلي الرقم يتقرا.
    /// </summary>
    public bool PageNumberBackdrop
    {
        get => _pageNumberBackdrop;
        set => SetProperty(ref _pageNumberBackdrop, value);
    }

    // ══════════ النص المخصص ══════════

    private bool _customTextEnabled;
    public bool CustomTextEnabled
    {
        get => _customTextEnabled;
        set => SetProperty(ref _customTextEnabled, value);
    }

    private string _customText = string.Empty;
    public string CustomText
    {
        get => _customText;
        set => SetProperty(ref _customText, value, v => v ?? string.Empty);
    }

    private ContentPosition _customTextPosition = ContentPosition.BottomRight;
    public ContentPosition CustomTextPosition
    {
        get => _customTextPosition;
        set => SetProperty(ref _customTextPosition, value);
    }

    // ══════════ العلامة المائية ══════════

    private bool _watermarkEnabled;
    public bool WatermarkEnabled
    {
        get => _watermarkEnabled;
        set => SetProperty(ref _watermarkEnabled, value);
    }

    private bool _watermarkOnWholeSheet = true;
    /// <summary>العلامة تتحط على الورقة كاملة بدل كل شريحة لوحدها.</summary>
    public bool WatermarkOnWholeSheet
    {
        get => _watermarkOnWholeSheet;
        set => SetProperty(ref _watermarkOnWholeSheet, value);
    }

    private bool _watermarkIsImage;
    /// <summary>true = صورة، false = نص.</summary>
    public bool WatermarkIsImage
    {
        get => _watermarkIsImage;
        set => SetProperty(ref _watermarkIsImage, value);
    }

    private string _watermarkText = string.Empty;
    public string WatermarkText
    {
        get => _watermarkText;
        set => SetProperty(ref _watermarkText, value, v => v ?? string.Empty);
    }

    private string _watermarkImagePath = string.Empty;
    public string WatermarkImagePath
    {
        get => _watermarkImagePath;
        set => SetProperty(ref _watermarkImagePath, value, v => v ?? string.Empty);
    }

    private string _watermarkColorHex = "#1B2A4A";
    public string WatermarkColorHex
    {
        get => _watermarkColorHex;
        set => SetProperty(ref _watermarkColorHex, value, v => string.IsNullOrWhiteSpace(v) ? "#1B2A4A" : v);
    }

    private string _watermarkFontFamily = "Arial";
    public string WatermarkFontFamily
    {
        get => _watermarkFontFamily;
        set => SetProperty(ref _watermarkFontFamily, value, v => string.IsNullOrWhiteSpace(v) ? "Arial" : v);
    }

    private bool _watermarkBold;
    public bool WatermarkBold
    {
        get => _watermarkBold;
        set => SetProperty(ref _watermarkBold, value);
    }

    private int _watermarkFontSize = 40;
    public int WatermarkFontSize
    {
        get => _watermarkFontSize;
        set => SetProperty(ref _watermarkFontSize, value, v => Math.Clamp(v, 6, 300));
    }

    private int _watermarkOpacityPercent = 50;
    public int WatermarkOpacityPercent
    {
        get => _watermarkOpacityPercent;
        set => SetProperty(ref _watermarkOpacityPercent, value, v => Math.Clamp(v, 0, 100));
    }

    private int _watermarkRotationDegrees = 45;
    public int WatermarkRotationDegrees
    {
        get => _watermarkRotationDegrees;
        set => SetProperty(ref _watermarkRotationDegrees, value, v => Math.Clamp(v, -180, 180));
    }
}
