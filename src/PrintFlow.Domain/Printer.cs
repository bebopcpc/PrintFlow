namespace PrintFlow.Domain;

public class Printer
{
    public required string Name { get; set; }
    public bool IsDefault { get; set; }
    public PrinterStatus Status { get; set; } = PrinterStatus.Unknown;
    public string? Port { get; set; }
    public string? DriverName { get; set; }
}