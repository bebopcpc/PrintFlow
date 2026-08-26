using PrintFlow.Domain;

namespace PrintFlow.Presentation;

/// <summary>ملف واحد في قايمة المعالجة.</summary>
public sealed class PdfFileItem : ObservableObject
{
    public PdfFileItem(string fullPath, long sizeBytes, DateTime modifiedUtc)
    {
        FullPath = fullPath;
        FileName = Path.GetFileName(fullPath);
        SizeBytes = sizeBytes;
        ModifiedUtc = modifiedUtc;
    }

    /// <summary>مسار الملف اللي السلسلة بتشتغل عليه — دايمًا PDF.</summary>
    public string FullPath { get; }

    /// <summary>
    /// المسار اللي المستخدم اختاره فعلًا. بيساوي <see cref="FullPath"/> إلا
    /// في الصور: ساعتها ده مسار الصورة الأصلية والـ FullPath هو الـ PDF
    /// المحوّل.
    ///
    /// مهم لمنع التكرار: من غيره، لو المستخدم رمى نفس الصورة مرتين كان
    /// هيتعمل تحويلين بأسامي مختلفة والاتنين يدخلوا القايمة.
    /// </summary>
    public string SourcePath { get; init; } = "";

    public string FileName { get; }
    public long SizeBytes { get; }
    public DateTime ModifiedUtc { get; }

    /// <summary>أصله صورة اتحوّلت لـ PDF؟</summary>
    public bool WasConverted =>
        SourcePath.Length > 0 &&
        !string.Equals(SourcePath, FullPath, StringComparison.OrdinalIgnoreCase);

    private int? _pageCount;
    /// <summary>عدد الصفحات — بيتملى بعدين، محتاج قراءة الملف.</summary>
    public int? PageCount
    {
        get => _pageCount;
        set
        {
            if (SetProperty(ref _pageCount, value))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string SizeText => SizeBytes >= 1024 * 1024
        ? $"{SizeBytes / 1024d / 1024d:0.#} م.ب"
        : $"{SizeBytes / 1024d:0.#} ك.ب";

    /// <summary>
    /// الاسم اللي بيظهر للمستخدم. للصور بنعرض اسم **الصورة الأصلية** مش
    /// الـ PDF المحوّل — هو رمى "فاتورة.jpg" ومش المفروض يدوّر عليها في
    /// القايمة تحت اسم تاني.
    /// </summary>
    private string ShownName => WasConverted ? Path.GetFileName(SourcePath) : FileName;

    public string DisplayText => PageCount is int pages
        ? $"{ShownName}{(WasConverted ? " ← PDF" : "")}  —  {pages} صفحة  —  {SizeText}"
        : $"{ShownName}{(WasConverted ? " ← PDF" : "")}  —  {SizeText}";
}

/// <summary>
/// طابعة في قايمة الاختيار. IsSelected هنا بدل SelectionMode="Multiple"،
/// وده بيلغي الحاجة لـ ExtractPrinterName اللي كانت بتفكّ اسم الطابعة من نص العرض.
/// </summary>
public sealed class PrinterItem : ObservableObject
{
    public PrinterItem(Printer printer)
    {
        Name = printer.Name;
        Update(printer);
    }

    public string Name { get; }

    private PrinterStatus _status;
    public PrinterStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(IsEligible));
            }
        }
    }

    private bool _isDefault;
    public bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (SetProperty(ref _isDefault, value))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    private string? _port;
    public string? Port
    {
        get => _port;
        set
        {
            if (SetProperty(ref _port, value))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// دي طابعة PrintFlow الوهمية نفسها؟
    ///
    /// **ليه ده مهم:** الطابعة الوهمية بتكتب في مجلد البرنامج، والبرنامج
    /// بيراقب المجلد ده. لو البرنامج طبع عليها، الجوب بيرجعله تاني —
    /// وفي وضع الطباعة التلقائية بتبقى حلقة لا نهائية بتاكل القرص.
    ///
    /// وده مش احتمال نظري: بعد ما الطابعة اتسطّبت بقت **الافتراضية** على
    /// الجهاز، والبرنامج بيبدأ على الافتراضية — فبقت هدف الطباعة لوحدها.
    /// </summary>
    public bool IsVirtualPrintFlow =>
        string.Equals(Name, VirtualPrinter.PrinterName, StringComparison.OrdinalIgnoreCase);

    public bool IsEligible =>
        Status != PrinterStatus.Offline && Status != PrinterStatus.Error && !IsVirtualPrintFlow;

    public string StatusText => Status switch
    {
        PrinterStatus.Ready => "جاهزة",
        PrinterStatus.Offline => "غير متصلة",
        PrinterStatus.Error => "خطأ",
        _ => "غير معروف"
    };

    public string DisplayText
    {
        get
        {
            if (IsVirtualPrintFlow)
            {
                return $"{Name} — دي طابعة الاستقبال، مش هدف للطباعة";
            }

            string defaultTag = IsDefault ? " (افتراضية)" : "";
            string port = string.IsNullOrWhiteSpace(Port) ? "" : $" — {Port}";
            return $"{Name}{defaultTag} — {StatusText}{port}";
        }
    }

    /// <summary>بتحدّث الحالة في نفس الكائن بدل ما نبني اللستة من الأول — فالاختيار مايضيعش.</summary>
    public void Update(Printer printer)
    {
        Status = printer.Status;
        IsDefault = printer.IsDefault;
        Port = printer.Port;
    }
}

/// <summary>
/// خانة واحدة في معاينة الشرائح — بإحداثيات بكسل جاهزة للرسم.
///
/// الأرقام دي **مش تقريبية**: بتتحسب بنفس دوال SheetLayout اللي بتحسب
/// الطباعة الحقيقية، مصغّرة بس. يعني المعاينة مستحيل تختلف عن الورق الطالع.
/// </summary>
public sealed class SlidePreviewCell
{
    public required int Number { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
}

/// <summary>لون في قايمة الألوان: الاسم العربي + قيمته hex.</summary>
public sealed class NamedColor
{
    public NamedColor(string hex, string label)
    {
        Hex = hex;
        Label = label;
    }

    public string Hex { get; }
    public string Label { get; }

    public override string ToString() => Label;
}

/// <summary>عنصر في ComboBox: القيمة الحقيقية + النص العربي اللي المستخدم بيشوفه.</summary>
public sealed class EnumOption<T> where T : struct, Enum
{
    public EnumOption(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public T Value { get; }
    public string Label { get; }

    public override string ToString() => Label;
}
