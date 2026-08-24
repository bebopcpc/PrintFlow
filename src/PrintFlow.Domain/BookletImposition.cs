namespace PrintFlow.Domain;

/// <summary>
/// ترتيب صفحات الكتيّب (البوكليت).
///
/// الفكرة: الورقة بتتطبع على الوش والضهر وبتتطوى من النص، فكل ورقة بتاخد
/// ٤ صفحات. عشان الكتيّب يطلع مظبوط بعد الطي، الصفحات لازم تترتب ترتيب
/// غريب على الورق — مش ١، ٢، ٣، ٤.
///
/// مثال على ٨ صفحات (ورقتين):
///
///   الورقة الأولى، الوش:   ٨ | ١        الضهر:   ٢ | ٧
///   الورقة التانية، الوش:  ٦ | ٣        الضهر:   ٤ | ٥
///
/// لما تطوي الاتنين جوه بعض وتدبّس من النص، بتقراهم ١، ٢، ٣... بالترتيب.
///
/// الحساب ده **أرقام خالصة** — مفيش PDF ولا رسم. أصعب حتة في البوكليت هي
/// الترتيب ده، ولو كان متشابك مع كود الرسم مكانش ينفع نتأكد منه غير بإننا
/// نطبع ورق ونطوّيه بإيدينا.
/// </summary>
public static class BookletImposition
{
    /// <summary>الخانة دي فاضية — بتحصل لما عدد الصفحات مش من مضاعفات ٤.</summary>
    public const int Blank = 0;

    /// <summary>
    /// الكتيّب لازم يبقى من مضاعفات ٤ (كل ورقة = ٤ صفحات).
    /// الناقص بيتكمّل صفحات فاضية في الآخر.
    /// </summary>
    public static int PaddedPageCount(int pageCount)
    {
        if (pageCount <= 0)
        {
            return 0;
        }

        int remainder = pageCount % 4;

        return remainder == 0 ? pageCount : pageCount + (4 - remainder);
    }

    /// <summary>عدد الورق المطلوب. كل ورقة بتشيل ٤ صفحات (وش وضهر).</summary>
    public static int SheetCount(int pageCount) => PaddedPageCount(pageCount) / 4;

    /// <summary>
    /// أرقام الصفحات بترتيب الطباعة الفعلي.
    ///
    /// كل عنصرين ورا بعض = وجه واحد من ورقة: الأول في الخانة الأولى والتاني
    /// في التانية. والأوجه بتتبع بعضها: وش الورقة الأولى، ضهرها، وش التانية…
    /// فالطباعة على الوجهين بتطلع مظبوطة على طول.
    ///
    /// <see cref="Blank"/> معناها الخانة دي تفضل فاضية.
    /// </summary>
    public static IReadOnlyList<int> Order(int pageCount, BookletStart start)
    {
        int total = PaddedPageCount(pageCount);

        if (total == 0)
        {
            return [];
        }

        var order = new List<int>(total);

        for (int sheet = 1; sheet <= total / 4; sheet++)
        {
            // الوش: آخر صفحة في الترتيب مع أول صفحة فيه
            int frontOuter = total - (2 * sheet) + 2;
            int frontInner = (2 * sheet) - 1;

            // الضهر: الصفحتين اللي بينهم
            int backInner = 2 * sheet;
            int backOuter = total - (2 * sheet) + 1;

            AddSide(order, frontOuter, frontInner, start, pageCount);
            AddSide(order, backInner, backOuter, start, pageCount);
        }

        return order;
    }

    /// <summary>
    /// بتحط وجه واحد. مع البدء من اليمين (العربي) الصفحة الخارجية بتيجي
    /// في الخانة الأولى — واللي بتروح لليمين في المُجمِّع.
    /// مع البدء من الشمال بيتقلبوا.
    /// </summary>
    private static void AddSide(List<int> order, int first, int second, BookletStart start, int realPageCount)
    {
        int a = first > realPageCount ? Blank : first;
        int b = second > realPageCount ? Blank : second;

        if (start == BookletStart.Right)
        {
            order.Add(a);
            order.Add(b);
        }
        else
        {
            order.Add(b);
            order.Add(a);
        }
    }
}
