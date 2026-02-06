namespace BudgetTracker.Api.Features.Transactions.Import.Enhancement;

public interface ITransactionEnhancer
{
    Task<List<EnhancedTransactionDescription>> EnhanceDescriptionAsync(
        List<string> descriptions,
        string account,
        string userId,
        string currentImportSessionHash);
}