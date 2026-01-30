using System.Text.RegularExpressions;

namespace BudgetTracker.Api.Infrastructure.Extensions;

public static partial class StringExtensions
{
    [GeneratedRegex(@"```json\s*([\s\S]*?)\s*```")]
    private static partial Regex MarkdownJsonRegex();
    
    [GeneratedRegex(@"\[[\s\S]*\]")]
    private static partial Regex JsonArrayRegex();

    [GeneratedRegex(@"\{[\s\S]*\}")]
    private static partial Regex JsonObjectRegex();
    
    public static string ExtractJsonFromCodeBlock(this string input)
    {
        var match = MarkdownJsonRegex().Match(input);

        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        // Try to find a JSON array directly
        var arrayMatch = JsonArrayRegex().Match(input);
        
        if (arrayMatch.Success)
        {
            return arrayMatch.Value;
        }

        // Try to find a JSON object directly
        var objectMatch = JsonObjectRegex().Match(input);

        if (objectMatch.Success)
        {
            return objectMatch.Value;
        }

        throw new FormatException("Could not extract JSON from the input string");
    }
}