using System.Collections.ObjectModel;
using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Presentation;

/// <summary>
/// كل حالة ومنطق الصفحة الرئيسية. الواجهة (MainWindow) بقت مجرد شكل مربوط عليه بالـ Binding.
///
/// ليه المشروع ده net10.0 عادي مش net10.0-windows؟ عشان بياخد الخدمات كـ Interfaces
/// من طبقة Application، فمش محتاج WPF ولا ويندوز — يعني ينفع نعمله Unit Tests عادي.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IPrinterRepository _printerRepository;
    private readonly IPdfMergeService _mergeService;
    private readonly IPdfPrintService _printService;
    private readonly IAppSettingsStore? _settingsStore;
    private readonly IPresetStore? _presetStore;
    private readonly IJobLog? _jobLog;
    private readonly IPdfInfoService? _pdfInfo;
    private readonly IPdfSlideComposer? _slideComposer;

    private List<string> _outputFiles = new();

    /// <summary>عدد صفحات المستند الناتج — بيتستخدم بس في حساب مهلة انتظار الطباعة.</summary>
    private int _outputPageCount;

    public MainViewModel(
        IPrinterRepository printerRepository,
        IPdfMergeService mergeService,
        IPdfPrintService printService,
        IAppSettingsStore? settingsStore = null,
        IPresetStore? presetStore = null,
        IFontCatalog? fontCatalog = null,
        IJobLog? jobLog = null,
        IPdfInfoService? pdfInfo = null,
        IPdfSlideComposer? slideComposer = null,
        string appVersion = "")
    {
        _jobLog = jobLog;
        _pdfInfo = pdfInfo;
        _slideComposer = slideComposer;
        AppVersion = appVersion;

        _printerRepository = printerRepository ?? throw new ArgumentNullException(nameof(printerRepository));
        _mergeService = mergeService ?? throw new ArgumentNullException(nameof(mergeService));
        _printService = printService ?? throw new ArgumentNullException(nameof(printService));
        _settingsStore = settingsStore;
        _presetStore = presetStore;

        WatermarkFonts = fontCatalog?.AvailableFonts is { Count: > 0 } fonts
            ? fonts
            : ["Arial", "Tahoma", "Times New Roman", "Courier New"];

        // التفضيلات بتتحمّل من القرص لو فيه مخزن، وإلا بنبدأ بالافتراضي
        App = settingsStore?.Load() ?? new AppSettings();
        Settings = new PrintSettings();

        // إعدادات محفوظة قديمة ممكن تكون فيها خط مش موجود في القايمة (زي Helvetica).
        // من غير التصحيح ده، القايمة هتبان فاضية قدام المستخدم.
        if (!WatermarkFonts.Contains(App.WatermarkFontFamily))
        {
            App.WatermarkFontFamily = WatermarkFonts[0];
        }

        foreach (var preset in presetStore?.LoadAll() ?? [])
        {
            Presets.Add(preset);
        }

        RefreshPrintersCommand = new AsyncRelayCommand(RefreshPrintersAsync);
        ProcessCommand = new AsyncRelayCommand(ProcessAsync, () => Files.Count > 0);
        PrintCommand = new AsyncRelayCommand(PrintAsync, () => _outputFiles.Count > 0);
        ResetCommand = new RelayCommand(Reset);
        RemoveFileCommand = new RelayCommand<PdfFileItem>(RemoveFile);

        AddPresetCommand = new RelayCommand(AddPreset, () => !string.IsNullOrWhiteSpace(NewPresetName));
        UpdatePresetCommand = new RelayCommand(UpdatePreset, () => SelectedPreset is not null);
        DeletePresetCommand = new RelayCommand(DeletePreset, () => SelectedPreset is not null);
        ApplyPresetCommand = new RelayCommand(ApplyPreset, () => SelectedPreset is not null);
        RestoreDefaultAppSettingsCommand = new RelayCommand(RestoreDefaultAppSettings);

        Files.CollectionChanged += (_, _) =>
        {
            ProcessCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(FilesCountText));
            RefreshBookletSummary();
        };

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PrintSettings.UseMultiplePrinters))
            {
                OnPropertyChanged(nameof(SinglePrinterMode));
            }

            // أي إعداد بيغيّر شكل الورقة لازم يحدّث المعاينة على طول
            if (e.PropertyName is nameof(PrintSettings.SlidesPerSheet)
                or nameof(PrintSettings.SlideOrientation)
                or nameof(PrintSettings.SlideOrder)
                or nameof(PrintSettings.SlideStart)
                or nameof(PrintSettings.SlideMargin))
            {
                RefreshSlidePreview();
            }

            if (e.PropertyName is nameof(PrintSettings.BookletMode)
                or nameof(PrintSettings.BookletStart))
            {
                RefreshBookletSummary();
            }
        };

        RefreshSlidePreview();

        // كان الإعداد ده بيتحفظ ومحدش بينده SortFiles — يعني الاختيار مالوش أي أثر
        App.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.FileSortOrder))
            {
                SortFiles();
            }
        };
    }

    // ══════════ الحالة ══════════

    /// <summary>خيارات الجوب الحالي — ده اللي بيتحفظ كـ Preset.</summary>
    public PrintSettings Settings { get; }

    /// <summary>تفضيلات البرنامج — تاب الإعدادات العامة.</summary>
    public AppSettings App { get; }

    public ObservableCollection<PdfFileItem> Files { get; } = new();

    public ObservableCollection<PrinterItem> Printers { get; } = new();

    /// <summary>سطور نتايج المعالجة والطباعة — بدل الـ MessageBox اللي كان بيقطع الشغل.</summary>
    public ObservableCollection<string> Log { get; } = new();

    private string _statusText = "البرنامج جاهز. حمّل ملفات PDF عشان تبدأ.";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !IsBusy;

    private int _printerCount;
    public int PrinterCount
    {
        get => _printerCount;
        private set
        {
            if (SetProperty(ref _printerCount, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    /// <summary>رقم النسخة — بيظهر في العنوان وفي اللوج عشان أي بلاغ نعرف هو من أنهي بيلد.</summary>
    public string AppVersion { get; }

    public string WindowTitle => string.IsNullOrEmpty(AppVersion)
        ? $"PrintFlow — {PrinterCount} طابعة"
        : $"PrintFlow {AppVersion} — {PrinterCount} طابعة";

    /// <summary>قايمة اختيار الطابعة الواحدة بتتقفل لما المستخدم يفعّل وضع "أكتر من طابعة".</summary>
    public bool SinglePrinterMode => !Settings.UseMultiplePrinters;

    /// <summary>كام مستند جاهز للطباعة بعد المعالجة. في وضع الدمج بيبقى ١.</summary>
    public int OutputFileCount => _outputFiles.Count;

    // ══════════ معاينة الشرائح ══════════

    /// <summary>أقصى مقاس لمربع المعاينة بالبكسل.</summary>
    private const double PreviewMaxWidth = 148;
    private const double PreviewMaxHeight = 196;

    /// <summary>A4 بالنقطة — الافتراض لما مايكونش في ملفات محمّلة.</summary>
    private static readonly (double Width, double Height) A4 = (595, 842);

    /// <summary>مقاس أول صفحة في أول ملف — التقسيم بيعتمد عليه.</summary>
    private (double Width, double Height)? _sourcePageSize;

    public ObservableCollection<SlidePreviewCell> SlidePreview { get; } = new();

    private double _slidePreviewWidth = PreviewMaxWidth;
    public double SlidePreviewWidth
    {
        get => _slidePreviewWidth;
        private set => SetProperty(ref _slidePreviewWidth, value);
    }

    private double _slidePreviewHeight = PreviewMaxHeight;
    public double SlidePreviewHeight
    {
        get => _slidePreviewHeight;
        private set => SetProperty(ref _slidePreviewHeight, value);
    }

    private string _bookletSummary = "";
    /// <summary>كام ورقة وكام صفحة فاضية — قبل ما المستخدم يشغّل المعالجة.</summary>
    public string BookletSummary
    {
        get => _bookletSummary;
        private set => SetProperty(ref _bookletSummary, value);
    }

    /// <summary>
    /// بيحسب شكل الكتيّب من عدد صفحات الملفات المحمّلة.
    /// أهم حاجة فيه إنه بيقول عدد الصفحات الفاضية **قبل** الطباعة —
    /// عشان اللي على الماكينة ماياخدش باله منها بعد ما الورق يطلع.
    /// </summary>
    private void RefreshBookletSummary()
    {
        if (!Settings.BookletMode)
        {
            BookletSummary = "";
            return;
        }

        int pages = Files.Sum(f => f.PageCount ?? 0);

        if (pages == 0)
        {
            BookletSummary = "حمّل ملفات عشان نحسبلك الورق المطلوب.";
            return;
        }

        int sheets = BookletImposition.SheetCount(pages);
        int blanks = BookletImposition.PaddedPageCount(pages) - pages;

        BookletSummary = blanks == 0
            ? $"{pages} صفحة على {sheets} ورقة بوش وضهر."
            : $"{pages} صفحة على {sheets} ورقة بوش وضهر، و{blanks} صفحة فاضية في الآخر.";
    }

    private string _slideLayoutSummary = "";
    /// <summary>وصف التقسيم بالكلام — "٣ صفوف × ٢ أعمدة".</summary>
    public string SlideLayoutSummary
    {
        get => _slideLayoutSummary;
        private set => SetProperty(ref _slideLayoutSummary, value);
    }

    /// <summary>
    /// بتحسب المعاينة من **نفس** دوال SheetLayout اللي بتحسب الطباعة.
    ///
    /// ده مقصود: لو المعاينة كان ليها حسابات خاصة بيها، أي تعديل في الطباعة
    /// كان هيخلي المعاينة تكذب على المستخدم من غير ما حد ياخد باله.
    /// </summary>
    public void RefreshSlidePreview()
    {
        var source = _sourcePageSize ?? A4;

        // شكل الورقة: نفس المقاس مقلوب حسب الاتجاه المطلوب
        double longSide = Math.Max(source.Width, source.Height);
        double shortSide = Math.Min(source.Width, source.Height);

        var (sheetWidth, sheetHeight) = Settings.SlideOrientation == PageOrientation.Landscape
            ? (longSide, shortSide)
            : (shortSide, longSide);

        int perSheet = Math.Max(1, Settings.SlidesPerSheet);

        var grid = SheetLayout.ChooseGrid(
            perSheet, sheetWidth, sheetHeight, source.Width, source.Height, Settings.SlideMargin);

        double scale = Math.Min(PreviewMaxWidth / sheetWidth, PreviewMaxHeight / sheetHeight);
        SlidePreviewWidth = sheetWidth * scale;
        SlidePreviewHeight = sheetHeight * scale;

        SlidePreview.Clear();

        for (int i = 0; i < grid.Capacity; i++)
        {
            var slot = SheetLayout.SlotFor(
                i, grid, sheetWidth, sheetHeight, source.Width, source.Height,
                Settings.SlideMargin, Settings.SlideOrder, Settings.SlideStart);

            SlidePreview.Add(new SlidePreviewCell
            {
                Number = i + 1,
                X = slot.X * scale,
                Y = slot.Y * scale,
                Width = slot.Width * scale,
                Height = slot.Height * scale
            });
        }

        SlideLayoutSummary = perSheet <= 1
            ? "كل صفحة على ورقة لوحدها"
            : $"{grid.Rows} صف × {grid.Columns} عمود على الورقة الواحدة";
    }

    public string FilesCountText
    {
        get
        {
            if (Files.Count == 0)
            {
                return "مفيش ملفات";
            }

            int pages = Files.Sum(f => f.PageCount ?? 0);
            bool allKnown = Files.All(f => f.PageCount is not null);

            return allKnown && pages > 0
                ? $"{Files.Count} ملف • {pages} صفحة"
                : $"{Files.Count} ملف";
        }
    }

    // ══════════ خيارات القوايم المنسدلة ══════════

    /// <summary>
    /// المقاسات اللي SumatraPDF بيفهمها في paper=. A0 و A1 اتشالوا لأنه مش بيدعمهم.
    ///
    /// ليه مش بناخدها من GetCapabilities() بتاعة الطابعة نفسها؟ لأن ويندوز بيرجّع
    /// أسامي زي "A4 210 x 297 mm" وSumatra مش هيفهمها، فهنكسر الطباعة بدل ما نحسّنها.
    /// تبقى مفيدة لو يوم اتحولنا لواجهة طباعة تانية.
    /// </summary>
    public IReadOnlyList<string> PaperSizes { get; } =
        new[] { "A2", "A3", "A4", "A5", "A6", "Letter", "Legal", "Tabloid", "Statement" };

    public IReadOnlyList<EnumOption<PageOrientation>> PageOrientations { get; } = new[]
    {
        new EnumOption<PageOrientation>(PageOrientation.Portrait, "طولي"),
        new EnumOption<PageOrientation>(PageOrientation.Landscape, "عرضي")
    };

    public IReadOnlyList<EnumOption<DuplexFlip>> DuplexFlips { get; } = new[]
    {
        new EnumOption<DuplexFlip>(DuplexFlip.LongEdge, "الاتجاه الطويل"),
        new EnumOption<DuplexFlip>(DuplexFlip.ShortEdge, "الاتجاه القصير")
    };

    public IReadOnlyList<EnumOption<BookletStart>> BookletStarts { get; } = new[]
    {
        new EnumOption<BookletStart>(BookletStart.Right, "يمين"),
        new EnumOption<BookletStart>(BookletStart.Left, "يسار")
    };

    public IReadOnlyList<EnumOption<SlideStart>> SlideStarts { get; } = new[]
    {
        new EnumOption<SlideStart>(SlideStart.Right, "يمين"),
        new EnumOption<SlideStart>(SlideStart.Left, "شمال")
    };

    public IReadOnlyList<EnumOption<SlideOrder>> SlideOrders { get; } = new[]
    {
        new EnumOption<SlideOrder>(SlideOrder.Horizontal, "أفقي"),
        new EnumOption<SlideOrder>(SlideOrder.Vertical, "رأسي")
    };

    public IReadOnlyList<EnumOption<CompressionMode>> CompressionModes { get; } = new[]
    {
        new EnumOption<CompressionMode>(CompressionMode.None, "بدون ضغط"),
        new EnumOption<CompressionMode>(CompressionMode.Normal, "ضغط عادي"),
        new EnumOption<CompressionMode>(CompressionMode.Advanced, "ضغط متقدم")
    };

    public IReadOnlyList<int> SlidesPerSheetOptions { get; } = new[] { 1, 2, 4, 6, 8, 9, 16 };

    // ══════════ خيارات تاب الإعدادات العامة ══════════

    public IReadOnlyList<NamedColor> ColorPalette { get; } = new[]
    {
        new NamedColor("#1B2A4A", "كحلي"),
        new NamedColor("#000000", "أسود"),
        new NamedColor("#C0392B", "أحمر"),
        new NamedColor("#E67E22", "برتقالي"),
        new NamedColor("#27AE60", "أخضر"),
        new NamedColor("#4A4A4A", "رمادي غامق"),
        new NamedColor("#FFFFFF", "أبيض")
    };

    /// <summary>
    /// الخطوط المتاحة للعلامة المائية — جاية من الكتالوج اللي بيفلتر على
    /// اللي متسطّب فعلًا واللي بيغطي العربي. يعني المستخدم مايقدرش يختار
    /// خط هيطلّع مربعات فاضية في الـ PDF.
    /// </summary>
    public IReadOnlyList<string> WatermarkFonts { get; }

    public IReadOnlyList<EnumOption<ContentPosition>> ContentPositions { get; } = new[]
    {
        new EnumOption<ContentPosition>(ContentPosition.BottomRight, "أسفل - يمين"),
        new EnumOption<ContentPosition>(ContentPosition.BottomCenter, "أسفل - وسط"),
        new EnumOption<ContentPosition>(ContentPosition.BottomLeft, "أسفل - يسار"),
        new EnumOption<ContentPosition>(ContentPosition.TopRight, "أعلى - يمين"),
        new EnumOption<ContentPosition>(ContentPosition.TopCenter, "أعلى - وسط"),
        new EnumOption<ContentPosition>(ContentPosition.TopLeft, "أعلى - يسار")
    };

    public IReadOnlyList<EnumOption<FileSortOrder>> FileSortOrders { get; } = new[]
    {
        new EnumOption<FileSortOrder>(FileSortOrder.Default, "افتراضي"),
        new EnumOption<FileSortOrder>(FileSortOrder.ByName, "اسم الملف"),
        new EnumOption<FileSortOrder>(FileSortOrder.ByPageCount, "عدد الصفحات"),
        new EnumOption<FileSortOrder>(FileSortOrder.BySize, "حجم الملف"),
        new EnumOption<FileSortOrder>(FileSortOrder.ByDate, "تاريخ الملف")
    };

    public IReadOnlyList<EnumOption<CountingMethod>> CountingMethods { get; } = new[]
    {
        new EnumOption<CountingMethod>(CountingMethod.ByPage, "بالصفحة"),
        new EnumOption<CountingMethod>(CountingMethod.BySheet, "بالورقة")
    };

    public IReadOnlyList<EnumOption<AppTheme>> Themes { get; } = new[]
    {
        new EnumOption<AppTheme>(AppTheme.Light, "فاتح"),
        new EnumOption<AppTheme>(AppTheme.Dark, "معتم")
    };

    public IReadOnlyList<EnumOption<AppLanguage>> Languages { get; } = new[]
    {
        new EnumOption<AppLanguage>(AppLanguage.Arabic, "عربي"),
        new EnumOption<AppLanguage>(AppLanguage.English, "إنجليزي")
    };

    // ══════════ الأوامر ══════════

    public AsyncRelayCommand RefreshPrintersCommand { get; }
    public AsyncRelayCommand ProcessCommand { get; }
    public AsyncRelayCommand PrintCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand<PdfFileItem> RemoveFileCommand { get; }
    public RelayCommand AddPresetCommand { get; }
    public RelayCommand UpdatePresetCommand { get; }
    public RelayCommand DeletePresetCommand { get; }
    public RelayCommand ApplyPresetCommand { get; }
    public RelayCommand RestoreDefaultAppSettingsCommand { get; }

    // ══════════ الإعدادات المسبقة ══════════

    public ObservableCollection<Preset> Presets { get; } = new();

    private Preset? _selectedPreset;
    public Preset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value))
            {
                UpdatePresetCommand.RaiseCanExecuteChanged();
                DeletePresetCommand.RaiseCanExecuteChanged();
                ApplyPresetCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedPresetSummary));
            }
        }
    }

    public string SelectedPresetSummary =>
        SelectedPreset?.Summarize() ?? "اختر إعداد مسبق عشان تشوف تفاصيله.";

    private string _newPresetName = string.Empty;
    public string NewPresetName
    {
        get => _newPresetName;
        set
        {
            if (SetProperty(ref _newPresetName, value ?? string.Empty))
            {
                AddPresetCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>بيحفظ الإعدادات الحالية كـ Preset جديد بالاسم اللي المستخدم كتبه.</summary>
    private void AddPreset()
    {
        string name = NewPresetName.Trim();

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var existing = Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            // نفس الاسم موجود — بنستبدله بدل ما نعمل نسختين بنفس الاسم
            existing.Settings = Settings.Clone();
            SelectedPreset = existing;
            StatusText = $"اتحدّث الإعداد المسبق \"{name}\".";
        }
        else
        {
            var preset = new Preset { Name = name, Settings = Settings.Clone() };
            Presets.Add(preset);
            SelectedPreset = preset;
            StatusText = $"اتحفظ إعداد مسبق جديد باسم \"{name}\".";
        }

        NewPresetName = string.Empty;
        PersistPresets();
    }

    /// <summary>بيكتب الإعدادات الحالية فوق الـ Preset المختار.</summary>
    private void UpdatePreset()
    {
        if (SelectedPreset is not { } preset)
        {
            return;
        }

        preset.Settings = Settings.Clone();
        OnPropertyChanged(nameof(SelectedPresetSummary));
        PersistPresets();
        StatusText = $"اتحدّث الإعداد المسبق \"{preset.Name}\" بالإعدادات الحالية.";
    }

    private void DeletePreset()
    {
        if (SelectedPreset is not { } preset)
        {
            return;
        }

        Presets.Remove(preset);
        SelectedPreset = null;
        PersistPresets();
        StatusText = $"اتحذف الإعداد المسبق \"{preset.Name}\".";
    }

    /// <summary>
    /// بينقل قيم الـ Preset للإعدادات الحالية.
    ///
    /// لاحظ CopyFrom مش استبدال الكائن: الـ Bindings كلها مربوطة على نفس النسخة،
    /// فلو استبدلناها الواجهة مكانتش هتلاحظ. CopyFrom بتبعت إشعار لكل خاصية.
    /// </summary>
    private void ApplyPreset()
    {
        if (SelectedPreset is not { } preset)
        {
            return;
        }

        Settings.CopyFrom(preset.Settings);
        ApplySelectedPrintersFromSettings();
        StatusText = $"اتطبّق الإعداد المسبق \"{preset.Name}\".";
    }

    private void PersistPresets() => _presetStore?.SaveAll(Presets);

    // ══════════ الإعدادات العامة ══════════

    /// <summary>بيحفظ تفضيلات البرنامج. الواجهة بتناديها عند الإغلاق.</summary>
    public void SaveAppSettings() => _settingsStore?.Save(App);

    /// <summary>
    /// بترجّع كل تفضيلات البرنامج لقيمتها الافتراضية.
    ///
    /// بتمشي بالـ Reflection مش بلستة مكتوبة بالإيد. النسخة القديمة كانت لستة
    /// وفعلًا نسيت خاصية جديدة (RestartNumberingForEachFile) فكان زرار
    /// "استرجاع الافتراضي" بيسيبها زي ما هي من غير ما حد ياخد باله.
    /// الشكل ده مستحيل يقدم على الكلاس.
    ///
    /// بنعدّي على نفس الكائن (App) مش بنستبدله، لأن كل الـ Bindings مربوطة عليه
    /// — والـ setters هي اللي بتبعت PropertyChanged لكل خاصية.
    /// </summary>
    private void RestoreDefaultAppSettings()
    {
        var defaults = new AppSettings();

        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(App, property.GetValue(defaults));
            }
        }

        SaveAppSettings();
        StatusText = "اترجعت الإعدادات العامة للوضع الافتراضي.";
    }

    // ══════════ الملفات ══════════

    /// <summary>بتضيف ملفات جديدة وبتتجاهل المكرر. بترجّع عدد اللي اتضاف فعلاً.</summary>
    public int AddFiles(IEnumerable<string> paths)
    {
        int added = 0;

        foreach (var path in paths)
        {
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Files.Any(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!File.Exists(path))
            {
                continue;
            }

            var info = new FileInfo(path);
            Files.Add(new PdfFileItem(path, info.Length, info.LastWriteTimeUtc));
            added++;
        }

        StatusText = added > 0
            ? $"اتضاف {added} ملف. الإجمالي {Files.Count}."
            : "مفيش ملفات جديدة اتضافت (لازم تكون PDF ومش مكررة).";

        if (added > 0)
        {
            // بيكمل في الخلفية: قراية 20 ملف ممكن تاخد لحظة، والواجهة ماتستناش
            _ = LoadPageCountsAsync();
        }

        return added;
    }

    /// <summary>
    /// بيملى عدد صفحات كل ملف. الـ await بيرجّع التنفيذ لثريد الواجهة،
    /// فتحديث العناصر آمن من غير Dispatcher.
    /// </summary>
    public async Task LoadPageCountsAsync()
    {
        if (_pdfInfo is null)
        {
            return;
        }

        foreach (var file in Files.Where(f => f.PageCount is null).ToList())
        {
            file.PageCount = await Task.Run(() => _pdfInfo.TryGetPageCount(file.FullPath));
        }

        OnPropertyChanged(nameof(FilesCountText));
        RefreshBookletSummary();

        // مقاس أول صفحة بيحدد تقسيم الورقة، فالمعاينة لازم تعرفه. من غيره
        // كانت هتفترض A4 طولية وتوري شكل غلط لشغل البوربوينت العرضي.
        if (Files.Count > 0)
        {
            string first = Files[0].FullPath;
            var size = await Task.Run(() => _pdfInfo.TryGetPageSize(first));

            if (size != _sourcePageSize)
            {
                _sourcePageSize = size;
                RefreshSlidePreview();
            }
        }

        // لو الترتيب بعدد الصفحات، دلوقتي بس بقى عندنا الأرقام
        if (App.FileSortOrder == FileSortOrder.ByPageCount)
        {
            SortFiles();
        }
    }

    private void RemoveFile(PdfFileItem? item)
    {
        if (item is not null)
        {
            Files.Remove(item);
        }
    }

    /// <summary>بترتّب الملفات حسب اختيار المستخدم في الإعدادات العامة.</summary>
    public void SortFiles()
    {
        List<PdfFileItem> sorted = App.FileSortOrder switch
        {
            FileSortOrder.ByName => Files.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList(),
            FileSortOrder.ByPageCount => Files.OrderBy(f => f.PageCount ?? int.MaxValue).ToList(),
            FileSortOrder.BySize => Files.OrderBy(f => f.SizeBytes).ToList(),
            FileSortOrder.ByDate => Files.OrderBy(f => f.ModifiedUtc).ToList(),
            _ => Files.ToList()
        };

        for (int i = 0; i < sorted.Count; i++)
        {
            int currentIndex = Files.IndexOf(sorted[i]);
            if (currentIndex != i)
            {
                Files.Move(currentIndex, i);
            }
        }
    }

    // ══════════ الطابعات ══════════

    public async Task RefreshPrintersAsync()
    {
        List<Printer> printers;

        try
        {
            printers = await _printerRepository.GetPrintersAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            StatusText = $"مش قادر أقرا الطابعات: {ex.Message}";
            return;
        }

        // تحديث في المكان بدل إعادة بناء اللستة — عشان اختيار المستخدم مايضيعش كل تحديث
        foreach (var printer in printers)
        {
            var existing = Printers.FirstOrDefault(p => p.Name == printer.Name);
            if (existing is null)
            {
                var item = new PrinterItem(printer);
                item.IsSelected = Settings.SelectedPrinters.Contains(printer.Name);
                Printers.Add(item);
            }
            else
            {
                existing.Update(printer);
            }
        }

        var goneNames = Printers.Select(p => p.Name).Except(printers.Select(p => p.Name)).ToList();
        foreach (var name in goneNames)
        {
            var gone = Printers.FirstOrDefault(p => p.Name == name);
            if (gone is not null)
            {
                Printers.Remove(gone);
            }
        }

        PrinterCount = Printers.Count;

        var windowsDefault = Printers.FirstOrDefault(p => p.IsDefault) ?? Printers.FirstOrDefault();

        // ترتيب الأولوية لطابعة الجوب — وده اللي بيحسم "البرنامج بياخد الأمر منين":
        //   1) اختيار المستخدم في تاب الرئيسية (لو لسه موجود في القايمة)
        //   2) "الطابعة الافتراضية للبرنامج" من تاب الإعدادات العامة
        //   3) الطابعة الافتراضية في ويندوز
        //
        // النقطة (2) دي كانت **ساقطة تمامًا**: الإعداد كان بيتحفظ في الملف
        // ومحدش بيقراه خالص، فالمستخدم يختار طابعة في الإعدادات العامة
        // والبرنامج يطبع على غيرها من غير أي تفسير.
        if (string.IsNullOrEmpty(Settings.PrinterName) || Printers.All(p => p.Name != Settings.PrinterName))
        {
            var preferred = Printers.FirstOrDefault(p => p.Name == App.DefaultPrinterName) ?? windowsDefault;

            if (preferred is not null)
            {
                Settings.PrinterName = preferred.Name;
            }
        }

        // القايمة في الإعدادات العامة كانت بتفضل فاضية لحد ما المستخدم يختار
        if (string.IsNullOrEmpty(App.DefaultPrinterName) && windowsDefault is not null)
        {
            App.DefaultPrinterName = windowsDefault.Name;
        }
    }

    /// <summary>
    /// بتشغّل تحديث دوري لحالة الطابعات. بتتنادى من الواجهة مرة واحدة عند الفتح،
    /// والـ await بيرجّع التنفيذ لثريد الواجهة لوحده فالتحديث آمن.
    /// </summary>
    public async Task RunAutoRefreshAsync(CancellationToken cancellationToken)
    {
        await RefreshPrintersAsync();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(App.PrinterRefreshSeconds), cancellationToken);
                await RefreshPrintersAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // إغلاق البرنامج - طبيعي
        }
    }

    /// <summary>بتنقل اختيار الطابعات من الواجهة للإعدادات (عشان الـ Preset يحفظها).</summary>
    public void SyncSelectedPrintersToSettings()
        => Settings.SelectedPrinters = Printers.Where(p => p.IsSelected).Select(p => p.Name).ToList();

    /// <summary>بتنقل اختيار الطابعات من الإعدادات للواجهة (بعد تحميل Preset).</summary>
    public void ApplySelectedPrintersFromSettings()
    {
        foreach (var printer in Printers)
        {
            printer.IsSelected = Settings.SelectedPrinters.Contains(printer.Name);
        }
    }

    // ══════════ المعالجة والطباعة ══════════

    private async Task ProcessAsync()
    {
        if (Files.Count == 0)
        {
            StatusText = "حمّل ملفات الأول.";
            return;
        }

        IsBusy = true;
        StatusText = "جاري معالجة الملفات...";

        try
        {
            CleanOldTempFiles();

            var inputs = Files.Select(f => f.FullPath).ToList();

            if (Settings.MergeFiles)
            {
                string outputFolder = Path.Combine(Path.GetTempPath(), "PrintFlow");
                Directory.CreateDirectory(outputFolder);
                string outputPath = Path.Combine(outputFolder, $"merged_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

                _jobLog?.Info($"بدء معالجة {inputs.Count} ملف → {outputPath}");

                var request = MergeRequest.From(Settings, App, inputs, outputPath);
                var result = await RunPipelineAsync(request);

                Log.Add(result.Message);
                _jobLog?.Info(result.Message);

                if (!result.Success)
                {
                    _outputFiles = new List<string>();
                    _outputPageCount = 0;
                    StatusText = "فشلت المعالجة. شوف التفاصيل في اللوج.";
                    return;
                }

                _outputFiles = new List<string> { outputPath };
                _outputPageCount = result.PageCount;
                StatusText = $"تمت المعالجة: {Files.Count} ملف في {result.PageCount} صفحة.";
            }
            else
            {
                await ProcessWithoutMergingAsync(inputs);
            }
        }
        finally
        {
            IsBusy = false;
            PrintCommand.RaiseCanExecuteChanged();
        }

        if (Settings.PrintDirectlyAfterProcessing)
        {
            await PrintAsync();
        }
    }

    /// <summary>
    /// السلسلة الكاملة لمستند واحد: دمج ← إضافات على الصفحة ← تجميع شرائح
    /// ← إضافات على الورقة.
    ///
    /// لما مايكونش في تجميع شرائح، كل ده بيرجع خطوة واحدة زي الأول بالظبط —
    /// ومفيش ملفات مؤقتة أصلًا.
    /// </summary>
    private async Task<MergeResult> RunPipelineAsync(MergeRequest request)
    {
        // مفيش شرائح؟ كل الإضافات بتتحط مرة واحدة والخلاص
        if (Settings.SlidesPerSheet <= 1 || _slideComposer is null)
        {
            return await Task.Run(() => _mergeService.Merge(request));
        }

        var before = SlidePipeline.BeforeSlides(App);
        var after = SlidePipeline.AfterSlides(App);

        string stem = Path.Combine(
            Path.GetDirectoryName(request.OutputPath)!,
            Path.GetFileNameWithoutExtension(request.OutputPath));

        string merged = stem + ".stage1.pdf";
        string composed = stem + ".stage2.pdf";

        try
        {
            // ١) الدمج + الإضافات اللي على الصفحة الأصلية
            var mergeResult = await Task.Run(
                () => _mergeService.Merge(request.KeepOnly(before) with { OutputPath = merged }));

            if (!mergeResult.Success)
            {
                return mergeResult;
            }

            // ٢) تجميع الشرائح على الورق
            var slideRequest = SlideRequest.From(Settings, merged, composed);
            var slideResult = await Task.Run(() => _slideComposer.Compose(slideRequest));

            if (!slideResult.Success)
            {
                return slideResult;
            }

            // ٣) الإضافات اللي على الورقة كاملة
            if (after.Nothing || !request.KeepOnly(after).HasAnyOverlay)
            {
                File.Move(composed, request.OutputPath, overwrite: true);

                return MergeResult.Succeeded(
                    $"{mergeResult.Message.Replace("[نجاح] ", "")} — {slideResult.Message.Replace("[نجاح] ", "")}",
                    slideResult.PageCount);
            }

            var finalResult = await Task.Run(() => _mergeService.Merge(
                request.KeepOnly(after) with
                {
                    InputFiles = [composed],
                    OutputPath = request.OutputPath
                }));

            if (!finalResult.Success)
            {
                return finalResult;
            }

            return MergeResult.Succeeded(
                $"{mergeResult.Message.Replace("[نجاح] ", "")} — {slideResult.Message.Replace("[نجاح] ", "")}",
                finalResult.PageCount);
        }
        finally
        {
            // الملفات الوسيطة مالهاش لازمة بعد كده، ومش عايزين نسيبها
            // تتراكم في التيمب على أجهزة المطابع
            TryDelete(merged);
            TryDelete(composed);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ملف مؤقت فضل موجود مش سبب نوقف الطباعة
        }
    }

    /// <summary>
    /// وضع "من غير دمج": كل ملف بيتعالج لوحده وبيطلع ملف ناتج لوحده.
    ///
    /// فرقين مقصودين عن وضع الدمج:
    ///
    /// ١) **ملف بايظ مابيوقفش الباقي.** في الدمج ده منطقي — مستند واحد ناقص
    ///    منه جزء يبقى غلط. هنا الملفات مستقلة، واللي واقف على الماكينة
    ///    محمّل ٢٠ ملف؛ إنه يخسر الـ ١٩ السليمين عشان واحد بايظ ده مش
    ///    سلوك برنامج مطبعة. بنعالج اللي ينفع ونقول بالاسم اللي مانفعش.
    ///
    /// ٢) **الترقيم بيفضل متصل عبر الملفات المنفصلة** لو المستخدم مختار
    ///    الترقيم المتصل: الملف الأول ١..٥ والتاني بيكمّل من ٦ من ٤٠.
    ///    عشان كده بنعد الصفحات كلها الأول قبل ما نبدأ نعالج.
    /// </summary>
    private async Task ProcessWithoutMergingAsync(IReadOnlyList<string> inputs)
    {
        var overlays = MergeRequest.From(Settings, App, inputs, string.Empty);

        // مفيش أي حاجة تتحط على الورق؟ يبقى إعادة كتابة الملفات هدر خالص —
        // بنطبع الأصول زي ما هي، وده كمان بيحافظ على جودتها بالظبط.
        if (overlays.PageNumbers is null && overlays.Watermark is null && overlays.CustomText is null)
        {
            _outputFiles = inputs.ToList();
            _outputPageCount = Files.Sum(f => f.PageCount ?? 0);
            StatusText = $"جاهز لطباعة {inputs.Count} ملف كل واحد لوحده (مفيش إضافات مطلوبة).";
            _jobLog?.Info($"وضع من غير دمج: {inputs.Count} ملف هتتطبع زي ما هي");
            return;
        }

        string folder = ResolveProcessedOutputFolder();
        Directory.CreateDirectory(folder);
        _jobLog?.Info($"بدء معالجة {inputs.Count} ملف كل واحد لوحده → {folder}");

        // عدّ الصفحات الأول: الترقيم المتصل محتاج يعرف الإجمالي قبل ما يبدأ
        var pageCounts = await CountPagesAsync(inputs);
        int grandTotal = pageCounts.Where(c => c > 0).Sum();

        var produced = new List<string>();
        var failures = new List<string>();
        int nextNumber = 1;
        int totalProcessedPages = 0;

        for (int i = 0; i < inputs.Count; i++)
        {
            string source = inputs[i];
            string name = ProcessedFileNaming.NameFor(i + 1, source);
            name = ProcessedFileNaming.MakeUnique(name, candidate => File.Exists(Path.Combine(folder, candidate)));
            string destination = Path.Combine(folder, name);

            StatusText = $"جاري المعالجة: {i + 1} من {inputs.Count} — {Path.GetFileName(source)}";

            var request = overlays with
            {
                InputFiles = [source],
                OutputPath = destination,
                PageNumbers = overlays.PageNumbers?.ContinuingFrom(nextNumber, grandTotal)
            };

            var result = await RunPipelineAsync(request);

            if (result.Success)
            {
                produced.Add(destination);
                totalProcessedPages += result.PageCount;
                nextNumber += result.PageCount;
            }
            else
            {
                failures.Add(result.Message);
                Log.Add(result.Message);
                _jobLog?.Info($"تخطّينا ملف: {result.Message}");

                // الملف ده مالوش صفحات في المخرج، بس لو عرفنا عدده بنحرّك
                // العداد عشان الترقيم يفضل مطابق للإجمالي المكتوب على الورق
                if (i < pageCounts.Count && pageCounts[i] > 0)
                {
                    nextNumber += pageCounts[i];
                }
            }
        }

        _outputFiles = produced;
        _outputPageCount = totalProcessedPages;

        if (produced.Count == 0)
        {
            StatusText = "فشلت معالجة كل الملفات. شوف التفاصيل في اللوج.";
            return;
        }

        string summary = failures.Count == 0
            ? $"تمت معالجة {produced.Count} ملف في {totalProcessedPages} صفحة."
            : $"تمت معالجة {produced.Count} ملف في {totalProcessedPages} صفحة، وفشل {failures.Count}.";

        if (SavesProcessedFilesPermanently)
        {
            summary += $" اتحفظوا في {folder}";
        }

        Log.Add($"[نجاح] {summary}");
        _jobLog?.Info(summary);
        StatusText = summary;
    }

    /// <summary>
    /// المستخدم طالب إن الملفات المعالجة تتحفظ عنده مش في التيمب؟
    ///
    /// "مجلد افتراضي لحفظ الملفات" كان بيتحفظ في الإعدادات ومحدش بيقراه —
    /// المستخدم يختار مجلد ومايوصلهوش حاجة. دلوقتي هو وجهة الملفات المعالجة.
    /// </summary>
    private bool SavesProcessedFilesPermanently =>
        Settings.SaveAfterProcessing && !string.IsNullOrWhiteSpace(App.DefaultOutputFolder);

    private string ResolveProcessedOutputFolder()
    {
        if (SavesProcessedFilesPermanently && Directory.Exists(App.DefaultOutputFolder))
        {
            return App.DefaultOutputFolder;
        }

        // مجلد لكل تشغيلة: أسامي الملفات بتبدأ بـ 01 كل مرة، فمن غير الفصل ده
        // تشغيلة النهارده هتدهس اللي قبلها
        return Path.Combine(Path.GetTempPath(), "PrintFlow", $"batch_{DateTime.Now:yyyyMMdd_HHmmss}");
    }

    /// <summary>
    /// عدد صفحات كل ملف بالترتيب. صفر = مقدرناش نقراه (تالف أو محمي).
    /// بنستخدم اللي محمّل في القايمة لو موجود، وبنقرا الباقي من القرص.
    /// </summary>
    private async Task<IReadOnlyList<int>> CountPagesAsync(IReadOnlyList<string> inputs)
    {
        var known = Files.ToDictionary(f => f.FullPath, f => f.PageCount, StringComparer.OrdinalIgnoreCase);
        var counts = new List<int>(inputs.Count);

        foreach (string path in inputs)
        {
            if (known.TryGetValue(path, out int? cached) && cached is int value)
            {
                counts.Add(value);
                continue;
            }

            int? read = _pdfInfo is null ? null : await Task.Run(() => _pdfInfo.TryGetPageCount(path));
            counts.Add(read ?? 0);
        }

        return counts;
    }

    private async Task PrintAsync()
    {
        if (_outputFiles.Count == 0)
        {
            StatusText = "اضغط \"بدء معالجة الملفات\" الأول.";
            return;
        }

        SyncSelectedPrintersToSettings();

        // بنحدّث حالة الطابعات **لحظة الطباعة** مش بنعتمد على آخر تحديث دوري.
        //
        // ليه: معالجة ملف ٢١٠ صفحة بتاخد وقت، والطابعة ممكن تكون اتقفلت أو
        // اتفصلت في الوقت ده. الفلترة على آخر حالة معروفة معناها إننا ممكن
        // نبعت لطابعة اتفصلت من دقيقتين، ونستنى المهلة كلها على الفاضي.
        //
        // اتأكد بالتجربة: الـ HP كانت WorkOffline=True والبرنامج تخطّاها صح.
        try
        {
            await RefreshPrintersAsync();
        }
        catch (Exception ex)
        {
            // فشل التحديث مش سبب نمنع الطباعة — بنكمل بآخر حالة معروفة
            _jobLog?.Error("مقدرناش نحدّث حالة الطابعات قبل الطباعة", ex);
        }

        var targets = ResolveTargetPrinters();
        if (targets.Count == 0)
        {
            StatusText = "مفيش طابعة مؤهلة متاحة حاليًا. اتأكد إن الطابعة متوصلة وشغالة.";
            return;
        }

        IsBusy = true;
        StatusText = "جاري الإرسال للطباعة...";

        _jobLog?.Info(
            $"طباعة: {_outputFiles.Count} مستند × {targets.Count} طابعة ({string.Join("، ", targets.Select(t => t.Name))}) — " +
            $"{Settings.TotalCopies} نسخة، {Settings.PaperSize}" +
            $"{(Settings.Grayscale ? "، أبيض وأسود" : "")}{(Settings.Duplex ? "، وجهين" : "")}" +
            $"{(Settings.DistributeCopies ? "، توزيع" : "")}");

        try
        {
            foreach (var file in _outputFiles)
            {
                var results = Settings.DistributeCopies && targets.Count > 1
                    ? await PrintDistributedAsync(file, targets)
                    : await PrintUniformAsync(file, targets);

                foreach (var line in results)
                {
                    Log.Add(line);
                    _jobLog?.Info(line);
                }
            }

            StatusText = $"خلص الإرسال إلى {targets.Count} طابعة.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>نفس عدد النسخ على كل طابعة، والطابعات بالتوازي.</summary>
    private async Task<string[]> PrintUniformAsync(string file, List<PrinterItem> targets)
    {
        // مفيش Task.Run دلوقتي: PrintAsync بقت غير متزامنة بجد (WaitForExitAsync)،
        // فمفيش ثريد بيتحجز وهو مستني بروسيس الطباعة يخلص.
        var tasks = targets.Select(printer =>
            _printService.PrintAsync(PrintJob.From(
                Settings, file, printer.Name, Settings.TotalCopies, _outputPageCount)));

        return await Task.WhenAll(tasks);
    }

    /// <summary>توزيع إجمالي النسخ على الطابعات.</summary>
    private async Task<string[]> PrintDistributedAsync(string file, List<PrinterItem> targets)
    {
        var distribution = CopyDistributionCalculator.Distribute(
            Settings.TotalCopies,
            targets.Select(p => p.Name).ToList());

        var tasks = distribution.Select(item =>
            _printService.PrintAsync(PrintJob.From(
                Settings, file, item.PrinterName, item.CopiesAssigned, _outputPageCount)));

        return await Task.WhenAll(tasks);
    }

    private List<PrinterItem> ResolveTargetPrinters()
    {
        IEnumerable<PrinterItem> candidates;

        if (Settings.UseMultiplePrinters)
        {
            candidates = Printers.Where(p => p.IsSelected);
        }
        else if (!string.IsNullOrWhiteSpace(Settings.PrinterName))
        {
            candidates = Printers.Where(p => p.Name == Settings.PrinterName);
        }
        else
        {
            var chosen = Printers.FirstOrDefault(p => p.IsDefault) ?? Printers.FirstOrDefault();
            candidates = chosen is null ? Enumerable.Empty<PrinterItem>() : new[] { chosen };
        }

        return candidates.Where(p => p.IsEligible).ToList();
    }

    // ══════════ تصفير ══════════

    private void Reset()
    {
        Files.Clear();
        Log.Clear();
        _outputFiles = new List<string>();
        _outputPageCount = 0;

        Settings.CopyFrom(new PrintSettings());

        foreach (var printer in Printers)
        {
            printer.IsSelected = false;
        }

        PrintCommand.RaiseCanExecuteChanged();
        StatusText = "اترجعت الإعدادات للوضع الافتراضي.";
    }

    /// <summary>
    /// بتمسح ملفات الدمج المؤقتة القديمة. من غير الحتة دي مجلد التيمب
    /// بيفضل يكبر لحد ما ياكل الهارد على أجهزة المطابع.
    /// </summary>
    private void CleanOldTempFiles()
    {
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "PrintFlow");
            if (!Directory.Exists(folder))
            {
                return;
            }

            var cutoff = DateTime.Now.AddDays(-App.TempFileRetentionDays);

            foreach (var path in Directory.EnumerateFiles(folder, "merged_*.pdf"))
            {
                if (File.GetLastWriteTime(path) < cutoff)
                {
                    File.Delete(path);
                }
            }

            // وضع "من غير دمج" بيعمل مجلد لكل تشغيلة. من غير التنضيف ده
            // المجلدات دي بتتراكم للأبد — وكل واحد فيه نسخة من كل الملفات.
            foreach (var path in Directory.EnumerateDirectories(folder, "batch_*"))
            {
                if (Directory.GetLastWriteTime(path) < cutoff)
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }
        catch
        {
            // تنضيف التيمب مش حاجة تستاهل توقف الشغل لو فشلت
        }
    }
}
