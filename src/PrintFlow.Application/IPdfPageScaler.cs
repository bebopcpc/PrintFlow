using PrintFlow.Domain;

namespace PrintFlow.Application;

public interface IPdfPageScaler
{
    /// <summary>
    /// بيصغّر أو يكبّر محتوى كل صفحة حوالين مركزها، ومقاس الورقة مابيتغيّرش.
    /// عدد الصفحات في النتيجة زي ما هو.
    /// </summary>
    MergeResult Scale(ScaleRequest request);
}
