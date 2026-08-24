using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// سجل التشغيل. القاعدة زي مخزن الإعدادات: **اللوج عمره ما يوقف البرنامج**.
/// </summary>
public class FileJobLogTests : IDisposable
{
    private readonly string _folder;

    public FileJobLogTests()
        => _folder = Path.Combine(Path.GetTempPath(), "PrintFlowLog_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Writes_A_Daily_File_With_Readable_Arabic()
    {
        var log = new FileJobLog(_folder);
        log.Info("طباعة 25 نسخة على HP LaserJet");

        string content = File.ReadAllText(log.TodayPath);

        Assert.Contains("طباعة 25 نسخة على HP LaserJet", content);
        Assert.Contains("[معلومة]", content);
    }

    [Fact]
    public void Errors_Include_The_Exception_Type_And_Message()
    {
        var log = new FileJobLog(_folder);
        log.Error("فشل الدمج", new InvalidOperationException("الملف مقفول"));

        string content = File.ReadAllText(log.TodayPath);

        Assert.Contains("[خطأ]", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("الملف مقفول", content);
    }

    [Fact]
    public void Entries_Accumulate_Rather_Than_Overwrite()
    {
        var log = new FileJobLog(_folder);
        log.Info("أول");
        log.Info("تاني");
        log.Info("تالت");

        Assert.Equal(3, File.ReadAllLines(log.TodayPath).Length);
    }

    /// <summary>
    /// مسار مستحيل الكتابة فيه. المفروض ميرميش استثناء يوقف الطباعة.
    /// </summary>
    [Fact]
    public void Unwritable_Folder_Does_Not_Throw()
    {
        var log = new FileJobLog("\0مسار::غلط<>|");

        log.Info("مفروض ميحصلش حاجة");
        log.Error("ولا ده");
    }

    [Fact]
    public void Old_Logs_Are_Cleaned_On_Startup()
    {
        Directory.CreateDirectory(_folder);

        string old = Path.Combine(_folder, "printflow-2020-01-01.log");
        File.WriteAllText(old, "قديم");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-90));

        string recent = Path.Combine(_folder, "printflow-9999-01-01.log");
        File.WriteAllText(recent, "جديد");

        _ = new FileJobLog(_folder, retentionDays: 30);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }
}
