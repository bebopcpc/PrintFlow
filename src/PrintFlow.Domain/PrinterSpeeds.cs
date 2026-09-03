namespace PrintFlow.Domain;

/// <summary>
/// سرعة كل مكنة بالصفحة/الثانية — لقطة ثابتة بتتقرا وقت التوزيع.
///
/// ═══ ليه نوع مستقل مش مجرد Dictionary ═══
///
/// عشان سؤال «المكنة دي سرعتها كام» ليه إجابة واحدة صح في كل مكان:
/// المكنة اللي ليها تاريخ بتاخد رقمها، والمكنة الجديدة اللي لسه ماشتغلتش
/// بتاخد **متوسط اللي نعرفهم**. لو سيبنا ده لكل واحد يحسبه لوحده، أول
/// مرة حد ينسى، المكنة الجديدة هتاخد صفر وتقعد فاضية طول الأوردر.
///
/// اللقطة دي **قراءة بس**: الموزّع مايقدرش يغيّرها وهو شغال، فالخطة
/// اللي اتكتبت في اللوج هي نفسها اللي اتنفّذت.
///
/// حساب خالص على أرقام — متختبر من غير طابعة ولا ملف.
/// </summary>
public sealed class PrinterSpeeds
{
    /// <summary>مفيش أي قياسات — كل المكن زي بعض، يعني نفس سلوك النسخ القديمة بالظبط.</summary>
    public static PrinterSpeeds Equal { get; } = new(new Dictionary<string, double>());

    private readonly Dictionary<string, double> _pagesPerSecond;
    private readonly double _average;

    public PrinterSpeeds(IReadOnlyDictionary<string, double> pagesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(pagesPerSecond);

        // الأرقام الغلط بتتشال هنا مرة واحدة بدل ما كل اللي بيقرا يتحاسب
        // عليها. صفر أو سالب أو NaN معناه قياس بايظ — نعتبره مش موجود.
        _pagesPerSecond = pagesPerSecond
            .Where(pair => double.IsFinite(pair.Value) && pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        _average = _pagesPerSecond.Count > 0 ? _pagesPerSecond.Values.Average() : 1d;
    }

    public bool IsEmpty => _pagesPerSecond.Count == 0;

    public int Count => _pagesPerSecond.Count;

    /// <summary>
    /// سرعة المكنة دي. المكنة المجهولة بتاخد متوسط المعروفين.
    ///
    /// **ليه المتوسط مش صفر ولا واحد:** المكنة الجديدة لو أدّيناها رقم
    /// صغير هتقعد تتفرّج، ولو أدّيناها رقم كبير هتتكوّم عليها الدنيا
    /// وتتأخر. المتوسط بيخليها تاخد نصيب معقول لحد ما أول أوردر يقيسها.
    /// </summary>
    public double For(string printerName)
        => printerName is not null && _pagesPerSecond.TryGetValue(printerName, out double speed)
            ? speed
            : _average;

    /// <summary>سطر عربي للّوج: مين أسرع من مين، ومين لسه مجهول.</summary>
    public string Describe(IReadOnlyList<string> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);

        if (printers.Count == 0)
        {
            return "";
        }

        var parts = printers.Select(name =>
        {
            bool known = _pagesPerSecond.ContainsKey(name);
            string tag = known ? "" : " (متوسط — لسه مااتقاستش)";
            return $"{name} {For(name):0.00} ص/ث{tag}";
        });

        return "السرعات المعتمدة: " + string.Join("، ", parts) + ".";
    }
}