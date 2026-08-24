using PrintFlow.Domain;

namespace PrintFlow.Application;

public interface IPdfSlideComposer
{
    /// <summary>
    /// بيجمّع صفحات المستند على ورق أقل — أكتر من صفحة على كل ورقة.
    /// عدد الصفحات في النتيجة هو عدد **الورق** الطالع مش الشرائح.
    /// </summary>
    MergeResult Compose(SlideRequest request);
}
