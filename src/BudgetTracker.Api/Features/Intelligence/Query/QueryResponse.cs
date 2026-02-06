using BudgetTracker.Api.Features.Transactions;

namespace BudgetTracker.Api.Features.Intelligence.Query;

public class QueryResponse
{
    public string Answer { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public List<TransactionDto>? Transactions { get; set; }
}