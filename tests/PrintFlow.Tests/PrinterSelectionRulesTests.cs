using PrintFlow.Application;
using PrintFlow.Domain;
using Xunit;

namespace PrintFlow.Tests;

public class PrinterSelectionRulesTests
{
    [Fact]
    public void ReadyPrinter_IsEligible()
    {
        var printer = new Printer { Name = "P1", Status = PrinterStatus.Ready };
        Assert.True(PrinterSelectionRules.IsEligibleForJob(printer));
    }

    [Fact]
    public void OfflinePrinter_IsNotEligible()
    {
        var printer = new Printer { Name = "P1", Status = PrinterStatus.Offline };
        Assert.False(PrinterSelectionRules.IsEligibleForJob(printer));
    }

    [Fact]
    public void ErrorPrinter_IsNotEligible()
    {
        var printer = new Printer { Name = "P1", Status = PrinterStatus.Error };
        Assert.False(PrinterSelectionRules.IsEligibleForJob(printer));
    }

    [Fact]
    public void UnknownStatusPrinter_IsEligible_BestEffort()
    {
        // best-effort: منعرفش الحالة أكيد، فمنمنعوش تلقائيًا - المستخدم يقرر
        var printer = new Printer { Name = "P1", Status = PrinterStatus.Unknown };
        Assert.True(PrinterSelectionRules.IsEligibleForJob(printer));
    }

    [Fact]
    public void FilterEligible_ExcludesOnlyOfflineAndError()
    {
        var printers = new List<Printer>
        {
            new() { Name = "Ready1", Status = PrinterStatus.Ready },
            new() { Name = "Offline1", Status = PrinterStatus.Offline },
            new() { Name = "Error1", Status = PrinterStatus.Error },
            new() { Name = "Unknown1", Status = PrinterStatus.Unknown }
        };

        var eligible = PrinterSelectionRules.FilterEligible(printers);

        Assert.Equal(2, eligible.Count);
        Assert.Contains(eligible, p => p.Name == "Ready1");
        Assert.Contains(eligible, p => p.Name == "Unknown1");
    }
}