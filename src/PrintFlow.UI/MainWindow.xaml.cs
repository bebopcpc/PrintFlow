using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PrintFlow.Application;
using PrintFlow.Domain;
using PrintFlow.Infrastructure;

namespace PrintFlow.UI;

public partial class MainWindow : Window
{
    private readonly IPrinterRepository _printerRepository = new PrinterService();
    private readonly IPdfPrintService _pdfPrintService = new PdfPrintService();
    private readonly IPdfMergeService _pdfMergeService = new PdfMergeService();

    private readonly DispatcherTimer _refreshTimer;
    private CancellationTokenSource? _refreshCts;
    private bool _isRefreshing;
    private List<Printer> _lastPrinters = new();
    private List<string> _loadedFiles = new();
    private string? _mergedFilePath;

    public MainWindow()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (s, e) => await RefreshPrintersAsync(false);
        _refreshTimer.Start();

        Closed += (s, e) =>
        {
            _refreshTimer.Stop();
            _refreshCts?.Cancel();
        };

        _ = RefreshPrintersAsync(false);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPrintersAsync(true);
    }

    private async Task RefreshPrintersAsync(bool isManualRefresh)
    {
        if (_isRefreshing) return;

        _isRefreshing = true;
        _refreshCts = new CancellationTokenSource();

        try
        {
            var printers = await _printerRepository.GetPrintersAsync(_refreshCts.Token);
            _lastPrinters = printers;

            var previouslySelected = PrintersListBox.SelectedItems
                .Cast<string>()
                .Select(ExtractPrinterName)
                .ToList();

            PrintersListBox.Items.Clear();
            foreach (var printer in printers)
            {
                string defaultTag = printer.IsDefault ? " (افتراضية)" : "";
                string displayItem = $"{printer.Name}{defaultTag} — {printer.Status} — {printer.Port}";
                PrintersListBox.Items.Add(displayItem);

                if (previouslySelected.Contains(printer.Name))
                {
                    PrintersListBox.SelectedItems.Add(displayItem);
                }
            }

            this.Title = $"PrintFlow - تم العثور على {printers.Count} برنتر | آخر تحديث: {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) { }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static string ExtractPrinterName(string displayText)
    {
        int dashIndex = displayText.IndexOf(" —");
        return (dashIndex > 0 ? displayText[..dashIndex] : displayText).Replace(" (افتراضية)", "");
    }

    private void MultiPrinterCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        MultiPrinterPanel.Visibility = MultiPrinterCheckBox.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void FileDropArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void FileDropArea_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);
        var pdfFiles = droppedFiles.Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();

        if (pdfFiles.Count == 0)
        {
            ResultText_SetIfExists("الملفات المسحوبة لازم تكون PDF.");
            return;
        }

        AddFilesToList(pdfFiles);
    }

    private void FileDropArea_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PDF Files (*.pdf)|*.pdf", Multiselect = true };
        if (dialog.ShowDialog() == true)
        {
            AddFilesToList(dialog.FileNames.ToList());
        }
    }

    private void FileDropArea_Click(object sender, MouseButtonEventArgs e) => FileDropArea_Click(sender, (RoutedEventArgs)null!);

    private void AddFilesToList(List<string> newFiles)
    {
        foreach (var file in newFiles)
        {
            if (!_loadedFiles.Contains(file))
            {
                _loadedFiles.Add(file);
            }
        }

        LoadedFilesListBox.Items.Clear();
        foreach (var file in _loadedFiles)
        {
            LoadedFilesListBox.Items.Add(Path.GetFileName(file));
        }
    }

    private void MergeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedFiles.Count == 0)
        {
            MessageBox.Show("حمّل ملفات الأول.");
            return;
        }

        string outputFolder = Path.Combine(Path.GetTempPath(), "PrintFlow");
        Directory.CreateDirectory(outputFolder);
        _mergedFilePath = Path.Combine(outputFolder, $"merged_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

        string? watermark = string.IsNullOrWhiteSpace(WatermarkTextBox.Text) ? null : WatermarkTextBox.Text;
        bool addPageNumbers = PageNumbersCheckBox.IsChecked == true;

        string result = _pdfMergeService.MergeFiles(_loadedFiles, _mergedFilePath, watermark, addPageNumbers);
        MessageBox.Show(result);
    }

    private async void PrintToAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_mergedFilePath) || !File.Exists(_mergedFilePath))
        {
            MessageBox.Show("اضغط \"بدء معالجة الملفات\" الأول قبل الطباعة.");
            return;
        }

        if (!int.TryParse(CopiesPerPrinterTextBox.Text, out int totalCopiesEntered) || totalCopiesEntered <= 0)
        {
            MessageBox.Show("اكتب عدد نسخ صحيح.");
            return;
        }

        string paperSize = (PaperSizeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A4";
        bool grayscale = GrayscaleCheckBox.IsChecked == true;
        bool duplex = DuplexCheckBox.IsChecked == true;
        bool multiPrinterMode = MultiPrinterCheckBox.IsChecked == true;
        bool distributeMode = DistributeCheckBox.IsChecked == true;

        // تحديد البرنترات المستهدفة حسب الوضع
        List<Printer> targetPrinters;
        if (multiPrinterMode)
        {
            var selectedNames = PrintersListBox.SelectedItems.Cast<string>().Select(ExtractPrinterName).ToList();
            targetPrinters = _lastPrinters.Where(p => selectedNames.Contains(p.Name)).ToList();
        }
        else
        {
            // وضع طابعة واحدة: نستخدم الافتراضية تلقائيًا
            targetPrinters = _lastPrinters.Where(p => p.IsDefault).ToList();
            if (targetPrinters.Count == 0 && _lastPrinters.Count > 0)
            {
                targetPrinters = new List<Printer> { _lastPrinters[0] };
            }
        }

        var eligible = PrinterSelectionRules.FilterEligible(targetPrinters);

        if (eligible.Count == 0)
        {
            MessageBox.Show("مفيش برنتر مؤهلة متاحة حاليًا.");
            return;
        }

        string mergedFilePath = _mergedFilePath;

        var resultLines = await Task.Run(() =>
        {
            var lines = new List<string>();

            if (distributeMode && eligible.Count > 1)
            {
                // توزيع إجمالي النسخ على البرنترات
                var distribution = CopyDistributionCalculator.Distribute(totalCopiesEntered, eligible.Select(p => p.Name).ToList());
                foreach (var item in distribution)
                {
                    lines.Add(_pdfPrintService.PrintPdf(mergedFilePath, item.PrinterName, paperSize, item.CopiesAssigned, grayscale, duplex));
                }
            }
            else
            {
                // نفس عدد النسخ على كل برنتر، بالتوازي
                var results = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
                Parallel.For(0, eligible.Count, i =>
                {
                    var printer = eligible[i];
                    results[i] = _pdfPrintService.PrintPdf(mergedFilePath, printer.Name, paperSize, totalCopiesEntered, grayscale, duplex);
                });
                lines.AddRange(Enumerable.Range(0, eligible.Count).Select(i => results[i]));
            }

            return lines;
        });

        MessageBox.Show(string.Join("\n", resultLines));
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _loadedFiles.Clear();
        LoadedFilesListBox.Items.Clear();
        WatermarkTextBox.Text = "";
        PageNumbersCheckBox.IsChecked = false;
        GrayscaleCheckBox.IsChecked = false;
        DuplexCheckBox.IsChecked = false;
        MultiPrinterCheckBox.IsChecked = false;
        DistributeCheckBox.IsChecked = false;
        CopiesPerPrinterTextBox.Text = "1";
        _mergedFilePath = null;
    }

    private void ResultText_SetIfExists(string text) => MessageBox.Show(text);
}