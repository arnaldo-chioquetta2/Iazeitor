using System;
using System.Text.RegularExpressions;

namespace GptBolDll
{
    public sealed class TokenCounter
    {
        public int InputTokens { get; private set; }
        public int OutputTokens { get; private set; }

        public int TotalTokens => InputTokens + OutputTokens;

        public int RegisterInput(string text)
        {
            int count = CountTokens(text);
            InputTokens += count;
            return count;
        }

        public int RegisterOutput(string text)
        {
            int count = CountTokens(text);
            OutputTokens += count;
            return count;
        }

        public string FormatStatus()
        {
            return $"Tokens acumulados: {TotalTokens} (entrada {InputTokens}, saida {OutputTokens})";
        }

        public static int CountTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            return Regex.Matches(text, @"\w+|[^\s\w]", RegexOptions.Compiled).Count;
        }
    }
}
