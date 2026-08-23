namespace PrintFlow.Domain;

public class PrinterCapabilities
{
    public bool SupportsColor { get; init; }
    public bool SupportsDuplex { get; init; }
    public List<string> PaperSizes { get; init; } = new();
    public string? DefaultPaperSize { get; init; }
}