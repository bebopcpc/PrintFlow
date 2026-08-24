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
        string appVersion = "")
    {
        _jobLog = jobLog;
        _pdfInfo = pdfInfo;
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
        };

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PrintSettings.UseMultiplePrinters))
            {
                OnPropertyChanged(nameof(SinglePrinterMode));
            }
        };

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

                // كل أشكال العلامة المائية والترقيم والنص المخصص بتتجمّع هنا في طلب واحد
                _jobLog?.Info($"بدء معالجة {inputs.Count} ملف → {outputPath}");

            var request = MergeRequest.From(Settings, App, inputs, outputPath);

                // الدمج بيتعمل على ثريد تاني عشان الواجهة ماتتجمدش على ملفات كبيرة
                var result = await Task.Run(() => _mergeService.Merge(request));

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
                // TODO: المعالجة لكل ملف على حدة (علامة مائية/ترقيم من غير دمج)
                // محتاجة توسعة IPdfMergeService — دلوقتي بنطبع الملفات زي ما هي.
                _outputFiles = inputs;
                _outputPageCount = Files.Sum(f => f.PageCount ?? 0);
                StatusText = $"جاهز لطباعة {inputs.Count} ملف كل واحد لوحده (من غير معالجة).";
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
        // اتفصلت في الوقت ده. لو بعتنا لطابعة مش موجودة، SumatraPDF بيرجّع
        // كود 0 عادي (بسبب -silent) والمستخدم بيقرا "نجاح" ومفيش ورقة طلعت.
        // ده اتأكد عمليًا: تشغيلتين على طابعة مفصولة، الكود 0 والطابور فاضي.
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
        }
        catch
        {
            // تنضيف التيمب مش حاجة تستاهل توقف الشغل لو فشلت
        }
    }
}
