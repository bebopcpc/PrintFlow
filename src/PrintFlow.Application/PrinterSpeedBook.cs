using System.Text.Json;
using PrintFlow.Domain;

namespace PrintFlow.Application;

/// <summary>
/// بيقيس سرعة كل مكنة من الأوردرات اللي بتخلص، وبيحفظها بين التشغيلات.
///
/// ═══ إيه اللي بيتقاس بالظبط ═══
///
/// **مش** الوقت اللي أمر الطباعة أخده. الأمر بيرجع أول ما الجوب يوصل
/// طابور ويندوز — يعني نفس الرقم تقريبًا لكل المكن، وقياسه كان هيبقى
/// كذب مرتب.
///
/// اللي بيتقاس: **الصفحات اللي المكنة سلّمتها ÷ زمن الأوردر كله**.
///
/// ⚠ المقام هو الأوردر كله عن قصد — مش لحد آخر قطعة المكنة سلّمتها.
/// جرّبنا التانية أول مرة وطلعت بتكدب: مكنة سلّمت ٥٧ صفحة في أول ١١
/// ثانية وبعدين قعدت أربع دقايق مابتاخدش شغل، اتسجّلت ٥ ص/ث. ومكنة
/// تانية شالت ٣٩٩ صفحة على ٢٥١ ثانية اتسجّلت ١.٦ ص/ث. يعني اللي عملت
/// ٧٠٪ من الأوردر طلعت "الأبطأ".
///
/// بزمن الأوردر كله، المكنة اللي وقفت تتفرّج بيبان إنها وقفت.
///
/// ═══ ليه التقليل أأمن من التهويل ═══
///
/// لو قلّلنا تقدير مكنة سريعة، سرقة الشغل بتصلّحها فورًا — بتخلص
/// نصيبها الصغير وتشيل من غيرها. لو هوّلنا تقدير مكنة بطيئة، بتاخد
/// نصيب كبير وتبقى هي عنق الزجاجة، **وسرقة الشغل مش بتقدر تصلّح ده**
/// لأنها بتسحب من الطوابير مش من المكنة اللي ماسكة الشغل خلاص.
/// فالخطأ في اتجاه واحد بيتصلّح، والتاني لأ. اخترنا الاتجاه اللي بيتصلّح.
///
/// ═══ ليه الأرقام مابتتصدّقش من أول مرة ═══
///
///   • العيّنة الصغيرة مابتتحسبش خالص (شوف <see cref="MinimumPages"/>).
///     أوردر ٣ صفحات مابيقولش حاجة عن سرعة مكنة.
///   • المكنة اللي وقعت أو دخلت في الشك بيتشال قياسها — الوقفة دي مشكلة
///     توفّر مش بطء، ولو حسبناها المكنة هتفضل "بطيئة" شهر بعد ما تتصلّح.
///   • الرقم الجديد بيتخلط مع القديم (<see cref="NewSampleWeight"/>)، فأوردر
///     واحد شاذ مايقلبش التوزيع.
///
/// الملف ده **مجرد ذاكرة مساعدة**. لو ضاع أو اتبوّظ، البرنامج بيرجع
/// يوزّع بالتساوي زي ما كان — مفيش حاجة بتقف.
/// </summary>
public sealed class PrinterSpeedBook
{
    /// <summary>أقل عدد صفحات نصدّق عنده قياس.</summary>
    public const int MinimumPages = 20;

    /// <summary>وزن القياس الجديد جنب المحفوظ. الباقي للقديم.</summary>
    public const double NewSampleWeight = 0.30;

    /// <summary>حدود العقل: أقل وأكتر سرعة نقبلها، عشان قياس شاذ مايتسجّلش.</summary>
    private const double SlowestBelievable = 0.02;
    private const double FastestBelievable = 200d;

    private static readonly TimeSpan MinimumSpan = TimeSpan.FromSeconds(3);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, double> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sample> _current = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _path;

    private DateTimeOffset _orderStart;

    private sealed class Sample
    {
        public int Pages;
        public bool Trusted = true;
    }

    /// <param name="folder">مجلد الحفظ. الافتراضي %AppData%\PrintFlow.</param>
    /// <param name="clock">الساعة — بتتغيّر في التستات بس.</param>
    public PrinterSpeedBook(string? folder = null, Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        string home = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrintFlow");

        _path = Path.Combine(home, "printer-speeds.json");
        _orderStart = _clock();

        Load();
    }

    /// <summary>مسار الملف — بيتقال في اللوج عشان اللي في المطبعة يعرف يمسحه لو حب.</summary>
    public string FilePath => _path;

    /// <summary>لقطة للموزّع. **نسخة**، فالتوزيع مابيتغيّرش تحت رجليه.</summary>
    public PrinterSpeeds Snapshot()
    {
        lock (_gate)
        {
            return new PrinterSpeeds(new Dictionary<string, double>(_known, StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>أوردر جديد بدأ — بنصفّر الشريط ونظبط الساعة.</summary>
    public void OrderStarted()
    {
        lock (_gate)
        {
            _current.Clear();
            _orderStart = _clock();
        }
    }

    /// <summary>قطعة وصلت فعلًا. بيتنده من ثريد خلفي، فكله جوّه القفل.</summary>
    public void NoteDelivered(string printerName, int pages)
    {
        if (string.IsNullOrWhiteSpace(printerName) || pages <= 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!_current.TryGetValue(printerName, out var sample))
            {
                sample = new Sample();
                _current[printerName] = sample;
            }

            sample.Pages += pages;
        }
    }

    /// <summary>
    /// المكنة دي وقعت أو دخلت في الشك — قياسها النهاردة مايتحسبش.
    ///
    /// من غير ده، مكنة الورق خلص منها لخمس دقايق كانت هتتسجّل "بطيئة"
    /// وتفضل واخدة نصيب أقل في كل أوردر جاي — عقوبة على عطل خلص.
    /// </summary>
    public void Distrust(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return;
        }

        lock (_gate)
        {
            if (_current.TryGetValue(printerName, out var sample))
            {
                sample.Trusted = false;
            }
            else
            {
                _current[printerName] = new Sample { Trusted = false };
            }
        }
    }

    /// <summary>
    /// الأوردر خلص — بنطلّع القياسات ونحفظها، ونرجّع سطر يتكتب في اللوج.
    /// بيرجّع "" لو مفيش أي عيّنة تستاهل.
    /// </summary>
    public string OrderFinished()
    {
        var learned = new List<string>();

        lock (_gate)
        {
            // مقام واحد لكل المكن: زمن الأوردر من أوله لآخره.
            var whole = _clock() - _orderStart;

            if (whole < MinimumSpan)
            {
                _current.Clear();
                return "";
            }

            foreach (var (printer, sample) in _current)
            {
                if (!sample.Trusted || sample.Pages < MinimumPages)
                {
                    continue;
                }

                double measured = sample.Pages / whole.TotalSeconds;

                if (!double.IsFinite(measured) || measured < SlowestBelievable || measured > FastestBelievable)
                {
                    continue;
                }

                double blended = _known.TryGetValue(printer, out double old) && old > 0
                    ? (old * (1 - NewSampleWeight)) + (measured * NewSampleWeight)
                    : measured;

                _known[printer] = blended;
                learned.Add($"{printer} {blended:0.00} ص/ث");
            }

            _current.Clear();
        }

        if (learned.Count == 0)
        {
            return "";
        }

        Save();

        return "[قياس] السرعات اتحدّثت: " + string.Join("، ", learned) + ".";
    }

    /// <summary>بيمسح كل القياسات ويرجّع التوزيع للتساوي. أداة صيانة.</summary>
    public void Forget()
    {
        lock (_gate)
        {
            _known.Clear();
        }

        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var stored = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(_path));

            if (stored is null)
            {
                return;
            }

            foreach (var (printer, speed) in stored)
            {
                if (double.IsFinite(speed) && speed > 0)
                {
                    _known[printer] = speed;
                }
            }
        }
        catch
        {
            // ملف بايظ = مالوش قيمة. بنبدأ من فاضي بدل ما نمنع الطباعة
            // عشان ملف كاش.
        }
    }

    private void Save()
    {
        try
        {
            Dictionary<string, double> copy;

            lock (_gate)
            {
                copy = new Dictionary<string, double>(_known, StringComparer.OrdinalIgnoreCase);
            }

            string? folder = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // بنكتب على جنب وبعدين نبدّل: لو الكهربا قطعت في نص الكتابة،
            // الملف القديم يفضل سليم بدل ما يبقى نُص ملف مالوش لازمة.
            string temp = _path + ".tmp";

            File.WriteAllText(temp, JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, overwrite: true);
        }
        catch
        {
            // مانقدرش نحفظ؟ الأوردر خلص خلاص. القياس هيتعاد المرة الجاية.
        }
    }
}