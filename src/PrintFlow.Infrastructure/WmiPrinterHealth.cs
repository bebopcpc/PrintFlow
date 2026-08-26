using System.Management;
using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيقرا حالة الطابعة وطابورها من WMI، والقرار نفسه في
/// <see cref="PrinterReady"/> — دالة خالصة متختبرة بأرقام.
///
/// الملف ده **قراءة بس**. أي منطق يتحط هنا يبقى منطق مالوش تست، لأن
/// WMI مش موجودة في بيئة التستات أصلًا.
///
/// ═══ مبدأ ثابت: الفحص عمره ما يوقف الطباعة ═══
///
/// أي فشل في القراءة (WMI مقفولة، درايفر مش بيدعم الحقول، صلاحيات)
/// بيرجّع "تمام". لأن منع الطباعة بسبب **فحص** فشل أوحش بكتير من العطل
/// اللي الفحص أصلًا موجود عشانه.
/// </summary>
public sealed class WmiPrinterHealth : IPrinterHealth
{
    private readonly int _queueRoom;

    public WmiPrinterHealth(int queueRoom = PrinterReady.QueueRoom)
    {
        _queueRoom = queueRoom;
    }

    public Task<PrinterHealth> CheckAsync(
        string printerName,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Check(printerName), cancellationToken);

    private PrinterHealth Check(string printerName)
    {
        try
        {
            string escaped = printerName.Replace("'", "''");

            using var searcher = new ManagementObjectSearcher(
                "SELECT PrinterStatus, DetectedErrorState, PrinterState, WorkOffline " +
                $"FROM Win32_Printer WHERE Name = '{escaped}'");

            foreach (var printer in searcher.Get())
            {
                using (printer)
                {
                    int? status = ReadInt(printer, "PrinterStatus");
                    int? error = ReadInt(printer, "DetectedErrorState");
                    bool offline = ReadBool(printer, "WorkOffline");

                    // بت الإيقاف في PrinterState القديمة
                    int? state = ReadInt(printer, "PrinterState");
                    bool paused = state is not null && (state.Value & 0x00000001) != 0;

                    var verdict = PrinterReady.Decide(
                        offline, status, error, paused, CountQueued(printerName), _queueRoom);

                    return Translate(verdict);
                }
            }
        }
        catch
        {
            // مقدرناش نقرا — بنكمّل عادي
        }

        return PrinterHealth.Fine;
    }

    private static PrinterHealth Translate(PrinterVerdict verdict) => verdict.State switch
    {
        PrinterReadiness.Faulted => PrinterHealth.Stopped(verdict.Reason ?? "فيها عطل"),
        PrinterReadiness.Busy => PrinterHealth.Busy(verdict.Reason ?? "مشغولة"),
        _ => PrinterHealth.Fine
    };

    /// <summary>
    /// كام جوب مستني في طابور الطابعة دي. null = مقدرناش نعد.
    ///
    /// <c>Win32_PrintJob.Name</c> شكله "اسم الطابعة, رقم الجوب"، عشان كده
    /// بنستخدم LIKE مش المساواة.
    /// </summary>
    private static int? CountQueued(string printerName)
    {
        try
        {
            string escaped = printerName
                .Replace("\\", "\\\\")
                .Replace("'", "''");

            using var searcher = new ManagementObjectSearcher(
                $"SELECT JobId FROM Win32_PrintJob WHERE Name LIKE '{escaped},%'");

            int count = 0;

            foreach (var job in searcher.Get())
            {
                job.Dispose();
                count++;
            }

            return count;
        }
        catch
        {
            // مافيش عد؟ يبقى مافيش كابح. أهون من إننا نوقف الشغل.
            return null;
        }
    }

    private static int? ReadInt(ManagementBaseObject source, string property)
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

    private static bool ReadBool(ManagementBaseObject source, string property)
    {
        try
        {
            object? value = source[property];
            return value is not null && Convert.ToBoolean(value);
        }
        catch
        {
            return false;
        }
    }
}
