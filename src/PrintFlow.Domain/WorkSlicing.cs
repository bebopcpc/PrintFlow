namespace PrintFlow.Domain;

/// <summary>
/// قطعة شغل واحدة: مستند + عدد نسخ محدود.
///
/// دي أصغر وحدة الموزّع بيحرّكها. اللي بيميّزها عن
/// <see cref="WorkAssignment"/> إنها **صغيرة عن قصد** — نصيب المكنة كله
/// بيتقسّم لكذا قطعة بدل ما يبقى أمر طباعة واحد كبير.
/// </summary>
public sealed record WorkUnit(string Path, int Pages, int Copies)
{
    /// <summary>إجمالي الورق اللي القطعة دي هتطلعه.</summary>
    public int Weight => Math.Max(1, Pages) * Math.Max(0, Copies);
}

/// <summary>
/// بيقسّم نصيب كل مكنة لقطع صغيرة.
///
/// ═══ ليه التقطيع أصلًا ═══
///
/// <see cref="WorkloadBalancer"/> بيطلّع خطة عادلة: ٥٠ نسخة على ٣ مكن =
/// ١٧ و١٧ و١٦. لو بعتنا كل نصيب كأمر طباعة **واحد**، بيحصل حاجتين وحشين:
///
///   ١) لو مكنة وقعت في نص شغلها، الـ ١٧ نسخة كلها في الشك مرة واحدة.
///      محدش يعرف طلع منها كام.
///
///   ٢) المكنة السريعة بتخلص نصيبها وتقف تتفرّج على البطيئة. الخطة
///      اتحسبت بالصفحات مش بالسرعة الحقيقية، ومحدش يعرف سرعة كل مكنة
///      قبل ما تشتغل.
///
/// لما النصيب يتقسّم لأربع قطع، الاتنين بيتحلّوا:
///
///   • القطعة الواقعة صغيرة، فاللي في الشك ربع النصيب مش كله.
///   • المكنة اللي خلصت بتشيل قطعة من طابور مكنة لسه شغالة (شوف
///     الموزّع) — فالسرعة الحقيقية هي اللي بتوزّع في الآخر، مش التقدير.
///
/// ═══ ليه أربعة ═══
///
/// كل قطعة = تشغيلة SumatraPDF لوحدها، وليها تكلفة ثابتة صغيرة. أربعة
/// رقم وسط: التوازن بيتحسّن بوضوح، والتكلفة تفضل مهملة جنب زمن الطباعة
/// الحقيقي. وفي نفس الوقت مابنكسرش النسخة الواحدة لنُصّين — أقل قطعة
/// نسخة كاملة.
///
/// حساب خالص على أرقام — متختبر من غير طابعة ولا ملف.
/// </summary>
public static class WorkSlicing
{
    /// <summary>كام قطعة نقسّم عليها نصيب المكنة الواحدة من المستند الواحد.</summary>
    public const int PiecesPerAssignment = 4;

    /// <summary>
    /// بيقسّم نصيب واحد لقطع.
    ///
    /// النسخ بتتوزّع بالتساوي قدر الإمكان، والباقي بيروح للقطع الأولى —
    /// عشان القطعة الأخيرة تبقى هي الأصغر، فلو الشغل اتقطع في آخره يبقى
    /// اللي ضاع أقل حاجة ممكنة.
    /// </summary>
    public static IReadOnlyList<WorkUnit> Split(
        WorkAssignment assignment,
        int pieces = PiecesPerAssignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        if (assignment.Copies <= 0)
        {
            return [];
        }

        // مابنكسرش نسخة: عدد القطع مايزيدش عن عدد النسخ
        int count = Math.Clamp(pieces, 1, assignment.Copies);

        int share = assignment.Copies / count;
        int extra = assignment.Copies % count;

        var units = new List<WorkUnit>(count);

        for (int piece = 0; piece < count; piece++)
        {
            int copies = share + (piece < extra ? 1 : 0);
            units.Add(new WorkUnit(assignment.Path, assignment.Pages, copies));
        }

        return units;
    }

    /// <summary>
    /// بيحوّل الخطة كلها لطابور لكل مكنة.
    ///
    /// كل مكنة بتاخد طابورها الخاص — **مش طابور مشترك واحد**. الفرق مهم:
    /// الطابور المشترك بيوزّع بالسحب بس، والسحب لوحده مابيوصلش لعدالة
    /// الخطة (٥٠ نسخة في قطع من ٥ على ٣ مكن بتطلع ٢٠/١٥/١٥ مش ١٧/١٧/١٦).
    /// الطوابير المنفصلة بتبدأ من الخطة العادلة بالظبط، والسحب من طابور
    /// الغير بيبقى **تصحيح** لما مكنة تقع أو تبقى أسرع من التقدير.
    ///
    /// جوه الطابور الواحد: الأتقل الأول، عشان الشغل التقيل يمشي بدري
    /// ومايفضلش لآخر لحظة.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<WorkUnit>> Lanes(
        WorkloadPlan plan,
        int pieces = PiecesPerAssignment)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var lanes = new Dictionary<string, List<WorkUnit>>(StringComparer.Ordinal);

        // كل المكن بتاخد طابور حتى لو فاضي — المكنة الفاضية لسه ممكن
        // تشيل شغل من غيرها، فلازم يبقى ليها عامل شغّال
        foreach (var printer in plan.Printers)
        {
            lanes[printer.PrinterName] = [];
        }

        foreach (var assignment in plan.Assignments)
        {
            if (!lanes.TryGetValue(assignment.PrinterName, out var lane))
            {
                lane = [];
                lanes[assignment.PrinterName] = lane;
            }

            lane.AddRange(Split(assignment, pieces));
        }

        foreach (var lane in lanes.Values)
        {
            // الأتقل الأول. الترتيب ثابت تمامًا: عند تساوي الوزن بنرجع
            // لاسم الملف، عشان نفس المدخلات تدّي نفس الترتيب كل مرة.
            lane.Sort((a, b) =>
            {
                int byWeight = b.Weight.CompareTo(a.Weight);
                return byWeight != 0 ? byWeight : string.CompareOrdinal(a.Path, b.Path);
            });
        }

        return lanes.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<WorkUnit>)pair.Value,
            StringComparer.Ordinal);
    }
}
