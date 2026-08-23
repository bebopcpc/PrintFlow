using PrintFlow.Domain;
using Xunit;

namespace PrintFlow.Tests;

public class PrinterTests
{
    [Fact]
    public void Printer_WithDefaultFlag_IsMarkedAsDefault()
    {
        var printer = new Printer { Name = "HP LaserJet", IsDefault = true };

        Assert.True(printer.IsDefault);
        Assert.Equal("HP LaserJet", printer.Name);
    }

    [Fact]
    public void Printer_WithoutDefaultFlag_IsNotDefault()
    {
        var printer = new Printer { Name = "Canon MX490" };

        Assert.False(printer.IsDefault);
    }
}