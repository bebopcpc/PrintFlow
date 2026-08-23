using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using PrintFlow.Application;
using PrintFlow.Domain;
using PrintFlow.Infrastructure;
using System.Collections.Concurrent;

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

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
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

            if (isManualRefresh)
            {
                ResultText.Text = "تم تحديث القائمة بنجاح.";
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void PrintTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (PrintersListBox.SelectedItem is not string selected)
        {
            ResultText.Text = "اختر برنتر من القائمة الأول.";
            return;
        }

        string printerName = ExtractPrinterName(selected);
        ResultText.Text = _printerRepository.SendTestPage(printerName);
    }

    private void CapabilitiesButton_Click(object sender, RoutedEventArgs e)
    {
        if (PrintersListBox.SelectedItem is not string selected)
        {
            ResultText.Text = "اختر برنتر من القائمة الأول.";
            return;
        }

        string printerName = ExtractPrinterName(selected);

        var capabilities = _printerRepository.GetCapabilities(printerName);
        string paperSizesText = string.Join(", ", capabilities.PaperSizes.Take(5));

        ResultText.Text =
            $"ألوان: {(capabilities.SupportsColor ? "نعم" : "لا")} | " +
            $"وجهين: {(capabilities.SupportsDuplex ? "نعم" : "لا")} | " +
            $"افتراضي: {capabilities.DefaultPaperSize}\n" +
            $"أحجام ورق متاحة: {paperSizesText}...";
    }

    private void SelectForJobButton_Click(object sender, RoutedEventArgs e)
    {
        if (PrintersListBox.SelectedItems.Count == 0)
        {
            ResultText.Text = "اختر برنتر واحدة على الأقل من القائمة.";
            return;
        }

        var selectedNames = PrintersListBox.SelectedItems
            .Cast<string>()
            .Select(ExtractPrinterName)
            .ToList();

        var selectedPrinters = _lastPrinters.Where(p => selectedNames.Contains(p.Name)).ToList();
        var eligible = PrinterSelectionRules.FilterEligible(selectedPrinters);
        var excluded = selectedPrinters.Except(eligible).ToList();

        string message = $"تم تحديد {eligible.Count} برنتر مؤهلة للـ Job.";
        if (excluded.Count > 0)
        {
            string excludedNames = string.Join(", ", excluded.Select(p => $"{p.Name} ({p.Status})"));
            message += $"\nتم استبعاد: {excludedNames}";
        }

        ResultText.Text = message;
    }

    private static string ExtractPrinterName(string displayText)
    {
        int dashIndex = displayText.IndexOf(" —");
        return (dashIndex > 0 ? displayText[..dashIndex] : displayText).Replace(" (افتراضية)", "");
    }

    private void DistributeButton_Click(object sender, RoutedEventArgs e)
    {
        if (PrintersListBox.SelectedItems.Count == 0)
        {
            ResultText.Text = "اختر برنتر واحدة على الأقل من القائمة.";
            return;
        }

        if (!int.TryParse(TotalCopiesTextBox.Text, out int totalCopies) || totalCopies <= 0)
        {
            ResultText.Text = "اكتب عدد نسخ صحيح أكبر من صفر.";
            return;
        }

        var selectedNames = PrintersListBox.SelectedItems.Cast<string>().Select(ExtractPrinterName).ToList();
        var selectedPrinters = _lastPrinters.Where(p => selectedNames.Contains(p.Name)).ToList();
        var eligible = PrinterSelectionRules.FilterEligible(selectedPrinters);

        if (eligible.Count == 0)
        {
            ResultText.Text = "مفيش برنتر مؤهلة من المحدد.";
            return;
        }

        var eligibleNames = eligible.Select(p => p.Name).ToList();
        var distribution = CopyDistributionCalculator.Distribute(totalCopies, eligibleNames);

        var resultLines = new List<string>();
        foreach (var item in distribution)
        {
            string result = _printerRepository.SendCopies(item.PrinterName, item.CopiesAssigned);
            resultLines.Add(result);
        }

        ResultText.Text = string.Join("\n", resultLines);
    }

    private void LoadFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            _loadedFiles = dialog.FileNames.ToList();
            LoadedFilesListBox.Items.Clear();
            foreach (var file in _loadedFiles)
            {
                LoadedFilesListBox.Items.Add(Path.GetFileName(file));
            }
            ResultText.Text = $"تم تحميل {_loadedFiles.Count} ملف.";
        }
    }

    private void MergeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedFiles.Count == 0)
        {
            ResultText.Text = "حمّل ملفات الأول.";
            return;
        }

        string outputFolder = Path.Combine(Path.GetTempPath(), "PrintFlow");
        Directory.CreateDirectory(outputFolder);
        _mergedFilePath = Path.Combine(outputFolder, $"merged_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

        string? watermark = string.IsNullOrWhiteSpace(WatermarkTextBox.Text) ? null : WatermarkTextBox.Text;
        ResultText.Text = _pdfMergeService.MergeFiles(_loadedFiles, _mergedFilePath, watermark);
    }

    private async void PrintToAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_mergedFilePath) || !File.Exists(_mergedFilePath))
        {
            ResultText.Text = "ادمج الملفات الأول قبل الطباعة.";
            return;
        }

        if (PrintersListBox.SelectedItems.Count == 0)
        {
            ResultText.Text = "اختر برنتر واحدة على الأقل.";
            return;
        }

        if (!int.TryParse(CopiesPerPrinterTextBox.Text, out int copiesPerPrinter) || copiesPerPrinter <= 0)
        {
            ResultText.Text = "اكتب عدد نسخ صحيح لكل برنتر.";
            return;
        }

        string paperSize = (PaperSizeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A4";

        var selectedNames = PrintersListBox.SelectedItems.Cast<string>().Select(ExtractPrinterName).ToList();
        var selectedPrinters = _lastPrinters.Where(p => selectedNames.Contains(p.Name)).ToList();
        var eligible = PrinterSelectionRules.FilterEligible(selectedPrinters);

        if (eligible.Count == 0)
        {
            ResultText.Text = "مفيش برنتر مؤهلة من المحدد.";
            return;
        }

        // نمنع المستخدم يدوس تاني وهو لسه شغال، ونوريه إن الطباعة بالتوازي شغال
        PrintToAllButton.IsEnabled = false;
        ResultText.Text = $"جاري الطباعة على {eligible.Count} برنتر بالتوازي، من فضلك انتظر...";

        string mergedFilePath = _mergedFilePath;
        var results = new ConcurrentDictionary<int, string>();

        await Task.Run(() =>
        {
            Parallel.For(0, eligible.Count, i =>
            {
                var printer = eligible[i];
                string result = _pdfPrintService.PrintPdf(mergedFilePath, printer.Name, paperSize, copiesPerPrinter);
                results[i] = result;
            });
        });

        // ترتيب النتايج بنفس ترتيب البرنترات الأصلي
        var resultLines = Enumerable.Range(0, eligible.Count).Select(i => results[i]).ToList();

        ResultText.Text = string.Join("\n", resultLines);
        PrintToAllButton.IsEnabled = true;
    }
}