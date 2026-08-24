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

    public string FullPath { get; }
    public string FileName { get; }
    public long SizeBytes { get; }
    public DateTime ModifiedUtc { get; }

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

    public string DisplayText => PageCount is int pages
        ? $"{FileName}  —  {pages} صفحة  —  {SizeText}"
        : $"{FileName}  —  {SizeText}";
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

    public bool IsEligible => Status != PrinterStatus.Offline && Status != PrinterStatus.Error;

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
