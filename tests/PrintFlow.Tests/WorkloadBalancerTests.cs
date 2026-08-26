using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// توزيع الشغل على المكن.
///
/// أهم تست في الملف ده هو <see cref="Nothing_Is_Ever_Lost_Or_Duplicated"/> —
/// لأن أسوأ عطل ممكن يحصل هنا مش إن التوزيع يطلع مش متساوي، ده إن **ملزمة
/// تضيع** فالزبون ياخد ناقص، أو **تتطبع مرتين** فالمطبعة تخسر ورق. التساوي
/// تحسين؛ العدد الصح شرط.
/// </summary>
public class WorkloadBalancerTests
{
    private static List<PrintableDocument> Docs(params int[] pageCounts)
        => pageCounts.Select((p, i) => new PrintableDocument($"ملزمة{i + 1}.pdf", p)).ToList();

    private static List<string> Machines(int count)
        => Enumerable.Range(1, count).Select(i => $"مكنة{i}").ToList();

    // ══════════ الحالة اللي المطبعة طلبتها ══════════

    [Fact]
    public void Fifty_Booklets_Land_Five_On_Each_Of_Ten_Machines()
    {
        var plan = WorkloadBalancer.Balance(Docs(Enumerable.Repeat(40, 50).ToArray()), 1, Machines(10));

        Assert.All(plan.Printers, p => Assert.Equal(5, p.Documents));
        Assert.All(plan.Printers, p => Assert.Equal(200, p.Pages));
        Assert.Equal(0, plan.Spread);
    }

    [Fact]
    public void Fifty_Booklets_Reach_Ten_Different_Machines_Not_One()
    {
        // ده اللي كان بيحصل قبل كده: التوزيع القديم كان بيدّي المكنة الأولى
        // نسخة والباقيين صفر، فالخمسين ملزمة كلها كانت بتروح لمكنة واحدة
        var plan = WorkloadBalancer.Balance(Docs(Enumerable.Repeat(40, 50).ToArray()), 1, Machines(10));

        Assert.Equal(10, plan.Assignments.Select(a => a.PrinterName).Distinct().Count());
    }

    [Fact]
    public void The_Heavy_Booklet_Goes_First_Not_Last()
    {
        // الحالة اللي بتفضح "الأتقل الأول". ست ملازم صغيرة وواحدة تقيلة،
        // **والتقيلة آخر القايمة**:
        //
        //   من غير ترتيب: الصغيرين بيتوزعوا بالتساوي (٦،٦،٦) وبعدين
        //                 التقيلة بتقع فوق واحدة منهم → ١٥، ٦، ٦
        //   بالترتيب:     التقيلة بتاخد مكنة لوحدها والصغيرين يملوا
        //                 الباقي → ٩، ٩، ٩
        //
        // من غير التست ده، شيل الترتيب من الكود ومفيش تست بيقع.
        var plan = WorkloadBalancer.Balance(Docs(3, 3, 3, 3, 3, 3, 9), 1, Machines(3));

        Assert.Equal(0, plan.Spread);
        Assert.All(plan.Printers, p => Assert.Equal(9, p.Pages));
    }

    [Fact]
    public void Uneven_Booklets_Still_Finish_Together()
    {
        // ملازم من ١٠ لـ ٢٠٠ صفحة، **مبعترة عن قصد** — ترتيب المدخلات
        // ماينفعش يفرق في النتيجة
        int[] pages = [30, 200, 10, 90, 120, 20, 150, 60, 180, 40, 100, 80];

        var plan = WorkloadBalancer.Balance(Docs(pages), 1, Machines(4));

        int ideal = pages.Sum() / 4;

        Assert.All(plan.Printers, p => Assert.InRange(p.Pages, ideal - 20, ideal + 20));
    }

    [Fact]
    public void Shuffling_The_Input_Does_Not_Change_How_Even_It_Is()
    {
        int[] pages = [30, 200, 10, 90, 120, 20, 150, 60, 180, 40, 100, 80];

        var asGiven = WorkloadBalancer.Balance(Docs(pages), 1, Machines(4));
        var sorted = WorkloadBalancer.Balance(Docs(pages.OrderBy(p => p).ToArray()), 1, Machines(4));

        Assert.Equal(asGiven.Spread, sorted.Spread);
    }

    // ══════════ ماينفعش يضيع ولا يتكرر ══════════

    [Theory]
    [InlineData(50, 1, 10)]
    [InlineData(50, 3, 10)]
    [InlineData(1, 50, 10)]
    [InlineData(7, 5, 3)]
    [InlineData(3, 1, 10)]
    [InlineData(1, 1, 1)]
    [InlineData(120, 2, 7)]
    public void Nothing_Is_Ever_Lost_Or_Duplicated(int documentCount, int copies, int printerCount)
    {
        var documents = Docs(Enumerable.Range(1, documentCount).Select(i => i * 3).ToArray());

        var plan = WorkloadBalancer.Balance(documents, copies, Machines(printerCount));

        foreach (var document in documents)
        {
            int printed = plan.Assignments
                .Where(a => a.Path == document.Path)
                .Sum(a => a.Copies);

            Assert.Equal(copies, printed);
        }
    }

    [Fact]
    public void The_Total_Paper_Matches_What_Was_Asked_For()
    {
        var documents = Docs(10, 25, 4, 60);

        var plan = WorkloadBalancer.Balance(documents, 3, Machines(3));

        Assert.Equal(documents.Sum(d => d.Weight) * 3, plan.TotalPages);
        Assert.Equal(plan.TotalPages, plan.Assignments.Sum(a => a.TotalPages));
    }

    // ══════════ بتعمّم القديم مابتلغيهوش ══════════

    [Theory]
    [InlineData(50, 10)]
    [InlineData(7, 3)]
    [InlineData(100, 8)]
    [InlineData(3, 10)]
    public void One_Document_Many_Copies_Matches_The_Old_Calculator(int copies, int printerCount)
    {
        // الخوارزمية الجديدة لازم تدّي نفس نتيجة CopyDistributionCalculator
        // في الحالة اللي هو كان بيتعامل معاها. لو اختلفوا، يبقى إحنا غيّرنا
        // سلوك كان شغال صح.
        var machines = Machines(printerCount);

        var plan = WorkloadBalancer.Balance(Docs(40), copies, machines);
        var old = CopyDistributionCalculator.Distribute(copies, machines);

        foreach (var expected in old)
        {
            int actual = plan.Assignments
                .Where(a => a.PrinterName == expected.PrinterName)
                .Sum(a => a.Copies);

            Assert.Equal(expected.CopiesAssigned, actual);
        }
    }

    [Fact]
    public void Copies_Of_The_Same_Document_Become_One_Job_Not_Many()
    {
        // ٥٠ نسخة على ١٠ مكن لازم تبقى ١٠ جوبات × ٥ نسخ،
        // مش ٥٠ جوب × نسخة — طابور الطباعة مش سلة مهملات
        var plan = WorkloadBalancer.Balance(Docs(40), 50, Machines(10));

        Assert.Equal(10, plan.Assignments.Count);
        Assert.All(plan.Assignments, a => Assert.Equal(5, a.Copies));
    }

    // ══════════ الحالات الحدّية ══════════

    [Fact]
    public void One_Machine_Takes_Everything()
    {
        var plan = WorkloadBalancer.Balance(Docs(10, 20, 30), 2, Machines(1));

        Assert.Equal(3, plan.Assignments.Count);
        Assert.Equal(120, plan.Printers[0].Pages);
        Assert.Equal(0, plan.Spread);
    }

    [Fact]
    public void Spare_Machines_Are_Reported_Not_Hidden()
    {
        // ٣ ملازم على ١٠ مكن: ٧ مكن هتقعد فاضية، واللي في المطبعة
        // لازم يعرف عشان ما يقفش يستنى ورق مش جاي
        var plan = WorkloadBalancer.Balance(Docs(10, 10, 10), 1, Machines(10));

        Assert.Equal(7, plan.Idle.Count);
        Assert.Contains("ماخدتش شغل", plan.Describe());
    }

    [Fact]
    public void Documents_With_Unknown_Page_Counts_Still_Spread_Out()
    {
        // مقدرناش نعد صفحات الملفات (تالفة أو محمية). لو وزنها صفر
        // كانت كلها هتتكوّم على أول مكنة لأنها كلها "مجانية".
        var plan = WorkloadBalancer.Balance(Docs(0, 0, 0, 0, 0, 0), 1, Machines(3));

        Assert.All(plan.Printers, p => Assert.Equal(2, p.Documents));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Zero_Copies_Produces_No_Work_Instead_Of_Throwing(int copies)
    {
        var plan = WorkloadBalancer.Balance(Docs(10, 20), copies, Machines(3));

        Assert.Empty(plan.Assignments);
        Assert.Equal(3, plan.Idle.Count);
    }

    [Fact]
    public void No_Printers_Produces_No_Work_Instead_Of_Throwing()
    {
        var plan = WorkloadBalancer.Balance(Docs(10), 5, []);

        Assert.Empty(plan.Assignments);
        Assert.Equal("مفيش شغل يتوزّع.", plan.Describe());
    }

    [Fact]
    public void No_Documents_Produces_No_Work_Instead_Of_Throwing()
    {
        var plan = WorkloadBalancer.Balance([], 5, Machines(3));

        Assert.Empty(plan.Assignments);
    }

    // ══════════ نفس المدخلات = نفس النتيجة ══════════

    [Fact]
    public void The_Same_Job_Always_Splits_The_Same_Way()
    {
        // توزيع بيتغيّر من تشغيلة للتانية مستحيل حد يراجعه أو يصدّقه
        var documents = Docs(50, 12, 90, 7, 33, 61, 18);

        var first = WorkloadBalancer.Balance(documents, 4, Machines(5));
        var second = WorkloadBalancer.Balance(documents, 4, Machines(5));

        Assert.Equal(
            first.Assignments.Select(a => $"{a.PrinterName}|{a.Path}|{a.Copies}"),
            second.Assignments.Select(a => $"{a.PrinterName}|{a.Path}|{a.Copies}"));
    }

    [Fact]
    public void A_Big_Job_Does_Not_Take_Forever()
    {
        // ٥٠٠ مستند × ٢٠ نسخة = ١٠٠٠٠ قطعة على ١٦ مكنة
        var documents = Docs(Enumerable.Range(1, 500).Select(i => (i % 90) + 10).ToArray());

        var plan = WorkloadBalancer.Balance(documents, 20, Machines(16));

        Assert.All(documents, d =>
            Assert.Equal(20, plan.Assignments.Where(a => a.Path == d.Path).Sum(a => a.Copies)));

        // على ١٠٠٠٠ قطعة، الفرق المفروض يبقى قد أتقل مستند تقريبًا
        Assert.InRange(plan.Spread, 0, 100);
    }

    // ══════════ الوصف اللي بيتكتب في اللوج ══════════

    [Fact]
    public void The_Log_Line_Says_The_Total_And_How_Even_It_Is()
    {
        var plan = WorkloadBalancer.Balance(Docs(40, 40, 40, 40), 1, Machines(2));

        string text = plan.Describe();

        Assert.Contains("160", text);
        Assert.Contains("2 مكنة", text);
        Assert.Contains("0", text);
    }
}
