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

    public async Task<string> PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Copies <= 0)
        {
            return $"[تخطي] '{job.PrinterName}' نصيبها صفر نسخة، مفيش حاجة اتبعتت.";
        }

        if (!File.Exists(SumatraPath))
        {
            return "[فشل] SumatraPDF.exe مش موجود في مجلد tools. تأكد إنك حطيته صح.";
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
            // اللي إحنا متأكدين منه إن SumatraPDF خرج بكود 0. ده **مش** إثبات إن
            // ورق طلع: مع -silent، Sumatra بيبلع رسالة الخطأ وبيرجّع 0 برضه لو
            // الطابعة مش موصولة. اتجرب عمليًا: تشغيلتين، كود 0، والطابور فاضي.
            //
            // فالرسالة بتقول اللي حصل بالظبط. اللي بيمنع الحالة دي فعلًا هو
            // فلترة الطابعات غير المؤهلة قبل ما نوصل هنا أصلًا.
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
