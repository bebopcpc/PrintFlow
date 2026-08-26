using System.Diagnostics;
using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيشغّل SumatraPDF عشان يبعت الملف للطابعة.
///
/// اتغير عن النسخة القديمة في تلات حاجات:
///   1) بروسيس واحد لكل جوب بدل بروسيس لكل نسخة (عن طريق {n}x).
///   2) WaitForExitAsync بدل WaitForExit — مفيش ثريد بيتحجز وهو مستني.
///   3) ArgumentList بدل بناء سطر أوامر بالإيد — الويندوز بيتولى التهريب.
///
/// ملاحظة ترخيص: SumatraPDF ترخيصه GPLv3 وبيتوزّع كملف تنفيذي منفصل بيتنادى
/// من سطر الأوامر. لو البرنامج هيتباع، لازم مراجعة التزامات التوزيع.
/// </summary>
public class PdfPrintService : IPdfPrintService
{
    private static readonly string SumatraPath =
        Path.Combine(AppContext.BaseDirectory, "tools", "SumatraPDF.exe");

    /// <summary>
    /// بيتأكد إن الملف اللي اسمه SumatraPDF.exe هو فعلًا SumatraPDF.
    ///
    /// ليه ده موجود: اتشحن مرة ملف بالاسم ده وهو برنامج تاني خالص (Delphi،
    /// 533 ك.ب، من غير أي معلومات نسخة). مكانش بيفهم -print-to، بيرجّع كود 0،
    /// وميطبعش ورقة. النتيجة كانت "نجاح" في اللوج ومفيش أي ورق — أسوأ شكل
    /// للعطل، لأنه بيبان زي الشغل السليم بالظبط.
    ///
    /// بيتحسب مرة واحدة عند أول طباعة بس.
    /// </summary>
    private static readonly Lazy<string?> SumatraProblem = new(() =>
    {
        if (!File.Exists(SumatraPath))
        {
            return "SumatraPDF.exe مش موجود في مجلد tools. تأكد إنك حطيته صح.";
        }

        try
        {
            // أقوى إشارة: بصمة النسخة اللي إحنا شاحنينها بنفسنا. لو طابقت،
            // يبقى الملف هو هو بالبايت ومفيش داعي لأي فحص تاني.
            //
            // لو ماطابقتش مابنرفضش — المستخدم ممكن يكون حدّث SumatraPDF بنفسه
            // لنسخة أحدث، وده تصرف سليم. بنكمّل على الفحوصات الأضعف.
            if (FileHash(SumatraPath) == KnownGoodSha256)
            {
                return null;
            }

            // الحجم هو الفحص الأساسي، ومتعمد كده: SumatraPDF الحقيقي حوالي ٢٠ م.ب،
            // والملف المزيف كان نص ميجا. والفحص ده شغال على أي نظام.
            long megabytes = new FileInfo(SumatraPath).Length / 1024 / 1024;

            if (megabytes < MinimumSumatraMegabytes)
            {
                return $"الملف tools\\SumatraPDF.exe حجمه {megabytes} م.ب بس — " +
                       "ده مش SumatraPDF. النسخة الحقيقية حوالي ٢٠ م.ب. " +
                       "نزّل النسخة المحمولة 64-bit من sumatrapdfreader.org وحطها مكانه.";
            }

            // فحص إضافي لما ويندوز يقدر يقرا معلومات النسخة. لو رجعت فاضية
            // (بيحصل على أنظمة تانية) مابنرفضش — الحجم عدّى وده كفاية.
            // القاعدة: مانمنعش الطباعة غير لما نتأكد إيجابًا إن الملف غلط.
            string? productName = FileVersionInfo.GetVersionInfo(SumatraPath).ProductName;

            if (!string.IsNullOrWhiteSpace(productName) &&
                !productName.Contains("SumatraPDF", StringComparison.OrdinalIgnoreCase))
            {
                return $"الملف tools\\SumatraPDF.exe اسم منتجه '{productName}' مش SumatraPDF. " +
                       "نزّل النسخة المحمولة 64-bit من sumatrapdfreader.org وحطها مكانه.";
            }
        }
        catch (Exception ex)
        {
            // مقدرناش نفحص؟ بنكمل ونسيب Sumatra يتكلم. منع الطباعة بسبب فشل
            // فحص أسوأ من الباج اللي الفحص أصلًا موجود عشانه.
            System.Diagnostics.Debug.WriteLine($"فحص SumatraPDF فشل: {ex.Message}");
        }

        return null;
    });

    /// <summary>SumatraPDF 3.6.1 حجمه ~٢٠ م.ب. الحد ده واسع عن قصد.</summary>
    private const long MinimumSumatraMegabytes = 5;

    /// <summary>
    /// بصمة SumatraPDF 3.6.1 (64-bit) اللي بتتشحن مع البرنامج.
    ///
    /// لو الملف طابقها، يبقى مفيش أي شك فيه. ولو مطابقش، مابنرفضش —
    /// يمكن يكون اتحدّث لنسخة أحدث، وساعتها الحجم واسم المنتج بيتكلموا.
    /// </summary>
    private const string KnownGoodSha256 =
        "719f689b34f47be8ca105ce8484948474dafde0e106bab599e4a89326070c3d0";

    private static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// بيبعت الجوب. **كل الشغل بيتنفّذ بعيد عن ثريد الواجهة.**
    ///
    /// الجسم اللي تحت بيلمس WMI (بروسيس DCOM متزامن)، وبيشغّل بروسيسات،
    /// وأول مرة بيحسب SHA256 لملف ٢٠ ميجا. وده كله كان بيتنفّذ على ثريد
    /// الواجهة لأن الميثود بتتنده من هناك و await في WPF بيرجّع التنفيذ
    /// للواجهة بعد كل انتظار. النتيجة: الشاشة بتتجمد طول الأوردر.
    ///
    /// التوكن **مش** بيتبعت لـ Task.Run عن قصد: عايزين الإلغاء يرجّع
    /// PrintOutcome.Cancelled بهدوء عشان الموزّع يفهمه، مش يرمي استثناء.
    /// </summary>
    public Task<PrintOutcome> PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        return Task.Run(() => PrintOnBackgroundAsync(job, cancellationToken));
    }

    private async Task<PrintOutcome> PrintOnBackgroundAsync(PrintJob job, CancellationToken cancellationToken)
    {
        if (job.Copies <= 0)
        {
            return PrintOutcome.Skipped($"[تخطي] '{job.PrinterName}' نصيبها صفر نسخة، مفيش حاجة اتبعتت.");
        }

        if (SumatraProblem.Value is { } problem)
        {
            // الأداة نفسها ناقصة أو غلط — نقل الشغل لمكنة تانية هيفشل
            // بالظبط زي ما فشل هنا، فمالوش لازمة
            return PrintOutcome.BadJob($"[فشل] {problem}");
        }

        if (!File.Exists(job.FilePath))
        {
            // الملف مش موجود — مش عيب المكنة، ونقله مش هيلاقيه
            return PrintOutcome.BadJob($"[فشل] الملف مش موجود: {job.FilePath}");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = SumatraPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in SumatraArguments.BuildArguments(job))
            {
                startInfo.ArgumentList.Add(argument);
            }

            // عدّاد جوبات الطابعة قبل ما نبعت. بنقارنه بعدين عشان نتأكد إن
            // الجوب وصل الطابور فعلًا — كود الخروج لوحده مش إثبات.
            long? jobsBefore = TryReadJobCounter(job.PrinterName);

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                // مفيش بروسيس اشتغل أصلًا → مستحيل يكون ورق اتحرك
                return PrintOutcome.NotSent(
                    $"[فشل] مقدرناش نشغّل SumatraPDF للطباعة على '{job.PrinterName}'.");
            }

            // المهلة بتكبر مع حجم الشغل. مهلة ثابتة كانت بتقتل الجوبات الكبيرة
            // في نص الطباعة وتطلّع ورق ناقص من غير ما حد ياخد باله.
            var spoolTimeout = SpoolTimeoutPolicy.For(job.PageCount, job.Copies);

            PrintOutcome? stalled = await WaitForSpoolAsync(
                process, job.PrinterName, spoolTimeout, cancellationToken);

            if (stalled is not null)
            {
                return stalled;
            }

            if (process.ExitCode != 0)
            {
                // Sumatra بترجّع كود غير صفر لما ترفض الجوب **قبل** ما
                // تسلّمه للسبولر (اسم طابعة غلط، طابعة مابتقبلش طباعة
                // صامتة). يعني مفيش ورق اتحرك → آمن ننقلها.
                return PrintOutcome.NotSent(
                    $"[فشل] '{job.PrinterName}' — SumatraPDF رجّع كود {process.ExitCode}. " +
                    "غالبًا اسم الطابعة غلط أو الطابعة مش متاحة.");
            }

            // "اتسلّمت لطابور" مش "اتطبعت" — وده مقصود.
            //
            // SumatraPDF بيسلّم الجوب للسبولر وبيخرج — هو مش شايف الورق وهو
            // بيطلع. فأصدق حاجة نقدر نقولها إن الجوب وصل الطابور؛ بعد كده
            // الطابعة ممكن تزنق ورق أو يخلص الحبر ومفيش حاجة تقولنا.
            //
            // اتأكد بالتجربة إن Sumatra الحقيقي **بيرجّع كود غير صفر** لما يفشل
            // (طابعة مابتقبلش الطباعة الصامتة رجّعت 4)، فالكود مؤشر فشل مأمون —
            // بس نجاحه مش دليل إن ورق طلع.
            string message = $"[نجاح] اتسلّمت {job.Copies} نسخة لطابور '{job.PrinterName}' " +
                             $"بمقاس {job.PaperSize}{(job.Grayscale ? " (أبيض وأسود)" : "")}{(job.Duplex ? " (وجهين)" : "")}.";

            return PrintOutcome.Delivered(
                message + await SpoolerNoteAsync(job.PrinterName, jobsBefore, cancellationToken));
        }
        catch (Exception ex)
        {
            // الاستثناءات هنا بتيجي من تشغيل البروسيس أو قراءة الملف —
            // كلها قبل ما أي حاجة توصل الطابعة
            return PrintOutcome.NotSent(
                $"[فشل] لم تتم الطباعة إلى '{job.PrinterName}'. السبب: {ex.Message}");
        }
    }

    /// <summary>
    /// بيستنى بروسيس الطباعة يخلص. بيرجّع null لو خلص، أو النتيجة لو لأ.
    ///
    /// **أهم قاعدة هنا: مانلغيش جوب بسبب حاجة اليد البشرية بتحلها.**
    ///
    /// في المطبعة الورق بيخلص كل شوية، وده سلوك طبيعي مش عطل — بتحط ورق
    /// والجوب بيكمّل من مكانه. لو المهلة قتلت البروسيس في اللحظة دي، نص
    /// الملزمة بيطلع والباقي بيضيع، والمستخدم مايعرفش غير لما يعدّ الورق.
    ///
    /// فلما المهلة تعدّي، بنسأل الطابعة الأول: إيه اللي واقف؟ لو ورق أو
    /// حبر أو الطابعة موقوفة أو لسه بتطبع — بنمدّد ونكتب السبب في اللوج.
    /// مابنلغيش غير لما مالاقيش أي سبب معروف.
    ///
    /// ملاحظة عملية: في الوضع الافتراضي لويندوز (spool ثم اطبع) البروسيس
    /// بيخلّص تسليمه للسبولر بسرعة وبيخرج، فالورق لما يخلص بعد كده مالوش
    /// أي علاقة بينا أصلًا. الكود ده بيحمي الحالة التانية — لما الطابعة
    /// متظبّطة "اطبع مباشرة من غير spool"، وساعتها البروسيس بيفضل واقف
    /// طول ما الطابعة واقفة.
    /// </summary>
    private static async Task<PrintOutcome?> WaitForSpoolAsync(
        Process process,
        string printerName,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + SpoolTimeoutPolicy.Maximum;
        var lastReason = StallReason.Unknown;

        while (true)
        {
            using var slice = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            slice.CancelAfter(budget);

            try
            {
                await process.WaitForExitAsync(slice.Token);
                return null;
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    TryKill(process);
                    return PrintOutcome.Cancelled($"[إلغاء] اتلغت الطباعة على '{printerName}'.");
                }
            }

            var reason = DiagnosePrinter(printerName);

            // مافيش سبب معروف، أو السقف الأقصى عدّى → دلوقتي بس بنلغي
            if (!PrinterStall.ShouldKeepWaiting(reason) || DateTime.UtcNow >= deadline)
            {
                TryKill(process);

                string why = PrinterStall.ShouldKeepWaiting(reason)
                    ? $"فضلت واقفة ({PrinterStall.Describe(reason)}) لحد ما عدّى السقف الأقصى"
                    : "ومفيش سبب واضح من الطابعة";

                // ⚠ التصنيف ده أهم حاجة هنا: البروسيس **كان شغّال**
                // ووصل للطابعة، وإحنا اللي قتلناه. ممكن يكون طلع ورق
                // وممكن لأ — مش عارفين. Abandoned معناها للموزّع:
                // ماتعيدش الشغل ده لوحدك، قول للبني آدم.
                return PrintOutcome.Abandoned(
                    $"[فشل] '{printerName}' مردّتش خلال {budget.TotalMinutes:0} دقيقة {why}. " +
                    "اتلغى الأمر، ويحتمل يكون طلع ورق ناقص — راجع الطابعة.");
            }

            if (reason != lastReason)
            {
                lastReason = reason;
                System.Diagnostics.Debug.WriteLine(
                    $"'{printerName}': {PrinterStall.Describe(reason)} — مستنيين، الجوب مش هيتلغي.");
            }
        }
    }

    /// <summary>
    /// بيقرا حالة الطابعة من WMI ويحوّلها لسبب مفهوم.
    /// عمره ما يرمي — لو مقدرناش نقرا، بنرجّع "مجهول" وده بيخلّي القرار
    /// زي ما كان قبل الميزة دي بالظبط.
    /// </summary>
    private static StallReason DiagnosePrinter(string printerName)
    {
        try
        {
            string escaped = printerName.Replace("'", "''");

            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT PrinterStatus, DetectedErrorState, PrinterState, WorkOffline, JobCountSinceLastReset " +
                $"FROM Win32_Printer WHERE Name = '{escaped}'");

            foreach (var printer in searcher.Get())
            {
                using (printer)
                {
                    int? status = ReadInt(printer, "PrinterStatus");
                    int? error = ReadInt(printer, "DetectedErrorState");

                    // بت الإيقاف في PrinterState القديمة
                    int? state = ReadInt(printer, "PrinterState");
                    bool paused = state is not null && (state.Value & 0x00000001) != 0;

                    return PrinterStall.Diagnose(status, error, paused, jobsWaiting: HasJobs(printerName));
                }
            }
        }
        catch
        {
            // WMI مش متاحة أو الدرايفر مش بيدعم الحقول — مش مشكلة
        }

        return StallReason.Unknown;
    }

    private static int? ReadInt(System.Management.ManagementBaseObject source, string property)
    {
        try
        {
            object? value = source[property];
            return value is null ? null : Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static bool HasJobs(string printerName)
    {
        try
        {
            string escaped = printerName.Replace("'", "''").Replace("\\", "\\\\");

            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT Name FROM Win32_PrintJob WHERE Name LIKE '{escaped}%'");

            foreach (var job in searcher.Get())
            {
                job.Dispose();
                return true;
            }
        }
        catch
        {
            // مش مشكلة
        }

        return false;
    }

    /// <summary>كام ثانية ندّي للسبولر يوري الجوب قبل ما نستغرب.</summary>
    private static readonly TimeSpan SightingWindow = TimeSpan.FromSeconds(1.5);

    private static readonly TimeSpan SightingPoll = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// بيدوّر على أي أثر إن الجوب وصل السبولر فعلًا، وبيرجّع ملاحظة لو
    /// مالقاش. **مابيغيّرش نتيجة الطباعة** — النتيجة تفضل Delivered في
    /// الحالتين، والسطر ده كلام للبني آدم بس.
    ///
    /// ═══ ليه اتغيّرت في ١.٩.٨ ═══
    ///
    /// النسخة القديمة كانت بتقرا <c>JobCountSinceLastReset</c> مرة واحدة،
    /// على طول بعد ما بروسيس الطباعة يخرج. والمشكلة إن العدّاد ده بيزيد
    /// لما السبولر **يخلّص** الجوب، مش لما يستلمه — وSumatra بتخرج بمجرد
    /// ما تسلّم. يعني في اللحظة اللي كنا بنقرا فيها، الجوب لسه في الطابور
    /// والعدّاد لسه مكانه.
    ///
    /// النتيجة: التحذير كان بيطلع على **كل** طباعة ناجحة. معمل الاختبار
    /// قفشه على السطور الناجحة كلها.
    ///
    /// وتحذير بيطلع دايمًا أسوأ من تحذير مش موجود: بيتحوّل لضوضاء،
    /// المستخدم بيبطّل يقراه، وساعتها التحذير الحقيقي بيعدّي وسط الزحمة.
    ///
    /// دلوقتي بنسأل السؤال الصح: **الجوب بان في الطابور؟** الطابور بيتسجّل
    /// ساعة الاستلام مش ساعة الخلاص. وبنقبل العدّاد كمان كدليل، عشان
    /// الجوب الصغير ممكن يخلص ويختفي قبل ما نبص. أول دليل من الاتنين
    /// بيسكّت الملاحظة على طول — فالحالة الطبيعية مابتأخّرش حاجة.
    /// </summary>
    private static async Task<string> SpoolerNoteAsync(
        string printerName, long? before, CancellationToken cancellationToken)
    {
        // مقدرناش نقرا العدّاد قبل الإرسال = WMI مش شغّالة على الجهاز ده.
        // وساعتها الفحص التاني (الطابور) هيفشل هو كمان، وهنطلّع ملاحظة على
        // **كل** طباعة ناجحة — وده نفس العيب اللي بنصلّحه هنا بالظبط.
        //
        // «مقدرناش نشوف» مش زي «شفنا إن مفيش». في الحالة دي بنسكت.
        if (before is not long)
        {
            return "";
        }

        var deadline = DateTime.UtcNow + SightingWindow;

        while (true)
        {
            // شفناه في الطابور — وصل، خلاص
            if (HasJobs(printerName))
            {
                return "";
            }

            // أو خلص وعدّى على العدّاد وإحنا بنبص
            if (before is long previous
                && TryReadJobCounter(printerName) is long now
                && now > previous)
            {
                return "";
            }

            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            try
            {
                await Task.Delay(SightingPoll, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // اتلغى وإحنا بنتفرج — مش وقت ملاحظات
                return "";
            }
        }

        return " — ملاحظة: مشفناش الجوب في طابور الطابعة. لو الورق ماطلعش، راجع الطابعة.";
    }

    /// <summary>
    /// عدد الجوبات اللي عدّت على الطابعة من آخر تصفير. null = مقدرناش نقراه.
    /// عمره ما يرمي — الطباعة مش المفروض تقف عشان فحص فشل.
    /// </summary>
    private static long? TryReadJobCounter(string printerName)
    {
        try
        {
            string escaped = printerName.Replace("'", "''");

            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT JobCountSinceLastReset FROM Win32_Printer WHERE Name = '{escaped}'");

            foreach (var printer in searcher.Get())
            {
                using (printer)
                {
                    object? value = printer["JobCountSinceLastReset"];
                    return value is null ? null : Convert.ToInt64(value);
                }
            }
        }
        catch
        {
            // WMI مش متاحة أو الدرايفر مش بيدعم الخاصية — مش مشكلة
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // البروسيس خلص لوحده في نفس اللحظة — مش مشكلة
        }
    }
}
