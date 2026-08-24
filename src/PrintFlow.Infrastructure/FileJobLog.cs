using System.Text;
using PrintFlow.Application;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيكتب ملف لوج لكل يوم في %AppData%\PrintFlow\logs.
///
/// قاعدة ثابتة زي مخزن الإعدادات: **اللوج عمره ما يوقف البرنامج**.
/// أي مشكلة في الكتابة بتتبلع بصمت — مطبعة مش هتقف عشان ملف نصي.
/// </summary>
public sealed class FileJobLog : IJobLog
{
    private readonly Lock _gate = new();
    private readonly int _retentionDays;

    public FileJobLog(string? folder = null, int retentionDays = 30)
    {
        LogFolder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrintFlow",
            "logs");

        _retentionDays = retentionDays;

        CleanOldLogs();
    }

    public string LogFolder { get; }

    public string TodayPath => Path.Combine(LogFolder, $"printflow-{DateTime.Now:yyyy-MM-dd}.log");

    public void Info(string message) => Write("معلومة", message);

    public void Error(string message, Exception? exception = null) =>
        Write("خطأ", exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(LogFolder);
                File.AppendAllText(
                    TodayPath,
                    $"{DateTime.Now:HH:mm:ss}  [{level}]  {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // اللوج مايوقفش شغل
        }
    }

    private void CleanOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogFolder))
            {
                return;
            }

            var cutoff = DateTime.Now.AddDays(-_retentionDays);

            foreach (string path in Directory.EnumerateFiles(LogFolder, "printflow-*.log"))
            {
                if (File.GetLastWriteTime(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
        }
        catch
        {
            // مش مهم
        }
    }
}
