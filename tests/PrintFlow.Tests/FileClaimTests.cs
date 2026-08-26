using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// تصنيف سبب فشل أخذ الملف الوارد.
///
/// التستات دي مكتوبة بعد باج حقيقي: أول جوب حقيقي (١٧٦ صفحة من Foxit)
/// فشل برسالة <c>Access to the path is denied</c>، والرسالة كانت بتتكرر
/// كل نص ثانية بنص إنجليزي مالوش أي حل جواه.
///
/// الدرس اللي التستات بتحرسه: **مش كل فشل زي التاني**. القفل بيتحل
/// بالانتظار، والصلاحيات عمرها ما هتتحل بالانتظار مهما استنينا.
/// </summary>
public class FileClaimTests
{
    // ══════════ التصنيف ══════════

    [Fact]
    public void Access_Denied_Is_A_Permission_Problem_Not_A_Lock()
    {
        // ده الاستثناء اللي طلع فعلًا عند المستخدم
        var failure = FileClaim.Classify(new UnauthorizedAccessException("Access to the path is denied."));

        Assert.Equal(ClaimFailure.NoPermission, failure);
    }

    [Fact]
    public void UnauthorizedAccess_Is_Not_An_IOException_In_Dotnet()
    {
        // ده بيت القصيد: الكود القديم كان بيمسك IOException بس، و
        // UnauthorizedAccessException بيورث من SystemException مباشرة —
        // فمكانش بيتمسك، وكان بيعدّي للـ catch العام ويطلّع رسالة خام.
        Assert.False(typeof(IOException).IsAssignableFrom(typeof(UnauthorizedAccessException)));
    }

    [Fact]
    public void A_Real_Lock_Is_Classified_As_A_Lock()
    {
        var failure = FileClaim.Classify(
            new IOException("The process cannot access the file because it is being used by another process."));

        Assert.Equal(ClaimFailure.Locked, failure);
    }

    [Fact]
    public void A_Vanished_File_Is_Told_Apart()
    {
        Assert.Equal(ClaimFailure.Missing, FileClaim.Classify(new FileNotFoundException()));
        Assert.Equal(ClaimFailure.Missing, FileClaim.Classify(new DirectoryNotFoundException()));
    }

    [Fact]
    public void A_Missing_File_Comes_Before_The_Lock_Check()
    {
        // FileNotFoundException بيورث من IOException. لو الترتيب اتقلب،
        // ملف اختفى كان هيتحسب "مقفول" وهنفضل نستنى ملف مش موجود.
        Assert.True(typeof(IOException).IsAssignableFrom(typeof(FileNotFoundException)));
        Assert.Equal(ClaimFailure.Missing, FileClaim.Classify(new FileNotFoundException()));
    }

    [Fact]
    public void No_Exception_Means_Success()
    {
        Assert.Equal(ClaimFailure.None, FileClaim.Classify(null));
    }

    [Fact]
    public void Anything_Else_Is_Unknown()
    {
        Assert.Equal(ClaimFailure.Unknown, FileClaim.Classify(new InvalidOperationException()));
    }

    // ══════════ نعيد المحاولة ولا لأ ══════════

    [Fact]
    public void A_Lock_Is_Worth_Waiting_For()
    {
        Assert.True(FileClaim.WorthRetrying(ClaimFailure.Locked));
    }

    [Fact]
    public void A_Lock_Gets_Far_More_Patience_Than_A_Permission_Problem()
    {
        // الدرس من أول تجربة حقيقية: ملزمة ١٧٦ صفحة / ١٢ ميجا فضلت مقفولة
        // شوية، والبرنامج حذّر **بعد ثانيتين** وبعدين الملف وصل سليم.
        // التحذير كان صح تقنيًا وغلط عمليًا.
        Assert.True(
            FileClaim.QuietAttemptsFor(ClaimFailure.Locked) >
            FileClaim.QuietAttemptsFor(ClaimFailure.NoPermission) * 5,
            "القفل لازم ياخد صبر أكتر بكتير من الصلاحيات");
    }

    [Fact]
    public void The_Lock_Threshold_Is_Long_Enough_For_A_Heavy_Job()
    {
        // القراءة كل ٤٠٠ مللي — يعني على الأقل ٢٠ ثانية قبل ما نتكلم
        double seconds = FileClaim.QuietAttemptsFor(ClaimFailure.Locked) * 0.4;

        Assert.True(seconds >= 20, $"٢٠ ثانية على الأقل، وإحنا عندنا {seconds}");
    }

    [Fact]
    public void A_Permission_Problem_Is_Reported_Fast()
    {
        // ده عطل مابيتحلش لوحده — السكوت عليه بيضيّع وقت المستخدم
        double seconds = FileClaim.QuietAttemptsFor(ClaimFailure.NoPermission) * 0.4;

        Assert.True(seconds <= 5, $"٥ ثواني بالكتير، وإحنا عندنا {seconds}");
    }

    [Fact]
    public void A_File_That_Sorted_Itself_Out_Says_So()
    {
        // من غير السطر ده، آخر حاجة في اللوج بتفضل تحذير عن مشكلة خلصت
        string text = FileClaim.Resolved("incoming.pdf");

        Assert.Contains("incoming.pdf", text);
        Assert.Contains("اتحل", text);
        Assert.DoesNotContain("تنبيه", text);
    }

    [Fact]
    public void Permissions_Get_A_Few_Tries_Then_We_Speak_Up()
    {
        // بنعيد شوية لأن المنع ممكن يكون لحظي (السبولر لسه ماسك الملف)،
        // بس مانفضلش نستنى للأبد — ده مش حاجة الوقت بيحلها
        Assert.True(FileClaim.WorthRetrying(ClaimFailure.NoPermission));
        Assert.True(FileClaim.QuietAttempts is > 1 and < 20);
    }

    [Fact]
    public void A_Vanished_File_Is_Not_Worth_Retrying_Or_Reporting()
    {
        Assert.False(FileClaim.WorthRetrying(ClaimFailure.Missing));
        Assert.True(FileClaim.IsSilent(ClaimFailure.Missing));
    }

    [Fact]
    public void An_Unknown_Failure_Is_Reported_Straight_Away()
    {
        // مش عارفين نعمل إيه، فمانضيّعش وقت المستخدم في محاولات فاضية
        Assert.False(FileClaim.WorthRetrying(ClaimFailure.Unknown));
        Assert.False(FileClaim.IsSilent(ClaimFailure.Unknown));
    }

    [Fact]
    public void Success_Is_Silent()
    {
        Assert.True(FileClaim.IsSilent(ClaimFailure.None));
    }

    // ══════════ الرسايل ══════════

    [Fact]
    public void The_Permission_Message_Says_Exactly_What_To_Run()
    {
        // أهم جملة في الإصلاح كله. الرسالة القديمة كانت
        // "Access to the path is denied" وخلاص — واللي في المطبعة
        // مش هيعرف منها حاجة.
        string text = FileClaim.Explain(ClaimFailure.NoPermission, "incoming.pdf");

        Assert.Contains("install-printer.ps1", text);
        Assert.Contains("-FixPermissions", text);
        Assert.Contains("مسؤول", text);
    }

    [Fact]
    public void The_Permission_Message_Names_The_File()
    {
        Assert.Contains("incoming.pdf", FileClaim.Explain(ClaimFailure.NoPermission, "incoming.pdf"));
    }

    [Fact]
    public void The_Lock_Message_Says_It_Will_Keep_Trying()
    {
        string text = FileClaim.Explain(ClaimFailure.Locked, "job.pdf");

        Assert.Contains("هيفضل يحاول", text);
    }

    [Fact]
    public void Silent_Failures_Have_Nothing_To_Say()
    {
        Assert.Equal("", FileClaim.Explain(ClaimFailure.None, "x.pdf"));
        Assert.Equal("", FileClaim.Explain(ClaimFailure.Missing, "x.pdf"));
    }

    [Fact]
    public void Every_Loud_Failure_Says_Something_Useful()
    {
        foreach (ClaimFailure failure in Enum.GetValues<ClaimFailure>())
        {
            if (FileClaim.IsSilent(failure))
            {
                continue;
            }

            string text = FileClaim.Explain(failure, "job.pdf");

            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.Contains("job.pdf", text);
        }
    }
}
