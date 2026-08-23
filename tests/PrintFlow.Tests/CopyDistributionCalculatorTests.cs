using PrintFlow.Application;
using Xunit;

namespace PrintFlow.Tests;

public class CopyDistributionCalculatorTests
{
    [Fact]
    public void EvenDivision_SplitsEqually()
    {
        var result = CopyDistributionCalculator.Distribute(100, new List<string> { "P1", "P2", "P3", "P4" });

        Assert.All(result, r => Assert.Equal(25, r.CopiesAssigned));
        Assert.Equal(100, result.Sum(r => r.CopiesAssigned));
    }

    [Fact]
    public void UnevenDivision_DistributesRemainderToFirstPrinters()
    {
        var result = CopyDistributionCalculator.Distribute(100, new List<string> { "P1", "P2", "P3" });

        Assert.Equal(34, result[0].CopiesAssigned); // أخد الباقي
        Assert.Equal(33, result[1].CopiesAssigned);
        Assert.Equal(33, result[2].CopiesAssigned);
        Assert.Equal(100, result.Sum(r => r.CopiesAssigned));
    }

    [Fact]
    public void SinglePrinter_GetsAllCopies()
    {
        var result = CopyDistributionCalculator.Distribute(100, new List<string> { "OnlyPrinter" });

        Assert.Single(result);
        Assert.Equal(100, result[0].CopiesAssigned);
    }

    [Fact]
    public void FewerCopiesThanPrinters_SomeGetZero()
    {
        var result = CopyDistributionCalculator.Distribute(2, new List<string> { "P1", "P2", "P3", "P4", "P5" });

        Assert.Equal(1, result[0].CopiesAssigned);
        Assert.Equal(1, result[1].CopiesAssigned);
        Assert.Equal(0, result[2].CopiesAssigned);
        Assert.Equal(0, result[3].CopiesAssigned);
        Assert.Equal(0, result[4].CopiesAssigned);
        Assert.Equal(2, result.Sum(r => r.CopiesAssigned));
    }

    [Fact]
    public void ZeroCopies_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            CopyDistributionCalculator.Distribute(0, new List<string> { "P1" }));
    }

    [Fact]
    public void EmptyPrinterList_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            CopyDistributionCalculator.Distribute(100, new List<string>()));
    }

    [Fact]
    public void RealisticScenario_100Copies_8Printers()
    {
        var printers = Enumerable.Range(1, 8).Select(i => $"P{i}").ToList();
        var result = CopyDistributionCalculator.Distribute(100, printers);

        Assert.Equal(100, result.Sum(r => r.CopiesAssigned));
        Assert.True(result.All(r => r.CopiesAssigned is 12 or 13));
    }
}