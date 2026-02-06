using System.Diagnostics;
using System.Text.Json;
using BudgetTracker.Api.Features.Intelligence.Search;
using BudgetTracker.Api.Infrastructure;
using BudgetTracker.Api.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Pgvector.EntityFrameworkCore;

namespace BudgetTracker.Api.Features.Transactions.Import.Enhancement;

public partial class TransactionEnhancer : ITransactionEnhancer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IChatClient _chatClient;
    private readonly ILogger<TransactionEnhancer> _logger;
    private readonly IAzureEmbeddingService _embeddingService;
    private readonly BudgetTrackerContext _context;

    private const int DefaultContextLimit = 25;
    private const int ContextWindowDays = 365;

    public TransactionEnhancer(IChatClient chatClient, ILogger<TransactionEnhancer> logger, IAzureEmbeddingService embeddingService, BudgetTrackerContext context)
    {
        _chatClient = chatClient;
        _logger = logger;
        _embeddingService = embeddingService;
        _context = context;
    }
    
    public async Task<List<EnhancedTransactionDescription>> EnhanceDescriptionAsync(List<string> descriptions, string account, string userId, string currentImportSessionHash)
    {
        if (descriptions.Count == 0)
        {
            return [];
        }

        var startTime = Stopwatch.GetTimestamp();

        try
        {
            var contextTransactions = await GetSemanticContextTransactionsAsync(descriptions, userId, account,
            DefaultContextLimit, currentImportSessionHash);

            _logger.LogInformation("Retrieved {ContextCount} context transactions for account {Account}",
            contextTransactions.Count, account);

            var systemPrompt = CreateEnhancedSystemPrompt(contextTransactions);
            var userPrompt = CreateUserPrompt(descriptions);

            var response = await _chatClient.GetResponseAsync([
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt)
            ]);

            var content = response.Text ?? string.Empty;
            var results = ParseEnhancedDescriptions(content, descriptions);
            
            _logger.LogInformation("AI processing completed in {ElapsedMilliseconds} ms", Stopwatch.GetElapsedTime(startTime).TotalMilliseconds);
            
            return results;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to enhance transaction descriptions");
            return CreateFallbackResults(descriptions);
        }
    }

    private static string CreateSystemPrompt()
    {
        return """
               You are tasked with categorizing banking transactions. Descriptions are often messy and your job is to provide a readable and understandable description as well as a category for the transaction.

               Guidelines:
               1. Provide an understandable description of the transaction
               2. Remove cryptic codes or transaction numbers from the description
               3. Identify the merchant or service provider and put it into the description
               4. Provide a category for the transaction
               5. Do not invent any information that is not in the original description

               Common categories:
               - Shopping, Groceries, Food & Drink, Entertainment, Gas & Fuel
               - Utilities, Transportation, Healthcare, Transfer, Cash & ATM
               - Income, Investments, Insurance
               - Technology, Subscriptions, Travel, Education, Other

               Examples:
               - "AMZN MKTP US*123456789" → "Amazon Marketplace Purchase" (Category: Shopping)
               - "STARBUCKS COFFEE #1234" → "Starbucks Coffee" (Category: Food & Drink)
               - "SHELL OIL #4567" → "Shell Gas Station" (Category: Gas & Fuel)
               - "DD VODAFONE PORTU 222111000" → "Vodafone Portugal - Direct Debit" (Category: Utilities)
               - "COMPRA 0000 TEMU.COM DUBLIN" → "Temu Online Purchase" (Category: Shopping)
               - "TRF MB WAY P/ Manuel Silva" → "MB WAY Transfer to Manuel Silva" (Category: Transfer)

               Respond with a JSON array where each item has the following fields:
               "originalDescription": the original description of the transaction
               "enhancedDescription": the enhanced description you found for the transaction
               "suggestedCategory": the category you suggest for this transaction
               "confidenceScore": the confidence of your enhancement between 0 and 1

               Output Rules:
               - Respond with JSON only
               - No extra text before or after
               - Be conservative with the confidenceScore
               - Use a score of 0.8 or higher only if you are very ocnfident that you found the right merchant or service provider and category
               """;
    }

    private string CreateEnhancedSystemPrompt(List<Transaction> contextTransactions)
    {
        var basePrompt = """
                     You are a transaction categorization assistant. Your job is to clean up messy bank transaction descriptions and make them more readable and meaningful for users.

                     Guidelines:
                     1. Transform cryptic merchant codes and bank jargon into clear, readable descriptions
                     2. Remove unnecessary reference numbers, codes, and technical identifiers
                     3. Identify the actual merchant or service provider
                     4. Suggest appropriate spending categories when possible
                     5. Maintain accuracy - don't invent information not present in the original
                     """;

        if (contextTransactions.Any())
        {
            var contextSection = "\n\nSIMILAR TRANSACTIONS for this account:\n";
            contextSection += string.Join("\n", contextTransactions.Select(t =>
                $"- \"{t.Description}\" → Amount: {t.Amount:C} → Category: \"{t.Category}\"").Distinct());

            contextSection +=
                "\n\nThese transactions were selected based on semantic similarity to the new transactions being processed.";
            contextSection +=
                "\nUse these patterns to inform your categorization decisions, paying special attention to:";
            contextSection += "\n- Similar merchant names or transaction types";
            contextSection += "\n- Comparable amount ranges for similar categories";
            contextSection += "\n- Established categorization patterns for this user";

            basePrompt += contextSection;
        }

        basePrompt += """

                    Examples:
                    - "AMZN MKTP US*123456789" → "Amazon Marketplace Purchase"
                    - "STARBUCKS COFFEE #1234" → "Starbucks Coffee"
                    - "SHELL OIL #4567" → "Shell Gas Station"
                    - "DD VODAFONE PORTU 222111000 PT00110011" → "Vodafone Portugal - Direct Debit"
                    - "COMPRA 0000 TEMU.COM DUBLIN" → "Temu Online Purchase"
                    - "TRF MB WAY P/ Manuel Silva" → "MB WAY Transfer to Manuel Silva"

                    Respond with a JSON array where each object has:
                    - "originalDescription": the input description
                    - "enhancedDescription": the cleaned description
                    - "suggestedCategory": optional category (e.g., "Groceries", "Entertainment", "Transportation", "Utilities", "Shopping", "Food & Drink", "Gas & Fuel", "Transfer")
                    - "confidenceScore": number between 0-1 indicating confidence in the enhancement

                    Be conservative with confidence scores - only use high scores (>0.8) when you're very certain about the merchant identification.
                    """;

        return basePrompt;
    }

    private static string CreateUserPrompt(List<string> descriptions)
    {
        var descriptionJson = JsonSerializer.Serialize(descriptions);
        return $"Please enhance these transaction descriptions:\n{descriptionJson}";
    }

    private List<EnhancedTransactionDescription> ParseEnhancedDescriptions(
        string content,
        List<string> descriptions)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("AI returned empty response, using fallback results");
            return CreateFallbackResults(descriptions);
        }

        try
        {
            var jsonContent = content.ExtractJsonArrayFromCodeBlock();
            var enhancedDescriptions =
                JsonSerializer.Deserialize<List<EnhancedTransactionDescription>>(jsonContent, JsonOptions);

            if (enhancedDescriptions?.Count == descriptions.Count)
            {
                return enhancedDescriptions;
            }

            _logger.LogWarning(
                "AI returned {ActualCount} descriptions, but {ExpectedCount} were expected, using fallback results",
                enhancedDescriptions?.Count ?? 0, descriptions.Count
            );
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI response as JSON: {Content}", content);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Failed to extract JSON from AI response: {Content}", content);
        }
        
        _logger.LogWarning("AI response format was invalid, return original descriptions");
        return CreateFallbackResults(descriptions);
    }

    private async Task<List<Transaction>> GetSemanticContextTransactionsAsync(
        List<string> descriptions,
        string userId,
        string account,
        int limit,
        string excludeImportSessionHash,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var combinedQuery = string.Join(" ", descriptions.Take(5));

            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(combinedQuery, cancellationToken);
            var vectorString = queryEmbedding.ToString();

            var cutOffDate = DateTime.UtcNow.AddDays(-ContextWindowDays);

            var conditions = new List<string>
            {
                "\"Embedding\" IS NOT NULL",
                "\"UserId\" = {0}",
                "\"Account\" = {1}",
                "\"ImportedAt\" >= {2}",
                "\"Category\" IS NOT NULL AND \"Category\" != ''",
                "\"ImportSessionHash\" != {3}"
            };

            var parameters = new List<object> {userId, account, cutOffDate, excludeImportSessionHash, vectorString, limit};

            var whereClause = string.Join(" AND ", conditions);

            var similarTransactions = await _context.Transactions
                .FromSqlRaw($@"
                    SELECT *
                    FROM ""Transactions""
                    WHERE {whereClause}
                        AND cosine_distance(""Embedding"", {{4}}::vector) < 0.6
                    ORDER BY cosine_distance(""Embedding"", {{4}}::vector) ASC,
                        ""Date"" DESC
                    LIMIT {{5}}",
                    parameters.ToArray())
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} semantically similar context transactions for enhancement",
            similarTransactions.Count);

            return similarTransactions;
        }
        catch(Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get semantic context, falling back to empty list");

            // Fallback to empty list - better to proceed without context than fail
            return new List<Transaction>();
        }
    }

    private static List<EnhancedTransactionDescription> CreateFallbackResults(List<string> descriptions)
    {
        return descriptions
            .Select(d => new EnhancedTransactionDescription
            {
                OriginalDescription = d,
                EnhancedDescription = d,
                SuggestedCategory = null,
                ConfidenceScore = 0
            })
            .ToList();
    }
}