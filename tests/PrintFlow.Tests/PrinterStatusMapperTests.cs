using PrintFlow.Domain;
using PrintFlow.Infrastructure;
using Xunit;

namespace PrintFlow.Tests;

public class PrinterStatusMapperTests
{
    [Fact]
    public void Offline_Flag_AlwaysReturnsOffline_RegardlessOfStatusCode()
    {
        var result = PrinterStatusMapper.Map(isOffline: true, printerStatusCode: 3);
        Assert.Equal(PrinterStatus.Offline, result);
    }

    [Fact]
    public void StatusCode3_WhenOnline_ReturnsReady()
    {
        var result = PrinterStatusMapper.Map(isOffline: false, printerStatusCode: 3);
        Assert.Equal(PrinterStatus.Ready, result);
    }

    [Fact]
    public void StatusCode2_WhenOnline_ReturnsError()
    {
        var result = PrinterStatusMapper.Map(isOffline: false, printerStatusCode: 2);
        Assert.Equal(PrinterStatus.Error, result);
    }

    [Fact]
    public void StatusCode7_WhenOnline_ReturnsOffline()
    {
        var result = PrinterStatusMapper.Map(isOffline: false, printerStatusCode: 7);
        Assert.Equal(PrinterStatus.Offline, result);
    }

    [Fact]
    public void NullStatusCode_WhenOnline_ReturnsUnknown()
    {
        var result = PrinterStatusMapper.Map(isOffline: false, printerStatusCode: null);
        Assert.Equal(PrinterStatus.Unknown, result);
    }

    [Fact]
    public void UnrecognizedStatusCode_ReturnsUnknown()
    {
        var result = PrinterStatusMapper.Map(isOffline: false, printerStatusCode: 999);
        Assert.Equal(PrinterStatus.Unknown, result);
    }
}