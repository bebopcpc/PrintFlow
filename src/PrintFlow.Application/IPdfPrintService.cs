using PrintFlow.Domain;

namespace PrintFlow.Application;

public interface IPdfPrintService
{
    /// <summary>
    /// بيبعت أمر طباعة واحد ويرجّع النتيجة: السطر العربي اللي بيتعرض في
    /// اللوج + تصنيف الموزّع بيبني عليه قراره.
    ///
    /// كان بيرجّع نص بس. النص لوحده مكانش بيكفي لما التوزيع بقى حي:
    /// الموزّع محتاج يعرف الفرق بين "فشلت قبل ما ورق يتحرك" (آمن ننقلها
    /// لمكنة تانية) و"اتبعتت وبعدين وقفت" (ممنوع نكررها عشان ماتطلعش
    /// مرتين). شوف <see cref="PrintOutcome"/>.
    ///
    /// غير متزامن بجد — مش Task.Run ملفوفة حوالين انتظار متزامن.
    /// </summary>
    Task<PrintOutcome> PrintAsync(PrintJob job, CancellationToken cancellationToken = default);
}
