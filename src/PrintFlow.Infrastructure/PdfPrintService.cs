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

    public async Task<string> PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Copies <= 0)
        {
            return $"[تخطي] '{job.PrinterName}' نصيبها صفر نسخة، مفيش حاجة اتبعتت.";
        }

        if (SumatraProblem.Value is { } problem)
        {
            return $"[فشل] {problem}";
        }

        if (!File.Exists(job.FilePath))
        {
            return $"[فشل] الملف مش موجود: {job.FilePath}";
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

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return $"[فشل] مقدرناش نشغّل SumatraPDF للطباعة على '{job.PrinterName}'.";
            }

            // المهلة بتكبر مع حجم الشغل. مهلة ثابتة كانت بتقتل الجوبات الكبيرة
            // في نص الطباعة وتطلّع ورق ناقص من غير ما حد ياخد باله.
            var spoolTimeout = SpoolTimeoutPolicy.For(job.PageCount, job.Copies);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(spoolTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);

                return cancellationToken.IsCancellationRequested
                    ? $"[إلغاء] اتلغت الطباعة على '{job.PrinterName}'."
                    : $"[فشل] '{job.PrinterName}' مردّتش خلال {spoolTimeout.TotalMinutes:0} دقيقة. " +
                      "اتلغى الأمر، ويحتمل يكون طلع ورق ناقص — راجع الطابعة.";
            }

            if (process.ExitCode != 0)
            {
                return $"[فشل] '{job.PrinterName}' — SumatraPDF رجّع كود {process.ExitCode}. " +
                       "غالبًا اسم الطابعة غلط أو الطابعة مش متاحة.";
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
            return $"[نجاح] اتسلّمت {job.Copies} نسخة لطابور '{job.PrinterName}' " +
                   $"بمقاس {job.PaperSize}{(job.Grayscale ? " (أبيض وأسود)" : "")}{(job.Duplex ? " (وجهين)" : "")}.";
        }
        catch (Exception ex)
        {
            return $"[فشل] لم تتم الطباعة إلى '{job.PrinterName}'. السبب: {ex.Message}";
        }
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
