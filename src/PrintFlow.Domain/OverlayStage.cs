namespace PrintFlow.Domain;

/// <summary>أي إضافات بتترسم في مرحلة معيّنة.</summary>
public readonly record struct OverlayStage(bool PageNumbers, bool Watermark, bool CustomText)
{
    public bool Nothing => !PageNumbers && !Watermark && !CustomText;
}

/// <summary>
/// بيقرر كل إضافة تترسم قبل تجميع الشرائح ولا بعده.
///
/// دي فكرة الميزة كلها في سطرين: الإعدادين "ترقيم الورقة كاملة بدل الشريحة"
/// و"العلامة على الورقة كاملة بدل الشريحة" **مش محتاجين كود رسم جديد** —
/// هما بس بيحددوا **مكان** الإضافة في السلسلة.
///
///   قبل التجميع → الرقم جزء من الصفحة الأصلية، بيصغّر معاها لما تبقى شريحة
///   بعد التجميع → الرقم على الورقة نفسها بحجمه الطبيعي، ورقم واحد للورقة
///
/// النص المخصص بيروح على الورقة دايمًا: هو حاجة زي اسم المطبعة أو
/// "نسخة للمراجعة" — بتتكتب مرة على الورقة، مش ٦ مرات على كل شريحة.
/// </summary>
public static class SlidePipeline
{
    /// <summary>الإضافات اللي بتتحط على الصفحات الأصلية قبل ما تتجمّع.</summary>
    public static OverlayStage BeforeSlides(AppSettings app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return new OverlayStage(
            PageNumbers: !app.NumberWholeSheetInsteadOfSlide,
            Watermark: !app.WatermarkOnWholeSheet,
            CustomText: false);
    }

    /// <summary>الإضافات اللي بتتحط على الورقة بعد ما الشرائح تتجمّع عليها.</summary>
    public static OverlayStage AfterSlides(AppSettings app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return new OverlayStage(
            PageNumbers: app.NumberWholeSheetInsteadOfSlide,
            Watermark: app.WatermarkOnWholeSheet,
            CustomText: true);
    }

    /// <summary>
    /// مفيش تجميع شرائح؟ يبقى مفيش "قبل وبعد" أصلًا — كل حاجة بتتحط مرة واحدة.
    /// </summary>
    public static OverlayStage Everything() => new(true, true, true);
}
