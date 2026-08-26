using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// قواعد إعادة المحاولة لما استبدال ملف يفشل.
///
/// ═══ البلاغ اللي التستات دي اتكتبت بسببه ═══
///
/// في ١.٩.٥ عملنا الملف المؤقت باسم فريد عشان نسختين من البرنامج مايكتبوش
/// فوق بعض. الإصلاح كان ناقص: الكتابة بقت آمنة، بس **النقل** للاسم النهائي
/// فضل نقطة اصطدام. أول بيلد على ويندوز طلّع:
///
///   System.UnauthorizedAccessException : Access to the path is denied
///     at System.IO.FileSystem.MoveFile
///     at PrintFlow.Infrastructure.JsonSettingsStore.Write
///
/// الدرس: <c>MoveFileEx</c> بيرفض لو الملف الهدف مفتوح عند أي حد — حتى
/// لو بيقرا بس، وحتى لو الصلاحيات مظبوطة تمامًا.
/// </summary>
public class FileReplaceTests
{
    [Fact]
    public void A_Busy_Destination_Is_Worth_Retrying()
    {
        // ده الاستثناء اللي طلع فعلًا في البيلد
        Assert.True(FileReplace.WorthRetrying(
            new UnauthorizedAccessException("Access to the path is denied.")));
    }

    [Fact]
    public void A_Locked_File_Is_Worth_Retrying_Too()
    {
        Assert.True(FileReplace.WorthRetrying(new IOException("being used by another process")));
    }

    [Fact]
    public void A_Bad_Path_Is_Not_Worth_Retrying()
    {
        // مسار غلط مش هيبقى صح بعد ٨ محاولات — ده وقت ضايع
        Assert.False(FileReplace.WorthRetrying(new ArgumentException("مسار غلط")));
        Assert.False(FileReplace.WorthRetrying(new NotSupportedException()));
        Assert.False(FileReplace.WorthRetrying(null));
    }

    [Fact]
    public void The_First_Attempt_Waits_For_Nothing()
    {
        // الحالة الغالبة: مفيش أي اصطدام. مايصحش ندفع تمن انتظار فيها.
        Assert.Equal(0, FileReplace.DelayMilliseconds(0));
    }

    [Fact]
    public void The_Waiting_Gets_Longer_Not_Shorter()
    {
        for (int attempt = 1; attempt < FileReplace.Attempts; attempt++)
        {
            Assert.True(
                FileReplace.DelayMilliseconds(attempt) >= FileReplace.DelayMilliseconds(attempt - 1),
                $"المحاولة {attempt} بتستنى أقل من اللي قبلها");
        }
    }

    [Fact]
    public void The_Early_Attempts_Are_Quick()
    {
        // أغلب الاصطدامات بتتحل في أول ٢٠ مللي — مانستناش ١٦٠ من أول مرة
        Assert.True(FileReplace.DelayMilliseconds(1) <= 10);
        Assert.True(FileReplace.DelayMilliseconds(2) <= 20);
    }

    [Fact]
    public void The_Whole_Budget_Is_Long_Enough_For_An_Antivirus_Scan()
    {
        // مضاد الفيروسات بياخد جزء من الثانية على ملف صغير
        Assert.True(FileReplace.TotalBudgetMilliseconds >= 300,
            $"الميزانية {FileReplace.TotalBudgetMilliseconds} مللي — قليلة");
    }

    [Fact]
    public void And_Short_Enough_That_A_Real_Permission_Problem_Says_So_Fast()
    {
        // لو المشكلة صلاحيات حقيقية، الانتظار مش هيحلها. المستخدم يستنى
        // أقل من ثانية وبعدين يعرف الحقيقة.
        Assert.True(FileReplace.TotalBudgetMilliseconds <= 1000,
            $"الميزانية {FileReplace.TotalBudgetMilliseconds} مللي — كتير");
    }

    [Fact]
    public void There_Is_A_Last_Attempt()
    {
        // من غير حد أقصى، ملف مقفول للأبد بيعلّق البرنامج بدل ما يقول مشكلة
        Assert.True(FileReplace.IsLastAttempt(FileReplace.Attempts - 1));
        Assert.False(FileReplace.IsLastAttempt(FileReplace.Attempts - 2));
    }

    [Fact]
    public void Retrying_More_Than_Once_Is_The_Whole_Point()
    {
        Assert.True(FileReplace.Attempts > 1);
    }

    /// <summary>
    /// الفرق بين هنا وبين <see cref="FileClaim"/> مقصود ومهم.
    ///
    /// هناك قلنا إن الصلاحيات **مابتتحلش بالانتظار** فبنتكلم بسرعة.
    /// هنا بنعيد المحاولة على نفس نوع الاستثناء. والاتنين صح، لأن
    /// المصدر مختلف: هناك ملف عمله SYSTEM ومش بتاعنا (قرار دائم)، وهنا
    /// ملفنا إحنا بس مشغول دلوقتي (بيعدّي بعد لحظات).
    ///
    /// التست ده مكتوب عشان أي حد يقرا الاتنين ميفتكرش إن فيه تناقض.
    /// </summary>
    [Fact]
    public void This_Is_A_Different_Situation_From_Claiming_An_Incoming_File()
    {
        var busy = new UnauthorizedAccessException("Access to the path is denied.");

        // استبدال ملفنا: نستنى شوية، الانشغال بيعدّي
        Assert.True(FileReplace.WorthRetrying(busy));
        Assert.True(FileReplace.TotalBudgetMilliseconds <= 1000);

        // أخذ ملف عمله SYSTEM: بنعيد مرات قليلة وبعدين نقول للمستخدم
        // يشغّل السكربت — لأن الانتظار مش هيغيّر الصلاحيات
        Assert.Equal(ClaimFailure.NoPermission, FileClaim.Classify(busy));
        Assert.Contains("install-printer.ps1", FileClaim.Explain(ClaimFailure.NoPermission, "x.pdf"));
    }
}
