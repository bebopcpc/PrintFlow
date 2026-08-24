namespace PrintFlow.Domain;

/// <summary>اتجاه الصفحة.</summary>
public enum PageOrientation
{
    Portrait,
    Landscape
}

/// <summary>اتجاه قلب الصفحة في الطباعة على الوجهين.</summary>
public enum DuplexFlip
{
    LongEdge,
    ShortEdge
}

/// <summary>اتجاه بدء البوكليت.</summary>
public enum BookletStart
{
    Right,
    Left
}

/// <summary>ترتيب الشرائح جوه الورقة.</summary>
public enum SlideOrder
{
    Horizontal,
    Vertical
}

/// <summary>الركن اللي البرنامج يبدأ منه توزيع الشرائح.</summary>
public enum SlideStart
{
    Right,
    Left
}

/// <summary>مستوى ضغط ملفات PDF.</summary>
public enum CompressionMode
{
    None,
    Normal,
    Advanced
}

/// <summary>مكان عنصر (رقم صفحة / نص مخصص) على الورقة.</summary>
public enum ContentPosition
{
    BottomLeft,
    BottomCenter,
    BottomRight,
    TopLeft,
    TopCenter,
    TopRight
}

/// <summary>ترتيب الملفات في قايمة المعالجة.</summary>
public enum FileSortOrder
{
    Default,
    ByName,
    ByPageCount,
    BySize,
    ByDate
}

/// <summary>طريقة حساب التكلفة/الكمية: بالصفحة ولا بالورقة الفعلية.</summary>
public enum CountingMethod
{
    ByPage,
    BySheet
}

/// <summary>مظهر البرنامج.</summary>
public enum AppTheme
{
    Light,
    Dark
}

/// <summary>لغة واجهة البرنامج.</summary>
public enum AppLanguage
{
    Arabic,
    English
}
