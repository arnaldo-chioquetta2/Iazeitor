using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GptBolDll
{
    public static class GitDiffSafetyValidator
    {
        public static GitDiffSafetyResult Validate(GitDiffParseResult diff, string projectRoot)
        {
            var result = new GitDiffSafetyResult
            {
                IsSafe = false,
                FileCount = diff == null || diff.Files == null ? 0 : diff.Files.Count,
                HunkCount = diff == null ? 0 : diff.TotalHunks
            };

            if (diff == null || diff.Files == null || diff.Files.Count == 0)
            {
                result.Errors.Add("Diff vazio.");
                return result;
            }

            if (diff.ContainsOnlyEmptyHunks)
            {
                result.Errors.Add("diff contém apenas hunks vazios.");
                return result;
            }

            if (diff.HasEmptyHunkInMiddle)
            {
                result.Errors.Add("hunk vazio no meio do diff.");
                return result;
            }

            if (diff.Files.Count > 5)
                result.Errors.Add("diff excede limite de arquivos.");
            if (diff.TotalHunks > 20)
                result.Errors.Add("diff excede limite de hunks.");

            int totalAlteradas = 0;
            long totalBytes = 0;
            bool algumHunkSemAlteracao = false;
            int descartadosPorLixo = 0;
            bool existeArquivoValido = false;
            var candidatosDescartaveis = new List<GitDiffFileChange>();
            var errosPendentes = new List<string>();

            foreach (var file in diff.Files)
            {
                if (file == null)
                    continue;

                string effectivePath = file.EffectivePath ?? string.Empty;
                string normalized = NormalizePath(effectivePath);

                if (string.IsNullOrWhiteSpace(effectivePath) ||
                    Path.IsPathRooted(effectivePath) ||
                    normalized.Contains("../") ||
                    normalized.Contains("..\\") ||
                    normalized.StartsWith("/") ||
                    normalized.StartsWith("\\") ||
                    ContainsBlockedSegment(normalized))
                {
                    if (IsEmptyMalformedTail(file))
                    {
                        WriteLog("[GIT-DIFF] Arquivo inválido sem hunks detectado.");
                        candidatosDescartaveis.Add(file);
                        errosPendentes.Add("caminho inseguro: " + effectivePath);
                        continue;
                    }

                    result.Errors.Add("caminho inseguro: " + effectivePath);
                    continue;
                }

                if (file.IsNewFile || file.IsDeletedFile || file.IsRename ||
                    IsDevNull(file.OldPath) || IsDevNull(file.NewPath) ||
                    ContainsRenameMarkers(file) )
                {
                    if (IsEmptyMalformedTail(file))
                    {
                        WriteLog("[GIT-DIFF] Arquivo inválido sem hunks detectado.");
                        candidatosDescartaveis.Add(file);
                        errosPendentes.Add("criação/deleção/rename ainda não suportado.");
                        continue;
                    }

                    result.Errors.Add("criação/deleção/rename ainda não suportado.");
                    continue;
                }

                if (!IsAllowedExtension(effectivePath))
                {
                    if (IsEmptyMalformedTail(file))
                    {
                        WriteLog("[GIT-DIFF] Arquivo inválido sem hunks detectado.");
                        candidatosDescartaveis.Add(file);
                        errosPendentes.Add("extensão não permitida: " + effectivePath);
                        continue;
                    }

                    result.Errors.Add("extensão não permitida: " + effectivePath);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(projectRoot))
                {
                    result.Errors.Add("projectRoot ausente.");
                    continue;
                }

                string fullPath = ResolveFullPath(projectRoot, effectivePath);
                if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                {
                    if (IsEmptyMalformedTail(file))
                    {
                        WriteLog("[GIT-DIFF] Arquivo inválido sem hunks detectado.");
                        candidatosDescartaveis.Add(file);
                        errosPendentes.Add("arquivo do diff não existe no projeto: " + effectivePath);
                        continue;
                    }

                    result.Errors.Add("arquivo do diff não existe no projeto: " + effectivePath);
                    continue;
                }

                if (file.Hunks == null || file.Hunks.Count == 0)
                {
                    if (IsEmptyMalformedTail(file))
                    {
                        WriteLog("[GIT-DIFF] Arquivo inválido sem hunks detectado.");
                        candidatosDescartaveis.Add(file);
                        errosPendentes.Add("arquivo sem hunk.");
                        continue;
                    }

                    result.Errors.Add("arquivo sem hunk.");
                    continue;
                }

                bool arquivoTemMudanca = false;
                foreach (var hunk in file.Hunks)
                {
                    if (hunk == null)
                        continue;

                    int changed = hunk.Lines == null ? 0 : hunk.Lines.Count(l => l != null && (l.Kind == GitDiffLineKind.Added || l.Kind == GitDiffLineKind.Removed));
                    if (changed == 0)
                    {
                        result.Errors.Add("hunk sem alteração real.");
                        algumHunkSemAlteracao = true;
                    }
                    else
                    {
                        arquivoTemMudanca = true;
                    }

                    totalAlteradas += changed;
                }

                totalBytes += EstimateSize(file);

                if (arquivoTemMudanca)
                    existeArquivoValido = true;
            }

            if (candidatosDescartaveis.Count > 0 && existeArquivoValido)
            {
                foreach (var candidato in candidatosDescartaveis)
                {
                    string path = candidato?.EffectivePath ?? string.Empty;
                    WriteLog("[GIT-DIFF] Arquivo inválido sem alterações descartado com segurança: " + path);
                }

                diff.Files.RemoveAll(file => file != null && candidatosDescartaveis.Contains(file));

                descartadosPorLixo = candidatosDescartaveis.Count;
                result.Errors.RemoveAll(e =>
                    errosPendentes.Any(p => string.Equals(p, e, StringComparison.OrdinalIgnoreCase)));
                result.FileCount = diff.Files.Count;
                result.HunkCount = diff.Files.Sum(file => file?.Hunks == null ? 0 : file.Hunks.Count);
                WriteLog("[GIT-DIFF] Arquivos descartados por lixo de saída da IA: " + descartadosPorLixo);
            }

            if (candidatosDescartaveis.Count > 0 && !existeArquivoValido)
            {
                foreach (string erro in errosPendentes)
                {
                    if (!result.Errors.Any(existing => string.Equals(existing, erro, StringComparison.OrdinalIgnoreCase)))
                        result.Errors.Add(erro);
                }
            }

            if (totalAlteradas == 0 && algumHunkSemAlteracao)
            {
                result.Errors.RemoveAll(e => string.Equals(e, "hunk sem alteração real.", StringComparison.OrdinalIgnoreCase));
                result.Errors.Insert(0, "diff contém cabeçalho, mas não contém alterações aplicáveis.");
            }

            if (totalAlteradas > 300)
                result.Errors.Add("diff excede limite de linhas alteradas.");
            if (totalBytes > 120L * 1024L)
                result.Errors.Add("diff excede tamanho máximo.");

            result.IsSafe = result.Errors.Count == 0;
            return result;
        }

        private static bool IsEmptyMalformedTail(GitDiffFileChange file)
        {
            if (file == null)
                return false;

            int removed = file.Hunks == null ? 0 : file.Hunks.Sum(h => h?.Lines == null ? 0 : h.Lines.Count(l => l != null && l.Kind == GitDiffLineKind.Removed));
            int added = file.Hunks == null ? 0 : file.Hunks.Sum(h => h?.Lines == null ? 0 : h.Lines.Count(l => l != null && l.Kind == GitDiffLineKind.Added));
            int context = file.Hunks == null ? 0 : file.Hunks.Sum(h => h?.Lines == null ? 0 : h.Lines.Count(l => l != null && l.Kind == GitDiffLineKind.Context));
            return (file.Hunks == null || file.Hunks.Count == 0) && removed == 0 && added == 0 && context == 0;
        }

        private static void WriteLog(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }

        private static bool ContainsRenameMarkers(GitDiffFileChange file)
        {
            if (file == null)
                return false;

            return file.IsRename;
        }

        private static long EstimateSize(GitDiffFileChange file)
        {
            if (file == null || file.Hunks == null)
                return 0;

            long size = 0;
            foreach (var hunk in file.Hunks)
            {
                if (hunk?.Header != null)
                    size += hunk.Header.Length;
                if (hunk?.Lines != null)
                    size += hunk.Lines.Sum(l => l?.Text == null ? 0 : l.Text.Length + 1);
            }
            return size;
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

        private static bool IsAllowedExtension(string path)
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

        private static bool ContainsBlockedSegment(string normalized)
        {
            string[] blocked =
            {
                "vendor", "node_modules", ".venv", "venv", "bin", "obj", ".git",
                "cache", "storage/logs", "bootstrap/cache"
            };

            return blocked.Any(b => normalized.IndexOf(b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsDevNull(string path)
        {
            return string.Equals(path, "/dev/null", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string text)
        {
            return (text ?? string.Empty).Replace('\\', '/').Trim();
        }
    }
}
