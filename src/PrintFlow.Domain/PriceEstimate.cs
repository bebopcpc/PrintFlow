using System.Globalization;

namespace PrintFlow.Domain;

/// <summary>
/// تكلفة الأوردر من عدّ الورق (<see cref="PaperCount"/>) وسعر الوحدة.
///
/// ═══ الوحدة إيه؟ المستخدم هو اللي بيقرر ═══
///
/// في المطابع طريقتين تسعير شائعتين، والاتنين صح:
///
///   • <c>ByPage</c>  — بالوجه. كل وش مطبوع بسعر، والوجهين بيكلّف الضِعف.
///     دي طريقة المصوّراتي: التونر والشغل هما التكلفة، والورقة رخيصة.
///
///   • <c>BySheet</c> — بالورقة. الورقة بسعر مهما اتطبع عليها وش ولا وشين.
///     دي طريقة اللي بيبيع ملازم وكتيّبات: الورق هو التكلفة الكبيرة.
///
/// الفرق بينهم مش تفصيلة: أوردر ١٢٠ وجه على ٦٠ ورقة بيطلع بسعرين
/// مختلفين تمامًا. عشان كده الاختيار موجود بدل ما نفرض واحدة.
///
/// ═══ صفر معناه "مفيش تسعير" مش "ببلاش" ═══
///
/// لحد ما المستخدم يكتب سعر، السطر بيختفي خالص. رقم تكلفة صفر جنب
/// أوردر حقيقي بيبان زي عطل، والمستخدم بيقعد يدوّر على السبب.
///
/// ═══ ليه الأرقام بتتكتب بثقافة ثابتة ═══
///
/// الفاصلة العشرية بتختلف بين ويندوز عربي وإنجليزي، والتستات كانت
/// هتعدّي عندي وتقع عنده (أو العكس). الرقم هنا شكله واحد في كل مكان.
///
/// حساب خالص — متختبر من غير واجهة ولا طابعة.
/// </summary>
public static class PriceEstimate
{
    /// <summary>عدد الوحدات اللي هيتحسب عليها السعر، حسب الطريقة.</summary>
    public static int UnitsIn(PaperTally tally, CountingMethod method)
        => method == CountingMethod.BySheet ? tally.Sheets : tally.Sides;

    /// <summary>التكلفة. بترجّع صفر لو مفيش سعر أو مفيش ورق.</summary>
    public static decimal Of(PaperTally tally, decimal unitPrice, CountingMethod method)
    {
        if (unitPrice <= 0)
        {
            return 0m;
        }

        int units = UnitsIn(tally, method);

        return units <= 0 ? 0m : units * unitPrice;
    }

    /// <summary>
    /// سطر عربي للواجهة. بيرجّع "" لما مفيش سعر متكتوب أو مفيش ورق.
    /// </summary>
    public static string Describe(PaperTally tally, decimal unitPrice, CountingMethod method)
    {
        decimal total = Of(tally, unitPrice, method);

        if (total <= 0)
        {
            return "";
        }

        int units = UnitsIn(tally, method);
        string unitName = method == CountingMethod.BySheet ? "ورقة" : "وجه";

        // ثقافة ثابتة: الفاصلة العشرية مالهاش دعوة بلغة الويندوز.
        string money = total.ToString("0.00", CultureInfo.InvariantCulture);
        string each = unitPrice.ToString("0.###", CultureInfo.InvariantCulture);

        // من غير نجوم ماركداون — السطر ده بيتعرض نص خام في الواجهة.
        return $"التكلفة المتوقعة: {money} جنيه ({units} {unitName} × {each}).";
    }
}
