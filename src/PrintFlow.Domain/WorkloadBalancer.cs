namespace PrintFlow.Domain;

/// <summary>مستند جاهز للطباعة وعدد صفحاته.</summary>
public sealed record PrintableDocument(string Path, int Pages)
{
    /// <summary>
    /// الوزن اللي بيتوزّع بيه. صفحة على الأقل حتى لو مقدرناش نعد الصفحات.
    ///
    /// من غير الحد الأدنى ده، مستند عدد صفحاته صفر وزنه صفر — فكل المستندات
    /// المجهولة كانت هتتكوّم على أول مكنة لأنها كلها "مجانية".
    /// </summary>
    public int Weight => Math.Max(1, Pages);
}

/// <summary>نصيب مكنة واحدة من مستند واحد.</summary>
public sealed record WorkAssignment(string PrinterName, string Path, int Copies, int Pages)
{
    /// <summary>إجمالي الورق اللي المكنة دي هتطلعه من المستند ده.</summary>
    public int TotalPages => Math.Max(1, Pages) * Copies;
}

/// <summary>حِمل مكنة واحدة بعد التوزيع.</summary>
public sealed record PrinterWorkload(string PrinterName, int Documents, int Pages)
{
    public bool IsIdle => Documents == 0;
}

/// <summary>
/// خطة التوزيع كاملة: مين بيطبع إيه، وكل مكنة نصيبها كام.
/// </summary>
public sealed record WorkloadPlan(
    IReadOnlyList<WorkAssignment> Assignments,
    IReadOnlyList<PrinterWorkload> Printers)
{
    public int TotalPages => Printers.Sum(p => p.Pages);

    /// <summary>
    /// الفرق بين أتقل مكنة وأخف مكنة **شغّالة**، بالصفحات.
    ///
    /// ده مقياس جودة التوزيع: صفر يعني كل المكن هتخلص مع بعض. المكن اللي
    /// ماخدتش شغل خالص مش داخلة في الحساب — هي مش "متأخرة"، هي فاضية.
    /// </summary>
    public int Spread
    {
        get
        {
            var working = Printers.Where(p => !p.IsIdle).ToList();

            return working.Count == 0 ? 0 : working.Max(p => p.Pages) - working.Min(p => p.Pages);
        }
    }

    public IReadOnlyList<PrinterWorkload> Idle => Printers.Where(p => p.IsIdle).ToList();

    /// <summary>سطر عربي يتكتب في اللوج قبل ما الطباعة تبدأ.</summary>
    public string Describe()
    {
        if (Assignments.Count == 0)
        {
            return "مفيش شغل يتوزّع.";
        }

        var busy = Printers.Where(p => !p.IsIdle).ToList();

        string line = $"التوزيع: {TotalPages} صفحة على {busy.Count} مكنة " +
                      $"(الفرق بين أتقل وأخف مكنة {Spread} صفحة).";

        if (Idle.Count > 0)
        {
            line += $" مكن ماخدتش شغل: {string.Join("، ", Idle.Select(p => p.PrinterName))}.";
        }

        return line;
    }
}

/// <summary>
/// بيوزّع شغل الطباعة على المكن بحيث **الكل يخلص مع بعض**.
///
/// ليه بالصفحات مش بعدد الملفات: ٥٠ ملزمة أحجامها مختلفة، لو قسمناها
/// ٥ لكل مكنة، المكنة اللي وقع نصيبها الملازم التقيلة هتفضل شغالة ساعة
/// والباقيين خلصوا من زمان — والمطبعة بتستنى أبطأ مكنة مش متوسطهم.
///
/// الخوارزمية: LPT (Longest Processing Time first) — رتّب الشغل من الأتقل
/// للأخف، وكل قطعة تروح للمكنة اللي حِملها أقل دلوقتي. خوارزمية معروفة
/// ومثبت إن نتيجتها مابتزيدش عن ٤/٣ من التوزيع المثالي، وفي نفس الوقت
/// بسيطة كفاية إن أي حد يقراها ويتأكد إنها صح.
///
/// **بتعمّم التوزيع القديم مابتلغيهوش:** مستند واحد × ٥٠ نسخة × ١٠ مكن
/// بتطلع منها ٥ نسخ لكل مكنة — نفس نتيجة <see cref="CopyDistributionCalculator"/>
/// بالظبط. وفي تست بيثبت ده.
///
/// حساب خالص على أرقام وأسامي — متختبر من غير أي طابعة ولا ملف.
/// </summary>
public static class WorkloadBalancer
{
    /// <summary>
    /// بيقسّم المستندات ونسخها على المكن.
    /// </summary>
    /// <param name="documents">المستندات الجاهزة بعد المعالجة.</param>
    /// <param name="copiesPerDocument">عدد النسخ المطلوبة من كل مستند.</param>
    /// <param name="printers">أسامي المكن المؤهلة، بالترتيب.</param>
    /// <param name="speeds">
    /// سرعات المكن المقيسة من أوردرات فاتت. سيبها فاضية والتوزيع بيرجع
    /// بالتساوي بالظبط زي ما كان — ولا سطر في النتيجة بيتغيّر.
    /// </param>
    public static WorkloadPlan Balance(
        IReadOnlyList<PrintableDocument> documents,
        int copiesPerDocument,
        IReadOnlyList<string> printers,
        PrinterSpeeds? speeds = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(printers);

        if (printers.Count == 0 || documents.Count == 0 || copiesPerDocument <= 0)
        {
            return new WorkloadPlan([], printers.Select(p => new PrinterWorkload(p, 0, 0)).ToList());
        }

        // ═══ ١) فرد الشغل: كل (مستند، نسخة) قطعة لوحدها ═══
        //
        // النسخ بتتفرد عن قصد. ملف واحد × ٥٠ نسخة لازم يتقسّم على المكن،
        // ومن غير الفرد ده كان هيبقى قطعة واحدة تقيلة تروح لمكنة واحدة.

        var units = new List<(int Document, int Weight)>(documents.Count * copiesPerDocument);

        for (int document = 0; document < documents.Count; document++)
        {
            for (int copy = 0; copy < copiesPerDocument; copy++)
            {
                units.Add((document, documents[document].Weight));
            }
        }

        // ═══ ٢) الأتقل الأول ═══
        //
        // ترتيب ثابت تمامًا (الوزن ثم رقم المستند) عشان نفس المدخلات تدّي
        // نفس النتيجة كل مرة — التوزيع اللي بيتغيّر من تشغيلة للتانية
        // مستحيل حد يصدّقه ولا يراجعه.

        units.Sort((a, b) =>
        {
            int byWeight = b.Weight.CompareTo(a.Weight);
            return byWeight != 0 ? byWeight : a.Document.CompareTo(b.Document);
        });

        // ═══ ٣) كل قطعة للمكنة اللي هتخلّص بدري ═══
        //
        // كان: "روح للمكنة اللي حِملها أقل صفحات". ده بيتقسّم بالتساوي
        // المطلق — ٥٧٠ صفحة على ٣ مكن = ١٩٠ لكل واحدة، حتى لو واحدة فيهم
        // أسرع من التانية بالضعف. النتيجة إن السريعة بتخلص وتقف تستنى.
        //
        // بقى: "روح للمكنة اللي **وقت خلاصها** أقرب" — يعني الحِمل مقسوم
        // على سرعتها. المكنة اللي بتطبع الضعف بتاخد الضعف، والكل بيخلص
        // مع بعض فعلًا مش على الورق.
        //
        // ⚠ لما كل السرعات تبقى واحدة (مفيش قياسات لسه)، القسمة على نفس
        // الرقم مابتغيّرش الترتيب — فالنتيجة **مطابقة حرفيًا** للقديم.
        // وده اللي بيخلي كل التستات القديمة تفضل عدّاية من غير تعديل.
        //
        // ⚠ ودي لسه **توقُّع** مش أمر: القياس ممكن يبقى قديم أو المكنة
        // تبوظ النهاردة. سرقة الشغل في WorkDispatcher هي صمام الأمان،
        // وهي ما اتلمستش ولا سطر.

        var load = new long[printers.Count];
        var speed = new double[printers.Count];

        var book = speeds ?? PrinterSpeeds.Equal;

        for (int printer = 0; printer < printers.Count; printer++)
        {
            double value = book.For(printers[printer]);

            // حزام أمان: قسمة على صفر أو NaN كانت هتفضّي الأوردر كله على
            // مكنة واحدة من غير ما حد ياخد باله.
            speed[printer] = double.IsFinite(value) && value > 0 ? value : 1d;
        }

        // [مكنة][مستند] = عدد النسخ
        var assigned = new Dictionary<(int Printer, int Document), int>();

        foreach (var (document, weight) in units)
        {
            int earliest = 0;
            double bestFinish = load[0] / speed[0];

            for (int printer = 1; printer < printers.Count; printer++)
            {
                double finish = load[printer] / speed[printer];

                // "أقل من" مش "أقل أو يساوي" — عند التساوي بنفضل المكنة
                // الأولى، وده اللي بيخلي النتيجة ثابتة
                if (finish < bestFinish)
                {
                    earliest = printer;
                    bestFinish = finish;
                }
            }

            load[earliest] += weight;

            var key = (earliest, document);
            assigned[key] = assigned.TryGetValue(key, out int copies) ? copies + 1 : 1;
        }
        

        // ═══ ٤) لمّ النسخ: نفس المستند على نفس المكنة = جوب واحد ═══
        //
        // من غير اللمّة دي، ٥٠ نسخة على مكنة واحدة كانت هتبقى ٥٠ جوب في
        // طابور الطباعة بدل جوب واحد بـ ٥٠ نسخة.

        var assignments = new List<WorkAssignment>(assigned.Count);

        for (int printer = 0; printer < printers.Count; printer++)
        {
            for (int document = 0; document < documents.Count; document++)
            {
                if (assigned.TryGetValue((printer, document), out int copies) && copies > 0)
                {
                    assignments.Add(new WorkAssignment(
                        printers[printer], documents[document].Path, copies, documents[document].Pages));
                }
            }
        }

        var workloads = new List<PrinterWorkload>(printers.Count);

        for (int printer = 0; printer < printers.Count; printer++)
        {
            int documentCount = 0;

            for (int document = 0; document < documents.Count; document++)
            {
                if (assigned.TryGetValue((printer, document), out int copies) && copies > 0)
                {
                    documentCount++;
                }
            }

            workloads.Add(new PrinterWorkload(printers[printer], documentCount, (int)load[printer]));
        }

        return new WorkloadPlan(assignments, workloads);
    }
}
