using PrintFlow.Domain;

namespace PrintFlow.Application;

/// <summary>
/// منطق بحت: يحدد أي برنتر مؤهلة للدخول في Job جديد.
/// القاعدة: لا يدخل offline أو error تلقائيًا - المستخدم لازم يشوف السبب لو حاول يحددها.
/// </summary>
public static class PrinterSelectionRules
{
    public static bool IsEligibleForJob(Printer printer)
    {
        return printer.Status != PrinterStatus.Offline
            && printer.Status != PrinterStatus.Error;
    }

    public static List<Printer> FilterEligible(IEnumerable<Printer> printers)
    {
        return printers.Where(IsEligibleForJob).ToList();
    }
}