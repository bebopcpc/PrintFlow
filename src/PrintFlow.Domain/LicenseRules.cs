namespace PrintFlow.Domain;

/// <summary>حالة الترخيص. الترتيب من الأسوأ للأحسن — والأسوأ بيكسب.</summary>
public enum LicenseState
{
    /// <summary>مفيش كود متسجّل أصلًا — أول تشغيل.</summary>
    Missing,

    /// <summary>الكود مش مقروء: حروف غلط أو ناقص.</summary>
    Malformed,

    /// <summary>الكود سليم بس متوقّع بمفتاح تاني — أو حد عبث فيه.</summary>
    Forged,

    /// <summary>كود صح، بس مطلوع لجهاز تاني.</summary>
    WrongMachine,

    /// <summary>الساعة اترجّعت لورا. شوف الشرح في <see cref="Evaluate"/>.</summary>
    ClockMovedBack,

    /// <summary>المدة خلصت.</summary>
    Expired,

    /// <summary>شغّال.</summary>
    Valid
}

/// <summary>نتيجة الفحص: الحالة، وتاريخ الانتهاء، وكام يوم فاضل.</summary>
public readonly record struct LicenseCheck(LicenseState State, DateOnly? ExpiresOn, int DaysLeft)
{
    /// <summary>البرنامج يفتح ولا لأ؟</summary>
    public bool CanRun => State == LicenseState.Valid;

    /// <summary>قرّبت تخلص؟ الواجهة بتعرض تنبيه على أساسها.</summary>
    public bool IsEndingSoon => State == LicenseState.Valid && DaysLeft <= LicenseRules.WarnWithinDays;
}

/// <summary>
/// قواعد الترخيص. حساب خالص على تواريخ وحالات — مفيش تشفير ولا ملفات هنا.
///
/// ═══ ليه منفصلة عن التوقيع ═══
///
/// التحقق من التوقيع محتاج ويندوز ومكتبة تشفير، فمينفعش يتختبر بسهولة.
/// القواعد دي (خلصت؟ الجهاز صح؟ الساعة اترجّعت؟) هي اللي بتقرر يفتح ولا
/// لأ — ودي أخطر حتة، فلازم تكون متختبرة بالكامل.
///
/// اللي فوق بينده التوقيع الأول ويبعت النتيجة هنا كـ bool. كده كل حتة
/// في مكانها: التشفير في طبقة ويندوز، والقرار هنا.
/// </summary>
public static class LicenseRules
{
    /// <summary>قبل الانتهاء بكام يوم نبدأ ننبّه.</summary>
    public const int WarnWithinDays = 14;

    /// <summary>
    /// بيقرّر البرنامج يفتح ولا لأ.
    ///
    /// ═══ ليه بنسأل عن آخر يوم اتشاف ═══
    ///
    /// الترخيص من غير إنترنت بيعتمد على ساعة الجهاز، والساعة دي المستخدم
    /// بيملكها. أسهل التفاف على الدنيا: رجّع التاريخ سنة لورا.
    ///
    /// فالبرنامج بيسجّل آخر يوم شافه. لو النهاردة **أقدم** من آخر يوم
    /// اتسجّل، يبقى الساعة اترجّعت — والبرنامج بيقف ويقول السبب بالنص.
    ///
    /// ⚠ ودي بتقع على مستخدم بريء أحيانًا: بطارية اللوحة خلصت والساعة
    /// رجعت لسنة ٢٠٠٠. عشان كده الرسالة بتقول "اظبط تاريخ الجهاز"
    /// مش "انت بتغش" — الحالتين شكلهم واحد من هنا.
    /// </summary>
    /// <param name="expiresOn">آخر يوم شغل، من جوّه الكود. null = مفيش كود.</param>
    /// <param name="signatureOk">التوقيع اتأكد؟ بييجي من طبقة التشفير.</param>
    /// <param name="machineOk">الكود مطلوع للجهاز ده؟</param>
    /// <param name="today">تاريخ النهاردة من ساعة الجهاز.</param>
    /// <param name="lastSeen">آخر يوم البرنامج سجّله. null = أول تشغيل.</param>
    public static LicenseCheck Evaluate(
        DateOnly? expiresOn,
        bool signatureOk,
        bool machineOk,
        DateOnly today,
        DateOnly? lastSeen)
    {
        if (expiresOn is not DateOnly expiry)
        {
            return new LicenseCheck(LicenseState.Missing, null, 0);
        }

        // ═══ الجهاز الأول، وبعدين التوقيع ═══
        //
        // أول ترتيب كتبته كان التوقيع الأول، والمنطق كان "ماتصدّقش أي
        // حاجة في الكود قبل ما تتأكد إنه مننا". التست كشف إن ده غلط
        // عمليًا:
        //
        // التوقيع بيتعمل على **رقم الجهاز كامل**، فالكود المطلوع لجهاز
        // تاني بيفشل في التوقيع كمان. يعني عميل دافع أخد كود صاحبه
        // بالغلط كان البرنامج بيقوله "الكود ده مش مطلوع من عندنا" —
        // بيتهمه بالتزوير وهو غلطان في اللزق بس.
        //
        // بالترتيب ده: التلميح مش بتاعي ← "لجهاز تاني" (وده صح ومفيد).
        // التلميح بتاعي والتوقيع غلط ← "مش مننا" (وده تزوير فعلًا).
        //
        // أسوأ حالة في الترتيب ده إن مزوّر يشوف رسالة "لجهاز تاني" بدل
        // "مزوّر" — ومالوش أي تمن. الحالة التانية كانت بتقع على عميل
        // بريء، ودي ليها تمن.
        if (!machineOk)
        {
            return new LicenseCheck(LicenseState.WrongMachine, null, 0);
        }

        if (!signatureOk)
        {
            return new LicenseCheck(LicenseState.Forged, null, 0);
        }

        // الساعة قبل الانتهاء: لو اترجّعت، التاريخ اللي قدامنا مالوش قيمة
        if (lastSeen is DateOnly seen && today < seen)
        {
            return new LicenseCheck(LicenseState.ClockMovedBack, expiry, 0);
        }

        if (today > expiry)
        {
            return new LicenseCheck(LicenseState.Expired, expiry, 0);
        }

        return new LicenseCheck(LicenseState.Valid, expiry, expiry.DayNumber - today.DayNumber);
    }

    /// <summary>
    /// سطر عربي للمستخدم. بيقول اللي حصل واللي المفروض يعمله — من غير
    /// اتهام، لأن نص الحالات دي بتحصل بحسن نية.
    /// </summary>
    public static string Describe(LicenseCheck check)
    {
        return check.State switch
        {
            LicenseState.Missing =>
                "البرنامج محتاج كود تفعيل. ابعت رقم الجهاز اللي تحت للي باعلك البرنامج، وهيبعتلك الكود.",

            LicenseState.Malformed =>
                "الكود ده مش مكتوب صح. راجعه — يفضل تنسخه وتلزقه بدل ما تكتبه بالإيد.",

            LicenseState.Forged =>
                "الكود ده مش مطلوع من عندنا. لو انت شاريه، ابعتلنا رقم الجهاز وهنطلّعلك كود صحيح.",

            LicenseState.WrongMachine =>
                "الكود ده مطلوع لجهاز تاني. كل جهاز ليه كود مخصوص — ابعت رقم الجهاز اللي تحت وهنطلّعلك كوده.",

            LicenseState.ClockMovedBack =>
                "تاريخ الجهاز مرجّع لورا، والبرنامج مش قادر يتأكد من المدة. اظبط تاريخ ويندوز الصح وافتح البرنامج تاني. "
                + "لو البطارية بتاعة اللوحة فاضية، دي بتخلي التاريخ يرجع لوحده كل مرة.",

            LicenseState.Expired =>
                $"مدة التفعيل خلصت يوم {Day(check.ExpiresOn)}. كلّمنا للتجديد وهنبعتلك كود جديد.",

            LicenseState.Valid when check.IsEndingSoon =>
                $"التفعيل بيخلص يوم {Day(check.ExpiresOn)} — فاضل {check.DaysLeft} يوم. جدّد قبل ما يقف الشغل.",

            _ => ""
        };
    }

    /// <summary>تاريخ مقروء. الشكل ثابت مهما كانت لغة ويندوز.</summary>
    private static string Day(DateOnly? date) => date?.ToString("yyyy/MM/dd") ?? "؟";
}
