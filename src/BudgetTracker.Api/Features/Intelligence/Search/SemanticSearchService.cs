using BudgetTracker.Api.Features.Transactions;
using BudgetTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace BudgetTracker.Api.Features.Intelligence.Search;

public class SemanticSearchService(
    IAzureEmbeddingService embeddingService,
    BudgetTrackerContext context,
    ILogger<SemanticSearchService> logger) : ISemanticSearchService
{
    public async Task<List<Transaction>> FindRelevantTransactionsAsync(string queryText, string userId, int maxResults = 50)
    {
        if(string.IsNullOrWhiteSpace(queryText) || string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        try
        {
            var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(queryText);
            var vectorString = queryEmbedding.ToString();

            var similarTransactions = await context.Transactions
                .FromSqlRaw(@"
                    SELECT *
                    FROM ""Transactions""
                    WHERE ""Embedding"" IS NOT NULL
                    AND ""UserId"" = {0}
                    ORDER BY cosine_distance(""Embedding"", {1}::vector) ASC
                    LIMIT {2}", userId, vectorString, maxResults)
                .ToListAsync();

            logger.LogInformation("Found {Count} relevant transactions for query: {Query}",
            similarTransactions.Count, queryText[..Math.Min(queryText.Length, 50)]);

            return similarTransactions;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to find relevant transactions for query: {Query}", queryText);
            return [];
        }
    }
}
