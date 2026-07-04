using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GptBolDll
{
    public static class GitDiffAdapter
    {
        public static GitDiffParseResult Parse(string rawResponse)
        {
            var result = new GitDiffParseResult
            {
                IsGitDiff = !string.IsNullOrWhiteSpace(rawResponse)
            };

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                result.Errors.Add("Resposta vazia.");
                return result;
            }

            string text = RemoveMarkdownWrapper(rawResponse).Trim();
            text = NormalizeEscapedGitDiffText(text);
            if (!ContainsGitDiffSignals(text))
            {
                result.Errors.Add("Sinais de GitDiff não encontrados.");
                return result;
            }

            Log("[GIT-DIFF] Adapter parse iniciado.");

            var lines = SplitLines(text);
            GitDiffFileChange currentFile = null;
            GitDiffHunk currentHunk = null;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                if (line.StartsWith("diff --git ", StringComparison.OrdinalIgnoreCase))
                {
                    currentFile = ParseFileHeader(line, result);
                    currentHunk = null;
                    if (currentFile != null)
                        result.Files.Add(currentFile);
                    continue;
                }

                if (currentFile == null)
                    continue;

                if (line.StartsWith("--- ", StringComparison.OrdinalIgnoreCase))
                {
                    string oldPathRaw = line.Substring(4).Trim();
                    currentFile.OldPath = NormalizePathToken(oldPathRaw);
                    currentFile.IsDeletedFile = string.Equals(oldPathRaw, "/dev/null", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (line.StartsWith("+++ ", StringComparison.OrdinalIgnoreCase))
                {
                    string newPathRaw = line.Substring(4).Trim();
                    currentFile.NewPath = NormalizePathToken(newPathRaw);
                    currentFile.IsNewFile = string.Equals(newPathRaw, "/dev/null", StringComparison.OrdinalIgnoreCase);
                    currentFile.EffectivePath = ResolveEffectivePath(currentFile);
                    ValidatePath(currentFile.EffectivePath, result);
                    continue;
                }

                if (line.StartsWith("@@", StringComparison.OrdinalIgnoreCase))
                {
                    currentHunk = ParseHunkHeader(line, result);
                    currentFile.Hunks.Add(currentHunk);
                    result.TotalHunks++;
                    continue;
                }

                if (currentHunk != null)
                {
                    // Uma linha vazia de unified diff precisa do prefixo de contexto
                    // (um espaco). A quebra apos um "@@" isolado nao e conteudo.
                    if (line.Length == 0)
                        continue;

                    currentHunk.Lines.Add(new GitDiffLine
                    {
                        Kind = ClassifyLine(line),
                        Text = line
                    });
                }
            }

            NormalizeEmptyHunks(result);

            foreach (var file in result.Files)
            {
                Log("[GIT-DIFF] Arquivo parseado: " + (file == null ? string.Empty : file.EffectivePath ?? string.Empty));
                Log("[GIT-DIFF] Hunks: " + (file == null ? 0 : file.Hunks.Count));
                Log("[GIT-DIFF] Linhas removidas: " + CountLines(file, GitDiffLineKind.Removed));
                Log("[GIT-DIFF] Linhas adicionadas: " + CountLines(file, GitDiffLineKind.Added));
                Log("[GIT-DIFF] Linhas contexto: " + CountLines(file, GitDiffLineKind.Context));
            }

            Log("[GIT-DIFF] Arquivos parseados: " + result.Files.Count);
            Log("[GIT-DIFF] Hunks parseados: " + result.TotalHunks);
            result.IsValid = result.Files.Count > 0 && !result.Errors.Any();
            return result;
        }

        private static void NormalizeEmptyHunks(GitDiffParseResult result)
        {
            if (result == null || result.Files == null)
                return;

            var allHunks = result.Files
                .Where(file => file != null && file.Hunks != null)
                .SelectMany(file => file.Hunks)
                .Where(hunk => hunk != null)
                .ToList();

            int emptyCount = allHunks.Count(IsEmptyHunk);
            if (emptyCount == 0)
                return;

            result.HadEmptyHunks = true;
            result.Warnings.Add("Hunk vazio detectado.");

            if (emptyCount == allHunks.Count)
            {
                result.ContainsOnlyEmptyHunks = true;
                foreach (var file in result.Files.Where(file => file != null))
                    file.Hunks.Clear();

                result.TotalHunks = 0;
                result.Warnings.Add("Diff contém apenas hunks vazios.");
                return;
            }

            foreach (var file in result.Files.Where(file => file != null && file.Hunks != null))
            {
                for (int index = file.Hunks.Count - 1; index >= 0; index--)
                {
                    GitDiffHunk hunk = file.Hunks[index];
                    if (!IsEmptyHunk(hunk))
                        continue;

                    if (index == file.Hunks.Count - 1)
                    {
                        file.Hunks.RemoveAt(index);
                        result.TotalHunks--;
                        result.IgnoredTrailingEmptyHunks = true;
                        result.Warnings.Add("Hunk vazio final ignorado com segurança.");
                        continue;
                    }

                    result.HasEmptyHunkInMiddle = true;
                    if (!result.Errors.Any(error => string.Equals(error, "hunk vazio no meio do diff.", StringComparison.OrdinalIgnoreCase)))
                        result.Errors.Add("hunk vazio no meio do diff.");
                }
            }
        }

        private static bool IsEmptyHunk(GitDiffHunk hunk)
        {
            if (hunk == null || hunk.Lines == null || hunk.Lines.Count == 0)
                return true;

            return hunk.Lines.All(line =>
                line == null ||
                (line.Kind != GitDiffLineKind.Added &&
                 line.Kind != GitDiffLineKind.Removed &&
                 line.Kind != GitDiffLineKind.Context));
        }

        private static GitDiffFileChange ParseFileHeader(string line, GitDiffParseResult result)
        {
            var match = Regex.Match(line ?? string.Empty, @"^diff --git a\/(?<old>.+?) b\/(?<new>.+?)$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                result.Warnings.Add("Cabeçalho diff --git não reconhecido.");
                return new GitDiffFileChange { EffectivePath = string.Empty };
            }

            var file = new GitDiffFileChange
            {
                OldPath = NormalizePathToken(match.Groups["old"].Value),
                NewPath = NormalizePathToken(match.Groups["new"].Value),
                EffectivePath = ResolveEffectivePath(match.Groups["new"].Value, match.Groups["old"].Value)
            };

            return file;
        }

        private static GitDiffHunk ParseHunkHeader(string line, GitDiffParseResult result)
        {
            var hunk = new GitDiffHunk { Header = line ?? string.Empty };
            var match = Regex.Match(line ?? string.Empty, @"^@@\s*-(?<oldStart>\d+)(,(?<oldCount>\d+))?\s*\+(?<newStart>\d+)(,(?<newCount>\d+))?\s*@@", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                hunk.OldStart = ParseInt(match.Groups["oldStart"].Value);
                hunk.OldCount = ParseNullableInt(match.Groups["oldCount"].Value);
                hunk.NewStart = ParseInt(match.Groups["newStart"].Value);
                hunk.NewCount = ParseNullableInt(match.Groups["newCount"].Value);
            }
            else
            {
                result.Warnings.Add("Hunk @@ simples encontrado.");
            }

            return hunk;
        }

        private static int ParseInt(string text)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private static int? ParseNullableInt(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? (int?)value : null;
        }

        private static GitDiffLineKind ClassifyLine(string line)
        {
            if (string.IsNullOrEmpty(line))
                return GitDiffLineKind.Context;

            if (line.StartsWith(@"\ No newline at end of file", StringComparison.OrdinalIgnoreCase))
                return GitDiffLineKind.Meta;

            if (line.StartsWith("+", StringComparison.Ordinal))
                return GitDiffLineKind.Added;

            if (line.StartsWith("-", StringComparison.Ordinal))
                return GitDiffLineKind.Removed;

            if (line.StartsWith(" ", StringComparison.Ordinal))
                return GitDiffLineKind.Context;

            return GitDiffLineKind.Context;
        }

        private static string ResolveEffectivePath(GitDiffFileChange file)
        {
            if (file == null)
                return string.Empty;

            if (file.IsNewFile && !string.IsNullOrWhiteSpace(file.OldPath))
                return NormalizePathToken(file.OldPath);

            if (!string.IsNullOrWhiteSpace(file.NewPath) && !string.Equals(file.NewPath, "/dev/null", StringComparison.OrdinalIgnoreCase))
                return NormalizePathToken(file.NewPath);

            return NormalizePathToken(file.OldPath);
        }

        private static string ResolveEffectivePath(string newPath, string oldPath)
        {
            if (!string.IsNullOrWhiteSpace(newPath) && !string.Equals(newPath, "/dev/null", StringComparison.OrdinalIgnoreCase))
                return NormalizePathToken(newPath);
            return NormalizePathToken(oldPath);
        }

        private static string NormalizePathToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string path = text.Trim();
            if (string.Equals(path, "/dev/null", StringComparison.OrdinalIgnoreCase))
                return "/dev/null";

            if (path.StartsWith("a/") || path.StartsWith("b/"))
                path = path.Substring(2);

            if (string.Equals(path, "/dev/null", StringComparison.OrdinalIgnoreCase))
                return "/dev/null";

            return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        }

        private static GitDiffLineKind ClassifyLineSafe(string line)
        {
            return ClassifyLine(line);
        }

        private static int CountLines(GitDiffFileChange file, GitDiffLineKind kind)
        {
            if (file == null)
                return 0;

            return file.Hunks.SelectMany(h => h.Lines).Count(l => l != null && l.Kind == kind);
        }

        private static void ValidatePath(string path, GitDiffParseResult result)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string normalized = path.Replace('\\', '/');
            if (Path.IsPathRooted(path) || normalized.Contains("../") || normalized.Contains("..\\") || normalized.StartsWith("/"))
                result.Warnings.Add("Caminho potencialmente inseguro: " + path);

            string[] blocked = { "vendor", "node_modules", ".venv", "bin", "obj", "cache" };
            if (blocked.Any(b => normalized.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0))
                result.Warnings.Add("Caminho potencialmente inseguro: " + path);

            if (result.Warnings.Any(w => w != null && w.Contains(path)))
                Log("[GIT-DIFF] Warning: caminho potencialmente inseguro: " + path);
        }

        private static string RemoveMarkdownWrapper(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string trimmed = text.Trim();
            if (trimmed.StartsWith("```diff", StringComparison.OrdinalIgnoreCase))
            {
                int firstNl = trimmed.IndexOf('\n');
                int lastFence = trimmed.LastIndexOf("```", StringComparison.OrdinalIgnoreCase);
                if (firstNl >= 0 && lastFence > firstNl)
                    return trimmed.Substring(firstNl + 1, lastFence - firstNl - 1);
            }

            if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                int firstNl = trimmed.IndexOf('\n');
                int lastFence = trimmed.LastIndexOf("```", StringComparison.OrdinalIgnoreCase);
                if (firstNl >= 0 && lastFence > firstNl)
                    return trimmed.Substring(firstNl + 1, lastFence - firstNl - 1);
            }

            return text;
        }

        private static string NormalizeEscapedGitDiffText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (text.IndexOf("\\n", StringComparison.Ordinal) < 0 &&
                text.IndexOf("\\r", StringComparison.Ordinal) < 0 &&
                text.IndexOf("\\t", StringComparison.Ordinal) < 0 &&
                text.IndexOf("\\\"", StringComparison.Ordinal) < 0 &&
                text.IndexOf("\\\\", StringComparison.Ordinal) < 0)
            {
                return text;
            }

            return text.Replace("\\r\\n", "\n")
                       .Replace("\\n", "\n")
                       .Replace("\\r", "\n")
                       .Replace("\\t", "\t")
                       .Replace("\\\"", "\"")
                       .Replace("\\\\", "\\");
        }

        private static bool ContainsGitDiffSignals(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.IndexOf("diff --git a/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (text.IndexOf("--- a/", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    text.IndexOf("+++ b/", StringComparison.OrdinalIgnoreCase) >= 0) ||
                   text.IndexOf("@@", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> SplitLines(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        }

        private static void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
}
