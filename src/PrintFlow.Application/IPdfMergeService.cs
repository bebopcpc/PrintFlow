namespace PrintFlow.Application;

public interface IPdfMergeService
{
    string MergeFiles(List<string> inputFilePaths, string outputPath, string? watermarkText = null);
}