using PrintFlow.Domain;

namespace PrintFlow.Application;

public interface IPdfPrintService
{
    /// <summary>
    /// بيبعت أمر طباعة واحد ويرجّع سطر نتيجة يتعرض في اللوج.
    /// غير متزامن بجد — مش Task.Run ملفوفة حوالين انتظار متزامن.
    /// </summary>
    Task<string> PrintAsync(PrintJob job, CancellationToken cancellationToken = default);
}
