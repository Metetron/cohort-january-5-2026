namespace BudgetTracker.Api.Features.Transactions.Import.Detection;

public interface ICsvDetector
{
    Task<CsvStructureDetectionResult> DetectStructureAsync(Stream csvStream);
}