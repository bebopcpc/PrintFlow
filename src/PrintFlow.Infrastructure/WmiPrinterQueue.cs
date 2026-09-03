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

        try
        {
            // نفس التهريب المستخدم في باقي استعلامات WMI في المشروع.
            string escaped = printerName.Replace("'", "''").Replace("\\", "\\\\");

            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalPages, PagesPrinted FROM Win32_PrintJob " +
                $"WHERE Name LIKE '{escaped},%'");

            int jobs = 0;
            int printed = 0;
            int total = 0;

            foreach (var job in searcher.Get())
            {
                using (job)
                {
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

        int removed = 0;

        try
        {
            string escaped = printerName.Replace("'", "''").Replace("\\", "\\\\");

            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_PrintJob WHERE Name LIKE '{escaped},%'");

            foreach (ManagementObject job in searcher.Get().Cast<ManagementObject>())
            {
                using (job)
                {
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