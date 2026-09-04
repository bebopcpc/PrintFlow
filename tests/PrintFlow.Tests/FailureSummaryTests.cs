using PrintFlow.Application;
using PrintFlow.Domain;
using PrintFlow.Presentation;

namespace PrintFlow.Tests;

/// <summary>
/// بيحمي شاشة النتايج من الغرق.
///
/// جه من تجربة حقيقية: ٤٤ ملف اتحمّلوا، فشل منهم ٢٠ لنفس السبب، فطلعوا
/// عشرين سطر متطابق في الشاشة — ومحدش فيهم بيقول أنهي ملف.
/// </summary>
public class FailureSummaryTests
{
    private const string Reason = "[فشل] الدمج: حذف الصفحات \"1\" شال كل الصفحات.";
    private const string Other = "[فشل] الملف تالف أو مش PDF سليم.";

    // ══════════ الحالة اللي التست اتكتب عشانها ══════════

    /// <summary>
    /// ⚠ عشرين ملف لنفس السبب = سطرين، مش عشرين سطر.
    /// </summary>
    [Fact]
    public void Twenty_Files_With_One_Reason_Become_Two_Lines()
    {
        var failures = Enumerable.Range(1, 20).Select(i => ($"file{i}.pdf", Reason));

        var lines = FailureSummary.Describe(failures);

        Assert.Equal(2, lines.Count);
        Assert.Contains("20 ملف", lines[0]);
    }

    /// <summary>
    /// ⚠ والأسامي بتتقال — دي اللي كانت ناقصة تمامًا.
    ///
    /// من غيرها المستخدم عارف إن حاجة فشلت ومش عارف يروح فين يصلّحها.
    /// </summary>
    [Fact]
    public void The_File_Names_Are_Said()
    {
        var lines = FailureSummary.Describe([("عقد.pdf", Reason), ("فاتورة.pdf", Reason)]);

        Assert.Contains("عقد.pdf", lines[1]);
        Assert.Contains("فاتورة.pdf", lines[1]);
    }

    /// <summary>
    /// بس مش كلهم — تلاتة وبعدين "وكذا غيرهم"، عشان السطر مايخرجش
    /// بره الشاشة في أوردر ٥٠٠ ملف.
    /// </summary>
    [Fact]
    public void Only_The_First_Three_Names_Are_Listed()
    {
        var failures = Enumerable.Range(1, 10).Select(i => ($"f{i}.pdf", Reason));

        var lines = FailureSummary.Describe(failures);

        Assert.Contains("f1.pdf", lines[1]);
        Assert.Contains("f3.pdf", lines[1]);
        Assert.DoesNotContain("f4.pdf", lines[1]);
        Assert.Contains("7 غيرهم", lines[1]);
    }

    /// <summary>تلاتة بالظبط بيتقالوا كلهم — مفيش "وصفر غيرهم".</summary>
    [Fact]
    public void Exactly_Three_Names_Are_All_Listed()
    {
        var lines = FailureSummary.Describe(
            [("a.pdf", Reason), ("b.pdf", Reason), ("c.pdf", Reason)]);

        Assert.Contains("a.pdf", lines[1]);
        Assert.Contains("c.pdf", lines[1]);
        Assert.DoesNotContain("غيرهم", lines[1]);
    }

    // ══════════ أكتر من سبب ══════════

    /// <summary>كل سبب ليه سطوره — وبترتيب حصولها مش بترتيب أبجدي.</summary>
    [Fact]
    public void Different_Reasons_Are_Kept_Apart_In_The_Order_They_Happened()
    {
        var lines = FailureSummary.Describe(
        [
            ("a.pdf", Other),
            ("b.pdf", Reason),
            ("c.pdf", Other)
        ]);

        Assert.Equal(4, lines.Count);
        Assert.Contains("تالف", lines[0]);      // الأول اللي حصل الأول
        Assert.Contains("2 ملف", lines[0]);
        Assert.Contains("حذف الصفحات", lines[2]);
    }

    // ══════════ الملف الواحد ══════════

    /// <summary>
    /// ملف واحد مابيتقالش عنه "1 ملف" — بيتقال السبب زي ما كان بالظبط.
    /// إضافة عدّاد لملف واحد كلام زيادة مالوش لازمة.
    /// </summary>
    [Fact]
    public void A_Single_Failure_Reads_Exactly_As_Before()
    {
        var lines = FailureSummary.Describe([("عقد.pdf", Reason)]);

        Assert.Equal(Reason, lines[0]);
        Assert.DoesNotContain("1 ملف", lines[0]);
    }

    // ══════════ شكل السطر ══════════

    /// <summary>
    /// ⚠ السطر لازم يفضل مبتدي بـ [فشل].
    ///
    /// شاشة النتايج بتلوّن السطور على أساس الوسم اللي في أولها. لو حطينا
    /// العدد قبله، السطر كان هيطلع بلون عادي وسط سطور النجاح.
    /// </summary>
    [Fact]
    public void The_Line_Still_Starts_With_The_Failure_Tag()
    {
        var failures = Enumerable.Range(1, 5).Select(i => ($"f{i}.pdf", Reason));

        var lines = FailureSummary.Describe(failures);

        Assert.StartsWith("[فشل] ", lines[0]);
        Assert.Contains("5 ملف", lines[0]);
    }

    /// <summary>ورسالة من غير وسم بتتجمّع برضه، من غير ما نخترع وسم.</summary>
    [Fact]
    public void A_Message_Without_A_Tag_Is_Still_Grouped()
    {
        var lines = FailureSummary.Describe([("a.pdf", "حاجة غريبة"), ("b.pdf", "حاجة غريبة")]);

        Assert.Contains("2 ملف", lines[0]);
        Assert.Contains("حاجة غريبة", lines[0]);
    }

    // ══════════ الحالات الفاضية ══════════

    [Fact]
    public void No_Failures_Means_No_Lines()
    {
        Assert.Empty(FailureSummary.Describe([]));
        Assert.Empty(FailureSummary.Describe(null!));
    }

    /// <summary>اسم ملف فاضي مايكسرش السطر — بنقول السبب من غير أسامي.</summary>
    [Fact]
    public void A_Missing_File_Name_Does_Not_Break_The_Line()
    {
        var lines = FailureSummary.Describe([("", Reason)]);

        Assert.Single(lines);
        Assert.Equal(Reason, lines[0]);
    }

    /// <summary>
    /// ⚠ النجوم بتظهر للمستخدم زي ما هي — السطور دي نص خام في الواجهة.
    /// </summary>
    [Fact]
    public void The_Lines_Have_No_Markdown_Stars()
    {
        var failures = Enumerable.Range(1, 6).Select(i => ($"f{i}.pdf", Reason));

        foreach (string line in FailureSummary.Describe(failures))
        {
            Assert.DoesNotContain("*", line);
        }
    }

    // ══════════ قايمة الأسامي (بتتستخدم من خدمة الدمج كمان) ══════════

    /// <summary>
    /// ⚠ الدالة دي عامة عشان خدمة الدمج بتنديها.
    ///
    /// هناك كانت المشكلة بشكل تاني: ٤٠ تنبيه بيتلموا في **سطر واحد
    /// عملاق** يملا مربع النتايج كله. نفس الحل: تلات أسامي وبعدين العدد.
    /// </summary>
    [Theory]
    [InlineData(1, "a1.pdf")]
    [InlineData(3, "a1.pdf، a2.pdf، a3.pdf")]
    [InlineData(5, "a1.pdf، a2.pdf، a3.pdf و2 غيرهم")]
    [InlineData(40, "a1.pdf، a2.pdf، a3.pdf و37 غيرهم")]
    public void The_Name_List_Stops_At_Three(int count, string expected)
    {
        var names = Enumerable.Range(1, count).Select(i => $"a{i}.pdf").ToList();

        Assert.Equal(expected, FailureSummary.NameList(names));
    }

    // ══════════ الوصلة للشاشة ══════════

    /// <summary>
    /// ⚠ ده التست اللي بيهم فعلًا: التجميع بيوصل شاشة النتايج.
    ///
    /// الدالة ممكن تبقى شغالة تمام وشاشة النتايج لسه بتكتب سطر لكل ملف —
    /// لو حد سابها في الحلقة بالغلط. التست ده بيمشي على المعالجة الحقيقية
    /// بعشر ملفات كلهم بيفشلوا، ويعدّ السطور اللي وصلت الشاشة.
    /// </summary>
    [Fact]
    public async Task Ten_Failing_Files_Do_Not_Fill_The_Results_Screen()
    {
        string folder = Path.Combine(Path.GetTempPath(), "PFFail_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var vm = new MainViewModel(
                new NoPrinters(), new AlwaysFailsService(), new NeverPrints(), pdfInfo: new FourPages());

            var paths = new List<string>();

            for (int i = 1; i <= 10; i++)
            {
                string path = Path.Combine(folder, $"f{i}.pdf");
                File.WriteAllText(path, "pdf");
                paths.Add(path);
            }

            vm.AddFiles(paths);

            vm.Settings.MergeFiles = false;
            vm.Settings.PrintDirectlyAfterProcessing = false;
            vm.Settings.DeletePages = true;
            vm.Settings.PagesToDelete = "1";

            await vm.ProcessCommand.ExecuteAsync();

            // سطرين للفشل المجمّع — مش عشرة
            var failureLines = vm.Log.Where(l => l.Contains("[فشل]")).ToList();

            Assert.Single(failureLines);
            Assert.Contains("10 ملف", failureLines[0]);
            Assert.Contains(vm.Log, l => l.Contains("f1.pdf") && l.Contains("7 غيرهم"));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* تنضيف */ }
        }
    }

    // ══════════ خدمات مزيفة ══════════

    private sealed class AlwaysFailsService : IPdfMergeService
    {
        public MergeResult Merge(MergeRequest request) => MergeResult.Failed("الملف تالف أو مش PDF سليم.");
    }

    private sealed class NoPrinters : IPrinterRepository
    {
        public Task<List<Printer>> GetPrintersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Printer>());

        public string SendTestPage(string printerName) => "ok";

        public PrinterCapabilities GetCapabilities(string printerName) => new();
    }

    private sealed class NeverPrints : IPdfPrintService
    {
        public Task<PrintOutcome> PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
            => Task.FromResult(PrintOutcome.Delivered("اتبعت"));
    }

    private sealed class FourPages : IPdfInfoService
    {
        public int? TryGetPageCount(string filePath) => 4;

        public (double Width, double Height)? TryGetPageSize(string filePath) => null;
    }
}
