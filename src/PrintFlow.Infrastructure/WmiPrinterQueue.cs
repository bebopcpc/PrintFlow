using System.Management;
using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيقرا طابور الطابعة من WMI (<c>Win32_PrintJob</c>).
///
/// ═══ اسم الجوب وليه بنقفله بفاصلة ═══
///
/// ويندوز بيسمّي الجوب <c>"اسم الطابعة, رقم الجوب"</c>. الكود القديم في
/// <c>PdfPrintService.HasJobs</c> بيدوّر بـ <c>LIKE 'الاسم%'</c> — وده
/// بيصطاد طابعات تانية اسمها بيبدأ بنفس الحروف: السؤال عن «Canon 1»
/// بيرجّع شغل «Canon 10» كمان.
///
/// هنا بنقفله: <c>LIKE 'الاسم,%'</c>. الفاصلة موجودة دايمًا بعد اسم
/// الطابعة، فمفيش طابعة تانية ممكن تعدّي.
///
/// ═══ ليه بنجمع كل الجوبات مش الجوب الحالي بس ═══
///
/// المكنة بياخدها أكتر من قطعة في الأوردر الواحد، فبيبقى في طابورها كذا
/// جوب مع بعض. اللي واقف قدامها عايز يعرف «فاضل عليها كام» مش «فاضل في
/// الورقة اللي تحت إيدها كام».
/// </summary>
public sealed class WmiPrinterQueue : IPrinterQueue
{
        public PrinterQueueState Read(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return PrinterQueueState.Idle;
        }

        // ⚠ من غير WHERE ... LIKE عن قصد — شوف تعليق الكلاس فوق.
        string prefix = printerName + ",";

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, TotalPages, PagesPrinted FROM Win32_PrintJob");

            // ⚠ Get() بترجّع مجموعة لازم تتقفل — من غير كده بنسرّب
            // مقبض COM كل نداء، والنداء ده بيحصل كل ثانية وإحنا بنطبع.
            using var results = searcher.Get();

            int jobs = 0;
            int printed = 0;
            int total = 0;

            foreach (var job in results)
            {
                using (job)
                {
                    string name = job["Name"] as string ?? string.Empty;

                    // نفس المقارنة اللي LIKE كانت بتعملها (مش حساسة لحالة
                    // الحروف) — بس من غير أي حرف بدل.
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    jobs++;
                    total += ReadInt(job, "TotalPages");
                    printed += ReadInt(job, "PagesPrinted");
                }
            }

            return jobs == 0 ? PrinterQueueState.Idle : new PrinterQueueState(jobs, printed, total);
        }
        catch
        {
            // WMI مقفولة، أو الدرايفر مابيدعمش الحقول، أو الجوب خلص وإحنا
            // بنقرا. كله مايستاهلش استثناء — دي معلومة للعرض بس.
            return PrinterQueueState.Idle;
        }
    }
         public int CancelAll(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return 0;
        }

        string prefix = printerName + ",";
        int removed = 0;

        try
        {
            // ⚠ SELECT * مقصودة هنا — Delete() محتاجة مسار الكائن كامل.
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PrintJob");
            using var results = searcher.Get();

            foreach (ManagementObject job in results.Cast<ManagementObject>())
            {
                using (job)
                {
                    string name = job["Name"] as string ?? string.Empty;

                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        job.Delete();
                        removed++;
                    }
                    catch
                    {
                        // الجوب خلص لوحده وإحنا بنشيله، أو مش بتاعنا.
                        // بنكمّل على الباقي — واحد فشل مش سبب نسيب التسعة.
                    }
                }
            }
        }
        catch
        {
            // WMI مش متاحة. اللي اتشال لحد دلوقتي بيترجّع زي ما هو.
        }

        return removed;
    }
    private static int ReadInt(ManagementBaseObject source, string property)
    {
        try
        {
            object? value = source[property];
            return value is null ? 0 : Convert.ToInt32(value);
        }
        catch
        {
            return 0;
        }
    }
}