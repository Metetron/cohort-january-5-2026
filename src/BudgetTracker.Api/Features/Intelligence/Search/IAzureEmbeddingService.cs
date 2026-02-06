using Pgvector;

namespace BudgetTracker.Api.Features.Intelligence.Search;

public interface IAzureEmbeddingService
{
    Task<Vector> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    Task<Vector> GenerateTransactionEmbeddingAsync(string description, string? category = null, CancellationToken cancellationToken = default);
}
