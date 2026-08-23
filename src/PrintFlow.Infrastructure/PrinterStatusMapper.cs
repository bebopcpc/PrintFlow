using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

public static class PrinterStatusMapper
{
    public static PrinterStatus Map(bool isOffline, int? printerStatusCode)
    {
        if (isOffline)
        {
            return PrinterStatus.Offline;
        }

        return printerStatusCode switch
        {
            3 => PrinterStatus.Ready,      // Idle
            4 => PrinterStatus.Ready,      // Printing
            5 => PrinterStatus.Ready,      // Warmup
            7 => PrinterStatus.Offline,
            2 => PrinterStatus.Error,      // Error
            null => PrinterStatus.Unknown,
            _ => PrinterStatus.Unknown
        };
    }
}