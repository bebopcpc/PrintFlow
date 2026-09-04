using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// بيحمي الحتة اللي بتقرر البرنامج يفتح ولا لأ.
///
/// الغلط هنا ليه اتجاهين والاتنين وحشين: إما عميل دافع ومش قادر يشتغل،
/// أو حد مش دافع وشغّال. عشان كده كل حالة ليها تست.
///
/// حساب خالص على تواريخ — مالوش دعوة بتشفير ولا ملفات.
/// </summary>
public class LicenseTests
{
    private static readonly DateOnly Today = new(2026, 9, 4);
    private static readonly DateOnly NextYear = new(2027, 9, 4);

    // ══════════ الحالة الطبيعية ══════════

    [Fact]
    public void A_Good_Code_Runs()
    {
        var check = LicenseRules.Evaluate(NextYear, signatureOk: true, machineOk: true, Today, lastSeen: null);

        Assert.Equal(LicenseState.Valid, check.State);
        Assert.True(check.CanRun);
        Assert.Equal(365, check.DaysLeft);
    }

    [Fact]
    public void No_Code_At_All_Asks_For_One()
    {
        var check = LicenseRules.Evaluate(null, signatureOk: false, machineOk: false, Today, null);

        Assert.Equal(LicenseState.Missing, check.State);
        Assert.False(check.CanRun);
        Assert.Contains("رقم الجهاز", LicenseRules.Describe(check));
    }

    /// <summary>آخر يوم في المدة لسه شغل — المدة بتشمل يوم الانتهاء.</summary>
    [Fact]
    public void The_Last_Day_Still_Works()
    {
        var check = LicenseRules.Evaluate(Today, signatureOk: true, machineOk: true, Today, null);

        Assert.True(check.CanRun);
        Assert.Equal(0, check.DaysLeft);
    }

    [Fact]
    public void The_Day_After_Does_Not()
    {
        var yesterday = Today.AddDays(-1);

        var check = LicenseRules.Evaluate(yesterday, signatureOk: true, machineOk: true, Today, null);

        Assert.Equal(LicenseState.Expired, check.State);
        Assert.False(check.CanRun);
    }

    // ══════════ الترتيب ══════════

    /// <summary>
    /// ⚠ الجهاز بيتفحص **قبل** التوقيع.
    ///
    /// التوقيع بيتعمل على رقم الجهاز كامل، فالكود المطلوع لجهاز تاني
    /// بيفشل في الاتنين. لو سألنا عن التوقيع الأول، عميل دافع أخد كود
    /// صاحبه بالغلط كان هيتقاله "الكود ده مش مطلوع من عندنا".
    ///
    /// الترتيب ده بيدّي كل واحد الرسالة اللي تنفعه.
    /// </summary>
    [Fact]
    public void Another_Machines_Code_Is_Not_Called_A_Forgery()
    {
        var check = LicenseRules.Evaluate(
            NextYear, signatureOk: false, machineOk: false, Today, lastSeen: null);

        Assert.Equal(LicenseState.WrongMachine, check.State);
        Assert.Contains("جهاز تاني", LicenseRules.Describe(check));
    }

    /// <summary>
    /// والتزوير الحقيقي: الكود مكتوب لجهازي أنا (التلميح مظبوط) بس
    /// التوقيع مش مننا.
    /// </summary>
    [Fact]
    public void A_Code_For_My_Machine_With_A_Bad_Signature_Is_A_Forgery()
    {
        var check = LicenseRules.Evaluate(
            NextYear, signatureOk: false, machineOk: true, Today, lastSeen: Today.AddDays(10));

        Assert.Equal(LicenseState.Forged, check.State);
        Assert.Contains("مش مطلوع من عندنا", LicenseRules.Describe(check));
    }

    /// <summary>
    /// ⚠ والاتنين بيتفحصوا **قبل** التاريخ وقبل الساعة.
    ///
    /// كود مزوّر ممكن يكون فيه أي تاريخ. لو قلنا "المدة خلصت"، المزوّر
    /// يعرف إنه محتاج بس يظبط التاريخ.
    /// </summary>
    [Fact]
    public void The_Date_Is_Never_Trusted_Before_The_Code_Is()
    {
        var check = LicenseRules.Evaluate(
            Today.AddDays(-100), signatureOk: false, machineOk: true, Today, null);

        Assert.Equal(LicenseState.Forged, check.State);
        Assert.NotEqual(LicenseState.Expired, check.State);
    }

    // ══════════ الساعة ══════════

    /// <summary>
    /// ⚠ أسهل التفاف على ترخيص من غير إنترنت: رجّع تاريخ ويندوز لورا.
    ///
    /// البرنامج بيسجّل آخر يوم شافه. لو النهاردة أقدم منه، يبقى في حاجة
    /// غلط — والبرنامج بيقف.
    /// </summary>
    [Fact]
    public void Winding_The_Clock_Back_Stops_The_Program()
    {
        var check = LicenseRules.Evaluate(
            NextYear, signatureOk: true, machineOk: true,
            today: Today, lastSeen: Today.AddDays(30));

        Assert.Equal(LicenseState.ClockMovedBack, check.State);
        Assert.False(check.CanRun);
    }

    /// <summary>نفس اليوم مش رجوع — البرنامج بيتفتح أكتر من مرة في اليوم.</summary>
    [Fact]
    public void Opening_It_Twice_In_One_Day_Is_Fine()
    {
        var check = LicenseRules.Evaluate(
            NextYear, signatureOk: true, machineOk: true, Today, lastSeen: Today);

        Assert.True(check.CanRun);
    }

    /// <summary>
    /// والرسالة بتقول "اظبط التاريخ" مش "انت بتغش".
    ///
    /// بطارية اللوحة الفاضية بتعمل نفس الحاجة بالظبط، والاتنين شكلهم
    /// واحد من هنا — فمينفعش نتهم حد.
    /// </summary>
    [Fact]
    public void The_Clock_Message_Does_Not_Accuse_Anyone()
    {
        var check = LicenseRules.Evaluate(
            NextYear, signatureOk: true, machineOk: true, Today, lastSeen: Today.AddDays(30));

        string message = LicenseRules.Describe(check);

        Assert.Contains("اظبط تاريخ", message);
        Assert.Contains("البطارية", message);
    }

    // ══════════ التنبيه قبل الانتهاء ══════════

    [Theory]
    [InlineData(30, false)]
    [InlineData(15, false)]
    [InlineData(14, true)]
    [InlineData(1, true)]
    [InlineData(0, true)]
    public void The_Warning_Starts_Two_Weeks_Before(int daysLeft, bool warns)
    {
        var check = LicenseRules.Evaluate(
            Today.AddDays(daysLeft), signatureOk: true, machineOk: true, Today, null);

        Assert.True(check.CanRun);
        Assert.Equal(warns, check.IsEndingSoon);
    }

    /// <summary>والتنبيه بيقول التاريخ وعدد الأيام — مش "قرّب يخلص".</summary>
    [Fact]
    public void The_Warning_Says_The_Date_And_The_Days()
    {
        var check = LicenseRules.Evaluate(
            new DateOnly(2026, 9, 10), signatureOk: true, machineOk: true, Today, null);

        string message = LicenseRules.Describe(check);

        Assert.Contains("2026/09/10", message);
        Assert.Contains("6 يوم", message);
    }

    /// <summary>الترخيص السليم البعيد مالوش رسالة خالص — مفيش زن.</summary>
    [Fact]
    public void A_Healthy_License_Says_Nothing()
    {
        var check = LicenseRules.Evaluate(NextYear, signatureOk: true, machineOk: true, Today, null);

        Assert.Equal("", LicenseRules.Describe(check));
    }

    // ══════════ شكل الكود ══════════

    [Fact]
    public void A_Code_Survives_The_Round_Trip()
    {
        var data = new byte[] { 1, 2, 3, 250, 251, 252, 0, 255 };

        string code = LicenseCode.Format(data);

        Assert.Equal(data, LicenseCode.Parse(code, data.Length));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(66)]    // الحجم الحقيقي: تاريخ + توقيع
    public void Any_Length_Survives_The_Round_Trip(int length)
    {
        var data = new byte[length];

        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)((i * 37) + 11);
        }

        Assert.Equal(data, LicenseCode.Parse(LicenseCode.Format(data), length));
    }

    /// <summary>الشُّرَط للقراءة بس — كل ٥ حروف.</summary>
    [Fact]
    public void The_Code_Is_Grouped_For_Reading()
    {
        string code = LicenseCode.Format(new byte[10]);

        Assert.Contains("-", code);
        Assert.Equal(LicenseCode.GroupSize, code.IndexOf('-'));
    }

    /// <summary>
    /// ⚠ اللي بيلزق من الواتساب بيجيب معاه مسافات وسطور.
    /// والكود لازم يشتغل برضه.
    /// </summary>
    [Fact]
    public void Pasting_With_Spaces_And_Line_Breaks_Still_Works()
    {
        var data = new byte[] { 9, 8, 7, 6, 5 };

        string code = LicenseCode.Format(data);
        string messy = "  " + code.Replace("-", " \n ") + "  ";

        Assert.Equal(data, LicenseCode.Parse(messy, data.Length));
    }

    /// <summary>
    /// ⚠ ودي أهم واحدة للي بيكتب بالإيد.
    ///
    /// صفر و O شكلهم واحد في أغلب الخطوط، وواحد و I و L كمان. اللي
    /// بيكتب الحرف بدل الرقم مش غلطان — الكود بتاعه لازم يشتغل بدل
    /// ما يقعد نص ساعة يدوّر على حرف.
    /// </summary>
    [Fact]
    public void Typing_O_Instead_Of_Zero_Still_Works()
    {
        var data = new byte[] { 0, 0, 0, 0, 0 };

        string code = LicenseCode.Format(data);

        Assert.Equal(data, LicenseCode.Parse(code.Replace('0', 'O'), data.Length));
        Assert.Equal(data, LicenseCode.Parse(code.ToLowerInvariant(), data.Length));
    }

    [Fact]
    public void A_Letter_That_Is_Not_In_The_Alphabet_Is_Refused()
    {
        Assert.Null(LicenseCode.Parse("ABC%E-12345", 5));
    }

    [Fact]
    public void A_Short_Code_Is_Refused_Instead_Of_Half_Read()
    {
        var data = new byte[10];

        string code = LicenseCode.Format(data);

        Assert.Null(LicenseCode.Parse(code[..8], data.Length));
    }

    [Fact]
    public void A_Long_Code_Is_Refused_Too()
    {
        var data = new byte[5];

        string code = LicenseCode.Format(data) + "ZZZZZZZZ";

        Assert.Null(LicenseCode.Parse(code, data.Length));
    }

    [Fact]
    public void Nothing_At_All_Is_Refused_Quietly()
    {
        Assert.Null(LicenseCode.Parse(null, 5));
        Assert.Null(LicenseCode.Parse("", 5));
        Assert.Null(LicenseCode.Parse("   ", 5));
        Assert.Equal("", LicenseCode.Format([]));
    }

    /// <summary>
    /// الأبجدية مفيهاش I ولا L ولا O ولا U — عشان مايحصلش لخبطة في
    /// القراءة، وعشان مايتكوّنش كلام مش لايق بالصدفة.
    /// </summary>
    [Fact]
    public void The_Alphabet_Has_No_Confusing_Letters()
    {
        var data = new byte[64];

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)i;
        }

        string code = LicenseCode.Format(data).Replace("-", "");

        Assert.DoesNotContain("I", code);
        Assert.DoesNotContain("L", code);
        Assert.DoesNotContain("O", code);
        Assert.DoesNotContain("U", code);
    }
}
