namespace BudgetTracker.Api.Features.Intelligence.Query;

public interface IQueryAssistantService
{
    Task<QueryResponse> ProcessQueryAsync(string query, string userId);
}