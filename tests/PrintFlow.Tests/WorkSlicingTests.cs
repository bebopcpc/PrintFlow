using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// تقطيع نصيب المكنة لقطع صغيرة.
///
/// أهم تست في الملف ده هو <see cref="Cutting_Never_Loses_Or_Duplicates_A_Copy"/>.
/// التقطيع الغلط أوحش من عدم التقطيع: لو ضاعت نسخة الزبون ياخد ناقص، ولو
/// اتكررت المطبعة تدفع تمن ورق. التوازن تحسين؛ العدد الصح شرط.
/// </summary>
public class WorkSlicingTests
{
    private static WorkAssignment Share(int copies, int pages = 10, string path = "ملزمة.pdf")
        => new(PrinterName: "مكنة", Path: path, Copies: copies, Pages: pages);

    // ══════════ الشرط: العدد الصح ══════════

    [Fact]
    public void Cutting_Never_Loses_Or_Duplicates_A_Copy()
    {
        // بنجرب كل عدد نسخ من ١ لـ ٢٠٠ — مش عيّنة، الكل. القطع لازم
        // تجمع بالظبط على العدد الأصلي في كل حالة.
        for (int copies = 1; copies <= 200; copies++)
        {
            var pieces = WorkSlicing.Split(Share(copies));

            Assert.Equal(copies, pieces.Sum(p => p.Copies));
        }
    }

    [Fact]
    public void No_Piece_Is_Ever_Empty()
    {
        // قطعة بصفر نسخة = تشغيلة طباعة على الفاضي. مش خطأ قاتل بس وقت
        // ضايع، والموزّع بيحسبها قطعة اتعملت.
        for (int copies = 1; copies <= 50; copies++)
        {
            Assert.All(WorkSlicing.Split(Share(copies)), piece => Assert.True(piece.Copies > 0));
        }
    }

    [Fact]
    public void A_Single_Copy_Is_Never_Cut_In_Half()
    {
        var pieces = WorkSlicing.Split(Share(copies: 1));

        Assert.Single(pieces);
        Assert.Equal(1, pieces[0].Copies);
    }

    [Fact]
    public void Two_Copies_Give_At_Most_Two_Pieces()
    {
        // عدد القطع مايزيدش عن عدد النسخ مهما طلبنا
        Assert.Equal(2, WorkSlicing.Split(Share(copies: 2), pieces: 10).Count);
    }

    [Fact]
    public void Zero_Copies_Gives_No_Work_Instead_Of_An_Empty_Job()
    {
        Assert.Empty(WorkSlicing.Split(Share(copies: 0)));
    }

    // ══════════ الشكل: أربع قطع ══════════

    [Fact]
    public void A_Share_Is_Cut_Into_Four_Pieces()
    {
        var pieces = WorkSlicing.Split(Share(copies: 17));

        Assert.Equal(4, pieces.Count);
        Assert.Equal(17, pieces.Sum(p => p.Copies));
    }

    [Fact]
    public void The_Remainder_Goes_To_The_Early_Pieces_Not_The_Last()
    {
        // ١٧ على ٤ = ٥،٤،٤،٤ مش ٤،٤،٤،٥.
        //
        // ليه ده مهم: القطعة الأخيرة هي اللي بتخلص عليها المكنة، وهي
        // أكتر حتة معرّضة إن الشغل يتقطع فيها. كل ما تكون أصغر، كل ما
        // اللي في الشك يبقى أقل.
        var pieces = WorkSlicing.Split(Share(copies: 17));

        Assert.Equal(5, pieces[0].Copies);
        Assert.Equal(4, pieces[^1].Copies);
        Assert.True(pieces[0].Copies >= pieces[^1].Copies);
    }

    [Fact]
    public void Every_Piece_Keeps_The_Document_It_Came_From()
    {
        var pieces = WorkSlicing.Split(Share(copies: 9, pages: 40, path: @"C:\شغل\كتالوج.pdf"));

        Assert.All(pieces, piece => Assert.Equal(@"C:\شغل\كتالوج.pdf", piece.Path));
        Assert.All(pieces, piece => Assert.Equal(40, piece.Pages));
    }

    [Fact]
    public void A_Pieces_Weight_Is_Its_Pages_Times_Its_Copies()
    {
        Assert.Equal(120, new WorkUnit("ملزمة.pdf", Pages: 40, Copies: 3).Weight);
    }

    [Fact]
    public void A_Document_We_Could_Not_Count_Still_Weighs_Something()
    {
        // نفس قاعدة PrintableDocument: صفحة على الأقل. من غيرها كل
        // المستندات المجهولة وزنها صفر وبتتكوّم على مكنة واحدة.
        Assert.Equal(5, new WorkUnit("مجهول.pdf", Pages: 0, Copies: 5).Weight);
    }

    // ══════════ الطوابير ══════════

    private static WorkloadPlan PlanFor(int[] pageCounts, int copies, int machines)
    {
        var documents = pageCounts
            .Select((pages, index) => new PrintableDocument($"ملزمة{index + 1}.pdf", pages))
            .ToList();

        var printers = Enumerable.Range(1, machines).Select(i => $"مكنة{i}").ToList();

        return WorkloadBalancer.Balance(documents, copies, printers);
    }

    [Fact]
    public void The_Lanes_Carry_Exactly_What_The_Plan_Said()
    {
        // ده الرباط بين الموازن والتقطيع. لو التقطيع غيّر أرقام الخطة،
        // التوزيع العادل بيضيع من غير ما حد ياخد باله.
        var plan = PlanFor([40, 12, 7, 55, 3], copies: 13, machines: 4);
        var lanes = WorkSlicing.Lanes(plan);

        foreach (var assignment in plan.Assignments)
        {
            int inLane = lanes[assignment.PrinterName]
                .Where(unit => unit.Path == assignment.Path)
                .Sum(unit => unit.Copies);

            Assert.Equal(assignment.Copies, inLane);
        }
    }

    [Fact]
    public void The_Whole_Order_Survives_The_Cutting()
    {
        var plan = PlanFor([40, 12, 7, 55, 3], copies: 13, machines: 4);
        var lanes = WorkSlicing.Lanes(plan);

        // ٥ ملازم × ١٣ نسخة = ٦٥ نسخة، مهما اتقسّمت
        Assert.Equal(65, lanes.Values.SelectMany(lane => lane).Sum(unit => unit.Copies));
    }

    [Fact]
    public void Every_Machine_Gets_A_Lane_Even_When_It_Has_No_Work()
    {
        // المكنة الفاضية لازم يبقى ليها طابور — عشان الموزّع يشغّل عليها
        // عامل، والعامل ده هو اللي هيشيل شغل غيره لو وقعت مكنة.
        //
        // من غير السطر ده، مكنة ماخدتش نصيب في الخطة بتفضل واقفة حتى لو
        // كل المكن التانية غرقانة.
        var plan = PlanFor([100], copies: 1, machines: 3);
        var lanes = WorkSlicing.Lanes(plan);

        Assert.Equal(3, lanes.Count);
        Assert.Contains(lanes, lane => lane.Value.Count == 0);
    }

    [Fact]
    public void The_Heaviest_Piece_Is_First_In_Its_Lane()
    {
        var plan = PlanFor([80, 5], copies: 1, machines: 1);
        var lane = WorkSlicing.Lanes(plan)["مكنة1"];

        Assert.True(lane[0].Weight >= lane[^1].Weight);
        Assert.Equal(80, lane[0].Pages);
    }

    [Fact]
    public void The_Same_Order_Gives_The_Same_Lanes_Every_Time()
    {
        // توزيع بيتغيّر من تشغيلة للتانية مستحيل حد يراجعه أو يصدّقه
        var first = WorkSlicing.Lanes(PlanFor([9, 9, 4, 22], copies: 7, machines: 3));
        var second = WorkSlicing.Lanes(PlanFor([9, 9, 4, 22], copies: 7, machines: 3));

        foreach (var lane in first)
        {
            Assert.Equal(lane.Value, second[lane.Key]);
        }
    }

    [Fact]
    public void Fifty_Booklets_On_Ten_Machines_Still_Land_Five_Each()
    {
        // الحالة اللي المطبعة طلبتها من الأول. التقطيع مالوش حق يلمسها.
        var plan = PlanFor([.. Enumerable.Repeat(40, 50)], copies: 1, machines: 10);
        var lanes = WorkSlicing.Lanes(plan);

        Assert.All(lanes.Values, lane => Assert.Equal(5, lane.Sum(unit => unit.Copies)));
    }
}
