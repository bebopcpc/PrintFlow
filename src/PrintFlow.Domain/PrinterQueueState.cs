namespace PrintFlow.Domain;

/// <summary>
/// اللي في طابور الطابعة **دلوقتي**، زي ما ويندوز شايفه.
///
/// ═══ ليه ده موجود جنب PrinterProgress اللي أصلًا بيعدّ ═══
///
/// <c>PrinterProgress</c> بيعدّ اللي **إحنا بعتناه**: أول ما القطعة تتسلّم
/// للسبولر، بنزوّد ١٨٠ صفحة مرة واحدة. وده صادق — بس مش كفاية لواحد واقف
/// قدام المكنة: القطعة بتتحسب في ثانية، وبعدها البار بيقف عشر دقايق
/// مايتحركش وهو بيتفرّج على الورق بيطلع. فبيفتكر البرنامج علّق.
///
/// الحالة دي بتيجي من مصدر تاني خالص — **الطابعة نفسها** — وبتقول كام
/// ورقة طلعت فعلًا من اللي اتبعت. نفس مبدأ الشاهد المستقل اللي شغالين
/// بيه: البرنامج مايكونش شاهد على نفسه.
///
/// ⚠ **مش كل درايفر بيقول الأرقام دي.** كتير بيسيبوا <c>TotalPages</c>
/// صفر أو مابيحدّثوش <c>PagesPrinted</c> غير في الآخر. عشان كده الحالة
/// دي **بتزوّد** ومابتستبدلش: لو الأرقام مش موجودة، الواجهة بترجع للعدّ
/// بتاعنا زي ما هي، ومحدش بيخسر حاجة.
///
/// حساب خالص على أرقام — متختبر من غير طابعة.
/// </summary>
/// <param name="JobsWaiting">عدد الجوبات في طابور الطابعة دي.</param>
/// <param name="PagesPrinted">الصفحات اللي الطابعة قالت إنها طلّعتها.</param>
/// <param name="PagesTotal">إجمالي صفحات الجوبات اللي في الطابور.</param>
public sealed record PrinterQueueState(int JobsWaiting, int PagesPrinted, int PagesTotal)
{
    /// <summary>مفيش حاجة في الطابور — أو مقدرناش نقرا.</summary>
    public static PrinterQueueState Idle { get; } = new(0, 0, 0);

    public bool IsBusy => JobsWaiting > 0;

    /// <summary>الدرايفر قال أرقام نقدر نصدّقها؟</summary>
    public bool HasCounts => PagesTotal > 0 && PagesPrinted >= 0;

    /// <summary>فاضل في الطابور. مقفول عند صفر — بعض الدرايفرات بتعدّ زيادة.</summary>
    public int PagesLeft => Math.Max(0, PagesTotal - PagesPrinted);

    /// <summary>
    /// سطر عربي للواجهة. بيرجّع "" لما مفيش حاجة تتقال — والواجهة بتخفي
    /// السطر ساعتها بدل ما تسيب مكان فاضي بيلخبط العين.
    /// </summary>
    public string Describe()
    {
        if (!IsBusy)
        {
            return "";
        }

        if (!HasCounts)
        {
            // الدرايفر مابيقولش أرقام. على الأقل نقول إن فيه شغل ماشي،
            // عشان اللي واقف يعرف إن المكنة مش واقفة.
            return JobsWaiting == 1
                ? "الطابعة شغّالة على جوب."
                : $"الطابعة شغّالة — {JobsWaiting} جوب في طابورها.";
        }

        return $"الطابعة طبعت {PagesPrinted} من {PagesTotal} صفحة في طابورها — فاضل {PagesLeft}.";
    }
}