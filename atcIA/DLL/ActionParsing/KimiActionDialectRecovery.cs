using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GptBolDll
{
    public sealed class KimiDialectRecoveryResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string FilePath { get; set; }
        public string OriginalPath { get; set; }
        public string CorrectedPath { get; set; }
        public string ExtractedContent { get; set; }
        public int Occurrences { get; set; }
        public bool PathCorrected { get; set; }
        public AgentResponse Response { get; set; }
        public AgentAction Action { get; set; }
    }

    public static class KimiActionDialectRecovery
    {
        private static readonly string[] PathKeys =
        {
            "caminho",
            "path",
            "arquivo",
            "file"
        };

        private static readonly string[] ContentKeys =
        {
            "conteudo",
            "content",
            "search_block",
            "searchBlock",
            "trecho",
            "bloco"
        };

        private static readonly string[] TypeKeys =
        {
            "tipo",
            "type"
        };

        public static bool IsPotentialDialectCandidate(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return false;

            if (rawResponse.IndexOf("diff --git", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return rawResponse.IndexOf("acacoes_diff", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawResponse.IndexOf("acoes_diff", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawResponse.IndexOf("alteracoes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawResponse.IndexOf("caminho", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawResponse.IndexOf("tipo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawResponse.IndexOf("SEARCH_BLOCK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawResponse.IndexOf("conteudo", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool TryRecoverDeleteOnly(
            string rawResponse,
            string projectRoot,
            Action<string> log,
            out KimiDialectRecoveryResult recovery)
        {
            recovery = new KimiDialectRecoveryResult();

            if (!IsPotentialDialectCandidate(rawResponse))
                return false;

            Log(log, "[KIMI-RECOVERY] Resposta sem GitDiff candidata a recuperação.");

            string caminho = ExtractSingleValue(rawResponse, PathKeys, out int pathCount);
            string conteudo = ExtractSingleValue(rawResponse, ContentKeys, out int contentCount);
            string tipo = ExtractSingleValue(rawResponse, TypeKeys, out int typeCount);

            if (pathCount != 1 || contentCount != 1)
            {
                recovery.ErrorMessage = "Resposta Kimi sem GitDiff contém múltiplas alterações e não foi recuperada com segurança.";
                Log(log, "[KIMI-RECOVERY] Recuperação bloqueada: " + recovery.ErrorMessage);
                return false;
            }

            if (!IsSupportedDialectType(tipo))
            {
                recovery.ErrorMessage = "Resposta Kimi sem GitDiff contém tipo não suportado para recuperação segura.";
                Log(log, "[KIMI-RECOVERY] Recuperação bloqueada: " + recovery.ErrorMessage);
                return false;
            }

            Log(log, "[KIMI-RECOVERY] Caminho extraído: " + caminho);
            Log(log, "[KIMI-RECOVERY] Conteúdo extraído chars: " + (conteudo == null ? 0 : conteudo.Length));

            List<string> block = SplitBlock(conteudo);
            string fullPathInformado = ResolveSafeFullPath(projectRoot, caminho);
            List<string> arquivosPermitidosComOcorrencia;
            int ocorrenciasNoArquivoInformado = 0;
            List<int> matchesInformado = new List<int>();

            if (!string.IsNullOrWhiteSpace(fullPathInformado) &&
                File.Exists(fullPathInformado) &&
                IsAllowedTextFile(fullPathInformado))
            {
                string[] linhasInformado = SplitLines(File.ReadAllText(fullPathInformado));
                matchesInformado = FindExactBlockMatches(linhasInformado, block);
                ocorrenciasNoArquivoInformado = matchesInformado.Count;
            }

            if (ocorrenciasNoArquivoInformado > 0)
            {
                Log(log, "[KIMI-RECOVERY] Ocorrências no arquivo real: " + ocorrenciasNoArquivoInformado);
                return CriarRecuperacaoComArquivoEncontrado(
                    projectRoot,
                    fullPathInformado,
                    caminho,
                    conteudo,
                    block,
                    matchesInformado,
                    log,
                    out recovery);
            }

            Log(log, "[KIMI-RECOVERY] Caminho informado não contém SEARCH_BLOCK.");
            Log(log, "[KIMI-RECOVERY] Procurando SEARCH_BLOCK em arquivos permitidos.");

            arquivosPermitidosComOcorrencia = BuscarArquivosPermitidosComOcorrencia(
                projectRoot,
                rawResponse,
                block,
                caminho,
                log,
                out Dictionary<string, List<int>> matchesPorArquivo);

            Log(log, "[KIMI-RECOVERY] Arquivos com ocorrência do SEARCH_BLOCK: " + arquivosPermitidosComOcorrencia.Count);

            if (arquivosPermitidosComOcorrencia.Count == 0)
            {
                recovery.ErrorMessage = "Resposta Kimi sem GitDiff não pôde ser recuperada: SEARCH_BLOCK não encontrado em arquivo permitido.";
                Log(log, "[KIMI-RECOVERY] Recuperação bloqueada: " + recovery.ErrorMessage);
                return false;
            }

            if (arquivosPermitidosComOcorrencia.Count > 1)
            {
                recovery.ErrorMessage = "Resposta Kimi sem GitDiff não pôde ser recuperada: SEARCH_BLOCK ambíguo em múltiplos arquivos.";
                Log(log, "[KIMI-RECOVERY] Recuperação bloqueada: " + recovery.ErrorMessage);
                return false;
            }

            string caminhoCorrigido = arquivosPermitidosComOcorrencia[0];
            List<int> matches = matchesPorArquivo[caminhoCorrigido];
            if (matches == null || matches.Count != 1)
            {
                recovery.ErrorMessage = "Resposta Kimi sem GitDiff não pôde ser recuperada: trecho ambíguo no arquivo.";
                Log(log, "[KIMI-RECOVERY] Recuperação bloqueada: " + recovery.ErrorMessage);
                return false;
            }

            Log(log, "[KIMI-RECOVERY] Caminho corrigido com segurança: " + caminhoCorrigido);
            Log(log, "[KIMI-RECOVERY] Caminho original da Kimi: " + caminho);
            Log(log, "[KIMI-RECOVERY] Ação canônica criada com caminho corrigido.");

            return CriarRecuperacaoComArquivoEncontrado(
                projectRoot,
                caminhoCorrigido,
                caminho,
                conteudo,
                block,
                matches,
                log,
                out recovery);
        }

        private static bool CriarRecuperacaoComArquivoEncontrado(
            string projectRoot,
            string fullPath,
            string caminhoOriginal,
            string conteudo,
            List<string> block,
            List<int> matches,
            Action<string> log,
            out KimiDialectRecoveryResult recovery)
        {
            recovery = new KimiDialectRecoveryResult();

            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                recovery.ErrorMessage = "Resposta Kimi sem GitDiff não pôde ser recuperada: arquivo não encontrado no projeto.";
                Log(log, "[KIMI-RECOVERY] Recuperação bloqueada: " + recovery.ErrorMessage);
                return false;
            }

            if (!IsAllowedTextFile(fullPath))
            {
                recovery.ErrorMessage = "Resposta Kimi sem GitDiff não pôde ser recuperada: arquivo fora do escopo permitido.";
                Log(log, "[KIMI-RECOVERY] Recuperação bloqueada: " + recovery.ErrorMessage);
                return false;
            }

            string[] lines = SplitLines(File.ReadAllText(fullPath));
            int start = matches[0];
            int before = Math.Min(3, start);
            int after = Math.Min(3, Math.Max(0, lines.Length - (start + block.Count)));

            var searchLines = new List<string>();
            var replaceLines = new List<string>();

            for (int i = start - before; i < start; i++)
            {
                searchLines.Add(lines[i]);
                replaceLines.Add(lines[i]);
            }

            foreach (string line in block)
                searchLines.Add(line);

            for (int i = start + block.Count; i < start + block.Count + after; i++)
            {
                searchLines.Add(lines[i]);
                replaceLines.Add(lines[i]);
            }

            string searchBlock = JoinLines(searchLines);
            string replaceBlock = JoinLines(replaceLines);
            if (string.Equals(searchBlock, replaceBlock, StringComparison.Ordinal))
            {
                recovery.ErrorMessage = "Resposta Kimi sem GitDiff não pôde ser recuperada: patch sem efeito.";
                Log(log, "[KIMI-RECOVERY] Recuperação bloqueada: " + recovery.ErrorMessage);
                return false;
            }

            string caminhoRelativo = MakeRelativePath(projectRoot, fullPath);
            string caminhoAcao = string.IsNullOrWhiteSpace(caminhoRelativo) ? fullPath : caminhoRelativo;
            string protocolText = BuildProtocolText(caminhoAcao, searchBlock, replaceBlock);
            var data = new JObject
            {
                ["protocolo"] = protocolText,
                ["__canonical"] = true,
                ["__source"] = "KimiDialectRecovery",
                ["isDeleteOnly"] = true,
                ["__pathCorrected"] = !string.Equals(fullPath, ResolveSafeFullPath(projectRoot, caminhoOriginal), StringComparison.OrdinalIgnoreCase),
                ["originalPath"] = caminhoOriginal,
                ["correctedPath"] = caminhoAcao,
                ["filePath"] = caminhoAcao,
                ["searchBlock"] = searchBlock,
                ["replaceBlock"] = replaceBlock
            };

            var action = new AgentAction
            {
                Tipo = AgentActionType.ArquivoLocal,
                Descricao = "Kimi recuperado: " + caminhoAcao,
                Dados = data,
                RequerConfirmacao = false
            };

            recovery.Success = true;
            recovery.FilePath = caminhoAcao;
            recovery.OriginalPath = caminhoOriginal;
            recovery.CorrectedPath = caminhoAcao;
            recovery.PathCorrected = !string.Equals(fullPath, ResolveSafeFullPath(projectRoot, caminhoOriginal), StringComparison.OrdinalIgnoreCase);
            recovery.ExtractedContent = conteudo;
            recovery.Occurrences = matches.Count;
            recovery.Action = action;
            recovery.Response = new AgentResponse
            {
                MensagemUsuario = string.Empty,
                Explicacao = string.Empty,
                RequerConfirmacao = false,
                Acoes = new List<AgentAction> { action }
            };

            Log(log, "[KIMI-RECOVERY] Ação delete-only canônica criada.");
            return true;
        }

        private static List<string> BuscarArquivosPermitidosComOcorrencia(
            string projectRoot,
            string rawResponse,
            List<string> block,
            string caminhoOriginal,
            Action<string> log,
            out Dictionary<string, List<int>> matchesPorArquivo)
        {
            matchesPorArquivo = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var arquivos = new List<string>();
            if (string.IsNullOrWhiteSpace(projectRoot) || block == null || block.Count == 0)
                return arquivos;

            var candidatos = new List<string>();
            string caminhoOriginalNormalizado = ResolveSafeFullPath(projectRoot, caminhoOriginal);
            if (!string.IsNullOrWhiteSpace(caminhoOriginalNormalizado) && File.Exists(caminhoOriginalNormalizado))
                candidatos.Add(caminhoOriginalNormalizado);

            foreach (string path in ExtractCandidatePaths(rawResponse, projectRoot))
            {
                if (!string.IsNullOrWhiteSpace(path) && !ContainsPath(candidatos, path))
                    candidatos.Add(path);
            }

            foreach (string path in EnumerateAllowedFiles(projectRoot))
            {
                if (!ContainsPath(candidatos, path))
                    candidatos.Add(path);
            }

            foreach (string path in candidatos)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !IsAllowedTextFile(path))
                    continue;

                string[] lines = SplitLines(File.ReadAllText(path));
                var matches = FindExactBlockMatches(lines, block);
                if (matches.Count == 0)
                    continue;

                matchesPorArquivo[path] = matches;
                arquivos.Add(path);
                if (matches.Count > 1)
                    break;
            }

            return arquivos;
        }

        private static IEnumerable<string> ExtractCandidatePaths(string rawResponse, string projectRoot)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(rawResponse) || string.IsNullOrWhiteSpace(projectRoot))
                return result;

            string pattern = @"(?<path>(?:[A-Za-z]:[\\/]|\.{1,2}[\\/]|[A-Za-z0-9_\-]+[\\/])?[A-Za-z0-9_\-\.\\/]+?\.(?:cs|php|js|ts|css|html|blade\.php|json|xml|txt|md|yml|yaml|env\.example))";
            foreach (Match match in Regex.Matches(rawResponse, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                if (!match.Success)
                    continue;

                string candidate = match.Groups["path"].Value;
                string safe = ResolveSafeFullPath(projectRoot, candidate);
                if (!string.IsNullOrWhiteSpace(safe) && File.Exists(safe))
                    result.Add(safe);
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool ContainsPath(IEnumerable<string> paths, string candidate)
        {
            if (paths == null || string.IsNullOrWhiteSpace(candidate))
                return false;

            return paths.Any(path => string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> EnumerateAllowedFiles(string projectRoot)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
                return result;

            try
            {
                foreach (string file in Directory.EnumerateFiles(projectRoot, "*.*", SearchOption.AllDirectories))
                {
                    if (IsIgnoredPath(file))
                        continue;
                    if (!IsAllowedTextFile(file))
                        continue;
                    result.Add(file);
                }
            }
            catch
            {
            }

            return result;
        }

        private static bool IsIgnoredPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return true;

            string normalized = path.Replace('\\', '/').ToLowerInvariant();
            return normalized.Contains("/vendor/") ||
                   normalized.Contains("/node_modules/") ||
                   normalized.Contains("/.git/") ||
                   normalized.Contains("/bin/") ||
                   normalized.Contains("/obj/") ||
                   normalized.Contains("/storage/logs/") ||
                   normalized.Contains("/cache/");
        }

        private static string MakeRelativePath(string projectRoot, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(fullPath))
                return string.Empty;

            try
            {
                string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string normalized = Path.GetFullPath(fullPath);
                if (!normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                string relative = normalized.Substring(root.Length);
                return relative.Replace(Path.DirectorySeparatorChar, '\\');
            }
            catch
            {
            }

            return string.Empty;
        }

        private static bool IsSupportedDialectType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return true;

            string normalized = type.Trim();
            return normalized.IndexOf("SEARCH_BLOCK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("REMOVE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("DELETE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("DELETE_ONLY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("APAGAR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("REMOVER", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractSingleValue(string rawResponse, IEnumerable<string> keys, out int count)
        {
            count = 0;
            if (string.IsNullOrWhiteSpace(rawResponse) || keys == null)
                return string.Empty;

            var matches = new List<string>();
            foreach (string key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"";
                foreach (Match match in Regex.Matches(rawResponse, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    if (!match.Success)
                        continue;

                    string value = DecodeText(match.Groups["value"].Value);
                    if (!string.IsNullOrWhiteSpace(value))
                        matches.Add(value);
                }
            }

            if (matches.Count == 0)
                matches.AddRange(ExtractLooseValues(rawResponse, keys));

            count = matches.Count;
            if (matches.Count == 0)
                return string.Empty;

            return matches[0];
        }

        private static IEnumerable<string> ExtractLooseValues(string rawResponse, IEnumerable<string> keys)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(rawResponse) || keys == null)
                return result;

            string[] lines = SplitLines(rawResponse);
            bool capturingBlock = false;
            var block = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] ?? string.Empty;
                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    if (capturingBlock && block.Length > 0)
                    {
                        AddLooseValue(result, block.ToString());
                        block.Clear();
                        capturingBlock = false;
                    }

                    continue;
                }

                if (TryExtractLooseInlineValue(trimmed, keys, out string inlineValue))
                {
                    AddLooseValue(result, inlineValue);
                    continue;
                }

                if (!capturingBlock && IsLooseKeyHeader(trimmed, keys))
                {
                    capturingBlock = true;
                    block.Clear();
                    continue;
                }

                if (capturingBlock)
                {
                    if (IsLooseBoundary(trimmed, keys))
                    {
                        if (block.Length > 0)
                            AddLooseValue(result, block.ToString());
                        block.Clear();
                        capturingBlock = false;
                        i--;
                        continue;
                    }

                    block.AppendLine(line);
                    continue;
                }

                if (LooksLikeLoosePathLine(trimmed))
                    AddLooseValue(result, trimmed.TrimEnd(':'));
            }

            if (capturingBlock && block.Length > 0)
                AddLooseValue(result, block.ToString());

            return result;
        }

        private static bool TryExtractLooseInlineValue(string line, IEnumerable<string> keys, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(line) || keys == null)
                return false;

            foreach (string key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                int keyIndex = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (keyIndex < 0)
                    continue;

                int separatorIndex = line.IndexOf(':', keyIndex + key.Length);
                if (separatorIndex < 0)
                    separatorIndex = line.IndexOf('=', keyIndex + key.Length);
                if (separatorIndex < 0)
                    continue;

                string candidate = line.Substring(separatorIndex + 1).Trim();
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                candidate = TrimLooseQuotes(candidate);
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                value = candidate;
                return true;
            }

            return false;
        }

        private static bool IsLooseKeyHeader(string line, IEnumerable<string> keys)
        {
            if (string.IsNullOrWhiteSpace(line) || keys == null)
                return false;

            foreach (string key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key) &&
                    line.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool IsLooseBoundary(string line, IEnumerable<string> keys)
        {
            if (string.IsNullOrWhiteSpace(line))
                return true;

            if (IsLooseKeyHeader(line, keys))
                return true;

            return LooksLikeLoosePathLine(line);
        }

        private static bool LooksLikeLoosePathLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string trimmed = line.Trim().TrimEnd(':');
            return Regex.IsMatch(
                trimmed,
                @"^(?:[A-Za-z]:[\\/]|\.{1,2}[\\/]|[A-Za-z0-9_\-]+[\\/])?[A-Za-z0-9_\-\.\\/]+?\.(?:cs|php|js|ts|css|html|blade\.php|json|xml|txt|md|yml|yaml|env\.example)$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        private static string TrimLooseQuotes(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim().Trim('"').Trim();
        }

        private static void AddLooseValue(ICollection<string> values, string candidate)
        {
            if (values == null || string.IsNullOrWhiteSpace(candidate))
                return;

            string normalized = candidate.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            if (!values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                values.Add(normalized);
        }

        private static string DecodeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            try
            {
                return JsonConvert.DeserializeObject<string>("\"" + value.Replace("\"", "\\\"") + "\"") ?? value;
            }
            catch
            {
            }

            try
            {
                return Regex.Unescape(value);
            }
            catch
            {
            }

            return value;
        }

        private static string ResolveSafeFullPath(string projectRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string combined = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(combined))
            {
                string rooted = Path.GetFullPath(combined);
                if (!rooted.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;
                return rooted;
            }

            string full = Path.GetFullPath(Path.Combine(projectRoot, combined));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return full;
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

        private static List<string> SplitBlock(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None)
                .Select(line => line ?? string.Empty)
                .ToList();
        }

        private static string[] SplitLines(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None);
        }

        private static List<int> FindExactBlockMatches(IReadOnlyList<string> lines, IReadOnlyList<string> block)
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

        private static string JoinLines(IEnumerable<string> lines)
        {
            return lines == null ? string.Empty : string.Join("\n", lines);
        }

        private static string BuildProtocolText(string filePath, string searchBlock, string replaceBlock)
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

        private static void Log(Action<string> log, string message)
        {
            if (log != null)
                log(message);
        }
    }
}
