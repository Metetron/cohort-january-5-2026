using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace BudgetTracker.Api.Features.Intelligence.Search;

public class AzureEmbeddingService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILogger<AzureEmbeddingService> logger) : IAzureEmbeddingService
{
    public async Task<Vector> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be empty or whitespace when generating embeddings.", nameof(text));
        }
            
        try
        {
            var result = await embeddingGenerator.GenerateAsync(text, cancellationToken: cancellationToken);
            return new Vector(result.Vector);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate embedding for text: {Text}", text[..Math.Min(text.Length, 50)]);
            throw;
        }
    }

    public async Task<Vector> GenerateTransactionEmbeddingAsync(string description, string? category = null, CancellationToken cancellationToken = default)
    {
        var text = string.IsNullOrWhiteSpace(category)
            ? description
            : $"{description} [{category}]";

        return await GenerateEmbeddingAsync(text, cancellationToken);
    }
}