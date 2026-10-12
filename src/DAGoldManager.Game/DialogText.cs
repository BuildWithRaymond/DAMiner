namespace DAGoldManager.Game;

/// <summary>Normalizes observed dialog text and substitutes character greetings without changing prices.</summary>
public static class DialogText
{
    public static string ForCharacter(string text, string name)
    {
        string normalized = text.ReplaceLineEndings("\n").Trim();
        if (string.IsNullOrWhiteSpace(name)) return normalized;
        // Only personalize the character name. Prices and other material text remain exact.
        return System.Text.RegularExpressions.Regex.Replace(normalized,
            @"\b" + System.Text.RegularExpressions.Regex.Escape(name) + @"\b", "{character}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
