using System.Diagnostics;
using System.Text.Json;
using BudgetTracker.Api.Infrastructure.Extensions;
using Microsoft.Extensions.AI;

namespace BudgetTracker.Api.Features.Transactions.Import.Enhancement;

public partial class TransactionEnhancer : ITransactionEnhancer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IChatClient _chatClient;
    private readonly ILogger<TransactionEnhancer> _logger;

    public TransactionEnhancer(IChatClient chatClient, ILogger<TransactionEnhancer> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }
    
    public async Task<List<EnhancedTransactionDescription>> EnhanceDescriptionAsync(List<string> descriptions, string account, string userId, string? currentImportSessionHash = null)
    {
        if (descriptions.Count == 0)
        {
            return [];
        }

        var startTime = Stopwatch.GetTimestamp();

        try
        {
            var systemPrompt = CreateSystemPrompt();
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