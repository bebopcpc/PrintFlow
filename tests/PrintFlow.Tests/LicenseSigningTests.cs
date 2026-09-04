using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// بيحمي التوقيع — الحتة اللي بتمنع أي حد يطلّع كود لنفسه.
///
/// أهم تست في الملف ده هو
/// <see cref="A_Code_Made_With_Another_Key_Is_Refused"/>: لو عدّى، يبقى
/// أي حد معاه مفتاح يقدر يفعّل البرنامج، والمنتج مالوش قيمة.
///
/// التشفير من مكتبة .NET القياسية، فكله شغّال ومتختبر على أي نظام.
/// </summary>
public class LicenseSigningTests
{
    private static readonly DateOnly Expiry = new(2027, 3, 15);

    private static byte[] Machine(string id) => MachineCode.From(id);

    // ══════════ الحالة الطبيعية ══════════

    [Fact]
    public void A_Code_You_Issued_Opens_On_That_Machine()
    {
        var (secret, publicKey) = LicenseSigning.NewKeyPair();
        var machine = Machine("PC-OF-THE-SHOP");

        string code = LicenseSigning.Issue(secret, machine, Expiry);
        var read = LicenseSigning.Read(publicKey, code, machine);

        Assert.True(read.Parsed);
        Assert.True(read.HintMatches);
        Assert.True(read.SignatureOk);
        Assert.Equal(Expiry, read.ExpiresOn);
    }

    /// <summary>الكود بيتبعت واتساب — فلازم يشتغل بعد اللزق بمسافات وسطور.</summary>
    [Fact]
    public void The_Code_Survives_Being_Pasted_From_A_Chat()
    {
        var (secret, publicKey) = LicenseSigning.NewKeyPair();
        var machine = Machine("PC-1");

        string code = LicenseSigning.Issue(secret, machine, Expiry);
        string messy = "\n  " + code.Replace("-", "\n") + "  \n";

        Assert.True(LicenseSigning.Read(publicKey, messy, machine).SignatureOk);
    }

    // ══════════ الحماية ══════════

    /// <summary>
    /// ⚠ ده التست اللي المنتج كله واقف عليه.
    ///
    /// حد طلّع مفتاحين بتوعه وعمل كود بيهم. البرنامج لازم يرفضه — لأن
    /// المفتاح العام اللي جوّه البرنامج مالوش دعوة بمفتاحه.
    ///
    /// لو التست ده عدّى بالغلط يوم من الأيام، يبقى أي حد يقدر يفعّل
    /// البرنامج من غير ما يدفع.
    /// </summary>
    [Fact]
    public void A_Code_Made_With_Another_Key_Is_Refused()
    {
        var (_, mine) = LicenseSigning.NewKeyPair();
        var (theirSecret, _) = LicenseSigning.NewKeyPair();

        var machine = Machine("PC-1");

        string forged = LicenseSigning.Issue(theirSecret, machine, Expiry);
        var read = LicenseSigning.Read(mine, forged, machine);

        Assert.True(read.Parsed);
        Assert.False(read.SignatureOk);
    }

    /// <summary>
    /// ⚠ والتاني في الأهمية: الكود مايتنقلش من مكنة لمكنة.
    ///
    /// العميل يبعت الكود لصاحبه — لازم يرفض. ده اللي بيخلي البيع
    /// بالجهاز مش بالنسخة.
    /// </summary>
    [Fact]
    public void A_Code_Does_Not_Travel_To_Another_Machine()
    {
        var (secret, publicKey) = LicenseSigning.NewKeyPair();

        string code = LicenseSigning.Issue(secret, Machine("PC-1"), Expiry);
        var read = LicenseSigning.Read(publicKey, code, Machine("PC-2"));

        Assert.True(read.Parsed);
        Assert.False(read.SignatureOk);
    }

    /// <summary>
    /// وبيعرف يقول إنه "لجهاز تاني" مش "مزوّر" — الرسالتين مختلفتين
    /// تمامًا للي واقف قدام الشاشة، والتلميح هو اللي بيفرّق.
    /// </summary>
    [Fact]
    public void It_Can_Tell_Another_Machine_From_A_Forgery()
    {
        var (secret, publicKey) = LicenseSigning.NewKeyPair();

        var mine = Machine("PC-1");

        // كود لجهاز تاني: التلميح مش بتاعي
        var elsewhere = LicenseSigning.Read(
            publicKey, LicenseSigning.Issue(secret, Machine("PC-2"), Expiry), mine);

        // كود مزوّر لجهازي أنا: التلميح بتاعي، بس التوقيع غلط
        var (theirSecret, _) = LicenseSigning.NewKeyPair();
        var forged = LicenseSigning.Read(
            publicKey, LicenseSigning.Issue(theirSecret, mine, Expiry), mine);

        Assert.False(elsewhere.HintMatches);
        Assert.True(forged.HintMatches);
        Assert.False(forged.SignatureOk);
    }

    /// <summary>
    /// تغيير حرف واحد في الكود بيبطّله — مفيش "قريب من الصح".
    /// </summary>
    [Fact]
    public void Changing_One_Letter_Breaks_It()
    {
        var (secret, publicKey) = LicenseSigning.NewKeyPair();
        var machine = Machine("PC-1");

        string code = LicenseSigning.Issue(secret, machine, Expiry);

        // بندوّر على أول حرف مش شرطة ونغيّره لحرف تاني من الأبجدية
        int i = code.IndexOf(code.First(c => c != '-'));
        char replacement = code[i] == 'Z' ? 'Y' : 'Z';

        string tampered = code[..i] + replacement + code[(i + 1)..];

        Assert.False(LicenseSigning.Read(publicKey, tampered, machine).SignatureOk);
    }

    /// <summary>
    /// ⚠ ومحاولة تمديد المدة بتفشل كمان.
    ///
    /// اللي يعرف إن أول بايتين هما التاريخ ويحاول يزوّدهم، بيكسر
    /// التوقيع — لأن التاريخ نفسه جزء من اللي اتوقّع عليه.
    /// </summary>
    [Fact]
    public void Stretching_The_Date_Breaks_The_Signature()
    {
        var (secret, publicKey) = LicenseSigning.NewKeyPair();
        var machine = Machine("PC-1");

        string code = LicenseSigning.Issue(secret, machine, Expiry);

        byte[] raw = LicenseCode.Parse(code, LicenseSigning.CodeBytes)!;

        // نزوّد التاريخ سنة كاملة
        int days = ((raw[0] << 8) | raw[1]) + 365;
        raw[0] = (byte)(days >> 8);
        raw[1] = (byte)(days & 0xFF);

        var read = LicenseSigning.Read(publicKey, LicenseCode.Format(raw), machine);

        Assert.True(read.Parsed);
        Assert.False(read.SignatureOk);
    }

    // ══════════ الأكواد البايظة ══════════

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCDE-FGHJK")]
    [InlineData("مش كود أصلاً")]
    public void Rubbish_Is_Refused_Without_Crashing(string typed)
    {
        var (_, publicKey) = LicenseSigning.NewKeyPair();

        var read = LicenseSigning.Read(publicKey, typed, Machine("PC-1"));

        Assert.False(read.Parsed);
        Assert.False(read.SignatureOk);
        Assert.Null(read.ExpiresOn);
    }

    [Fact]
    public void A_Null_Code_Is_Refused_Without_Crashing()
    {
        var (_, publicKey) = LicenseSigning.NewKeyPair();

        Assert.False(LicenseSigning.Read(publicKey, null, Machine("PC-1")).Parsed);
    }

    /// <summary>
    /// مفتاح عام بايظ في البرنامج = الكود مايتقبلش. البرنامج مايقعش —
    /// بيقول مش مظبوط، وده أوضح من شاشة خطأ تقنية.
    /// </summary>
    [Fact]
    public void A_Broken_Public_Key_Refuses_Instead_Of_Crashing()
    {
        var (secret, _) = LicenseSigning.NewKeyPair();
        var machine = Machine("PC-1");

        string code = LicenseSigning.Issue(secret, machine, Expiry);

        Assert.False(LicenseSigning.Read("مش مفتاح", code, machine).SignatureOk);
        Assert.False(LicenseSigning.Read("", code, machine).SignatureOk);
    }

    // ══════════ التاريخ ══════════

    [Theory]
    [InlineData(2026, 1, 1)]
    [InlineData(2026, 12, 31)]
    [InlineData(2030, 6, 15)]
    [InlineData(2199, 1, 1)]
    public void The_Date_Comes_Back_Exactly(int year, int month, int day)
    {
        var (secret, publicKey) = LicenseSigning.NewKeyPair();
        var machine = Machine("PC-1");
        var expiry = new DateOnly(year, month, day);

        string code = LicenseSigning.Issue(secret, machine, expiry);

        Assert.Equal(expiry, LicenseSigning.Read(publicKey, code, machine).ExpiresOn);
    }

    [Fact]
    public void A_Date_Outside_The_Range_Is_Refused_At_Issue_Time()
    {
        var (secret, _) = LicenseSigning.NewKeyPair();
        var machine = Machine("PC-1");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => LicenseSigning.Issue(secret, machine, new DateOnly(2019, 12, 31)));
    }

    // ══════════ رقم الجهاز ══════════

    [Fact]
    public void The_Same_Windows_Id_Always_Gives_The_Same_Machine_Code()
    {
        Assert.Equal(MachineCode.From("abc-123"), MachineCode.From("abc-123"));
        Assert.Equal(MachineCode.From("abc-123"), MachineCode.From("  ABC-123  "));
    }

    [Fact]
    public void Different_Machines_Give_Different_Codes()
    {
        Assert.NotEqual(
            MachineCode.Display(MachineCode.From("abc-123")),
            MachineCode.Display(MachineCode.From("abc-124")));
    }

    /// <summary>
    /// الرقم اللي بيتبعت واتساب: ١٦ حرف في مجموعات — قصير ومقروء.
    /// </summary>
    [Fact]
    public void The_Machine_Code_Is_Short_Enough_To_Send()
    {
        string shown = MachineCode.Display(MachineCode.From("some-windows-guid"));

        Assert.Equal(16, shown.Replace("-", "").Length);
        Assert.True(shown.Length < 25, $"طويل أوي: {shown}");
    }

    /// <summary>
    /// ⚠ ومن الرقم ده مفيش طريقة ترجع لمعرّف ويندوز الأصلي.
    ///
    /// الرقم بيتنقل في محادثات وبيتخزّن في تليفونات — فمفروض مايقولش
    /// أي حاجة عن الجهاز نفسه.
    /// </summary>
    [Fact]
    public void The_Windows_Id_Cannot_Be_Read_Back_From_It()
    {
        const string windowsId = "11112222-3333-4444-5555-666677778888";

        string shown = MachineCode.Display(MachineCode.From(windowsId));

        Assert.DoesNotContain("1111", shown);
        Assert.DoesNotContain("2222", shown);
        Assert.DoesNotContain("8888", shown);
    }

    [Fact]
    public void No_Windows_Id_Gives_No_Machine_Code()
    {
        Assert.Empty(MachineCode.From(null));
        Assert.Empty(MachineCode.From("   "));
    }

    // ══════════ الوصلة بالقواعد ══════════

    /// <summary>
    /// الرحلة كاملة: كود اتطلع، اتقرا، والقواعد قالت يفتح.
    ///
    /// التستات فوق بتختبر التوقيع لوحده، والقواعد متختبرة لوحدها —
    /// ده بيتأكد إن الاتنين متوصلين صح.
    /// </summary>
    [Fact]
    public void A_Real_Code_Reaches_The_Rules_And_Opens_The_Program()
    {
        var (secret, publicKey) = LicenseSigning.NewKeyPair();
        var machine = Machine("PC-OF-THE-SHOP");
        var today = new DateOnly(2026, 9, 4);

        string code = LicenseSigning.Issue(secret, machine, today.AddDays(365));
        var read = LicenseSigning.Read(publicKey, code, machine);

        var check = LicenseRules.Evaluate(
            read.ExpiresOn, read.SignatureOk, read.HintMatches, today, lastSeen: null);

        Assert.True(check.CanRun);
        Assert.Equal(365, check.DaysLeft);
    }

    /// <summary>وكود لجهاز تاني بيوصل القواعد برسالة "لجهاز تاني".</summary>
    [Fact]
    public void Another_Machines_Code_Reaches_The_Rules_With_The_Right_Message()
    {
        var (secret, publicKey) = LicenseSigning.NewKeyPair();
        var today = new DateOnly(2026, 9, 4);

        string code = LicenseSigning.Issue(secret, Machine("PC-2"), today.AddDays(365));
        var read = LicenseSigning.Read(publicKey, code, Machine("PC-1"));

        var check = LicenseRules.Evaluate(
            read.ExpiresOn, read.SignatureOk, read.HintMatches, today, null);

        Assert.False(check.CanRun);
        Assert.Contains("جهاز تاني", LicenseRules.Describe(check));
    }
}
