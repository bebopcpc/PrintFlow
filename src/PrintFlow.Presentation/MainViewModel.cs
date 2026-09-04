using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly IPdfPageScaler? _pageScaler;
    private readonly IPrinterQueue? _printerQueue;
    private readonly IImageToPdfConverter? _imageConverter;
    private readonly IIncomingJobWatcher? _incoming;

    /// <summary>
    /// بيسأل المكن عن حالتها أثناء التوزيع. لو مفيش، بنفترض إن كله تمام —
    /// يعني نفس سلوك النسخ القديمة بالظبط.
    /// </summary>
    private readonly IPrinterHealth _printerHealth;

    /// <summary>
    /// دفتر سرعات المكن. بيتقرا وقت التوزيع وبيتكتب بعد ما الأوردر يخلص.
    ///
    /// بيتبني هنا مش بيتحقن من بره عن قصد: مالوش أي اعتماد على ويندوز ولا
    /// على طابعة، ولو الملف بتاعه ضاع البرنامج بيرجع يوزّع بالتساوي زي
    /// ما كان. يعني مفيش حاجة في البرنامج بتقف عشانه.
    /// </summary>
    private readonly PrinterSpeedBook _speeds = new();

    /// <summary>
    /// ثريد الواجهة، بيتلقط لحظة بناء الـ ViewModel.
    ///
    /// **ليه ده موجود:** المراقب بيشتغل على ثريد خلفي، والأحداث بتاعته
    /// بتتنده من هناك. و<c>ObservableCollection</c> المربوطة بالواجهة
    /// بترمي استثناء لو حد عدّلها من ثريد تاني. النتيجة كانت إن أول جوب
    /// يوصل يرمي، والاستثناء يقتل حلقة المراقبة كلها في صمت — فالبرنامج
    /// يفضل مفتوح وشكله سليم ومش شايف أي جوب بعد كده.
    ///
    /// <c>SynchronizationContext</c> من الـ BCL مش من WPF، فالطبقة دي
    /// بتفضل من غير أي اعتماد على مكتبة واجهة.
    /// </summary>
    private readonly SynchronizationContext? _uiThread = SynchronizationContext.Current;

    /// <summary>
    /// المستندات الجاهزة للطباعة، كل واحد بعدد صفحاته.
    ///
    /// كانت قايمة مسارات + رقم واحد لإجمالي الصفحات. الرقم الواحد ده مكانش
    /// بيكفي لحاجتين: مهلة الانتظار المفروض تتحسب لكل مستند على حدة، والأهم
    /// إن توزيع الشغل على المكن محتاج يعرف تقل كل مستند لوحده.
    /// </summary>
    private List<PrintableDocument> _output = new();

    /// <summary>إجمالي صفحات كل المستندات الناتجة.</summary>
    private int _outputPageCount => _output.Sum(d => d.Pages);

    /// <summary>
    /// مجلد الصور المحوّلة جوه تيمب البرنامج. ثابت عن قصد: كل صور نفس
    /// التحميل بتقع جنب بعض، والتنضيف بيعرف يلاقيها.
    /// </summary>
    private const string ConvertedFolderName = "converted";

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
        IPdfPageScaler? pageScaler = null,
        IImageToPdfConverter? imageConverter = null,
        IIncomingJobWatcher? incomingWatcher = null,
        string appVersion = "",
        IPrinterHealth? printerHealth = null,
        IPrinterQueue? printerQueue = null)
    {
        _printerQueue = printerQueue;

        _printerHealth = printerHealth ?? new AlwaysFinePrinterHealth();
        _jobLog = jobLog;
        _pdfInfo = pdfInfo;
        _slideComposer = slideComposer;
        _pageScaler = pageScaler;
        _imageConverter = imageConverter;
        _incoming = incomingWatcher;
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

        // كل الزراير دي بتسأل عن IsPrinting كمان، مش بس عن نفسها.
        //
        // الحارس اللي جوه AsyncRelayCommand بيعرف الأمر بتاعه هو بس. لما
        // المعالجة بتنده الطباعة لوحدها، PrintCommand مابيعرفش إن فيه
        // طباعة ماشية — فيفضل مفتوح. ومن هنا طلع الأوردر مرتين في ١.٩.٧.
        //
        // Reset كمان: كان من غير أي شرط خالص، فينفع يمسح _output والأوردر
        // نصه بره.
        ProcessCommand = new AsyncRelayCommand(ProcessAsync, () => Files.Count > 0 && !IsPrinting);
        PrintCommand = new AsyncRelayCommand(
            PrintAsync,
            () => (_output.Count > 0 || Files.Count > 0) && !IsBusy && !IsPrinting);
        ResetCommand = new RelayCommand(Reset, () => !IsBusy && !IsPrinting);
        // ده الزرار الوحيد اللي **لازم** يشتغل والشغل ماشي، فشرطه معكوس.
        // IsBusy جوّه الشرط عشان المعالجة كمان بقى ليها إيقاف — قبل كده
        // كان اللي حمّل ٥٠ ملف ودَوس معالجة مالوش طريق غير Task Manager.
        CancelCommand = new RelayCommand(CancelPrinting, () => IsBusy || IsPrinting);
        RemoveFileCommand = new RelayCommand<PdfFileItem>(RemoveFile);

        AddPresetCommand = new RelayCommand(AddPreset, () => !string.IsNullOrWhiteSpace(NewPresetName));
        UpdatePresetCommand = new RelayCommand(UpdatePreset, () => SelectedPreset is not null);
        DeletePresetCommand = new RelayCommand(DeletePreset, () => SelectedPreset is not null);
        ApplyPresetCommand = new RelayCommand(ApplyPreset, () => SelectedPreset is not null);
        RestoreDefaultAppSettingsCommand = new RelayCommand(RestoreDefaultAppSettings);

        // IsIdle مش زيادة: تنضيف الطابور بيوقف خدمة الطباعة ويمسح الجوبات
        // اللي فيها. لو اتضغط والأوردر ماشي، الشغل اللي إحنا بعتناه هو
        // نفسه اللي هيتمسح — والمستخدم هيدفع تمن ورق طلع نُصه.
        CleanSpoolerCommand = new AsyncRelayCommand(CleanSpoolerAsync, () => IsIdle);

        // نفس شرط IsIdle: القياس بيتجمّع على طول الأوردر وبيتحفظ في آخره.
        // لو اتصفّر وهو في النص، اللي بيتحفظ بعدها بيبقى نُص قياس.
        ForgetSpeedsCommand = new RelayCommand(ForgetSpeeds, () => IsIdle);

        Files.CollectionChanged += (_, _) =>
        {
            ProcessCommand.RaiseCanExecuteChanged();
            PrintCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(FilesCountText));
            RefreshBookletSummary();
            RefreshDeleteSummary();
            RefreshDistributionSummary();
            RefreshPrinterChoiceSummary();
        };

        Settings.PropertyChanged += (_, e) =>
        {
            // ⚠ من غير شرط عن قصد.
            //
            // عدّاد الورق بيقرا تسع خصايص (النسخ، التوزيع، الوجهين،
            // الكتيّب، الشرائح، الحذف، نص الحذف، وأول وآخر صفحة). لستة
            // بالأسامي هنا معناها إن أي خيار جديد يتضاف بعد كده لازم حد
            // يفتكر يضيفه هنا كمان — وأول مرة حد ينسى، الرقم بيفضل
            // معروض غلط من غير أي علامة.
            //
            // والحسبة نفسها جمع على كام رقم صحيح، فمفيش أي تمن لتشغيلها
            // مع كل تغيير.
            RefreshPaperSummary();

            if (e.PropertyName == nameof(PrintSettings.UseMultiplePrinters))
            {
                OnPropertyChanged(nameof(SinglePrinterMode));
                RefreshPrinterChoiceSummary();
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

            // الكتيّب والوجهين قرار واحد متقسّم على مربعين في مجموعتين
            // مختلفتين. أي واحد فيهم يتغيّر، السطر الأحمر لازم يتحدّث.
            if (e.PropertyName is nameof(PrintSettings.BookletMode)
                or nameof(PrintSettings.Duplex))
            {
                OnPropertyChanged(nameof(BookletDuplexWarning));
                OnPropertyChanged(nameof(BookletDuplexIsActive));
            }

            if (e.PropertyName is nameof(PrintSettings.DeletePages)
                or nameof(PrintSettings.PagesToDelete))
            {
                RefreshDeleteSummary();
            }

            if (e.PropertyName == nameof(PrintSettings.ScalePercent))
            {
                ScaleSummary = PageScaling.Describe(Settings.ScalePercent);
            }

            if (e.PropertyName is nameof(PrintSettings.PageFrom)
                or nameof(PrintSettings.PageTo))
            {
                OnPropertyChanged(nameof(PageRangeSummary));
                OnPropertyChanged(nameof(PageRangeIsActive));
            }

            if (e.PropertyName is nameof(PrintSettings.UseMultiplePrinters)
                or nameof(PrintSettings.DistributeCopies)
                or nameof(PrintSettings.MergeFiles)
                or nameof(PrintSettings.TotalCopies))
            {
                RefreshDistributionSummary();
                RefreshPrinterChoiceSummary();
            }

            if (e.PropertyName == nameof(PrintSettings.DistributeCopies))
            {
                OnPropertyChanged(nameof(SameCountOnEveryPrinter));
            }

            // إعداد مسبق أو تاب الإعدادات العامة غيّر الطابعة → القايمة
            // لازم تعلّم عليها، وإلا المستخدم بيشوف اسم واحد والتعليم على
            // واحد تاني
            if (e.PropertyName == nameof(PrintSettings.PrinterName))
            {
                ApplyPrinterNameToTicks();
            }
        };

        if (_incoming is not null)
        {
            _incoming.JobArrived += file => OnUiThread(() => OnJobArrived(file));
            _incoming.Reported += line => OnUiThread(() => Log.Add(line));
        }

        RefreshSlidePreview();

        // كان الإعداد ده بيتحفظ ومحدش بينده SortFiles — يعني الاختيار مالوش أي أثر
        App.PropertyChanged += (_, e) =>
        {
            // السعر وطريقة الحساب عايشين في الإعدادات العامة مش في
            // إعدادات الجوب — فسطر التكلفة محفّزه هنا. من غير شرط، لنفس
            // سبب اللي في مستمع الجوب فوق.
            RefreshPaperSummary();

            if (e.PropertyName == nameof(AppSettings.FileSortOrder))
            {
                SortFiles();
            }

            // تشغيل/إيقاف الاستقبال لحظة ما المستخدم يغيّر الإعداد،
            // من غير ما يحتاج يقفل البرنامج ويفتحه
            if (e.PropertyName is nameof(AppSettings.ReceiveFromVirtualPrinter)
                or nameof(AppSettings.HotFolder))
            {
                ApplyReceptionSettings();
            }

            // الحفظ بقى لحظي (مؤجّل شوية) بدل ما يكون عند الإغلاق بس.
            // الشرح الكامل في ScheduleAppSettingsSave.
            ScheduleAppSettingsSave();
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

                // الزراير بتسأل عن الحالة دي، فلازم تتبلّغ إنها اتغيّرت.
                // من غير السطر ده، ResetCommand بيفضل مفتوح طول الشغل
                // لأن WPF مش بيعيد السؤال من نفسه على الأوامر دي.
                RefreshCommandStates();
            }
        }
    }

    /// <summary>
    /// «مفيش شغل ماشي». الواجهة كلها متربطة بيها.
    ///
    /// IsPrinting جزء منها عن قصد: كانت !IsBusy بس، وفيه لحظة IsBusy
    /// بترجع false والأوردر لسه بيتبعت. الواجهة كانت بتفتح في اللحظة دي.
    /// </summary>
    public bool IsIdle => !IsBusy && !IsPrinting;

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

    /// <summary>
    /// سطر تحت قايمة المكن بيقول اللي هيحصل لما تضغط طباعة.
    ///
    /// ═══ ليه ده موجود ═══
    ///
    /// النسخة اللي فاتت كانت بتحط هنا سطر بيقول للمستخدم "علّم على مربع
    /// كذا تحت عشان توزّع". يعني الواجهة كانت بتشرح لنفسها. لما الشرح
    /// بيبقى لازم، ده مش نقص في الشرح — ده نقص في التصميم.
    ///
    /// دلوقتي قايمة المكن نفسها هي المفتاح، والسطر ده بقى بيقول **النتيجة**
    /// مش التعليمات: "٣ مكن مختارة — ٥٠ نسخة هتتقسّم ١٧ / ١٧ / ١٦".
    /// المستخدم بيشوف القسمة قبل ما الورق يطلع، مش بعده.
    /// </summary>
    public string PrinterChoiceSummary
    {
        get
        {
            // ═══ فيه أوردر ماشي؟ السطر ده مالوش معنى دلوقتي ═══
            //
            // السطر ده بيوصف **الأوردر الجاي**، وبيتحسب من حالة المكن
            // دلوقتي. وحالة المكن بتتغيّر وسط الشغل: مكنة يخلص منها
            // الورق تبقى "خطأ"، تخرج من المؤهلين، فالسطر يقول "مكنة
            // واحدة هتطبع الـ٣٠ نسخة" — والبارات فوقه بتقول ٣ مكن
            // شغالة فعلًا.
            //
            // الاتنين صح، بس الشاشة كانت بتحطّهم جنب بعض من غير ما
            // تقول إن ده "دلوقتي" وده "الجاي". اللي واقف في المطبعة
            // بيقرا السطر على إنه وصف للأوردر اللي قدامه.
            if (IsPrinting)
            {
                return "فيه أوردر ماشي دلوقتي — شوف «تقدم الأوردر» فوق. السطر ده بيرجع أول ما يخلص.";
            }

            var ticked = Printers.Where(p => p.IsSelected && p.IsEligible).ToList();

            if (ticked.Count == 0)
            {
                // ⚠ نفس الدالة اللي الطباعة بتستخدمها — مش نسخة تانية من
                // نفس المنطق.
                //
                // النسخة القديمة كانت بتختار الاحتياطي بإيدها من غير ما
                // تسأل «هو مؤهل؟»، والطباعة بتسأل. فالسطر كان بيقول
                // «هيتطبع على PrintFlow» — وهي طابعة الاستقبال الوهمية
                // والطباعة عليها ممنوعة (الجوب بيرجع للبرنامج وتبقى حلقة)
                // — وأول ما تضغط طباعة تلاقي «مفيش طابعة مؤهلة».
                //
                // نفس درس مدى الصفحات: المعاينة والفعل لازم يقروا من
                // مصدر واحد، وإلا بيتفرّقوا من غير ما حد ياخد باله.
                var fallback = ResolveTargetPrinters().FirstOrDefault();

                return fallback is null
                    ? "مفيش طابعة مؤهلة دلوقتي — اتأكد إن المكن متوصلة ومش موقوفة."
                    : $"مفيش مكنة معلّمة — هيتطبع على \"{fallback.Name}\" لوحدها.";
            }

            if (ticked.Count == 1)
            {
                return $"مكنة واحدة: \"{ticked[0].Name}\" هتطبع الـ {Settings.TotalCopies} نسخة كلها.";
            }

            if (!Settings.DistributeCopies)
            {
                return $"{ticked.Count} مكن مختارة — كل واحدة هتطبع الـ {Settings.TotalCopies} نسخة كاملة " +
                       $"(المجموع {ticked.Count * Settings.TotalCopies} نسخة).";
            }

            // بنعرض القسمة الحقيقية اللي الموزّع هيعملها، مش تقدير.
            // نفس الدالة اللي بتشتغل وقت الطباعة بالظبط — فاللي المستخدم
            // بيشوفه هنا هو اللي هيطلع على الورق.
            var loaded = _output.Count > 0
                ? _output
                : Files.Select(f => new PrintableDocument(f.FullPath, f.PageCount ?? 0)).ToList();

            // ⚠ نفس تعديل مدى الصفحات اللي الطباعة بتعمله. من غيره السطر
            // ده بيعرض قسمة محسوبة على المستند كامل والورق بيطلع بقسمة
            // تانية — وده بالظبط اللي التعليق فوق بيقول إنه مش المفروض يحصل.
            var documents = loaded
                .Select(d => d with
                {
                    Pages = PageRange.CountIn(Settings.PageFrom, Settings.PageTo, d.Pages)
                })
                .ToList();

            if (documents.Count == 0)
            {
                return $"{ticked.Count} مكن مختارة — الشغل هيتقسّم عليهم. حمّل ملفات عشان نحسبلك القسمة.";
            }

            // نفس اللقطة اللي الطباعة بتقراها. من غيرها السطر ده بيعرض
            // القسمة بالتساوي والطباعة بتعمل قسمة موزونة — يعني الواجهة
            // بتقول رقم والورق بيطلع برقم تاني.
            var plan = WorkloadBalancer.Balance(
                documents, Settings.TotalCopies, ticked.Select(p => p.Name).ToList(), _speeds.Snapshot());

            string split = string.Join(" / ", plan.Printers.Select(p => p.Pages));

            return $"{ticked.Count} مكن مختارة — الشغل هيتقسّم عليهم بالصفحات: {split} " +
                   $"(الفرق بين أتقل وأخف مكنة {plan.Spread} صفحة).";
        }
    }

    /// <summary>
    /// مربع "كل مكنة تطبع العدد كامل".
    ///
    /// دي عكس التوزيع، ومقلوبة عن قصد: التوزيع هو الوضع الطبيعي اللي
    /// البرنامج اتعمل عشانه، فمينفعش يبقى محتاج علامة. اللي محتاج علامة
    /// هو الحالة النادرة — نسخة كاملة لكل فرع مثلًا.
    /// </summary>
    public bool SameCountOnEveryPrinter
    {
        get => !Settings.DistributeCopies;
        set
        {
            if (Settings.DistributeCopies == !value)
            {
                return;
            }

            Settings.DistributeCopies = !value;
            OnPropertyChanged();
            RefreshPrinterChoiceSummary();
        }
    }

    /// <summary>بيحدّث السطر بعد أي تغيير في التعليم أو عدد النسخ أو الملفات.</summary>
    private void RefreshPrinterChoiceSummary()
    {
        OnPropertyChanged(nameof(PrinterChoiceSummary));

        // عدّاد الورق بيتغيّر مع نفس الحاجات بالظبط: التعليم على المكن،
        // الملفات، عدد النسخ، وبداية ونهاية الأوردر. محفّز واحد للاتنين
        // بدل ما نفتكر نضيف نداء في ست أماكن.
        RefreshPaperSummary();
    }

    /// <summary>بيحدّث سطر الورق وسطر التكلفة وظهورهم.</summary>
    private void RefreshPaperSummary()
    {
        OnPropertyChanged(nameof(PaperSummary));
        OnPropertyChanged(nameof(PaperSummaryIsVisible));
        OnPropertyChanged(nameof(CostSummary));
        OnPropertyChanged(nameof(CostSummaryIsVisible));
    }

    /// <summary>
    /// بيخلّي حالة "مكنة واحدة معلّمة" و<see cref="PrintSettings.PrinterName"/>
    /// متطابقين في الاتجاهين.
    ///
    /// لازم عشان الاسم ده هو اللي بيتحفظ في الإعدادات المسبقة وبيتقرا من
    /// تاب الإعدادات العامة. من غيره، المستخدم يطبّق إعداد مسبق فيه طابعة
    /// ويلاقي القايمة معلّمة على حاجة تانية خالص.
    /// </summary>
    /// <summary>
    /// الاتجاه العكسي: الاسم اتغيّر من بره (إعداد مسبق / الإعدادات العامة)
    /// → نعلّم عليه في القايمة.
    ///
    /// بنعمل ده بس لما التعليم يكون **مكنة واحدة أو ولا حاجة**. لو
    /// المستخدم معلّم على تلاتة وبيوزّع، مش هنمسح اختياره عشان اسم
    /// اتحفظ في إعداد قديم.
    /// </summary>
    private void ApplyPrinterNameToTicks()
    {
        if (_syncingPrinterName || Printers.Count(p => p.IsSelected) > 1)
        {
            return;
        }

        var target = Printers.FirstOrDefault(p => p.Name == Settings.PrinterName);

        if (target is null)
        {
            return;
        }

        _syncingPrinterName = true;

        try
        {
            foreach (var printer in Printers)
            {
                printer.IsSelected = ReferenceEquals(printer, target);
            }
        }
        finally
        {
            _syncingPrinterName = false;
        }
    }

    /// <summary>بيمنع المزامنة إنها تنده نفسها ذهاب وعودة.</summary>
    private bool _syncingPrinterName;

    private void SyncSinglePrinterName()
    {
        if (_syncingPrinterName)
        {
            return;
        }

        var ticked = Printers.Where(p => p.IsSelected).ToList();

        if (ticked.Count == 1 && Settings.PrinterName != ticked[0].Name)
        {
            _syncingPrinterName = true;

            try
            {
                Settings.PrinterName = ticked[0].Name;
            }
            finally
            {
                _syncingPrinterName = false;
            }
        }
    }

    /// <summary>كام مستند جاهز للطباعة بعد المعالجة. في وضع الدمج بيبقى ١.</summary>
    public int OutputFileCount => _output.Count;

    // ══════════ شاشة التقدم ══════════

    /// <summary>صف لكل مكنة شغّالة في الأوردر الحالي.</summary>
    public ObservableCollection<PrinterProgress> Progress { get; } = new();

    private bool _showProgress;
    /// <summary>الشاشة بتبان وقت الشغل بس — مش عايزين بارات فاضية طول اليوم.</summary>
    public bool ShowProgress
    {
        get => _showProgress;
        private set => SetProperty(ref _showProgress, value);
    }

    private int _orderPagesPlanned;

    /// <summary>نسبة الأوردر كله — دي اللي بتتحط في شريط الحالة تحت.</summary>
    public double OrderPercent => _orderPagesPlanned <= 0
        ? 0
        : Math.Min(100d, Progress.Sum(p => p.PagesDone) * 100d / _orderPagesPlanned);

    public string OrderProgressText
    {
        get
        {
            if (!ShowProgress)
            {
                return "";
            }

            int done = Progress.Sum(p => p.PagesDone);
            int machines = Progress.Count(p => p.PagesDone > 0);

            return $"{done} من {_orderPagesPlanned} صفحة ({OrderPercent:0}٪) على {machines} مكنة";
        }
    }

    /// <summary>
    /// بيجهّز صفوف التقدم من الخطة.
    ///
    /// الأرقام اللي هنا **توقُّع** مش أمر: الموزّع ممكن ينقل شغل من مكنة
    /// وقعت لمكنة تانية، وساعتها الصف بتاعها بيعدّي نصيبه. الرقم اللي جنب
    /// البار هو اللي بيقول اللي حصل فعلًا.
    /// </summary>
    private void StartProgress(IReadOnlyList<(string Name, int Pages, int Copies)> rows)
    {
        Progress.Clear();

        foreach (var row in rows)
        {
            Progress.Add(new PrinterProgress(row.Name, row.Pages, row.Copies));
        }

        _orderPagesPlanned = rows.Sum(r => r.Pages);
        ShowProgress = Progress.Count > 0;
        RefreshOrderProgress();

        // من هنا وطول الأوردر، بنسأل ويندوز كل شوية: الطابعة طلّعت كام
        // فعلًا؟ من غير await عن قصد — الحلقة دي مالهاش دعوة بالطباعة،
        // ولو وقعت الأوردر يكمّل عادي.
        _ = PollPrinterQueuesAsync(PrintToken);
    }

    /// <summary>كل قد إيه نسأل الطابعات. تانيتين: حي كفاية، وخفيف على WMI.</summary>
    private static readonly TimeSpan QueuePollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// بيسأل طابور كل مكنة كل تانيتين ويحدّث السطر التاني في صفها.
    ///
    /// ═══ ليه ده لازم يبقى موجود ═══
    ///
    /// عدّادنا بيزيد لما **القطعة تتسلّم للسبولر** — قطعة ١٨٠ صفحة بتتحسب
    /// في ثانية واحدة. بعدها البار بيقف مايتحركش عشر دقايق كاملة والورق
    /// بيطلع قدام اللي واقف في المطبعة. وهو طبعًا بيفتكر إن البرنامج علّق.
    ///
    /// الحلقة دي بتجيب الرقم من **الطابعة نفسها**، فالسطر بيتحرك مع الورق.
    ///
    /// ═══ قواعد السلامة ═══
    ///
    ///   • WMI بطيء وبيتعلّق أحيانًا، فبيتنده جوّه <c>Task.Run</c> —
    ///     الواجهة عمرها ما تستناه.
    ///   • أي رمية بتتبلع. ده عرض، مش طباعة.
    ///   • لما الأوردر يخلص بنصفّر السطور، وإلا هتفضل مكتوب فيها كلام
    ///     قديم عن طابور فاضي من ساعة.
    /// </summary>
    private async Task PollPrinterQueuesAsync(CancellationToken token)
    {
        if (_printerQueue is null)
        {
            return;
        }

        // ⚠ **لقطة مرة واحدة، دلوقتي، قبل أول await.**
        //
        // الحلقة دي بتعيش طول الأوردر جنب الطباعة. لو فضلت تقرا من
        // <c>Progress</c> جوّه الحلقة، بتبقى بتلمس نفس اللستة اللي
        // <c>StartProgress</c> بيعمللها Clear وبعدين Add لما أوردر
        // تاني يبدأ — واللمستين دول على تريدين مختلفين بيبوّظوا
        // <c>ObservableCollection</c> من جوه وبترمي IndexOutOfRange
        // وسط الطباعة.
        //
        // مش نظري: تست «الضغط على طباعة مرتين» مسكها. وفي الواجهة
        // الحقيقية <c>OnUiThread</c> بيوصّل الشغل لتريد الواجهة —
        // بس لما مافيش SynchronizationContext (التستات، وأي مستضيف
        // مش WPF) بينفّذ **في مكانه** على تريد الخلفية.
        //
        // الصفوف نفسها كائنات مستقلة، فتحديثها بعد كده أمان — وحتى
        // لو أوردر تاني بدأ، بنكون بنحدّث صفوف قديمة محدش شايفها.
        var rows = Progress.ToArray();

        if (rows.Length == 0)
        {
            return;
        }

        var names = rows
            .Select(row => row.PrinterName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        try
        {
            while (!token.IsCancellationRequested && IsPrinting)
            {
                var states = await Task.Run(
                    () => names.ToDictionary(
                        name => name, name => _printerQueue.Read(name), StringComparer.Ordinal),
                    token);

                OnUiThread(() =>
                {
                    foreach (var row in rows)
                    {
                        if (states.TryGetValue(row.PrinterName, out var state))
                        {
                            row.Queue = state;
                        }
                    }
                });

                await Task.Delay(QueuePollInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
            // الأوردر اتلغى — طبيعي
        }
        catch
        {
            // مصدر معلومة للعرض. مايوقفش حاجة.
        }
        finally
        {
            // نفس اللقطة برضه — مش اللستة الحية.
            OnUiThread(() =>
            {
                foreach (var row in rows)
                {
                    row.Queue = PrinterQueueState.Idle;
                }
            });
        }
    }

    /// <summary>قطعة خلصت على مكنة. بيتنده من جوّه دالة الطباعة اللي الموزّع بينديها.</summary>
    private void RecordProgress(string printerName, WorkUnit unit, PrintOutcome outcome)
    {
        var row = Progress.FirstOrDefault(p => p.PrinterName == printerName);

        if (row is null)
        {
            return;
        }

        row.Record(unit, outcome);
        RefreshOrderProgress();
    }

    /// <summary>
    /// بيحرّك البار بعد **كل دفعة** في الجوب الكبير، والجوب لسه ماشي.
    ///
    /// ═══ المشكلة اللي بيحلها ═══
    ///
    /// الأوردر الكبير بيتبعت على دفعات، والنتيجة النهائية بتوصل بعد آخر
    /// دفعة بس. النتيجة إن البار بيفضل على صفر عشر دقايق وبعدين يقفز ١٠٠٪.
    ///
    /// واللي واقف على المكنة بيفتكر البرنامج واقف فيدوس «إيقاف فوري» —
    /// وساعتها الطابعات بتفضل تطلّع اللي في ذاكرتها وتقف في أوقات مختلفة.
    /// دي بالظبط شكوى «وقفت عند الورقة ٤٠»: البرنامج كان شغّال، بس ساكت.
    /// </summary>
    private void NoteChunkProgress(string printerName, WorkUnit slice)
    {
        var row = Progress.FirstOrDefault(p => p.PrinterName == printerName);

        if (row is null)
        {
            return;
        }

        row.NoteChunk(slice.Copies, slice.Weight);
        RefreshOrderProgress();
    }

    /// <summary>الأوردر خلص — كل صف بيقفل على كلمة أخيرة والبارات بتفضل بانة.</summary>
    /// <param name="orderCompleted">
    /// الأوردر مشي لآخره؟ لو المستخدم وقّفه، مفيش صف بيتقال عنه "خلصت".
    /// </param>
    private void FinishProgress(bool orderCompleted)
    {
        foreach (var row in Progress)
        {
            row.Finish(orderCompleted);
        }

        RefreshOrderProgress();
    }

    private void RefreshOrderProgress()
    {
        OnPropertyChanged(nameof(OrderPercent));
        OnPropertyChanged(nameof(OrderProgressText));
    }

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

    private string _pagesToDeleteSummary = "";
    /// <summary>
    /// وصف اللي هيتشال، بيتحدّث مع كل حرف بيتكتب.
    ///
    /// أهم حالة فيه: "مفيش أرقام مفهومة". من غيره، حد بيكتب "الأولى" بالكلام
    /// كان هيضغط معالجة ويلاقي مفيش حاجة اتشالت ومايعرفش ليه.
    /// </summary>
    public string PagesToDeleteSummary
    {
        get => _pagesToDeleteSummary;
        private set => SetProperty(ref _pagesToDeleteSummary, value);
    }

    private string _scaleSummary = PageScaling.Describe(100);
    /// <summary>وصف المقياس بالكلام — بيفرّق بين "هامش أبيض" و"هيتقص".</summary>
    public string ScaleSummary
    {
        get => _scaleSummary;
        private set => SetProperty(ref _scaleSummary, value);
    }

    /// <summary>
    /// تحذير مدى الصفحات — بيظهر بس لما المستخدم طالب جزء من المستند.
    ///
    /// ═══ ليه تحذير مش مجرد وصف ═══
    ///
    /// المدى بيتحفظ في الإعدادات وفي الـ Preset، يعني بيفضل شغّال بعد ما
    /// المستخدم يقفل البرنامج ويفتحه. من غير سطر أحمر واضح، حد يظبط
    /// "من ٥ لـ ٢٠" لأوردر واحد وينسى، وكل أوردر بعد كده يتقص من غير ما
    /// حد ياخد باله — وده أوردر رايح في الزبالة.
    /// </summary>
    public string PageRangeSummary => PageRange.Describe(Settings.PageFrom, Settings.PageTo);

    /// <summary>المدى شغّال؟ ده اللي بيظهّر السطر الأحمر ويخفيه.</summary>
    public bool PageRangeIsActive => PageRange.IsSubset(Settings.PageFrom, Settings.PageTo);

    /// <summary>
    /// تحذير الكتيّب من غير وجهين.
    ///
    /// الكتيّب بيعيد ترتيب الصفحات على أساس إن الورقة هتتطبع من الوجهين
    /// وتتطوي. لو الوجهين مقفول، كل وش بيروح على ورقة لوحده — الورق
    /// ضِعف اللازم ونُصّه فاضي، والطي مابيدّيش كتيّب.
    ///
    /// ⚠ والاتنين في مجموعتين مختلفتين في الواجهة: الكتيّب في "خيارات
    /// البوكليت" والوجهين في "خيارات الطباعة" تحتها. فاللي بيفتح الكتيّب
    /// مش شايف حالة الوجهين قدامه أصلًا.
    ///
    /// القرار نفسه في <see cref="BookletRules"/> — مش مكتوب هنا — عشان
    /// يفضل مصدر واحد لو احتجناه في مكان تاني.
    /// </summary>
    public string BookletDuplexWarning => BookletRules.Describe(Settings);

    /// <summary>في مشكلة؟ ده اللي بيظهّر السطر ويخفيه.</summary>
    public bool BookletDuplexIsActive => BookletRules.NeedsDuplex(Settings);

    /// <summary>
    /// الورق المتوقع من الأوردر ده — قبل ما حد يضغط طباعة.
    ///
    /// ═══ ليه ورق مش صفحات ═══
    ///
    /// اللي بيتحضّر وبيتسعّر في المطبعة ورق. "٢٤٠ صفحة" على الوجهين
    /// واتنين في الورقة = ٦٠ ورقة — واللي حضّر ٢٤٠ حضّر أربع أضعاف.
    ///
    /// ═══ الملف اللي اتعالج بيتحسب بطريقة تانية ═══
    ///
    /// بعد المعالجة، الحذف والتجميع اتنفّذوا على الملف خلاص. لو حسبناهم
    /// تاني الرقم بيطلع نُصّه. عشان كده بنقول لـ <see cref="PaperCount"/>
    /// إحنا في أنهي مرحلة بدل ما نخمّن.
    /// </summary>
    public string PaperSummary
    {
        get
        {
            var (pages, machines, processed) = CountingInputs;

            return PaperCount.Describe(pages, Settings, machines, processed);
        }
    }

    /// <summary>فيه رقم نعرضه أصلًا؟ بيرجّع false لما الأعداد لسه مجهولة.</summary>
    public bool PaperSummaryIsVisible => PaperSummary.Length > 0;

    /// <summary>
    /// تكلفة الأوردر بسعر الوحدة المكتوب في الإعدادات العامة.
    ///
    /// بيختفي خالص لما مفيش سعر متكتوب — مش بيعرض صفر. الصفر جنب أوردر
    /// حقيقي بيبان زي عطل والمستخدم بيقعد يدوّر على السبب.
    /// </summary>
    public string CostSummary
    {
        get
        {
            var (pages, machines, processed) = CountingInputs;
            var tally = PaperCount.For(pages, Settings, machines, processed);

            return PriceEstimate.Describe(tally, App.UnitPrice, App.CountingMethod);
        }
    }

    /// <summary>فيه سعر متكتوب وورق يتحسب عليه؟</summary>
    public bool CostSummaryIsVisible => CostSummary.Length > 0;

    /// <summary>
    /// مدخلات العدّ — سطر الورق وسطر التكلفة بيقروا منها الاتنين.
    ///
    /// ⚠ لازم تفضل مصدر واحد. لو كل سطر حسب مدخلاته بنفسه، أول تعديل
    /// على واحد فيهم بيخلّي السطرين يقولوا أرقام مش من نفس الأوردر —
    /// ونفس الدرس اتكرر معانا في PrinterChoiceSummary قبل كده.
    /// </summary>
    private (List<int> Pages, int Machines, bool Processed) CountingInputs
    {
        get
        {
            bool processed = _output.Count > 0;

            var pages = processed
                ? _output.Select(d => d.Pages).ToList()
                : Files.Select(f => f.PageCount ?? 0).ToList();

            // نفس شرط الأهلية اللي الطباعة بتستخدمه. من غير توزيع كل
            // مكنة بتطلّع العدد كامل، فالعدد ده بيضرب الورق.
            int machines = Math.Max(1, Printers.Count(p => p.IsSelected && p.IsEligible));

            return (pages, machines, processed);
        }
    }

    private string _distributionSummary = "";
    /// <summary>وصف اللي هيتوزّع على المكن، قبل ما الشغل يبدأ.</summary>
    public string DistributionSummary
    {
        get => _distributionSummary;
        private set => SetProperty(ref _distributionSummary, value);
    }

    private bool _distributionIsWarning;
    /// <summary>الوصف ده تحذير ولا معلومة؟ الواجهة بتلوّنه على أساسها.</summary>
    public bool DistributionIsWarning
    {
        get => _distributionIsWarning;
        private set => SetProperty(ref _distributionIsWarning, value);
    }

    /// <summary>
    /// بيقول للمستخدم الشغل هيتقسّم إزاي **قبل** ما يضغط.
    ///
    /// أهم حالة فيه هي فخّ الدمج: لو "دمج وحفظ الملفات" شغّال، الـ ٥٠ ملزمة
    /// بتبقى مستند واحد، والتوزيع مالوش حاجة يقسّمها غير النسخ. اللي طالب
    /// "وزّع الـ ٥٠ ملزمة على الـ ١٠ مكن" هيلاقي التقسيم مش زي ما توقّع
    /// ومش هيعرف ليه. التحذير ده بيقفل الفجوة دي قبل ما الورق يطلع.
    /// </summary>
    public void RefreshDistributionSummary()
    {
        // البوابة كانت بتسأل عن مفتاح "أكتر من طابعة". المفتاح ده اتشال من
        // الواجهة في ١.٩.٦ (بقى التعليم هو المفتاح)، فلو سبناها زي ما هي
        // كان **تحذير فخّ الدمج مايظهرش تاني أبدًا** — وهو أهم سطر هنا.
        if (!Settings.DistributeCopies)
        {
            DistributionSummary = "";
            DistributionIsWarning = false;
            return;
        }

        if (Settings.MergeFiles && Files.Count > 1)
        {
            DistributionIsWarning = true;
            DistributionSummary =
                $"تنبيه: \"دمج وحفظ الملفات\" شغّال، فالـ {Files.Count} ملف هيبقوا مستند واحد " +
                "والتوزيع هيقسّم نسخه بس. اقفل الدمج عشان الملازم تتوزّع على المكن.";
            return;
        }

        DistributionIsWarning = false;

        if (Files.Count == 0)
        {
            DistributionSummary = "حمّل ملفات عشان نحسبلك التقسيم.";
            return;
        }

        int machines = Printers.Count(p => p.IsSelected && p.IsEligible);

        if (machines == 0)
        {
            DistributionSummary = "اختار المكن من القايمة.";
            return;
        }

        // نفس قاعدة الموازن: صفحة على الأقل حتى لو مقدرناش نعد الصفحات
        int pages = Files.Sum(f => Math.Max(1, f.PageCount ?? 0)) * Settings.TotalCopies;

        DistributionSummary =
            $"{Files.Count} ملف × {Settings.TotalCopies} نسخة ≈ {pages} صفحة، " +
            $"هتتقسّم على {machines} مكنة (حوالي {pages / machines} صفحة لكل واحدة).";
    }

    /// <summary>
    /// بيحسب الوصف من **أقل عدد صفحات** في الملفات المحمّلة.
    ///
    /// ليه الأقل مش المتوسط ولا الإجمالي: الحذف بيتنفّذ على كل ملف لوحده،
    /// فأول ملف هيتأثر بمدى كبير هو أقصر ملف. لو حسبنا على الأكبر، التحذير
    /// "ده هيشيل كل صفحات الملف" مكانش هيظهر غير لما يبقى فات الأوان.
    /// </summary>
    private void RefreshDeleteSummary()
    {
        if (!Settings.DeletePages)
        {
            PagesToDeleteSummary = "";
            return;
        }

        var counts = Files.Select(f => f.PageCount ?? 0).Where(c => c > 0).ToList();
        int smallest = counts.Count > 0 ? counts.Min() : 0;

        PagesToDeleteSummary = PageRanges.Describe(Settings.PagesToDelete, smallest);
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
        new EnumOption<CountingMethod>(CountingMethod.ByPage, "بالوجه (كل وش مطبوع)"),
        new EnumOption<CountingMethod>(CountingMethod.BySheet, "بالورقة (بوجهيها)")
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
    public RelayCommand CancelCommand { get; }
    public RelayCommand<PdfFileItem> RemoveFileCommand { get; }
    public RelayCommand AddPresetCommand { get; }
    public RelayCommand UpdatePresetCommand { get; }
    public RelayCommand DeletePresetCommand { get; }
    public RelayCommand ApplyPresetCommand { get; }
    public RelayCommand RestoreDefaultAppSettingsCommand { get; }
    public AsyncRelayCommand CleanSpoolerCommand { get; }
    public RelayCommand ForgetSpeedsCommand { get; }

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

    /// <summary>
    /// بيحفظ الإعدادات المسبقة. زي حفظ التفضيلات بالظبط: **مابيرميش**.
    ///
    /// كان بينده المخزن على طول من غير أي حماية. يعني لو الكتابة فشلت
    /// لأي سبب — مضاد فيروسات ماسك الملف، القرص مليان، المجلد للقراءة بس —
    /// الاستثناء كان بيطلع من زرار "إضافة إعداد مسبق" ويوقّع البرنامج
    /// وسط الشغل. حفظ Preset مايستاهلش ده.
    /// </summary>
    private void PersistPresets()
    {
        try
        {
            _presetStore?.SaveAll(Presets);
        }
        catch (Exception exception)
        {
            string line = $"[تنبيه] مقدرناش نحفظ الإعدادات المسبقة: {exception.Message}";
            Log.Add(line);
            _jobLog?.Info(line);
        }
    }

    // ══════════ الإعدادات العامة ══════════

    /// <summary>
    /// كام مللي نستنى بعد آخر تغيير قبل ما نكتب على القرص.
    ///
    /// موجودة كخاصية عشان التستات تخليها صفر وتشتغل متزامنة من غير انتظار.
    /// </summary>
    public int SaveDelayMilliseconds { get; set; } = 800;

    /// <summary>
    /// الحفظ المؤجّل الشغّال دلوقتي — التستات بتستناه.
    ///
    /// لما يخلّص، تبقى الكتابة اتعملت **مرة واحدة** لكل موجة تغييرات،
    /// مش مرة لكل تغيير. ده مش تفصيلة تجميل: التست بيعتمد عليه، وأول
    /// نسخة من الكود ده كانت بتعمل Task لكل تغيير — فالتست اللي المفروض
    /// يحرس على التجميع كان بيعدّي حتى وأنا شايل التجميع، لأنه كان
    /// بيستنى آخر Task بس والباقي لسه في الطابور.
    /// </summary>
    public Task PendingSave { get; private set; } = Task.CompletedTask;

    private readonly object _saveGate = new();
    private bool _saveDirty;
    private bool _saveLoopLive;
    private Task? _saveLoop;

    /// <summary>
    /// بيحفظ تفضيلات البرنامج **حالًا**.
    ///
    /// مابيرميش استثناء أبدًا: المبدأ في المشروع ده إن مطبعة ماتقفش عشان
    /// ملف إعدادات. لو القرص مليان أو الصلاحيات ناقصة، بنكتب سطر في اللوج
    /// ونكمّل — البديل إن البرنامج يقع وهو بيتقفل، والمستخدم ماياخدش باله
    /// إن إعداداته ضاعت أصلًا.
    /// </summary>
    public void SaveAppSettings()
    {
        try
        {
            _settingsStore?.Save(App);
        }
        catch (Exception exception)
        {
            _jobLog?.Info($"مقدرناش نحفظ الإعدادات العامة: {exception.Message}");
        }
    }

    /// <summary>
    /// بيحجز حفظ بعد شوية سكوت، وبيلغي الحجز القديم لو التغيير لسه مستمر.
    ///
    /// ═══ ليه ده موجود ═══
    ///
    /// الإعدادات العامة كانت بتتحفظ في مكان **واحد** بس: لحظة إغلاق النافذة.
    /// يعني أي حاجة تمنع الإغلاق النضيف — الكهربا تقطع، الجهاز يعمل ريستارت،
    /// حد يقفل البرنامج من Task Manager، البرنامج يقع — بتضيّع كل حاجة
    /// المستخدم غيّرها في الجلسة دي.
    ///
    /// وأخطر واحدة فيهم الاستقبال: بتعلّم على المربع، يشتغل قدامك على طول،
    /// وتفتكره اتحفظ — وتلاقيه مقفول تاني بعد ريستارت من غير ما تعرف ليه.
    /// (ده بالظبط اللي اتشاف في التجربة، واتنسب لزرار التصفير الأحمر —
    /// والزرار ده أصلًا مابيلمسش الإعدادات العامة، التست
    /// <c>Reset_Never_Touches_The_General_Settings</c> حارس على كده.)
    ///
    /// ليه بنأجّل بدل ما نكتب على طول؟ سحب مؤشر "درجة الظهور" بيبعت عشرات
    /// الإشعارات في الثانية، والكتابة الفورية معناها عشرات الملفات المؤقتة
    /// والاستبدالات — على هارد بطيء في مطبعة ده بيلجلج الواجهة.
    ///
    /// الشكل: **حلقة واحدة وعلَم "فيه تغيير"**، مش Task لكل تغيير. أي
    /// تغيير بيرفع العلم؛ الحلقة بتنزّل العلم وتستنى؛ لو العلم اترفع تاني
    /// وهي مستنية بتستنى كمان دورة. أول ما تعدّي دورة كاملة في سكوت
    /// بتكتب مرة واحدة وتخلص.
    ///
    /// ده مش بس أبسط من الشكل الأول — ده اللي بيخلي
    /// <c>PendingSave</c> يعني حاجة: هو الحلقة نفسها، فلما يخلّص تبقى
    /// الكتابة اتعملت فعلًا ومفيش حاجة تانية في الطابور.
    /// </summary>
    private void ScheduleAppSettingsSave()
    {
        lock (_saveGate)
        {
            _saveDirty = true;

            if (_saveLoopLive)
            {
                return;
            }

            _saveLoopLive = true;
            _saveLoop = SaveWhenQuietAsync();
            PendingSave = _saveLoop;
        }
    }

    private async Task SaveWhenQuietAsync()
    {
        while (true)
        {
            if (SaveDelayMilliseconds > 0)
            {
                await Task.Delay(SaveDelayMilliseconds);
            }

            lock (_saveGate)
            {
                // جه تغيير تاني وإحنا مستنيين؟ نستنى دورة كمان
                if (_saveDirty)
                {
                    _saveDirty = false;
                    continue;
                }

                // سكوت. بننزّل العلم **وإحنا ماسكين القفل** وبنكتب جواه:
                // كده أي تغيير بيجي في نفس اللحظة يستنى، يلاقي الحلقة
                // خلصت، ويبدأ واحدة جديدة — بدل ما يضيع في الشق ما بين
                // "خلصنا" و"كتبنا".
                _saveLoopLive = false;
                SaveAppSettings();
                return;
            }
        }
    }

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
    ///
    /// **استثناء واحد مقصود:** الخصايص المعلّمة بـ
    /// <see cref="ConnectionSettingAttribute"/> (الاستقبال والمجلد المراقَب)
    /// بتفضل زي ما هي. السبب مشروح في السمة نفسها: رجوع لون الترقيم
    /// للافتراضي بيبان في نص ثانية، وقفل الاستقبال مابيبانش خالص —
    /// الشغل الجاي من بره بيروح في الهوا والبرنامج شكله سليم.
    /// </summary>
    private void RestoreDefaultAppSettings()
    {
        var defaults = new AppSettings();

        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (property.IsDefined(typeof(ConnectionSettingAttribute), inherit: true))
            {
                continue;
            }

            property.SetValue(App, property.GetValue(defaults));
        }

        SaveAppSettings();

        // بنقول بالنص إيه اللي ماتغيرش — الصمت هنا هو اللي بيخلي الناس
        // تفتكر إن الزرار قفل الاستقبال
        StatusText = "اترجعت الإعدادات العامة للوضع الافتراضي. إعدادات الاستقبال ماتغيرتش.";
    }

    // ══════════ الملفات ══════════

    /// <summary>بتضيف ملفات جديدة وبتتجاهل المكرر. بترجّع عدد اللي اتضاف فعلاً.</summary>
    public int AddFiles(IEnumerable<string> paths)
    {
        int added = 0;
        int converted = 0;
        var office = new List<string>();
        var unsupportedImages = new List<string>();
        var failed = new List<string>();

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var kind = SupportedInput.KindOf(path);

            if (kind == InputKind.Office)
            {
                // بيتقال بالاسم بدل ما يتجاهل في صمت — المستخدم رماه جوه
                // وله سبب، والمفروض يعرف ليه مادخلش
                office.Add(Path.GetFileName(path));
                continue;
            }

            if (kind == InputKind.UnsupportedImage)
            {
                unsupportedImages.Add(Path.GetFileName(path));
                continue;
            }

            if (kind == InputKind.Unsupported)
            {
                continue;
            }

            if (Files.Any(f => string.Equals(f.SourcePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string usable = path;

            if (kind == InputKind.Image)
            {
                // الصورة بتتحوّل هنا مش وقت المعالجة، عشان باقي البرنامج
                // ما يشوفش غير PDF. كده الترقيم والعلامة المائية والتجميع
                // والمعاينة كلهم شغالين عليها من غير أي كود خاص بالصور.
                string? pdf = ConvertImage(path);

                if (pdf is null)
                {
                    failed.Add(Path.GetFileName(path));
                    continue;
                }

                usable = pdf;
                converted++;
            }

            var info = new FileInfo(usable);
            Files.Add(new PdfFileItem(usable, info.Length, info.LastWriteTimeUtc)
            {
                SourcePath = path
            });

            added++;
        }

        StatusText = DescribeAdditions(added, converted, office, unsupportedImages, failed);

        if (added > 0)
        {
            // بيكمل في الخلفية: قراية 20 ملف ممكن تاخد لحظة، والواجهة ماتستناش
            _ = LoadPageCountsAsync();
        }

        return added;
    }

    private string DescribeAdditions(
        int added,
        int converted,
        List<string> office,
        List<string> unsupportedImages,
        List<string> failed)
    {
        var parts = new List<string>();

        if (added > 0)
        {
            parts.Add(converted > 0
                ? $"اتضاف {added} ملف (منهم {converted} صورة اتحوّلت لـ PDF). الإجمالي {Files.Count}."
                : $"اتضاف {added} ملف. الإجمالي {Files.Count}.");
        }

        if (office.Count > 0)
        {
            parts.Add($"وورد/بوربوينت لسه مش مدعومين: {Names(office)}. حوّلهم لـ PDF الأول.");
        }

        if (unsupportedImages.Count > 0)
        {
            parts.Add($"صيغة الصورة مش مدعومة: {Names(unsupportedImages)}. " +
                      "المدعوم JPG و PNG و BMP — احفظها JPEG وهتشتغل.");
        }

        if (failed.Count > 0)
        {
            parts.Add($"مقدرناش نقرا: {Names(failed)}. التفاصيل في اللوج.");
        }

        return parts.Count > 0
            ? string.Join(" ", parts)
            : "مفيش ملفات جديدة اتضافت (لازم تكون PDF أو صورة، ومش مكررة).";
    }

    /// <summary>أول تلات أسامي وبعدين "وكذا غيرهم" — عشان الشريط ما يطولش.</summary>
    private static string Names(List<string> names)
        => string.Join("، ", names.Take(3)) + (names.Count > 3 ? $" و{names.Count - 3} غيرهم" : "");

    /// <summary>
    /// بيحوّل صورة لـ PDF ويرجّع مساره، أو null لو فشل.
    ///
    /// الملف الناتج بيروح لمجلد الإخراج الدايم لو المستخدم طالب "حفظ الملفات
    /// المحوّلة"، وإلا بيروح للتيمب زي أي ملف وسيط.
    /// </summary>
    private string? ConvertImage(string imagePath)
    {
        if (_imageConverter is null)
        {
            return null;
        }

        try
        {
            // مش بننده ResolveProcessedOutputFolder عن قصد: هي مربوطة بـ
            // "حفظ الملفات بعد المعالجة" وبتعمل مجلد جديد بتوقيت كل نداء —
            // وده كان هيفرّق صور نفس التحميل على مجلدات مختلفة لو الثانية
            // اتغيرت بينهم. التحويل ليه شرطه وليه مجلد ثابت.
            string folder = Settings.SaveConvertedToPdf &&
                            !string.IsNullOrWhiteSpace(App.DefaultOutputFolder) &&
                            Directory.Exists(App.DefaultOutputFolder)
                ? App.DefaultOutputFolder
                : Path.Combine(Path.GetTempPath(), "PrintFlow", ConvertedFolderName);

            Directory.CreateDirectory(folder);

            string stem = ProcessedFileNaming.StemOf(imagePath);
            string name = ProcessedFileNaming.MakeUnique(
                stem + ".pdf", candidate => File.Exists(Path.Combine(folder, candidate)));

            string destination = Path.Combine(folder, name);

            var result = _imageConverter.Convert(new ImageConvertRequest
            {
                InputPath = imagePath,
                OutputPath = destination
            });

            if (!result.Success)
            {
                Log.Add(result.Message);
                _jobLog?.Info(result.Message);
                return null;
            }

            _jobLog?.Info(result.Message);
            return destination;
        }
        catch (Exception ex)
        {
            _jobLog?.Info($"فشل تحويل صورة: {ex.Message}");
            return null;
        }
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

        // عدد الصفحات هو اللي بيخلّي وصف الحذف يعرف يقول "ده هيشيل كل الملف"
        RefreshDeleteSummary();

        // والتوزيع محتاجه عشان يحسب "حوالي كام صفحة لكل مكنة"
        RefreshDistributionSummary();

        // وعدّاد الورق كان بيقول "" طول ما الأعداد لسه بتتقرا
        RefreshPaperSummary();

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

                // اختيار مكنة بيغيّر التقسيم، والمستخدم لازم يشوف الرقم
                // بيتحرك وهو بيعلّم — مش بعد ما يضغط طباعة
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(PrinterItem.IsSelected) or nameof(PrinterItem.Status))
                    {
                        RefreshDistributionSummary();
                        RefreshPrinterChoiceSummary();
                        SyncSinglePrinterName();
                    }
                };

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

        RefreshDistributionSummary();
        RefreshPrinterChoiceSummary();

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

    // ══════════ الاستقبال من بره البرنامج ══════════

    private string _receptionStatus = "";
    /// <summary>حالة الاستقبال — بتظهر في تاب الإعدادات العامة.</summary>
    public string ReceptionStatus
    {
        get => _receptionStatus;
        private set => SetProperty(ref _receptionStatus, value);
    }

    private bool _receptionIsRunning;
    /// <summary>الاستقبال شغّال دلوقتي؟ (بيستخدمه التصفير عشان يفكّر المستخدم).</summary>
    public bool ReceptionIsRunning
    {
        get => _receptionIsRunning;
        private set => SetProperty(ref _receptionIsRunning, value);
    }

    /// <summary>
    /// بيشغّل أو يوقّف مراقبة الطابعة الوهمية والمجلد المراقَب حسب الإعدادات.
    ///
    /// بيتنده عند بداية البرنامج وكل ما إعداد يتغيّر.
    /// </summary>
    public void ApplyReceptionSettings()
    {
        if (_incoming is null)
        {
            return;
        }

        bool wantsPrinter = App.ReceiveFromVirtualPrinter;
        bool wantsFolder = !string.IsNullOrWhiteSpace(App.HotFolder);

        _incoming.Stop();

        if (!wantsPrinter && !wantsFolder)
        {
            AnnounceReception("الاستقبال مقفول.", isOff: true);
            return;
        }

        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        _incoming.Start(
            VirtualPrinter.SpoolFolder(programData),
            VirtualPrinter.QueueFolder(programData),
            wantsFolder ? App.HotFolder : null);

        var parts = new List<string>();

        if (wantsPrinter)
        {
            parts.Add($"طابعة \"{VirtualPrinter.PrinterName}\"");
        }

        if (wantsFolder)
        {
            parts.Add($"مجلد {App.HotFolder}");
        }

        AnnounceReception($"الاستقبال شغّال من: {string.Join(" و", parts)}.", isOff: false);
    }

    /// <summary>
    /// بتحدّث سطر حالة الاستقبال، وبتكتب في شريط النتايج لو الحالة اتغيّرت.
    ///
    /// ═══ ليه ═══
    ///
    /// سطر الحالة عايش في تاب "الإعدادات العامة"، وشريط النتايج عايش في
    /// التاب الرئيسي — واللي في المطبعة قاعد على التاب الرئيسي طول اليوم.
    /// يعني الاستقبال ممكن يقف وهو مش شايف أي حاجة.
    ///
    /// أخطر حالة: الاستقبال بيتقفل. ساعتها مفيش أي عرض بيتغيّر — البرنامج
    /// شكله سليم تمامًا، والملفات اللي بتتبعت من بره بتروح في الهوا. السطر
    /// ده هو الفرق بين "البرنامج مش شغال" و"البرنامج مش بيستقبل".
    ///
    /// بنتكلم بس لما الحالة **تتغيّر**، عشان مانزنّش على المستخدم بنفس
    /// السطر كل ما حاجة تتحرك. وأول نداء عند فتح البرنامج والاستقبال مقفول
    /// بيعدّي في صمت — مش تغيير، ده الوضع الطبيعي لمعظم الناس.
    /// </summary>
    private void AnnounceReception(string status, bool isOff)
    {
        bool firstTime = ReceptionStatus.Length == 0;

        ReceptionIsRunning = !isOff;

        if (ReceptionStatus == status)
        {
            return;
        }

        ReceptionStatus = status;

        if (firstTime && isOff)
        {
            return;
        }

        string line = isOff
            ? "[استقبال] الاستقبال اتقفل — البرنامج مش هيلقط أي حاجة من بره لحد ما تشغّله تاني."
            : $"[استقبال] {status}";

        Log.Add(line);
        _jobLog?.Info(line);
    }

    /// <summary>بيوقّف الاستقبال — بيتنده وقت إغلاق البرنامج.</summary>
    public void StopReception() => _incoming?.Stop();

    /// <summary>
    /// بينفّذ الشغل على ثريد الواجهة.
    ///
    /// لو مفيش ثريد واجهة (في التستات مثلًا) بينفّذ في مكانه على طول —
    /// فالتستات بتفضل متزامنة وسهلة القراءة.
    /// </summary>
    private void OnUiThread(Action work)
    {
        var context = _uiThread;

        if (context is null || ReferenceEquals(SynchronizationContext.Current, context))
        {
            work();
            return;
        }

        context.Post(_ => work(), null);
    }

    /// <summary>
    /// ملف وصل من الطابعة الوهمية أو من المجلد المراقَب.
    ///
    /// بيدخل نفس مسار التحميل اليدوي بالظبط — يعني بياخد كل الفحوصات
    /// (الصيغة، التكرار، تحويل الصور) من غير أي كود خاص بالاستقبال.
    /// </summary>
    private void OnJobArrived(IncomingFile file)
    {
        int added = AddFiles([file.Path]);

        string line = added > 0
            ? $"[استقبال] وصل \"{file.FileName}\" من {file.SourceLabel}."
            : $"[استقبال] \"{file.FileName}\" من {file.SourceLabel} — مادخلش القايمة (مكرر أو صيغة مش مدعومة).";

        Log.Add(line);
        _jobLog?.Info(line);

        // الطباعة التلقائية مقفولة افتراضيًا: ورق بيطلع من غير ما حد ضغط
        // حاجة ده سلوك مخيف في مطبعة. واللي شغّالها عارف إنه طالبها.
        // الملف اللي بيوصل والبرنامج مشغول بيستنى في القايمة. لازم يتقال
        // — غير كده بيقعد ساكت والمستخدم يفتكره اتطبع.
        if (added > 0 && App.PrintReceivedAutomatically && (IsBusy || IsPrinting))
        {
            string waiting = $"[استقبال] فيه شغل ماشي — {added} ملف مستني في القايمة، اضغط «بدء معالجة الملفات» لما يخلص.";
            Log.Add(waiting);
            _jobLog?.Info(waiting);
        }

        if (added > 0 && App.PrintReceivedAutomatically && !IsBusy && !IsPrinting)
        {
            _ = ProcessCommand.ExecuteAsync();
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

        // بيتظبط **قبل** IsBusy: رفع IsBusy بينده RefreshCommandStates،
        // واللي بينوّر زرار الإيقاف. لو التوكن لسه null ساعتها، فيه لحظة
        // الزرار فيها مفتوح ومالوش أثر.
        _processCancel = new CancellationTokenSource();

        IsBusy = true;
        StatusText = "جاري معالجة الملفات...";

        try
        {
            var token = _processCancel.Token;

            CleanOldTempFiles();

            var inputs = Files.Select(f => f.FullPath).ToList();

            if (Settings.MergeFiles)
            {
                string outputFolder = Path.Combine(Path.GetTempPath(), "PrintFlow");
                Directory.CreateDirectory(outputFolder);
                string outputPath = Path.Combine(outputFolder, $"merged_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

                _jobLog?.Info($"بدء معالجة {inputs.Count} ملف → {outputPath}");

                var request = MergeRequest.From(Settings, App, inputs, outputPath);

                MergeResult result;

                try
                {
                    result = await RunPipelineAsync(request, token);
                }
                catch (OperationCanceledException)
                {
                    // الدمج مستند واحد — نُصّه مالوش قيمة، فمابنسيبش
                    // الملف الناقص في _output. الأصول زي ما هي.
                    _output = new List<PrintableDocument>();
                    NoteProcessingStopped(0, inputs.Count);
                    return;
                }

                Log.Add(result.Message);
                _jobLog?.Info(result.Message);

                if (!result.Success)
                {
                    _output = new List<PrintableDocument>();
                    StatusText = "فشلت المعالجة. شوف التفاصيل في اللوج.";
                    return;
                }

                // ⚠ المرحلة خلصت، بس المستخدم كان دَوس إيقاف وهي ماشية.
                //
                // السلسلة بتفحص التوكن **بين** المراحل، والدمج في الحالة
                // العادية مرحلة واحدة — فالإلغاء اللي بيجي وهي شغالة
                // مابيلحقش يوقفها. النتيجة كانت إن المستخدم يدوس إيقاف
                // والبرنامج يقوله «تمت المعالجة».
                //
                // الملف في التيمب ومحدش طالبه — بنسيبه ونقول الحقيقة.
                if (token.IsCancellationRequested)
                {
                    _output = new List<PrintableDocument>();
                    NoteProcessingStopped(0, inputs.Count);
                    return;
                }

                _output = [new PrintableDocument(outputPath, result.PageCount)];
                StatusText = $"تمت المعالجة: {Files.Count} ملف في {result.PageCount} صفحة.";
            }
            else
            {
                await ProcessWithoutMergingAsync(inputs, token);
            }

            // ═══ الطباعة التلقائية جوّه الـ try عن قصد ═══
            //
            // قبل كده كانت بره، بعد الـ finally. يعني الترتيب كان:
            // ترجّع IsBusy لـ false، تنوّر زرار «طباعة الآن» بإيدها
            // (RaiseCanExecuteChanged)، وبعدين تبدأ تطبع. البرنامج كان
            // بيقول للمستخدم «أنا فاضي» في نفس اللحظة اللي بيبعت فيها
            // الأوردر. فيضغط، ويطلع الورق مرتين.
            //
            // المعالجة والطباعة التلقائية دول فعل واحد في عين المستخدم —
            // ضغطة زرار واحدة — فلازم يفضلوا مشغولين لحد ما يخلصوا.
            //
            // ملحوظة: الـ return اللي فوق (لما الدمج يفشل) بيعدّي من هنا
            // من غير طباعة، وده المطلوب.
            // شرط _output: «المعالجة من غير دمج» بترجع عادي حتى لو كل
            // الملفات وقعت، وساعتها الطباعة كانت بتمشي على لستة فاضية
            // وتكتب «اضغط بدء معالجة الملفات الأول» فوق رسالة الفشل —
            // فالمستخدم يتقاله اضغط الزرار اللي هو ضغطه للتو.
            // ⚠ وشرط الإلغاء كمان: اللي دَوس إيقاف وسط المعالجة مش عايز
            // الملفات اللي خلصت تروح للمكن لوحدها بعدها.
            if (Settings.PrintDirectlyAfterProcessing && _output.Count > 0 && !token.IsCancellationRequested)
            {
                await PrintAsync();
            }
        }
        finally
        {
            var cts = _processCancel;
            _processCancel = null;
            cts?.Dispose();

            IsBusy = false;
            RefreshCommandStates();
        }
    }

    /// <summary>
    /// مرحلة واحدة في السلسلة: بتاخد ملف داخل وبتطلّع ملف خارج.
    /// الاسم بيظهر في اللوج لو المرحلة وقعت.
    /// </summary>
    private readonly record struct PipelineStage(string Name, Func<string, string, MergeResult> Run);

    /// <summary>
    /// في أي معالجة مطلوبة أصلًا على الملفات؟
    ///
    /// بيسأل نفس أسئلة <see cref="RunPipelineAsync"/> بالظبط — بما فيها
    /// وجود الخدمة نفسها. لو الاتنين اختلفوا، هيبقى في وضع البرنامج بيقول
    /// فيه "مفيش معالجة" وهو أصلًا كان هيعمل معالجة (أو العكس).
    /// </summary>
    private bool HasProcessingWork(MergeRequest request)
    {
        if (!request.HasNothingToDo)
        {
            return true;
        }

        if (_slideComposer is not null && !SlideRequest.From(Settings, "", "").IsPassThrough)
        {
            return true;
        }

        return _pageScaler is not null && !PageScaling.IsIdentity(Settings.ScalePercent);
    }

    /// <summary>
    /// عدد صفحات ملف بعد الحذف. بتتستخدم في حساب "من كام" في الترقيم المتصل
    /// قبل ما المعالجة تبدأ أصلًا.
    /// </summary>
    private int SurvivingPages(int pageCount)
        => Settings.DeletePages
            ? PageRanges.Remaining(Settings.PagesToDelete, pageCount).Count
            : pageCount;

    /// <summary>
    /// السلسلة الكاملة لمستند واحد:
    ///
    ///   ١) دمج (+ حذف صفحات من كل ملف) + الإضافات اللي على الصفحة الأصلية
    ///   ٢) تجميع شرائح أو كتيّب
    ///   ٣) الإضافات اللي على الورقة كاملة
    ///   ٤) مقياس الصفحة
    ///
    /// المراحل ٢ و٣ و٤ بتتبني **بس لو ليها لازمة**. الحالة الشائعة (مفيش
    /// شرائح ولا كتيّب ولا مقياس) بتبقى مرحلة واحدة بتكتب على الملف النهائي
    /// على طول — من غير أي ملف مؤقت ولا نسخة زيادة، زي ما كانت بالظبط.
    ///
    /// الشكل ده اتكتب من أول وجديد بدل التداخل اللي كان: كل مرحلة جديدة كانت
    /// بتضيف مسار جديد ونقطة ترتيب جديدة تتنسي (وده اللي حصل فعلًا مع الكتيّب).
    /// </summary>
    private async Task<MergeResult> RunPipelineAsync(MergeRequest request, CancellationToken token)
    {
        // القرار بيتاخد من SlideRequest نفسه — عشان الكتيّب والشرائح يبقى
        // ليهم مصدر حقيقة واحد. قبل كده كان السؤال متكرر هنا بالغلط.
        var slideRequest = SlideRequest.From(Settings, "", "");
        bool composing = !slideRequest.IsPassThrough && _slideComposer is not null;

        var scaleRequest = new ScaleRequest
        {
            InputPath = "",
            OutputPath = "",
            Percent = Settings.ScalePercent
        };

        bool scaling = !scaleRequest.IsPassThrough && _pageScaler is not null;

        // من غير تجميع، كل الإضافات بتتحط في المرحلة الأولى مرة واحدة
        var before = composing ? SlidePipeline.BeforeSlides(App) : SlidePipeline.Everything();
        var after = SlidePipeline.AfterSlides(App);

        var stages = new List<PipelineStage>
        {
            new("الدمج", (_, output) =>
                _mergeService.Merge(request.KeepOnly(before) with { OutputPath = output }))
        };

        if (composing)
        {
            stages.Add(new(slideRequest.Booklet ? "الكتيّب" : "تجميع الشرائح", (input, output) =>
                _slideComposer!.Compose(slideRequest with { InputPath = input, OutputPath = output })));

            // OverlayOnly بتصفّر حذف الصفحات جواها — الملف الداخل هنا
            // اتحذف منه خلاص، ولو الحذف اتكرر كان هيشيل ورق مجمّع عشوائي
            if (request.OverlayOnly(after, "", "").HasAnyOverlay)
            {
                stages.Add(new("الإضافات على الورقة", (input, output) =>
                    _mergeService.Merge(request.OverlayOnly(after, input, output))));
            }
        }

        if (scaling)
        {
            stages.Add(new("المقياس", (input, output) =>
                _pageScaler!.Scale(scaleRequest with { InputPath = input, OutputPath = output })));
        }

        return await Task.Run(() => RunStages(stages, request.OutputPath, token), token);
    }

    /// <summary>
    /// بيشغّل المراحل ورا بعض: كل واحدة بتكتب في ملف مؤقت والأخيرة بتكتب في
    /// الملف النهائي. أول فشل بيوقّف السلسلة، والملفات المؤقتة بتتمسح دايمًا.
    /// </summary>
    private static MergeResult RunStages(
        IReadOnlyList<PipelineStage> stages, string finalOutput, CancellationToken token)
    {
        string stem = Path.Combine(
            Path.GetDirectoryName(finalOutput)!,
            Path.GetFileNameWithoutExtension(finalOutput));

        var temporaries = new List<string>();
        var messages = new List<string>();

        try
        {
            string current = "";
            int pageCount = 0;

            for (int i = 0; i < stages.Count; i++)
            {
                // ⚠ الفحص **بين** المراحل مش جوّاها.
                //
                // كل مرحلة بتنده خدمة خارجية (دمج، تجميع، مقياس) وماعندهاش
                // توكن — فمانقدرش نقاطعها في نُصّها. أسوأ حالة إن المستخدم
                // يستنى المرحلة اللي ماشية تخلص، مش الملفات كلها.
                //
                // والرمية دي بتخرج من Task.Run وبتتلقط في اللي نداها،
                // والـ finally تحت بيمسح الملفات الوسيطة زي ما هو.
                token.ThrowIfCancellationRequested();

                bool last = i == stages.Count - 1;
                string output = last ? finalOutput : $"{stem}.stage{i + 1}.pdf";

                if (!last)
                {
                    temporaries.Add(output);
                }

                var result = stages[i].Run(current, output);

                if (!result.Success)
                {
                    // اسم المرحلة بيتقال عشان الرسالة تفرق بين "الدمج وقع"
                    // و"التجميع وقع" — الاتنين كانوا بيطلعوا زي بعض قبل كده
                    return MergeResult.Failed(
                        $"{stages[i].Name}: {result.Message.Replace("[فشل] ", "")}");
                }

                messages.Add(result.Message.Replace("[نجاح] ", ""));
                pageCount = result.PageCount;
                current = output;
            }

            return MergeResult.Succeeded(string.Join(" — ", messages), pageCount);
        }
        finally
        {
            // مش عايزين ملفات وسيطة تتراكم في التيمب على أجهزة المطابع
            foreach (string path in temporaries)
            {
                TryDelete(path);
            }
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
    private async Task ProcessWithoutMergingAsync(IReadOnlyList<string> inputs, CancellationToken token)
    {
        var overlays = MergeRequest.From(Settings, App, inputs, string.Empty);

        // مفيش أي شغل مطلوب؟ يبقى إعادة كتابة الملفات هدر خالص —
        // بنطبع الأصول زي ما هي، وده كمان بيحافظ على جودتها بالظبط.
        //
        // الشرط ده كان بيسأل عن الإضافات بس، فالوضع ده + شرائح (أو كتيّب أو
        // حذف صفحات أو مقياس) كان بيعدّي الملفات زي ما هي والمستخدم فاكر
        // إن الإعدادات اتنفّذت.
        if (!HasProcessingWork(overlays))
        {
            // عدد صفحات كل ملف من القايمة — التوزيع محتاجه، والصفر معناه
            // مقدرناش نعده والموازن بيتعامل معاه على إنه صفحة واحدة
            _output = inputs
                .Select(path => new PrintableDocument(
                    path,
                    Files.FirstOrDefault(f => f.FullPath == path)?.PageCount ?? 0))
                .ToList();
            StatusText = $"جاهز لطباعة {inputs.Count} ملف كل واحد لوحده (مفيش معالجة مطلوبة).";
            _jobLog?.Info($"وضع من غير دمج: {inputs.Count} ملف هتتطبع زي ما هي");
            return;
        }

        string folder = ResolveProcessedOutputFolder();
        Directory.CreateDirectory(folder);
        _jobLog?.Info($"بدء معالجة {inputs.Count} ملف كل واحد لوحده → {folder}");

        // عدّ الصفحات الأول: الترقيم المتصل محتاج يعرف الإجمالي قبل ما يبدأ.
        // بنعدّ الصفحات اللي **هتفضل** بعد الحذف، مش الأصلية — وإلا "من ٤٠"
        // هتفضل مكتوبة على ورق عدده ٣٠.
        var pageCounts = (await CountPagesAsync(inputs)).Select(SurvivingPages).ToList();
        int grandTotal = pageCounts.Where(c => c > 0).Sum();

        var produced = new List<PrintableDocument>();

        // اسم الملف جنب الرسالة: من غيره الفشل بيتقال ومحدش يعرف
        // يروح لأنهي ملف يصلّحه.
        var failures = new List<(string File, string Message)>();
        int nextNumber = 1;
        int totalProcessedPages = 0;
        bool stopped = false;

        for (int i = 0; i < inputs.Count; i++)
        {
            // الفحص على حد الملف: اللي خلص بيفضل، واللي بعده مايبدأش.
            if (token.IsCancellationRequested)
            {
                stopped = true;
                break;
            }

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

            MergeResult result;

            try
            {
                result = await RunPipelineAsync(request, token);
            }
            catch (OperationCanceledException)
            {
                // الملف ده وقف في نُصّه — مابيدخلش المخرج.
                stopped = true;
                break;
            }

            if (result.Success)
            {
                // عدد صفحات الناتج (مش الأصل) — بعد الحذف والتجميع
                produced.Add(new PrintableDocument(destination, result.PageCount));
                totalProcessedPages += result.PageCount;
                nextNumber += result.PageCount;
            }
            else
            {
                string failed = Path.GetFileName(source);

                failures.Add((failed, result.Message));

                // ⚠ مفيش Log.Add هنا عن قصد.
                //
                // كان في سطر لكل ملف. في تجربة حقيقية فشل ٢٠ ملف لنفس
                // السبب، فطلعوا عشرين سطر متطابق غرقوا سطر النجاح اللي
                // فوقهم. الفشل بيتقال مجمّع في الآخر — شوف ReportFailures.
                //
                // السجل على القرص لسه بياخد سطر لكل ملف: هو للمراجعة
                // بعد كده، مش للعرض وقت الشغل.
                _jobLog?.Info($"تخطّينا ملف: {failed} — {result.Message}");

                // الملف ده مالوش صفحات في المخرج، بس لو عرفنا عدده بنحرّك
                // العداد عشان الترقيم يفضل مطابق للإجمالي المكتوب على الورق
                if (i < pageCounts.Count && pageCounts[i] > 0)
                {
                    nextNumber += pageCounts[i];
                }
            }
        }

        // ⚠ الإلغاء اللي جه وآخر ملف ماشي.
        //
        // الحلقة خلصت، فمفيش تكرار جاي يشوف التوكن — والبرنامج كان
        // بيقول «تمت المعالجة» بعد ما المستخدم دَوس إيقاف.
        stopped = stopped || token.IsCancellationRequested;

        _output = produced;

        // ⚠ الإيقاف قبل الفشل: لو المستخدم وقّف قبل ما أي ملف يخلص،
        // «فشلت معالجة كل الملفات» بتخلّيه يدوّر على عطل مش موجود.
        ReportFailures(failures);

        if (stopped)
        {
            NoteProcessingStopped(produced.Count, inputs.Count);
            return;
        }

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
    /// بيكتب الفشل **مجمّع بالسبب** في شاشة النتايج، بأسامي الملفات.
    ///
    /// السطر اللي كان بيتكتب لكل ملف في الحلقة اتشال عن قصد: ٢٠ ملف
    /// بيفشلوا لنفس السبب كانوا بيطلعوا ٢٠ سطر متطابق، ومحدش فيهم
    /// بيقول أنهي ملف. بقى سطرين فيهم العدد والأسامي.
    /// </summary>
    private void ReportFailures(IReadOnlyList<(string File, string Message)> failures)
    {
        foreach (string line in FailureSummary.Describe(failures))
        {
            Log.Add(line);
        }
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

    /// <summary>
    /// ١ معناها فيه أوردر بيتبعت دلوقتي. بيتقرا ويتكتب بـ Interlocked عشان
    /// مايبقاش فيه شق بين الفحص والقفل حد يدخل منه.
    /// </summary>
    private int _printInFlight;
    /// <summary>بيتلغي لما المستخدم يضغط «إيقاف فوري». null = مفيش طباعة ماشية.</summary>
    private CancellationTokenSource? _printCancel;

    /// <summary>
    /// نفس الفكرة بس للمعالجة. null = مفيش معالجة ماشية.
    ///
    /// ⚠ منفصل عن توكن الطباعة عن قصد. المعالجة بتنده الطباعة التلقائية
    /// جوّاها، فالاتنين بيبقوا حيّين في نفس اللحظة — ولو كانوا توكن واحد،
    /// «وقّف الطباعة» كان هيوقّف معالجة خلصت خلاص، والعكس.
    /// </summary>
    private CancellationTokenSource? _processCancel;

    private CancellationToken PrintToken => _printCancel?.Token ?? CancellationToken.None;

    /// <summary>فيه أوردر بيتبعت دلوقتي؟ الزراير بتتقفل على أساسها.</summary>
    public bool IsPrinting => Volatile.Read(ref _printInFlight) == 1;

    /// <summary>
    /// بيبعت الأوردر للمكن. أوردر واحد في المرة، مهما كان الباب اللي دخل منه.
    ///
    /// ═══ ليه القفل هنا جوّه، مش على الزرار ═══
    ///
    /// في ١.٩.٧ الحارس الوحيد كان <c>AsyncRelayCommand._isRunning</c> — يعني
    /// حارس على **الزرار**، مش على الطباعة. والطباعة ليها أكتر من باب:
    /// الزرار، والطباعة التلقائية بعد المعالجة، والملفات الجاية من المجلد
    /// المراقب. الطباعة التلقائية كانت بتنده الدالة دي **على طول** من غير
    /// ما تعدّي على الأمر — فالحارس مايقفلش، والزرار يفضل مفتوح والأوردر
    /// لسه بيتبعت.
    ///
    /// النتيجة اللي المعمل قفشها: المستخدم يضغط «بدء معالجة الملفات»، تخلص
    /// المعالجة، الطباعة التلقائية تبدأ، الشاشة تسكت ثانية وهي بتقرا حالة
    /// الطابعات — فيضغط «طباعة الآن» فاكر إن مفيش حاجة بتحصل، و**الأوردر
    /// يطلع مرتين**. ٥٧٠ صفحة بقت ١١٤٠.
    ///
    /// الدرس: أي حارس على الواجهة ليه طريق حواليه. القفل لازم يبقى على
    /// الفعل نفسه — في أول سطر من الدالة اللي بتبعت الورق.
    ///
    /// <see cref="WorkDispatcher"/> بيضمن إن القطعة ماتتبعتش مرتين **جوّه
    /// الأوردر الواحد**. هو مايعرفش حاجة عن أوردر تاني ماشي جنبه بنفس
    /// الملفات — وده بالظبط اللي القفل ده بيمنعه.
    /// </summary>
    private async Task PrintAsync()
    {
        if (Interlocked.Exchange(ref _printInFlight, 1) == 1)
        {
            string busy = "[تجاهل] فيه أوردر بيتبعت دلوقتي — الطلب ده اتجاهل عشان الشغل مايطلعش مرتين.";
            Log.Add(busy);
            _jobLog?.Info(busy);
            return;
        }

        // الـ try بيبدأ من هنا مش من بعد RefreshCommandStates: القفل
        // اتاخد خلاص، فأي رمية بعده لازم تلاقي finally يفكّه. من غير كده
        // البرنامج بيتقفل للأبد ومفيش طريق يرجّعه غير قفله وفتحه.
        try
        {
            _printCancel = new CancellationTokenSource();
            RefreshCommandStates();
            await PrintCoreAsync();
        }
        finally
        {
            var cts = _printCancel;
            _printCancel = null;
            cts?.Dispose();
            Volatile.Write(ref _printInFlight, 0);
            RefreshCommandStates();
        }
    }

    /// <summary>الزراير كلها بتسأل عن نفس الحالة، فبتتحدّث مع بعض.</summary>
    private void RefreshCommandStates()
    {
        OnPropertyChanged(nameof(IsPrinting));
        OnPropertyChanged(nameof(IsIdle));
        PrintCommand.RaiseCanExecuteChanged();
        ProcessCommand.RaiseCanExecuteChanged();
        ResetCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        CleanSpoolerCommand.RaiseCanExecuteChanged();
        ForgetSpeedsCommand.RaiseCanExecuteChanged();

        // سطر "الشغل هيتقسّم إزاي" بيتغيّر معنـاه أول ما أوردر يبدأ أو
        // يخلص — من غير السطر ده بيفضل عالق على آخر حالة قبل الطباعة.
        RefreshPrinterChoiceSummary();
    }

    private async Task PrintCoreAsync()
    {
        if (ResolveDocumentsToPrint().Count == 0)
        {
            StatusText = "حمّل ملفات الأول.";
            return;
        }

        // IsBusy بتتظبط هنا **قبل** قراءة حالة الطابعات، مش بعدها.
        //
        // قراءة الطابعات من WMI بتاخد وقت حقيقي على جهاز فيه مكن كتير،
        // وكانت بتحصل والشاشة شكلها فاضي: شريط الانتظار مخفي والزراير
        // مفتوحة. الثانية دي بالظبط هي اللي المستخدم بيضغط فيها تاني.
        //
        // والـ try/finally حوالين الباقي كله مش حوالين الإرسال بس: دلوقتي
        // الزراير متربطة بـ IsBusy، فلو رميت حاجة في أي سطر بعد ما رفعناها
        // البرنامج بيتقفل قدام المستخدم للأبد.
        // بنرجّعها لقيمتها القديمة مش لـ false على طول.
        //
        // لما «بدء معالجة الملفات» هي اللي نادت الطباعة، IsBusy بتبقى
        // مرفوعة منها هي. لو رجّعناها false هنا، البرنامج بيقول «أنا
        // فاضي» والأوردر لسه على السلك — وده بالظبط أصل مشكلة ١.٩.٧.
        bool wasBusy = IsBusy;

        try
        {
            IsBusy = true;
            StatusText = "بنقرا حالة الطابعات...";
            await SendTheOrderAsync();
        }
        finally
        {
            IsBusy = wasBusy;
        }
    }

    private async Task SendTheOrderAsync()
    {
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

        // من غير الحارس ده، البرنامج بيكمّل على لستة فاضية ويقول
        // «خلص الإرسال إلى 0 طابعة» — يعني بيدّعي النجاح وهو مابعتش حاجة.
        if (targets.Count == 0)
        {
            StatusText = "مفيش طابعة مؤهلة متاحة حاليًا. اتأكد إن الطابعة متوصلة وشغالة.";
            return;
        }

        var documents = ResolveDocumentsToPrint();

        if (_output.Count == 0)
        {
            // بيتحسب من الإعدادات الفعلية مش جملة ثابتة. الجملة القديمة
            // كانت بتقول ٤ حاجات والحقيقة ٨ — والمستخدم اللي ظبط مقياس
            // الصفحة ٩٢٪ قرا تحذير مش مذكور فيه المقياس، فطمن غلط وطبع
            // أوردر من غير هامش. شوف SkippedProcessing.
            string raw = SkippedProcessing.Describe(Settings, App, Files.Count);
            Log.Add(raw);
            _jobLog?.Info(raw);
        }

        StatusText = "جاري الإرسال للطباعة...";

        // مكنة واحدة مالهاش حاجة تتقسّم عليها، وأكتر من واحدة بتتقسّم
        // إلا لو المستخدم طلب صراحة إن كل مكنة تطبع العدد كامل.
        bool distributing = Settings.DistributeCopies && targets.Count > 1;

        _jobLog?.Info(
            $"طباعة: {documents.Count} مستند × {targets.Count} طابعة ({string.Join("، ", targets.Select(t => t.Name))}) — " +
            $"{Settings.TotalCopies} نسخة، {Settings.PaperSize}" +
            $"{(Settings.Grayscale ? "، أبيض وأسود" : "")}{(Settings.Duplex ? "، وجهين" : "")}" +
            $"{(distributing ? "، توزيع" : "")}");

        if (distributing)
        {
            await PrintDistributedAsync(targets, documents);
        }
        else
        {
            foreach (var line in await PrintUniformAsync(targets, documents))
            {
                Log.Add(line);
                _jobLog?.Info(line);
            }

            StatusText = $"خلص الإرسال إلى {targets.Count} طابعة.";
        }
    }

    /// <summary>كل طابعة تاخد كل المستندات بالعدد الكامل من النسخ.</summary>
    private async Task<string[]> PrintUniformAsync(
        List<PrinterItem> targets, List<PrintableDocument> documents)
    {
        // نفس البارات هنا كمان. بار بيبان في وضع وبيختفي في وضع تاني
        // بيخلي المستخدم يشك إن البرنامج واقف.
        int pagesEach = documents.Sum(d =>
            Math.Max(1, PageRange.CountIn(Settings.PageFrom, Settings.PageTo, d.Pages)))
            * Settings.TotalCopies;

        // كل مكنة بتطلّع العدد كامل من **كل** مستند
        int copiesEach = documents.Count * Settings.TotalCopies;

        StartProgress(targets.Select(t => (t.Name, pagesEach, copiesEach)).ToList());

        // ═══ القياس هنا بيشتغل على مكنة واحدة بس ═══
        //
        // الطريق ده كان **مابيعلّمش كتاب السرعات ولا حاجة**. يعني المطبعة
        // اللي بتشتغل على مكنة واحدة طول اليوم، الكتاب بيفضل فاضي عندها
        // للأبد، ولما تيجي توزّع على مكنتين التوزيع بيبدأ من الصفر.
        //
        // ⚠ بس لما يبقى فيه أكتر من مكنة في الوضع ده، **كلهم بيطبعوا العدد
        // كامل في نفس الوقت**. زمن الأوردر ساعتها هو زمن **أبطأ** مكنة،
        // فالمكنة السريعة اللي خلّصت بدري هتتقاس على وقت غيرها وتتسجّل
        // أبطأ من حقيقتها — وهنا مفيش سرقة شغل تصلّح الغلط زي التوزيع.
        //
        // فبنقيس لما نبقى متأكدين، ومانقيسش لما نبقى مش متأكدين. رقم
        // مش موجود أحسن من رقم كذّاب.
        bool canMeasure = targets.Count == 1;

        if (canMeasure)
        {
            _speeds.OrderStarted();
        }

        // مفيش Task.Run دلوقتي: PrintAsync بقت غير متزامنة بجد (WaitForExitAsync)،
        // فمفيش ثريد بيتحجز وهو مستني بروسيس الطباعة يخلص.
        var tasks =
            from document in documents
            from printer in targets
            select PrintOneAsync(document, printer);

        var outcomes = await Task.WhenAll(tasks);

        FinishProgress(!PrintToken.IsCancellationRequested);

        var lines = outcomes.Select(o => o.Message).ToList();

        if (canMeasure)
        {
            string learned = _speeds.OrderFinished();

            if (learned.Length > 0)
            {
                lines.Add(learned);
            }
        }

        return [.. lines];

        async Task<PrintOutcome> PrintOneAsync(PrintableDocument document, PrinterItem printer)
        {
            var job = PrintJob.From(
                Settings, document.Path, printer.Name, Settings.TotalCopies, document.Pages);

            // ⚠ الوحدة دي للبار بس — فبتاخد الصفحات اللي هتطلع فعلًا.
            // والجوب فوقها بياخد الطول الخام، لأنه هو اللي المدى بيتقص عليه.
            var unit = new WorkUnit(
                document.Path,
                PageRange.CountIn(Settings.PageFrom, Settings.PageTo, document.Pages),
                Settings.TotalCopies);

            // النسخ اللي البار حسبها **وهي ماشية**، دفعة دفعة.
            //
            // الجوب الكبير بيتبعت على دفعات، ومن غير العدّاد ده النتيجة
            // بتوصل بعد آخر دفعة بس — فالبار يفضل صفر عشر دقايق وبعدين
            // يقفز ١٠٠٪، واللي واقف على المكنة يفتكره واقف ويوقّفه.
            //
            // بيتزوّد من ثريد خلفي، وبيتقرا بعد الـ await — والـ await
            // نفسه بيضمن إن كل النداءات خلصت وظهرت قبل القراءة.
            int credited = 0;

            void OnChunkDelivered(int copies)
            {
                credited += copies;

                // نفس المستند ونفس عدد الصفحات — النسخ بس هي اللي بتتغيّر،
                // و Weight بيتحسب لوحده منها.
                var slice = unit with { Copies = copies };

                OnUiThread(() => NoteChunkProgress(printer.Name, slice));
            }

            var outcome = await _printService.PrintAsync(job, PrintToken, OnChunkDelivered);

            if (canMeasure)
            {
                // نفس قواعد التوزيع بالحرف: اللي اتسلّم بيتقاس، واللي في
                // الشك مابيتقاسش. مكنة الورق خلص منها مش «بطيئة».
                if (outcome.Kind == PrintResult.Delivered)
                {
                    // الورق اللي طلع فعلًا بعد حساب مدى الصفحات — مش وزن
                    // القطعة. شوف نفس التعليق في PrintDistributedAsync.
                    _speeds.NoteDelivered(printer.Name, job.PagesPerCopy * unit.Copies);
                }
                else if (outcome.Kind is PrintResult.NotSent or PrintResult.Abandoned)
                {
                    _speeds.Distrust(printer.Name);
                }
            }

            // ⚠ الباقي بس — اللي اتحسب من الدفعات مايتحسبش تاني.
            //
            // من غير الطرح ده، الجوب اللي اتبعت على ١٠ دفعات هيتسجّل ٢٠
            // نسخة بدل ١٠: عشرة من النداءات وعشرة من النتيجة النهائية.
            // البار كان هيقول ٢٠٠٪ واللي واقف على المكنة يعد ورق مش موجود.
            var rest = unit with { Copies = Math.Max(0, unit.Copies - credited) };

            OnUiThread(() => RecordProgress(printer.Name, rest, outcome));

            return outcome;
        }
    }

    /// <summary>
    /// بيقسّم الشغل كله على المكن بحيث الكل يخلص مع بعض — **وبيفضل ماسك
    /// الشغل وهو ماشي** بدل ما يبعته كله ويسيبه.
    ///
    /// ═══ اللي اتغيّر في ١.٩.٦ ═══
    ///
    /// قبل كده الخطة كانت بتتحسب مرة واحدة وكل الأوامر تتبعت في نفس
    /// اللحظة (<c>Task.WhenAll</c> على كل النصايب). ده كان شغال تمام طول
    /// ما كل المكن سليمة، وبيقع في أول عطل حقيقي في المطبعة:
    ///
    ///   • مكنة الورق خلص منها بتفضل **تقبل** جوبات وتكوّمها في طابور
    ///     ويندوز بتاعها، والمكن التانية بتخلص وتقف. الأوردر بيستنى أبطأ
    ///     حاجة في الأوضة.
    ///   • مكنة اتفصلت بعد ما الأوامر اتبعتت = نصيبها كله ضاع، ومحدش
    ///     يعرف غير لما يعد الورق.
    ///
    /// دلوقتي الشغل بيتقطّع لقطع صغيرة (<see cref="WorkSlicing"/>) وكل
    /// مكنة بتسحب قطعتها لما تفضى (<see cref="WorkDispatcher"/>). المكنة
    /// اللي وقعت بتبطّل تسحب، وشغلها اللي لسه ماتبعتش بيروح للباقيين.
    ///
    /// الخطة الأصلية لسه بتتحسب وبتتكتب في اللوج — بس دلوقتي بقت
    /// **توقُّع** مش أمر نهائي، والتقرير في الآخر بيقول اللي حصل فعلًا.
    /// </summary>
    private async Task PrintDistributedAsync(
        List<PrinterItem> targets, List<PrintableDocument> documents)
    {
        var names = targets.Select(p => p.Name).ToList();

        // اللقطة بتتاخد **مرة واحدة** قبل التوزيع. لو قريناها جوّه الحلقة
        // كانت هتتغيّر تحت رجلينا والخطة اللي في اللوج تبقى مش اللي حصل.
        var speeds = _speeds.Snapshot();

        // ⚠ الموزّع لازم يشوف الورق اللي هيطلع، مش طول المستند. ملف ١٠٠
        // صفحة وملف ١٠ بمدى «من ٥ لـ ٢٠» نسبتهم الحقيقية ١٦:٦ مش ١٠٠:١٠.
        var weighted = documents
            .Select(d => d with
            {
                Pages = PageRange.CountIn(Settings.PageFrom, Settings.PageTo, d.Pages)
            })
            .ToList();

        // الطول الخام لسه لازم لـ PrintJob — المدى بيتحسب عليه هو، فلو
        // بعتناله الرقم المقصوص كان هيتقص مرتين (٢٤ ← ١٦ ← ١٢).
        var rawPages = documents
            .GroupBy(d => d.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Pages, StringComparer.Ordinal);

        var plan = WorkloadBalancer.Balance(weighted, Settings.TotalCopies, names, speeds);

        string expected = "المتوقع — " + plan.Describe();
        Log.Add(expected);
        _jobLog?.Info(expected);

        if (!speeds.IsEmpty)
        {
            string speedLine = speeds.Describe(names);
            Log.Add(speedLine);
            _jobLog?.Info(speedLine);
        }

        foreach (var printer in plan.Printers.Where(p => !p.IsIdle))
        {
            _jobLog?.Info($"  {printer.PrinterName}: {printer.Documents} مستند، {printer.Pages} صفحة");
        }

        // نصيب كل مكنة بالنسخ. PrinterWorkload بيقول الصفحات وعدد
        // المستندات بس، والنسخ عايشة في التكليفات نفسها — فبنجمعها من
        // هناك بدل ما نضيف حقل في ريكورد الدومين وناخد كل تستاته معانا.
        var copiesPerPrinter = plan.Assignments
            .GroupBy(a => a.PrinterName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Copies), StringComparer.Ordinal);

        // صفوف التقدم بتتبني من نفس الخطة اللي اتكتبت في اللوج فوق،
        // فاللي المستخدم بيشوفه في البارات هو نفس اللي البرنامج وعد بيه.
        StartProgress(plan.Printers
            .Where(p => !p.IsIdle)
            .Select(p => (
                p.PrinterName,
                p.Pages,
                copiesPerPrinter.TryGetValue(p.PrinterName, out int copies) ? copies : 0))
            .ToList());

        // ساعة القياس بتبدأ مع الأوردر مش مع أول قطعة: المكنة اللي أخدت
        // وقت قبل ما تسلّم حاجة، التأخير ده جزء من سرعتها الحقيقية.
        _speeds.OrderStarted();

        // Task.Run عشان حلقة التوزيع كلها تمشي بعيد عن ثريد الواجهة.
        // من غيرها كل await جوّه الموزّع بيرجع للواجهة، والواجهة بتتلجلج
        // طول الأوردر. الـ say بيمرّ على OnUiThread أصلًا فالسطور بتوصل صح.
        var report = await Task.Run(() => WorkDispatcher.RunAsync(
            WorkSlicing.Lanes(plan),
            async (printerName, unit, token) =>
            {
                // بنلفّ دالة الطباعة بدل ما نضيف حدث جديد للموزّع.
                // الموزّع منطقه محسوب بالمللي ومختبَر — مش هنلمسه عشان بار.
                var job = PrintJob.From(
                    Settings, unit.Path, printerName, unit.Copies,
                    rawPages.TryGetValue(unit.Path, out int raw) ? raw : unit.Pages);

                // نفس فكرة الطريق الأحادي: القطعة الكبيرة ممكن تتبعت على
                // دفعات، والبار لازم يتحرك مع كل دفعة مش يستنى الآخر.
                // شوف NoteChunkProgress للسبب الكامل.
                int credited = 0;

                void OnChunkDelivered(int copies)
                {
                    credited += copies;
                    var slice = unit with { Copies = copies };
                    OnUiThread(() => NoteChunkProgress(printerName, slice));
                }

                var outcome = await _printService.PrintAsync(job, token, OnChunkDelivered);

                // القياس بيتاخد من هنا كمان — نفس اللحظة اللي البار بيتحرك
                // فيها. الورق اللي مش متأكدين منه مابيتحسبش سرعة.
                if (outcome.Kind == PrintResult.Delivered)
                {
                    // ⚠ **الورق اللي طلع فعلًا**، مش وزن القطعة.
                    //
                    // <c>unit.Weight</c> بيحسب المستند كامل. مع مدى صفحات
                    // «من ٥ لـ ٢٠» في مستند ١٨٠ صفحة، القطعة وزنها ١٨٠
                    // والطابعة طلّعت ١٦ — يعني القياس كان هيقول إن المكنة
                    // أسرع من حقيقتها **باحد عشر ضعف**، والرقم ده بيتحفظ
                    // ويأثر على كل توزيع جاي حتى في أوردرات من غير مدى.
                    //
                    // PagesPerCopy بيحسب المدى، فالرقم ده مظبوط في الحالتين.
                    _speeds.NoteDelivered(printerName, job.PagesPerCopy * unit.Copies);
                }
                else if (outcome.Kind is PrintResult.NotSent or PrintResult.Abandoned)
                {
                    _speeds.Distrust(printerName);
                }

                // الباقي بس — اللي اتحسب من الدفعات مايتحسبش تاني.
                var rest = unit with { Copies = Math.Max(0, unit.Copies - credited) };

                OnUiThread(() => RecordProgress(printerName, rest, outcome));

                return outcome;
            },
            _printerHealth,
            say: line => OnUiThread(() =>
            {
                Log.Add(line);
                _jobLog?.Info(line);
            }),
            cancellationToken: PrintToken));

        string summary = report.Summarise();
        Log.Add(summary);
        _jobLog?.Info(summary);

        // الملازم اللي في الشك أو اللي ماوصلتش بتتقال **بالاسم**. الرقم
        // المجمّع لوحده مابيساعدش حد في المطبعة — هو محتاج يعرف أنهي ملف
        // وكام نسخة عشان يعيدها.
        foreach (var unit in report.InDoubt)
        {
            string line = $"  ⚠ في الشك: {Path.GetFileName(unit.Path)} — {unit.Copies} نسخة. " +
                          "عُد الورق الطالع قبل ما تعيدها عشان ماتطلعش مرتين.";
            Log.Add(line);
            _jobLog?.Info(line);
        }

        foreach (var unit in report.NeverSent)
        {
            string line = $"  ⚠ ماتبعتش خالص: {Path.GetFileName(unit.Path)} — {unit.Copies} نسخة. " +
                          "دي مضمون إنها ماطبعتش، ابعتها تاني بأمان.";
            Log.Add(line);
            _jobLog?.Info(line);
        }

        // ═══ المكنة اللي وقفت في النص مايتحسبش قياسها ═══
        //
        // Distrust جوّه حلقة الطباعة بيمسك حالة واحدة بس: مكنة فشلت
        // **وهي ماسكة قطعة**. أما المكنة اللي وقفت وقعدت واقفة، فالموزّع
        // مابيبعتلهاش حاجة من أصله — فمفيش نتيجة فشل توصل، وهي بتطلع في
        // آخر الأوردر بصفحات قليلة واتسجّلت "بطيئة".
        //
        // والفرق مهم: دي مشكلة **توفّر** مش بطء. لو حسبناها، المكنة اللي
        // خلص منها الورق النهاردة هتفضل واخدة نصيب صغير أسابيع بعد ما
        // حد يحط فيها ورق.
        //
        // التقرير عارف مين وقف (Retired) — فبناخد منه.
        foreach (var tally in report.Printers.Where(p => p.Retired))
        {
            _speeds.Distrust(tally.PrinterName);

            // والبار كمان لازم يعرف. المكنة اللي وقفت مابيوصلهاش قطعة
            // تفشل، فصفّها مايعرفش إنها ماتت — و FinishProgress كان
            // بيقفلها على "خلصت ١٠٠٪".
            Progress.FirstOrDefault(row => row.PrinterName == tally.PrinterName)
                ?.Stopped(tally.RetiredBecause ?? "وقفت");
        }

        // بعد ما علّمنا الواقفين: دلوقتي بس نقفل الصفوف.
        FinishProgress(!PrintToken.IsCancellationRequested);

        // القياس بيتقفل بعد التقرير: المكنة اللي وقعت اتشال قياسها خلاص
        // (Distrust)، واللي خلّصت بتدخل الدفتر. بيرجّع "" لو مفيش عيّنة
        // تستاهل — وساعتها مابنكتبش سطر فاضي في اللوج.
        string learned = _speeds.OrderFinished();

        if (learned.Length > 0)
        {
            Log.Add(learned);
            _jobLog?.Info(learned);
        }

        StatusText = report.Clean
            ? $"خلص التوزيع على {report.Printers.Count(p => p.Units > 0)} طابعة."
            : "خلص التوزيع بس في شغل محتاج مراجعة — شوف النتائج.";
    }

    /// <summary>
    /// المكن اللي الشغل هيروح لها.
    ///
    /// ═══ اتغيّرت في ١.٩.٦ ═══
    ///
    /// كانت بتسأل الأول: وضع "أكتر من طابعة" مفتوح؟ لو أه خد المعلّم
    /// عليهم، لو لأ خد الطابعة اللي في القايمة المنسدلة.
    ///
    /// المشكلة إن المستخدم مكانش بيلاقي المفتاح ده أصلًا: مربع صغير في
    /// آخر قايمة طويلة، وهو اللي بيخبّي قايمة اختيار المكن **في عمود
    /// تاني خالص**. يعني قرار واحد متقسّم على تلات أماكن، والطريق
    /// للميزة مقفول بالميزة نفسها. جه بلاغ بالنص: "مش عارف اختار أكتر
    /// من واحدة".
    ///
    /// القاعدة دلوقتي جملة واحدة: **اللي معلّم عليه هو اللي بيطبع.**
    /// مفيش مفتاح ولا وضع ولا خطوة مخفية. وبس لو مفيش أي حاجة معلّمة
    /// بنرجع للطابعة الافتراضية عشان البرنامج مايقفش في وش المستخدم.
    /// </summary>
    private List<PrinterItem> ResolveTargetPrinters()
    {
        var ticked = Printers.Where(p => p.IsSelected && p.IsEligible).ToList();

        if (ticked.Count > 0)
        {
            return ticked;
        }

        // مفيش أي تعليم — بنرجع للاسم المحفوظ، وبعدين للافتراضية
        var fallback = Printers.FirstOrDefault(p => p.Name == Settings.PrinterName)
                       ?? Printers.FirstOrDefault(p => p.IsDefault)
                       ?? Printers.FirstOrDefault();

        return fallback is not null && fallback.IsEligible ? [fallback] : [];
    }

    /// <summary>
    /// المستندات اللي هتتطبع. لو فيه ناتج معالجة بنطبعه؛ وإلا بنطبع
    /// الملفات زي ما هي — المستخدم اللي عايز يطبع ملف جاهز مالوش دعوة
    /// بالمعالجة أصلًا.
    /// </summary>
    private List<PrintableDocument> ResolveDocumentsToPrint()
        => _output.Count > 0
            ? _output
            : Files.Select(f => new PrintableDocument(f.FullPath, f.PageCount ?? 0)).ToList();

    // ══════════ تصفير ══════════

    /// <summary>
    /// زرار "حذف الملفات وإرجاع الإعدادات" الأحمر.
    ///
    /// **بيلمس إعدادات الجوب بس** (<see cref="Settings"/>) — الإعدادات
    /// العامة و<see cref="App"/> مش من شغله خالص، والتست
    /// <c>Reset_Never_Touches_The_General_Settings</c> حارس على كده
    /// بالـ Reflection على كل خاصية.
    ///
    /// السطرين اللي تحت (الرسالة الصريحة + إعادة سطر الاستقبال في اللوج)
    /// اتضافوا بعد بلاغ حقيقي: المستخدم ضغط الزرار ولقى الاستقبال باين
    /// مقفول، فافتكر إن الزرار هو اللي قفله. الزرار مالوش دعوة — بس
    /// <c>Log.Clear()</c> بيمسح سطر "الاستقبال شغّال"، والرسالة القديمة
    /// كانت بتقول "اترجعت الإعدادات" على إطلاقها. الاتنين مع بعض بيدّوا
    /// انطباع إن كل حاجة اترجعت للصفر.
    /// </summary>
    private void Reset()
    {
        // نفس مبدأ القفل بتاع الطباعة: الحارس على الفعل، مش على الزرار.
        // Reset بيمسح _output — ولو ده حصل والأوردر لسه بيتبعت، الموزّع
        // بيلاقي الأرض اتسحبت من تحته في نص الشغل.
        if (IsBusy || IsPrinting)
        {
            string busy = "[تجاهل] فيه شغل ماشي دلوقتي — استنى يخلص قبل ما ترجّع الإعدادات.";
            Log.Add(busy);
            _jobLog?.Info(busy);
            return;
        }

        Files.Clear();
        Log.Clear();
        _output = new List<PrintableDocument>();

        Settings.CopyFrom(new PrintSettings());

        foreach (var printer in Printers)
        {
            printer.IsSelected = false;
        }

        // اللوج اتمسح لسه — لو الاستقبال شغّال لازم يفضل باين، مش يختفي
        // مع باقي السطور ويسيب المستخدم مش عارف هو شغّال ولا لأ
        if (ReceptionIsRunning)
        {
            Log.Add($"[استقبال] {ReceptionStatus}");
        }

        PrintCommand.RaiseCanExecuteChanged();
        StatusText = "اترجعت إعدادات الجوب للوضع الافتراضي. الإعدادات العامة والاستقبال زي ما هما.";
    }

    /// <summary>
    /// بيلغي التوكن بس — مش بيقتل حاجة بالعافية. الموزّع بيشوف الإلغاء،
    /// يرجّع القطعة اللي في إيده للطابور، ويوقف كل العمال. واللي ماتبعتش
    /// بيطلع في التقرير بالاسم تحت «ماتبعتش خالص».
    ///
    /// اللي وصل طابور ويندوز خلاص مش بيرجع — ده بقى شغل السبولر.
    /// </summary>
    private void CancelPrinting()
    {
        var printing = _printCancel;
        var processing = _processCancel;

        // ⚠ الطباعة الأول لو الاتنين حيّين.
        //
        // المعالجة بتنده الطباعة التلقائية جوّاها، فالاتنين بيبقوا حيّين
        // في نفس اللحظة. اللي على السلك هو اللي بيصرف ورق — فهو الأولى
        // بالإيقاف، والمعالجة ساعتها خلصت شغلها أصلًا.
        if (printing is not null && !printing.IsCancellationRequested)
        {
            StopPrinting(printing);
            return;
        }

        if (processing is not null && !processing.IsCancellationRequested)
        {
            StopProcessing(processing);
        }
    }

    /// <summary>
    /// إيقاف المعالجة. مفيش طوابير تتفضّى هنا — لسه مفيش حاجة راحت
    /// لويندوز أصلًا. اللي بيتعمل: التوكن يتلغي، والسلسلة تقف عند أقرب
    /// حد آمن (آخر ملف أو آخر مرحلة).
    /// </summary>
    private void StopProcessing(CancellationTokenSource cts)
    {
        string line = "[إيقاف] المستخدم طلب إيقاف المعالجة — هنقف عند أقرب حد آمن.";
        Log.Add(line);
        _jobLog?.Info(line);
        StatusText = "بنوقف المعالجة...";

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // المعالجة خلصت لوحدها قبل ما نلحق
        }
    }

    /// <summary>بيكتب اللي حصل بعد إيقاف المعالجة — بالأرقام مش بالعموم.</summary>
    private void NoteProcessingStopped(int done, int total)
    {
        string line = done > 0
            ? $"[إيقاف] المعالجة اتوقفت — خلص {done} من {total} ملف، والباقي ماتعالجش."
            : "[إيقاف] المعالجة اتوقفت قبل ما أي ملف يخلص. الملفات الأصلية زي ما هي.";

        Log.Add(line);
        _jobLog?.Info(line);

        StatusText = done > 0
            ? $"المعالجة اتوقفت. {done} ملف خلصوا وجاهزين للطباعة، والباقي لأ."
            : "المعالجة اتوقفت. مفيش ملفات جاهزة.";
    }

    private void StopPrinting(CancellationTokenSource cts)
    {
        string line = "[إيقاف] المستخدم طلب إيقاف فوري — مفيش حاجة جديدة هتتبعت.";
        Log.Add(line);
        _jobLog?.Info(line);
        StatusText = "بنوقف وبنفضّي طوابير الطباعة...";

        // الأسامي بتتاخد **قبل** الإلغاء: صفوف التقدم ممكن تتقفل بعده.
        var printers = Progress
            .Select(row => row.PrinterName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // الأوردر خلص لوحده قبل ما نلحق — مفيش حاجة تتلغي
        }

        if (_printerQueue is not null && printers.Count > 0)
        {
            _ = PurgePrinterQueuesAsync(printers);
        }
    }

    /// <summary>
    /// بيفضّي طوابير المكن اللي كانت شغّالة في الأوردر.
    ///
    /// ═══ ليه ده كان لازم ═══
    ///
    /// قبل كده «إيقاف فوري» كان بيوقف **البرنامج** بس. الرسالة نفسها
    /// كانت بتقول للمستخدم «اللي وصل طابور الطابعة لازم تلغيه من ويندوز»
    /// — يعني إحنا عارفين المشكلة وسايبينله هو يحلها بإيده، وسط أوردر
    /// بيتحرق قدامه.
    ///
    /// والنتيجة الأسوأ إن ده كان بيبان **زي العطل**: تدوس إيقاف، الورق
    /// يفضل طالع دقيقة كمان، وبعدين المكن تقف واحدة ورا التانية — فتفتكر
    /// إن البرنامج بوّظ الأوردر. حصلت فعلًا وقعدنا ندوّر على عطل مش موجود.
    ///
    /// من غير await عن قصد: الزرار لازم يرجع للمستخدم فورًا، وWMI بطيء.
    /// </summary>
    private async Task PurgePrinterQueuesAsync(IReadOnlyList<string> printers)
    {
        int removed = 0;

        try
        {
            removed = await Task.Run(() => printers.Sum(name => _printerQueue!.CancelAll(name)));
        }
        catch
        {
            // مقدرناش نفضّي؟ الإلغاء نفسه حصل خلاص. بنقول الحقيقة تحت.
        }

        OnUiThread(() =>
        {
            string line = removed > 0
                ? $"[إيقاف] اتشال {removed} جوب من طوابير الطباعة. " +
                  "⚠ الورق اللي وصل ذاكرة المكنة نفسها هيطلع برضه — مفيش حاجة توقفه من الكمبيوتر."
                : "[إيقاف] مفيش جوبات في الطوابير تتشال.";

            Log.Add(line);
            _jobLog?.Info(line);
        });
    }

    // ══════════ تنضيف طابور الطباعة ══════════

    /// <summary>
    /// بيوقف خدمة السبولر، يمسح الجوبات الزنقانة، ويشغّلها تاني.
    ///
    /// ═══ اقرا ده قبل ما تضغط ═══
    ///
    ///   • بيمسح طابور **ويندوز كله**، مش طابور PrintFlow بس. أي حاجة
    ///     مستنية من Word أو من أي برنامج تاني بتضيع معاها. عشان كده
    ///     الزرار مقفول والأوردر ماشي.
    ///   • محتاج صلاحية مسؤول. من غيرها ويندوز بيرفض إيقاف الخدمة —
    ///     والرفض ده بيطلع على stderr **من غير ما كود الخروج يتغيّر**،
    ///     فبنقراه بأيدينا بدل ما نصدّق الصفر.
    ///   • الشاهد النهائي: بنسأل ويندوز في آخر السطر «الخدمة شغالة؟»
    ///     ومابنقولش نجحنا غير لما يرد "Running". أوحش نتيجة ممكنة هي
    ///     إن الخدمة تقف ومترجعش — والمطبعة تكتشف ده بعد ساعة.
    /// </summary>
    private async Task CleanSpoolerAsync()
    {
        string spool = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "PRINTERS");

        string starting = "[تنبيه] بنوقف خدمة الطباعة وننضّف الطابور — أي شغل مستني في ويندوز هيضيع.";
        Log.Add(starting);
        _jobLog?.Info(starting);
        StatusText = "بننضّف طابور الطباعة...";

        var (running, output) = await Task.Run(() => RunPowerShell(
            "$ErrorActionPreference='Stop'; " +
            "Stop-Service -Name Spooler -Force; " +
            $"Remove-Item -Path '{spool}\\*' -Force -Recurse -ErrorAction SilentlyContinue; " +
            "Start-Service -Name Spooler; " +
            "(Get-Service -Name Spooler).Status"));

        string line = running
            ? "[نجاح] الطابور اتنضّف وخدمة الطباعة رجعت شغالة."
            : "[فشل] مانفعش ننضّف الطابور. اقفل البرنامج وافتحه «كمسؤول» " +
              $"(كليك يمين ← Run as administrator) وجرّب تاني. رد ويندوز: {output}";

        Log.Add(line);
        _jobLog?.Info(line);

        StatusText = running
            ? "طابور الطباعة اتنضّف وخدمة الطباعة شغالة."
            : "مانفعش ننضّف الطابور — محتاج تشغّل البرنامج كمسؤول.";
    }

    /// <summary>
    /// بيشغّل سطر PowerShell ويرجّع: الخدمة رجعت شغالة؟ ونص الرد.
    ///
    /// الأوامر بتتبعت في <c>ArgumentList</c> مش في سترينج واحد — ويندوز
    /// هو اللي بيتولى التهريب، فمسار فيه مسافة مايكسرش الأمر.
    /// </summary>
    private static (bool Running, string Output) RunPowerShell(string script)
    {
        var info = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(script);

        try
        {
            using var process = Process.Start(info);

            if (process is null)
            {
                return (false, "مقدرناش نشغّل PowerShell.");
            }

            // بنقرا الاتنين قبل الانتظار: البروسيس بيقف لو الـ pipe اتملى
            // وإحنا مستنيينه يخلص — قفلة كاملة من غير أي رسالة خطأ.
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(60_000))
            {
                return (false, "الأمر أخد أكتر من دقيقة — سيبناه.");
            }

            bool running = stdout.Contains("Running", StringComparison.OrdinalIgnoreCase);

            string message = string.IsNullOrWhiteSpace(stderr)
                ? stdout.Trim()
                : stderr.Trim();

            return (running, string.IsNullOrWhiteSpace(message) ? $"كود الخروج {process.ExitCode}." : message);
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
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

            // الصور اللي اتحوّلت لـ PDF من غير ما المستخدم يطلب حفظها.
            // بنمسح **الملفات** مش المجلد، عشان مانمسحش صورة اتحوّلت
            // من دقايق وهي لسه في القايمة.
            string converted = Path.Combine(folder, ConvertedFolderName);

            if (Directory.Exists(converted))
            {
                foreach (var path in Directory.EnumerateFiles(converted, "*.pdf"))
                {
                    if (File.GetLastWriteTime(path) < cutoff)
                    {
                        File.Delete(path);
                    }
                }
            }
        }
        catch
        {
            // تنضيف التيمب مش حاجة تستاهل توقف الشغل لو فشلت
        }
    }

    /// <summary>
    /// بينسي كل اللي البرنامج اتعلّمه عن سرعة المكن.
    ///
    /// ═══ ليه الزرار ده موجود ═══
    ///
    /// الدفتر بيصلّح نفسه في الظروف العادية — كل قياس جديد بياخد ٣٠٪
    /// والمحفوظ ٧٠٪، فأي رقم غلط بيختفي بعد كام أوردر لوحده.
    ///
    /// بس فيه حالات القديم فيها مابيبقاش «قديم» — بيبقى **غلط**:
    ///
    ///   • مكنة اتصلّحت أو اتغيّر فيها الدرام فبقت أسرع
    ///   • مكنة اتبدّلت بواحدة تانية بنفس الاسم
    ///
    /// وبعد ما رفعنا حد أقل عيّنة (<see cref="PrinterSpeedBook.MinimumPages"/>)،
    /// الأوردرات الصغيرة بقت مش بتسجّل حاجة — فالرقم الغلط ممكن يفضل
    /// شهور في مطبعة شغلها كله أوردرات صغيرة.
    ///
    /// من غير الزرار ده الطريقة الوحيدة كانت مسح ملف من %AppData% بأمر
    /// PowerShell — وده حاجة صاحب المطبعة مش هيعملها.
    /// </summary>
    private void ForgetSpeeds()
    {
        _speeds.Forget();

        // السطر ده بيعرض القسمة المحسوبة على السرعات. من غير التحديث
        // بيفضل معروض عليه قسمة اتبنت على أرقام إحنا لسه ماسحينها.
        RefreshPrinterChoiceSummary();

        StatusText = "اتمسحت سرعات المكن المتسجّلة. لحد ما أوردر كبير يخلص، "
                   + "الشغل هيتوزّع بالتساوي وسرقة الشغل هي اللي هتظبّط الفرق.";
    }
}