namespace PrintFlow.Domain;

/// <summary>
/// أسامي الملفات الناتجة من وضع "من غير دمج".
///
/// الشكل: <c>01_اسم الملف الأصلي.pdf</c>
///
/// ليه في رقم في الأول: المطبعة بتحمّل ٢٠ ملف من مجلدات مختلفة، وممكن
/// يبقى فيهم اتنين بنفس الاسم (فاتورة.pdf من مجلدين). من غير الرقم
/// التاني هيدهس الأول ويضيع من غير ما حد ياخد باله. والرقم كمان بيخلي
/// ترتيب الطباعة واضح في المجلد.
///
/// كل الدوال هنا **حسابات على نصوص** — مفيش قراية ولا كتابة على القرص،
/// فتتختبر لوحدها.
/// </summary>
public static class ProcessedFileNaming
{
    /// <summary>الحروف اللي ويندوز مابيقبلهاش في اسم ملف.</summary>
    private static readonly char[] Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>
    /// اسم الملف الناتج من ملف مصدر واحد.
    /// </summary>
    /// <param name="oneBasedIndex">ترتيب الملف في القايمة، بيبدأ من ١.</param>
    /// <param name="sourcePath">مسار الملف الأصلي.</param>
    public static string NameFor(int oneBasedIndex, string sourcePath)
    {
        string stem = StemOf(sourcePath ?? string.Empty);

        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "ملف";
        }

        stem = Sanitize(stem);

        // اسم طويل جدًا + مسار مجلد طويل = تخطّي حد ويندوز (٢٦٠ حرف) وفشل الحفظ
        if (stem.Length > 80)
        {
            stem = stem[..80].TrimEnd();
        }

        return $"{oneBasedIndex:00}_{stem}.pdf";
    }

    /// <summary>
    /// بيرجّع اسم مش موجود في المجلد. لو الاسم متاخد، بيضيف (2) و(3) وهكذا.
    ///
    /// <paramref name="exists"/> بتتحقن من بره عشان الدالة تفضل قابلة للاختبار
    /// من غير ملفات حقيقية على القرص.
    /// </summary>
    public static string MakeUnique(string desiredName, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);

        if (!exists(desiredName))
        {
            return desiredName;
        }

        string stem = Path.GetFileNameWithoutExtension(desiredName);
        string extension = Path.GetExtension(desiredName);

        // ٩٩٩ محاولة كفاية جدًا؛ الحد موجود عشان مانعلّقش لو exists بترجّع true دايمًا
        for (int attempt = 2; attempt < 1000; attempt++)
        {
            string candidate = $"{stem} ({attempt}){extension}";

            if (!exists(candidate))
            {
                return candidate;
            }
        }

        return $"{stem} ({Guid.NewGuid():N}){extension}";
    }

    /// <summary>
    /// اسم الملف من غير المجلد ومن غير الامتداد.
    ///
    /// مش بنستخدم Path.GetFileNameWithoutExtension عن قصد: هي بتعتمد على
    /// فواصل المسار بتاعة نظام التشغيل الشغال، فمسار ويندوز زي
    /// <c>C:\a\فاتورة.pdf</c> بيرجع كله كاسم ملف لو الكود اتنفذ على لينكس.
    /// التستات بتجري على الاتنين، والنتيجة لازم تبقى واحدة.
    /// </summary>
    private static string StemOf(string path)
    {
        int lastSeparator = path.LastIndexOfAny(['\\', '/']);
        string fileName = lastSeparator >= 0 ? path[(lastSeparator + 1)..] : path;

        int lastDot = fileName.LastIndexOf('.');
        return lastDot > 0 ? fileName[..lastDot] : fileName;
    }

    private static string Sanitize(string name)
    {
        var clean = new System.Text.StringBuilder(name.Length);

        foreach (char c in name)
        {
            clean.Append(Array.IndexOf(Forbidden, c) >= 0 || char.IsControl(c) ? '_' : c);
        }

        // ويندوز بيرفض الأسامي اللي بتنتهي بنقطة أو مسافة
        return clean.ToString().TrimEnd('.', ' ');
    }
}
