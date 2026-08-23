namespace PrintFlow.Domain;

public class CopyDistribution
{
    public required string PrinterName { get; init; }
    public required int CopiesAssigned { get; init; }
}