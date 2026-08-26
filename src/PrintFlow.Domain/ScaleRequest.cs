namespace PrintFlow.Domain;

/// <summary>
/// طلب تغيير مقياس المحتوى على الورق.
///
/// آخر مرحلة في السلسلة عن قصد: اللي بيتصغّر هو **الورقة اللي هتروح للطابعة**
/// بكل حاجة عليها — الترقيم والعلامة المائية كمان. ولو الترقيم على حرف الورقة
/// والطابعة بتقص من الحواف، فهو أول حاجة بتتقص؛ فتصغيره معاه هو الصح.
/// </summary>
public sealed record ScaleRequest
{
    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    public int Percent { get; init; } = 100;

    /// <summary>مافيش شغل — الملف بيعدّي زي ما هو.</summary>
    public bool IsPassThrough => PageScaling.IsIdentity(Percent);
}
