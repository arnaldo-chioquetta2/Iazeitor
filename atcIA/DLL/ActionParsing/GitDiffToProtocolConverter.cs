using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GptBolDll
{
    public static class GitDiffToProtocolConverter
    {
        public static GitDiffConversionResult Convert(GitDiffParseResult diff)
        {
            return Convert(diff, null, null);
        }

        public static GitDiffConversionResult Convert(GitDiffParseResult diff, string projectRoot, Action<string> log = null)
        {
            var result = new GitDiffConversionResult();
            Log(log, "[GIT-DIFF] Conversão para protocolo interno iniciada.");

            if (diff == null || diff.Files == null || diff.Files.Count == 0)
            {
                result.Errors.Add("Diff vazio.");
                LogFinal(log, result);
                return result;
            }

            foreach (var file in diff.Files)
            {
                if (file?.Hunks == null || file.Hunks.Count == 0)
                {
                    result.Errors.Add("Arquivo sem hunk: " + (file?.EffectivePath ?? string.Empty));
                    continue;
                }

                for (int i = 0; i < file.Hunks.Count; i++)
                {
                    var hunk = file.Hunks[i];
                    if (hunk == null)
                    {
                        result.Errors.Add("Hunk nulo.");
                        continue;
                    }

                    var conversion = ConvertHunk(file.EffectivePath, hunk, i + 1, projectRoot, log);
                    result.Warnings.AddRange(conversion.Warnings);
                    if (!conversion.Success)
                    {
                        result.Errors.AddRange(conversion.Errors);
                        continue;
                    }

                    result.Operations.Add(conversion.Operation);
                    Log(log, "[GIT-DIFF] Operação convertida: " + conversion.Operation.FilePath + " hunk=" + conversion.Operation.HunkIndex);
                    Log(log, "[GIT-DIFF] SEARCH chars: " + (conversion.Operation.SearchBlock?.Length ?? 0));
                    Log(log, "[GIT-DIFF] REPLACE chars: " + (conversion.Operation.ReplaceBlock?.Length ?? 0));
                }
            }

            result.Success = result.Errors.Count == 0 && result.Operations.Count > 0;
            LogFinal(log, result);
            return result;
        }

        private static GitDiffHunkConversion ConvertHunk(string filePath, GitDiffHunk hunk, int hunkIndex, string projectRoot, Action<string> log)
        {
            var conversion = new GitDiffHunkConversion();
            var errors = conversion.Errors;
            var warnings = conversion.Warnings;

            string effectivePath = filePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(effectivePath))
            {
                errors.Add("FilePath vazio.");
                return conversion;
            }

            var searchLines = new List<string>();
            var replaceLines = new List<string>();
            var removedLines = new List<string>();
            int contextCount = 0;
            int removedCount = 0;
            int addedCount = 0;
            bool hasMeta = false;

            foreach (var line in hunk.Lines ?? Enumerable.Empty<GitDiffLine>())
            {
                if (line == null)
                    continue;

                if (line.Kind == GitDiffLineKind.Meta)
                {
                    hasMeta = true;
                    continue;
                }

                string normalized = NormalizeDiffLine(line.Text, line.Kind);
                if (normalized == null)
                    continue;

                switch (line.Kind)
                {
                    case GitDiffLineKind.Context:
                        contextCount++;
                        searchLines.Add(normalized);
                        replaceLines.Add(normalized);
                        break;
                    case GitDiffLineKind.Removed:
                        removedCount++;
                        searchLines.Add(normalized);
                        removedLines.Add(normalized);
                        break;
                    case GitDiffLineKind.Added:
                        addedCount++;
                        replaceLines.Add(normalized);
                        break;
                }
            }

            if (hasMeta)
                warnings.Add("Hunk contém linha meta ignorada.");

            if (searchLines.Count == 0)
            {
                errors.Add("hunk sem SEARCH_BLOCK.");
                return conversion;
            }

            bool deleteOnly = removedCount > 0 && addedCount == 0;

            string searchBlock = JoinLines(searchLines);
            string replaceBlock = JoinLines(replaceLines);
            bool searchExists = SearchBlockExistsInFile(projectRoot, effectivePath, searchBlock);
            Log(log, "[GIT-DIFF] SEARCH_BLOCK convertido existe no arquivo: " + searchExists.ToString().ToLowerInvariant());

            if (deleteOnly)
            {
                int searchChars = searchBlock.Length;

                Log(log, "[GIT-DIFF] Hunk de remoção pura detectado.");
                Log(log, "[GIT-DIFF] Delete-only sem contexto: " + (contextCount == 0).ToString().ToLowerInvariant());
                Log(log, "[GIT-DIFF] REPLACE_BLOCK vazio permitido para remoção pura.");
                Log(log, "[GIT-DIFF] RemovedLines: " + removedCount);
                Log(log, "[GIT-DIFF] Search chars: " + searchChars);

                if (!searchExists)
                {
                    Log(log, "[GIT-DIFF] Busca literal do bloco removido falhou.");
                    if (TryExpandDeleteOnlyContext(projectRoot, effectivePath, removedLines, out string expandedSearch, out string expandedReplace, out int beforeCount, out int afterCount, out string expansionError, log))
                    {
                        searchBlock = expandedSearch;
                        replaceBlock = expandedReplace;
                        contextCount = beforeCount + afterCount;
                        conversion.Operation = new ConvertedGitDiffOperation
                        {
                            FilePath = effectivePath,
                            SearchBlock = searchBlock,
                            ReplaceBlock = replaceBlock,
                            HunkIndex = hunkIndex,
                            RemovedLines = removedCount,
                            AddedLines = addedCount,
                            ContextLines = contextCount,
                            IsDeleteOnly = deleteOnly,
                            ExpandedFromDeleteOnlyContext = true,
                            ProtocolText = BuildProtocolText(effectivePath, searchBlock, replaceBlock, deleteOnly)
                        };

                        Log(log, "[GIT-DIFF] Delete-only convertido com contexto expandido.");
                        Log(log, "[GIT-DIFF] SEARCH_BLOCK convertido existe no arquivo: true");
                        Log(log, "[GIT-DIFF] SEARCH_BLOCK reconstruído com contexto real.");
                        Log(log, "[GIT-DIFF] Contexto expandido antes: " + beforeCount);
                        Log(log, "[GIT-DIFF] Contexto expandido depois: " + afterCount);
                        return conversion;
                    }

                    if (removedLines.Count == 1)
                    {
                        Log(log, "[GIT-DIFF] Tentando busca por Trim para delete-only simples.");
                        if (TryExpandDeleteOnlyContextByTrim(projectRoot, effectivePath, removedLines[0], out string trimmedSearch, out string trimmedReplace, out int trimmedBefore, out int trimmedAfter, out string trimError, log))
                        {
                            searchBlock = trimmedSearch;
                            replaceBlock = trimmedReplace;
                            contextCount = trimmedBefore + trimmedAfter;
                            conversion.Operation = new ConvertedGitDiffOperation
                            {
                                FilePath = effectivePath,
                                SearchBlock = searchBlock,
                                ReplaceBlock = replaceBlock,
                                HunkIndex = hunkIndex,
                                RemovedLines = removedCount,
                                AddedLines = addedCount,
                                ContextLines = contextCount,
                                IsDeleteOnly = deleteOnly,
                                ExpandedFromDeleteOnlyContext = true,
                                ProtocolText = BuildProtocolText(effectivePath, searchBlock, replaceBlock, deleteOnly)
                            };

                            Log(log, "[GIT-DIFF] Delete-only convertido com contexto expandido.");
                            Log(log, "[GIT-DIFF] SEARCH_BLOCK convertido existe no arquivo: true");
                            Log(log, "[GIT-DIFF] SEARCH_BLOCK reconstruído com contexto real.");
                            Log(log, "[GIT-DIFF] Contexto expandido antes: " + trimmedBefore);
                            Log(log, "[GIT-DIFF] Contexto expandido depois: " + trimmedAfter);
                            return conversion;
                        }

                        if (!string.IsNullOrWhiteSpace(trimError))
                            errors.Add(trimError);
                        return conversion;
                    }

                    if (!string.IsNullOrWhiteSpace(expansionError))
                        errors.Add(expansionError);
                    return conversion;
                }
            }
            else if (replaceLines.Count == 0)
            {
                errors.Add("hunk sem REPLACE_BLOCK.");
                return conversion;
            }

            if (!searchExists)
            {
                errors.Add("SEARCH_BLOCK convertido nao existe no arquivo real.");
                return conversion;
            }

            if (string.Equals(searchBlock, replaceBlock, StringComparison.Ordinal))
            {
                errors.Add("SEARCH_BLOCK e REPLACE_BLOCK idênticos.");
                return conversion;
            }

            if (contextCount == 0 && ((addedCount > 0 && removedCount == 0) || (removedCount > 0 && addedCount == 0)))
            {
                errors.Add("hunk sem contexto suficiente.");
                return conversion;
            }

            conversion.Operation = new ConvertedGitDiffOperation
            {
                FilePath = effectivePath,
                SearchBlock = searchBlock,
                ReplaceBlock = replaceBlock,
                HunkIndex = hunkIndex,
                RemovedLines = removedCount,
                AddedLines = addedCount,
                ContextLines = contextCount,
                IsDeleteOnly = deleteOnly,
                ProtocolText = BuildProtocolText(effectivePath, searchBlock, replaceBlock, deleteOnly)
            };

            return conversion;
        }

        private static bool TryExpandDeleteOnlyContextByTrim(
            string projectRoot,
            string filePath,
            string removedLine,
            out string expandedSearch,
            out string expandedReplace,
            out int contextBefore,
            out int contextAfter,
            out string error,
            Action<string> log)
        {
            expandedSearch = string.Empty;
            expandedReplace = string.Empty;
            contextBefore = 0;
            contextAfter = 0;
            error = string.Empty;

            string trimmedRemoved = (removedLine ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedRemoved))
            {
                error = "hunk de remoção sem contexto não encontrado";
                Log(log, "[GIT-DIFF] Reparo por Trim bloqueado: trecho não encontrado.");
                return false;
            }

            if (trimmedRemoved.Length < 20 || !ContainsStructuralSignal(trimmedRemoved))
            {
                error = "hunk de remoção sem contexto fraco demais";
                Log(log, "[GIT-DIFF] Reparo por Trim bloqueado: trecho fraco demais.");
                return false;
            }

            string fullPath = ResolveFullPath(projectRoot, filePath);
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath) || !IsAllowedTextFile(fullPath))
            {
                error = "hunk de remoção sem contexto não encontrado no arquivo";
                Log(log, "[GIT-DIFF] Reparo por Trim bloqueado: trecho não encontrado.");
                return false;
            }

            string content = File.ReadAllText(fullPath);
            var lines = SplitLines(content);
            var matches = new List<int>();
            for (int i = 0; i < lines.Count; i++)
            {
                if (string.Equals((lines[i] ?? string.Empty).Trim(), trimmedRemoved, StringComparison.Ordinal))
                    matches.Add(i);
            }

            Log(log, "[GIT-DIFF] Ocorrências por Trim: " + matches.Count);

            if (matches.Count == 0)
            {
                error = "hunk de remoção sem contexto não encontrado no arquivo";
                Log(log, "[GIT-DIFF] Reparo por Trim bloqueado: trecho não encontrado.");
                return false;
            }

            if (matches.Count > 1)
            {
                error = "hunk de remoção sem contexto ambíguo";
                Log(log, "[GIT-DIFF] Reparo por Trim bloqueado: trecho ambíguo.");
                return false;
            }

            int start = matches[0];
            int before = Math.Min(3, start);
            int after = Math.Min(3, Math.Max(0, lines.Count - (start + 1)));

            var search = new List<string>();
            var replace = new List<string>();

            for (int i = start - before; i < start; i++)
            {
                if (i >= 0 && i < lines.Count)
                {
                    search.Add(lines[i]);
                    replace.Add(lines[i]);
                }
            }

            search.Add(lines[start]);

            for (int i = start + 1; i < start + 1 + after; i++)
            {
                if (i >= 0 && i < lines.Count)
                {
                    search.Add(lines[i]);
                    replace.Add(lines[i]);
                }
            }

            expandedSearch = JoinLines(search);
            expandedReplace = JoinLines(replace);
            contextBefore = before;
            contextAfter = after;
            Log(log, "[GIT-DIFF] Linha removida reconstruída com indentação real do arquivo.");
            return true;
        }

        private static bool TryExpandDeleteOnlyContext(
            string projectRoot,
            string filePath,
            List<string> removedLines,
            out string expandedSearch,
            out string expandedReplace,
            out int contextBefore,
            out int contextAfter,
            out string error,
            Action<string> log)
        {
            expandedSearch = string.Empty;
            expandedReplace = string.Empty;
            contextBefore = 0;
            contextAfter = 0;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(filePath))
            {
                error = "hunk de remoção sem contexto não encontrado no arquivo";
                return false;
            }

            string fullPath = ResolveFullPath(projectRoot, filePath);
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                error = "hunk de remoção sem contexto não encontrado no arquivo";
                return false;
            }

            if (!IsAllowedTextFile(fullPath))
            {
                error = "hunk de remoção sem contexto não encontrado no arquivo";
                return false;
            }

            string content = File.ReadAllText(fullPath);
            var lines = SplitLines(content);
            var removed = (removedLines ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.TrimEnd()).ToList();
            if (removed.Count == 0)
            {
                error = "hunk de remoção sem contexto não encontrado no arquivo";
                return false;
            }

            var matches = FindExactBlockMatches(lines, removed);
            Log(log, "[GIT-DIFF] Linha/bloco removido encontrado no arquivo: " + (matches.Count > 0 ? "true" : "false"));
            Log(log, "[GIT-DIFF] Ocorrências do bloco removido: " + matches.Count);

            if (matches.Count == 0)
            {
                error = "hunk de remoção sem contexto não encontrado no arquivo";
                return false;
            }

            if (matches.Count > 1)
            {
                error = "hunk de remoção sem contexto ambíguo";
                return false;
            }

            int start = matches[0];
            int before = Math.Min(3, start);
            int after = Math.Min(3, Math.Max(0, lines.Count - (start + removed.Count)));

            var search = new List<string>();
            var replace = new List<string>();

            for (int i = start - before; i < start; i++)
            {
                if (i >= 0 && i < lines.Count)
                {
                    search.Add(lines[i]);
                    replace.Add(lines[i]);
                }
            }

            foreach (string line in removed)
                search.Add(line);

            for (int i = start + removed.Count; i < start + removed.Count + after; i++)
            {
                if (i >= 0 && i < lines.Count)
                {
                    search.Add(lines[i]);
                    replace.Add(lines[i]);
                }
            }

            expandedSearch = JoinLines(search);
            expandedReplace = JoinLines(replace);
            contextBefore = before;
            contextAfter = after;
            return true;
        }

        private static bool SearchBlockExistsInFile(string projectRoot, string filePath, string searchBlock)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) ||
                string.IsNullOrWhiteSpace(filePath) ||
                string.IsNullOrWhiteSpace(searchBlock))
                return false;

            string fullPath = ResolveFullPath(projectRoot, filePath);
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                return false;

            if (!IsAllowedTextFile(fullPath))
                return false;

            string content = NormalizeTextForMatch(File.ReadAllText(fullPath));
            string needle = NormalizeTextForMatch(searchBlock);
            return content.IndexOf(needle, StringComparison.Ordinal) >= 0;
        }

        private static List<int> FindExactBlockMatches(List<string> lines, List<string> block)
        {
            var matches = new List<int>();
            if (lines == null || block == null || block.Count == 0 || lines.Count < block.Count)
                return matches;

            for (int i = 0; i <= lines.Count - block.Count; i++)
            {
                bool ok = true;
                for (int j = 0; j < block.Count; j++)
                {
                    if (!string.Equals((lines[i + j] ?? string.Empty).TrimEnd(), (block[j] ?? string.Empty).TrimEnd(), StringComparison.Ordinal))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                    matches.Add(i);
            }

            return matches;
        }

        private static string NormalizeTextForMatch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static bool ContainsStructuralSignal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string lower = text.ToLowerInvariant();
            return lower.Contains("<button") ||
                   lower.Contains("id=") ||
                   lower.Contains("class=") ||
                   lower.Contains("function") ||
                   lower.Contains("const ") ||
                   lower.Contains("let ") ||
                   lower.Contains("var ") ||
                   lower.Contains("def ") ||
                   lower.Contains("public ") ||
                   lower.Contains("private ");
        }

        private static bool IsAllowedTextFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string lower = path.ToLowerInvariant();
            string[] allowed =
            {
                ".cs", ".php", ".js", ".ts", ".css", ".html", ".blade.php",
                ".json", ".xml", ".txt", ".md", ".yml", ".yaml", ".env.example"
            };

            return allowed.Any(ext => lower.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveFullPath(string projectRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string relative = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(projectRoot, relative));
            string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return full;
        }

        private static string BuildProtocolText(string filePath, string searchBlock, string replaceBlock, bool deleteOnly)
        {
            var sb = new StringBuilder();
            sb.Append("ARQ=").Append(filePath ?? string.Empty).AppendLine();
            sb.AppendLine("SEARCH_BLOCK");
            sb.AppendLine(searchBlock ?? string.Empty);
            sb.AppendLine("END_SEARCH");
            sb.AppendLine("REPLACE_BLOCK");
            sb.AppendLine(replaceBlock ?? string.Empty);
            sb.Append("END_REPLACE");
            return sb.ToString();
        }

        private static string NormalizeDiffLine(string text, GitDiffLineKind kind)
        {
            if (text == null)
                return null;

            switch (kind)
            {
                case GitDiffLineKind.Context:
                    return text.StartsWith(" ", StringComparison.Ordinal) ? text.Substring(1) : text;
                case GitDiffLineKind.Removed:
                    return text.StartsWith("-", StringComparison.Ordinal) ? text.Substring(1) : text;
                case GitDiffLineKind.Added:
                    return text.StartsWith("+", StringComparison.Ordinal) ? text.Substring(1) : text;
                default:
                    return null;
            }
        }

        private static string JoinLines(List<string> lines)
        {
            return lines == null ? string.Empty : string.Join("\n", lines);
        }

        private static List<string> SplitLines(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        }

        private static void Log(Action<string> log, string message)
        {
            if (log != null)
                log(message);
            else
                System.Diagnostics.Debug.WriteLine(message);
        }

        private static void LogFinal(Action<string> log, GitDiffConversionResult result)
        {
            Log(log, "[GIT-DIFF] Operações convertidas: " + (result?.Operations?.Count ?? 0));
            Log(log, "[GIT-DIFF] Erros de conversão: " + (result?.Errors?.Count ?? 0));
            Log(log, "[GIT-DIFF] Warnings de conversão: " + (result?.Warnings?.Count ?? 0));
        }

        private sealed class GitDiffHunkConversion
        {
            public bool Success => Operation != null && Errors.Count == 0;
            public ConvertedGitDiffOperation Operation { get; set; }
            public List<string> Errors { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();
        }
    }
}
