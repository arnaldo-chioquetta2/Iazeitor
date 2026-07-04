using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GptBolDll
{
    public sealed class AllowedCommandPolicy
    {
        public List<string> AllowedCommands { get; } = new List<string>();

        public AllowedCommandPolicy()
        {
        }

        public AllowedCommandPolicy(IEnumerable<string> allowedCommands)
        {
            if (allowedCommands == null)
                return;

            AllowedCommands.AddRange(allowedCommands.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
        }

        public void AddRange(IEnumerable<string> allowedCommands)
        {
            if (allowedCommands == null)
                return;

            AllowedCommands.AddRange(allowedCommands.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
        }

        public void EnsureAllowed(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new InvalidOperationException("Comando DOS vazio.");

            if (HasChainOperators(command))
                throw new InvalidOperationException("Comando DOS nao pode usar encadeamento (&, &&, ||, |).");

            if (AllowedCommands.Count == 0)
                return;

            var token = ExtractFirstToken(command);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Nao foi possivel identificar o comando principal.");

            if (!AllowedCommands.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Comando DOS nao permitido pelo projeto: " + token);
        }

        public static string ExtractFirstToken(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return string.Empty;

            var trimmed = command.TrimStart();
            int index = 0;

            while (index < trimmed.Length && !char.IsWhiteSpace(trimmed[index]) && trimmed[index] != '&' && trimmed[index] != '|' && trimmed[index] != '>')
                index++;

            if (index <= 0)
                return string.Empty;

            return trimmed.Substring(0, index);
        }

        public static bool HasChainOperators(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            return command.Contains("&&") || command.Contains("||") || command.Contains("|") || Regex.IsMatch(command, @"(^|[^&])&([^&]|$)");
        }

        public static bool IsSafeVerificationCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            string trimmed = command.Trim();
            if (HasChainOperators(trimmed))
                return false;

            if (ContainsForbiddenShellElements(trimmed))
                return false;

            var tokens = TokenizeCommand(trimmed).ToList();
            if (tokens.Count != 3)
                return false;

            string tool = tokens[0];
            string option = tokens[1];
            string target = tokens[2];

            if (string.Equals(tool, "php", StringComparison.OrdinalIgnoreCase))
                return string.Equals(option, "-l", StringComparison.OrdinalIgnoreCase) && IsSafeRelativePath(target) && HasAllowedScriptExtension(target, ".php");

            if (string.Equals(tool, "node", StringComparison.OrdinalIgnoreCase))
                return string.Equals(option, "--check", StringComparison.OrdinalIgnoreCase) && IsSafeRelativePath(target) && HasAllowedScriptExtension(target, ".js");

            return false;
        }

        private static bool ContainsForbiddenShellElements(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            string lower = command.ToLowerInvariant();
            return lower.Contains("powershell") ||
                   lower.Contains("cmd /c") ||
                   command.Contains(">") ||
                   command.Contains("<") ||
                   lower.Contains("npm ") ||
                   lower.Contains("composer ") ||
                   lower.Contains("artisan ") ||
                   lower.Contains("curl ") ||
                   lower.Contains(" del ") ||
                   lower.StartsWith("del ", StringComparison.OrdinalIgnoreCase) ||
                   lower.Contains(" copy ") ||
                   lower.StartsWith("copy ", StringComparison.OrdinalIgnoreCase) ||
                   lower.Contains(" move ") ||
                   lower.StartsWith("move ", StringComparison.OrdinalIgnoreCase) ||
                   lower.Contains(" rm ") ||
                   lower.StartsWith("rm ", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> TokenizeCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                yield break;

            foreach (Match match in Regex.Matches(command, "\"([^\"]*)\"|([^\\s]+)"))
            {
                if (match.Groups[1].Success)
                {
                    yield return match.Groups[1].Value;
                }
                else if (match.Groups[2].Success)
                {
                    yield return match.Groups[2].Value;
                }
            }
        }

        private static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string trimmed = path.Trim().Trim('"');
            if (Path.IsPathRooted(trimmed))
                return false;

            if (trimmed.Contains("..") || trimmed.Contains(":"))
                return false;

            return !trimmed.Contains("|") && !trimmed.Contains(">") && !trimmed.Contains("<") && !trimmed.Contains("&");
        }

        private static bool HasAllowedScriptExtension(string path, string expectedExtension)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(expectedExtension))
                return false;

            return string.Equals(Path.GetExtension(path.Trim().Trim('"')), expectedExtension, StringComparison.OrdinalIgnoreCase);
        }
    }
}
