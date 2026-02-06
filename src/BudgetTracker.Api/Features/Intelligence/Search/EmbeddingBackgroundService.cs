using BudgetTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BudgetTracker.Api.Features.Intelligence.Search;

public class EmbeddingBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<EmbeddingBackgroundService> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Embedding background service started - processing new transactions only");

        using var timer = new PeriodicTimer(PollingInterval);

        while(!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessPendingEmbeddingsAsync(stoppingToken);   
            }
            catch(Exception ex)
            {
                logger.LogError(ex, "Error occurred during embedding processing");
            }
        }
    }

    private async Task ProcessPendingEmbeddingsAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BudgetTrackerContext>();
        var embeddingService = scope.ServiceProvider.GetRequiredService<IAzureEmbeddingService>();

        var cutOffTime = DateTime.UtcNow.AddHours(-24);
        var transactionsWithoutEmbeddings = await dbContext.Transactions
            .Where(t => t.Embedding == null && t.ImportedAt >= cutOffTime)
            .OrderByDescending(t => t.ImportedAt)
            .Take(BatchSize)
            .ToListAsync(stoppingToken);

        if(transactionsWithoutEmbeddings.Count == 0)
        {
            logger.LogDebug("No recent transactions found that need embeddings");
            return;
        }

        logger.LogInformation("Processing embeddings for {Count} recent transactions", transactionsWithoutEmbeddings.Count);

        var successCount = 0;
        var errorCount = 0;

        foreach (var transaction in transactionsWithoutEmbeddings)
        {
            if(stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var embedding = await embeddingService.GenerateTransactionEmbeddingAsync(transaction.Description, transaction.Category, stoppingToken);

                transaction.Embedding = embedding;
                successCount++;

                logger.LogDebug("Generated embedding for transaction {Id}: {Description}",
                        transaction.Id, transaction.Description[..Math.Min(transaction.Description.Length, 50)]);
            }
            catch(Exception ex)
            {
                errorCount++;
                logger.LogWarning(ex, "Failed to generate embedding for transaction {Id}: {Description}",
                        transaction.Id, transaction.Description[..Math.Min(transaction.Description.Length, 50)]);

                continue;
            }
        }

        if (successCount > 0)
        {
            await dbContext.SaveChangesAsync(stoppingToken);
            logger.LogInformation("Successfully generated embeddings for {SuccessCount} transactions, {ErrorCount} errors",
                    successCount, errorCount);
        }
    }
}
