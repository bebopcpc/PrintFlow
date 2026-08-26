namespace PrintFlow.Domain;

/// <summary>
/// كل خيارات "الجوب" الواحد — أي حاجة المستخدم بيغيّرها في الصفحة الرئيسية.
/// ده بالظبط اللي بيتحفظ ويتقرا كـ Preset (إعداد مسبق)، فلما نيجي نعمل تاب
/// الإعدادات المسبقة هيبقى مجرد Serialize/Deserialize للكلاس ده — مش نسخ خصايص بالإيد.
///
/// ملاحظة معمارية: الكلاس ده بيورث ObservableObject (يعني INotifyPropertyChanged)
/// عشان الواجهة تربط عليه مباشرة بالـ Binding. INotifyPropertyChanged عقد من الـ BCL
/// مش من WPF، فالـ Domain لسه نضيف ومفيهوش أي اعتماد على مكتبة واجهة.
/// </summary>
public sealed class PrintSettings : ObservableObject
{
    // ══════════ خيارات دمج ومعالجة الملفات ══════════

    private bool _numberPagesPerFile;
    /// <summary>ترقيم صفحات كل ملف على حدة (مش ترقيم متصل على المستند المدموج).</summary>
    public bool NumberPagesPerFile
    {
        get => _numberPagesPerFile;
        set => SetProperty(ref _numberPagesPerFile, value);
    }

    private bool _mergeFiles = true;
    /// <summary>دمج كل الملفات في ملف واحد قبل الطباعة.</summary>
    public bool MergeFiles
    {
        get => _mergeFiles;
        set => SetProperty(ref _mergeFiles, value);
    }

    private bool _saveConvertedToPdf;
    /// <summary>
    /// حفظ نسخة دايمة من الصور اللي اتحوّلت لـ PDF في مجلد الإخراج.
    /// مقفولة = التحويل بيروح للتيمب وبيتمسح بعد أيام.
    /// </summary>
    public bool SaveConvertedToPdf
    {
        get => _saveConvertedToPdf;
        set => SetProperty(ref _saveConvertedToPdf, value);
    }

    private bool _saveAfterProcessing;
    /// <summary>حفظ نسخة من الملفات بعد المعالجة في مجلد الإخراج.</summary>
    public bool SaveAfterProcessing
    {
        get => _saveAfterProcessing;
        set => SetProperty(ref _saveAfterProcessing, value);
    }

    private CompressionMode _compression = CompressionMode.None;
    /// <summary>
    /// مستوى ضغط الملفات — **لسه مقفول**.
    ///
    /// اتقاس فعليًا قبل ما يتبني: إعادة حفظ الـ PDF بـ PdfSharp بتوفّر صفر
    /// تقريبًا (وأحيانًا بتكبّر الملف)، لأن الـ PDF أصلًا مضغوط. حتى الضغط
    /// الهيكلي الكامل بـ qpdf طلع ٠.٧٪ بس على المستندات الممسوحة — وهي
    /// أصلًا أتقل نوع. الوزن كله في الصور، والتوفير الحقيقي (٤٥-٧٠٪) بيجي
    /// من تصغير دقتها، وده محتاج أداة خارجية. الخاصية سايبة عشان الإعداد
    /// يفضل متحفوظ في الـ Preset، بس الواجهة مقفولة.
    /// </summary>
    public CompressionMode Compression
    {
        get => _compression;
        set => SetProperty(ref _compression, value);
    }

    private bool _deletePages;
    /// <summary>
    /// تفعيل حذف صفحات محددة **من كل ملف داخل على حدة**.
    /// النص بيفضل في <see cref="PagesToDelete"/> لما ده يتقفل، عشان المستخدم
    /// ما يضطرش يكتبه تاني — والعلامة دي هي اللي بتقرر.
    /// </summary>
    public bool DeletePages
    {
        get => _deletePages;
        set => SetProperty(ref _deletePages, value);
    }

    private string _pagesToDelete = string.Empty;
    /// <summary>أرقام الصفحات المطلوب حذفها، بصيغة زي "1,3,5-8".</summary>
    public string PagesToDelete
    {
        get => _pagesToDelete;
        set => SetProperty(ref _pagesToDelete, value, v => v ?? string.Empty);
    }

    // ══════════ خيارات البوكليت ══════════

    private bool _bookletMode;
    public bool BookletMode
    {
        get => _bookletMode;
        set => SetProperty(ref _bookletMode, value);
    }

    private BookletStart _bookletStart = BookletStart.Right;
    public BookletStart BookletStart
    {
        get => _bookletStart;
        set => SetProperty(ref _bookletStart, value);
    }

    // ══════════ خيارات الشرائح (N-up) ══════════

    private int _slidesPerSheet = 1;
    /// <summary>عدد الشرائح في الورقة الواحدة. 1 يعني الوضع العادي.</summary>
    public int SlidesPerSheet
    {
        get => _slidesPerSheet;
        set => SetProperty(ref _slidesPerSheet, value, v => v < 1 ? 1 : v);
    }

    private PageOrientation _slideOrientation = PageOrientation.Portrait;
    public PageOrientation SlideOrientation
    {
        get => _slideOrientation;
        set => SetProperty(ref _slideOrientation, value);
    }

    private SlideStart _slideStart = SlideStart.Right;
    public SlideStart SlideStart
    {
        get => _slideStart;
        set => SetProperty(ref _slideStart, value);
    }

    private SlideOrder _slideOrder = SlideOrder.Horizontal;
    public SlideOrder SlideOrder
    {
        get => _slideOrder;
        set => SetProperty(ref _slideOrder, value);
    }

    private int _slideMargin = 15;
    /// <summary>الهامش حوالين كل شريحة بالنقطة (point).</summary>
    public int SlideMargin
    {
        get => _slideMargin;
        set => SetProperty(ref _slideMargin, value, v => v < 0 ? 0 : v);
    }

    private bool _drawSlideBorder;
    public bool DrawSlideBorder
    {
        get => _drawSlideBorder;
        set => SetProperty(ref _drawSlideBorder, value);
    }

    // ══════════ خيارات الطباعة ══════════

    private bool _printDirectlyAfterProcessing = true;
    /// <summary>الطباعة تبدأ لوحدها بعد ما المعالجة تخلص، من غير ضغطة زر تانية.</summary>
    public bool PrintDirectlyAfterProcessing
    {
        get => _printDirectlyAfterProcessing;
        set => SetProperty(ref _printDirectlyAfterProcessing, value);
    }

    private bool _grayscale;
    public bool Grayscale
    {
        get => _grayscale;
        set => SetProperty(ref _grayscale, value);
    }

    private bool _duplex;
    public bool Duplex
    {
        get => _duplex;
        set => SetProperty(ref _duplex, value);
    }

    private DuplexFlip _duplexFlip = DuplexFlip.LongEdge;
    public DuplexFlip DuplexFlip
    {
        get => _duplexFlip;
        set => SetProperty(ref _duplexFlip, value);
    }

    private string _printerName = string.Empty;
    /// <summary>الطابعة المستهدفة في وضع الطابعة الواحدة. فاضية = استخدم الافتراضية.</summary>
    public string PrinterName
    {
        get => _printerName;
        set => SetProperty(ref _printerName, value, v => v ?? string.Empty);
    }

    private PageOrientation _pageOrientation = PageOrientation.Portrait;
    public PageOrientation PageOrientation
    {
        get => _pageOrientation;
        set => SetProperty(ref _pageOrientation, value);
    }

    private string _paperSize = "A4";
    public string PaperSize
    {
        get => _paperSize;
        set => SetProperty(ref _paperSize, value, v => string.IsNullOrWhiteSpace(v) ? "A4" : v);
    }

    private int _scalePercent = 100;
    /// <summary>مقياس الصفحة بالنسبة المئوية.</summary>
    public int ScalePercent
    {
        get => _scalePercent;
        set => SetProperty(ref _scalePercent, value, v => Math.Clamp(v, 10, 400));
    }

    private int _totalCopies = 1;
    public int TotalCopies
    {
        get => _totalCopies;
        set => SetProperty(ref _totalCopies, value, v => v < 1 ? 1 : v);
    }

    private bool _useMultiplePrinters;
    /// <summary>طباعة الملفات على أكتر من طابعة.</summary>
    public bool UseMultiplePrinters
    {
        get => _useMultiplePrinters;
        set => SetProperty(ref _useMultiplePrinters, value);
    }

    private bool _distributeCopies = true;
    /// <summary>
    /// توزيع إجمالي النسخ على المكن المعلّمة بدل ما كل مكنة تطبع العدد كامل.
    ///
    /// ═══ بقى true من ١.٩.٦ ═══
    ///
    /// كان false، يعني المستخدم لازم يدوّر على مربع ويعلّم عليه عشان
    /// التوزيع يشتغل. ودي كانت المشكلة بالظبط: البرنامج اتعمل أصلًا عشان
    /// المطبعة توزّع الشغل على المكن، فالحالة الطبيعية كانت مقفولة
    /// افتراضيًا ووراها خطوة مخفية.
    ///
    /// دلوقتي التوزيع هو الأصل: علّم على مكنتين يتقسّم عليهم. والحالة
    /// النادرة (كل مكنة تطلّع النسخ كاملة — نسخة لكل فرع مثلًا) هي اللي
    /// بقت محتاجة علامة.
    ///
    /// ملحوظة: ده مالوش أي أثر على مكنة واحدة — مافيش حاجة تتقسّم أصلًا.
    /// </summary>
    public bool DistributeCopies
    {
        get => _distributeCopies;
        set => SetProperty(ref _distributeCopies, value);
    }

    private List<string> _selectedPrinters = new();
    /// <summary>أسماء الطابعات المختارة في وضع "أكتر من طابعة".</summary>
    public List<string> SelectedPrinters
    {
        get => _selectedPrinters;
        set => SetProperty(ref _selectedPrinters, value, v => v ?? new List<string>());
    }

    // ══════════ نسخ ونقل القيم ══════════

    /// <summary>نسخة مستقلة تمامًا من الإعدادات الحالية.</summary>
    public PrintSettings Clone()
    {
        var copy = new PrintSettings();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>
    /// بتنقل القيم من إعدادات تانية للكائن ده **من غير ما تستبدله**،
    /// عشان كل الـ Bindings المربوطة عليه تتحدّث لوحدها.
    /// ده اللي هنستخدمه لما المستخدم يختار Preset.
    ///
    /// مهم: في تست (PrintSettingsTests.CopyFrom_Covers_Every_Property) بيمشي على كل
    /// خصايص الكلاس بالـ Reflection ويتأكد إن كل واحدة اتنقلت. فلو ضفت خاصية جديدة
    /// ونسيت تضيفها هنا، التست هيقع ويقولك اسمها.
    /// </summary>
    public void CopyFrom(PrintSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);

        NumberPagesPerFile = other.NumberPagesPerFile;
        MergeFiles = other.MergeFiles;
        SaveConvertedToPdf = other.SaveConvertedToPdf;
        SaveAfterProcessing = other.SaveAfterProcessing;
        Compression = other.Compression;
        DeletePages = other.DeletePages;
        PagesToDelete = other.PagesToDelete;

        BookletMode = other.BookletMode;
        BookletStart = other.BookletStart;

        SlidesPerSheet = other.SlidesPerSheet;
        SlideOrientation = other.SlideOrientation;
        SlideStart = other.SlideStart;
        SlideOrder = other.SlideOrder;
        SlideMargin = other.SlideMargin;
        DrawSlideBorder = other.DrawSlideBorder;

        PrintDirectlyAfterProcessing = other.PrintDirectlyAfterProcessing;
        Grayscale = other.Grayscale;
        Duplex = other.Duplex;
        DuplexFlip = other.DuplexFlip;
        PrinterName = other.PrinterName;
        PageOrientation = other.PageOrientation;
        PaperSize = other.PaperSize;
        ScalePercent = other.ScalePercent;
        TotalCopies = other.TotalCopies;
        UseMultiplePrinters = other.UseMultiplePrinters;
        DistributeCopies = other.DistributeCopies;

        // لستة جديدة مش نفس المرجع، عشان تعديل الـ Preset مايأثرش على الجوب الحالي
        SelectedPrinters = new List<string>(other.SelectedPrinters);
    }
}
