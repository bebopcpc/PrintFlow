using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PrintFlow.Infrastructure;
using PrintFlow.Presentation;

namespace PrintFlow.UI;

/// <summary>
/// الـ code-behind فيه حاجتين بس: تركيب الـ ViewModel، والحاجات اللي هي فعلًا
/// شغل واجهة (السحب والإفلات ونوافذ اختيار الملفات والمجلدات).
/// أي منطق أعمال عايش في MainViewModel — المبني على Interfaces والقابل للاختبار.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly FileJobLog _jobLog = new();
    private readonly CancellationTokenSource _lifetimeCts = new();

    public MainWindow()
    {
        InitializeComponent();

        // مخزن واحد بيخدم الإعدادات العامة والإعدادات المسبقة، الاتنين في %AppData%\PrintFlow
        var store = new JsonSettingsStore();

        _viewModel = new MainViewModel(
            new PrinterService(),
            new PdfMergeService(),
            new PdfPrintService(),
            store,
            store,
            new WindowsFontCatalog(),
            _jobLog,
            new PdfInfoService(),
            new PdfSlideComposer(),
            ReadVersion());

        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>رقم النسخة من الـ assembly — عشان أي بلاغ من التجربة نعرف هو من أنهي بيلد.</summary>
    private static string ReadVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_jobLog.LogFolder);
            Process.Start(new ProcessStartInfo(_jobLog.LogFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"مقدرناش نفتح مجلد اللوج: {ex.Message}");
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _jobLog.Info($"تشغيل PrintFlow {ReadVersion()} — ويندوز {Environment.OSVersion.Version}");

        // التحديث الدوري بيشتغل كحلقة async. الـ await بيرجّع التنفيذ لثريد الواجهة
        // لوحده، فتحديث قايمة الطابعات آمن من غير Dispatcher.Invoke.
        _ = _viewModel.RunAutoRefreshAsync(_lifetimeCts.Token);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();

        // الإعدادات المسبقة بتتحفظ لحظة ما تتغير؛ التفضيلات العامة بتتحفظ هنا مرة واحدة
        _viewModel.SaveAppSettings();
        _jobLog.Info("إغلاق البرنامج");
    }

    // ══════════ الملفات ══════════

    private void FileDropArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void FileDropArea_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.AddFiles(paths);
        }
    }

    private void FileDropArea_Click(object sender, MouseButtonEventArgs e) => PickFiles();

    private void LoadFilesButton_Click(object sender, RoutedEventArgs e) => PickFiles();

    private void PickFiles()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "ملفات PDF (*.pdf)|*.pdf",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.AddFiles(dialog.FileNames);
        }
    }

    // ══════════ الإعدادات العامة ══════════

    private void PickOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "اختر مجلد الحفظ الافتراضي"
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.App.DefaultOutputFolder = dialog.FolderName;
        }
    }

    private void ClearOutputFolder_Click(object sender, RoutedEventArgs e)
        => _viewModel.App.DefaultOutputFolder = string.Empty;

    private void PickWatermarkImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختر صورة العلامة المائية",
            Filter = "الصور (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.App.WatermarkImagePath = dialog.FileName;
        }
    }
}
