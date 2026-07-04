using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GptBolDll
{
    public static class ProjectContextBuilder
    {
        public static string Build(string projectRoot)
        {
            var def = ProjectDefinitionLoader.LoadOrDefault(projectRoot);
            return Build(def);
        }

        public static string Build(ProjectDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var projectRoot = definition.Root;
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
                return "[CONTEXTO] Projeto inválido (pasta não encontrada).";

            var sb = new StringBuilder();
            sb.AppendLine("[CONTEXTO DO PROJETO]");
            sb.AppendLine("Nome: " + (definition.Name ?? ""));
            sb.AppendLine("Raiz: " + projectRoot);

            var allFiles = EnumerateFilesSafe(projectRoot);

            var selected = FilterByGlobs(allFiles, projectRoot, definition.Include, includeByDefault: true);
            selected = FilterByGlobs(selected, projectRoot, definition.Exclude, includeByDefault: false);

            // prioriza ContextFiles
            var prioritized = new List<string>();
            var cf = (definition.ContextFiles ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            foreach (var rel in cf)
            {
                var full = Path.GetFullPath(Path.Combine(projectRoot, rel));
                if (File.Exists(full) && IsUnder(full, projectRoot))
                    prioritized.Add(full);
            }

            // junta sem duplicar
            var distinct = new List<string>();
            foreach (var p in prioritized.Concat(selected).Distinct(StringComparer.OrdinalIgnoreCase))
                distinct.Add(p);

            // limita quantidade e bytes
            int totalBytes = 0;
            int addedFiles = 0;

            sb.AppendLine();
            sb.AppendLine("[ARQUIVOS INCLUÍDOS (parcial)]");

            foreach (var path in distinct)
            {
                if (addedFiles >= definition.MaxFiles)
                    break;

                FileInfo fi;
                try { fi = new FileInfo(path); }
                catch { continue; }

                if (!fi.Exists)
                    continue;

                if (fi.Length <= 0)
                    continue;

                if (fi.Length > definition.MaxFileBytes)
                    continue;

                if (totalBytes + fi.Length > definition.MaxTotalBytes)
                    break;

                string content;
                try { content = File.ReadAllText(path); }
                catch { continue; }

                totalBytes += (int)fi.Length;
                addedFiles++;

                var relPath = MakeRelative(projectRoot, path);
                sb.AppendLine();
                sb.AppendLine($"--- FILE: {relPath} ---");
                sb.AppendLine(content);
                sb.AppendLine($"--- END FILE: {relPath} ---");
            }

            if (addedFiles == 0)
            {
                sb.AppendLine();
                sb.AppendLine("(Nenhum arquivo incluído pelo filtro; ajuste `atcia.project.json`.)");
            }

            return sb.ToString();
        }

        private static IEnumerable<string> EnumerateFilesSafe(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] subDirs;
                try { subDirs = Directory.GetDirectories(dir); }
                catch { continue; }

                foreach (var sd in subDirs)
                    stack.Push(sd);

                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch { continue; }

                foreach (var f in files)
                    yield return f;
            }
        }

        private static IEnumerable<string> FilterByGlobs(IEnumerable<string> files, string root, List<string> globs, bool includeByDefault)
        {
            if (files == null) return Enumerable.Empty<string>();

            var patterns = (globs ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeGlob)
                .ToList();

            if (patterns.Count == 0)
                return includeByDefault ? files : Enumerable.Empty<string>();

            return files.Where(f =>
            {
                var rel = NormalizePath(MakeRelative(root, f));
                bool match = patterns.Any(p => GlobMatch(rel, p));
                return includeByDefault ? match : !match;
            });
        }

        private static string NormalizeGlob(string glob) => NormalizePath(glob.Trim());

        private static string NormalizePath(string p) => (p ?? "").Replace('\\', '/');

        private static bool GlobMatch(string text, string pattern)
        {
            // glob bem simples: suporta **, *, ?
            text = text ?? "";
            pattern = pattern ?? "";

            int ti = 0, pi = 0;
            int starText = -1, starPat = -1;

            while (ti < text.Length)
            {
                if (pi < pattern.Length)
                {
                    // ** => pode atravessar diretórios
                    if (pattern[pi] == '*' && pi + 1 < pattern.Length && pattern[pi + 1] == '*')
                    {
                        // consome todos os * consecutivos
                        while (pi < pattern.Length && pattern[pi] == '*') pi++;
                        starPat = pi;
                        starText = ti;
                        continue;
                    }

                    if (pattern[pi] == '*')
                    {
                        pi++;
                        starPat = pi;
                        starText = ti;
                        continue;
                    }

                    if (pattern[pi] == '?' || pattern[pi] == text[ti])
                    {
                        pi++;
                        ti++;
                        continue;
                    }
                }

                if (starPat != -1)
                {
                    pi = starPat;
                    ti = ++starText;
                    continue;
                }

                return false;
            }

            while (pi < pattern.Length && pattern[pi] == '*')
                pi++;

            return pi == pattern.Length;
        }

        private static string MakeRelative(string root, string fullPath)
        {
            try
            {
                var rel = Path.GetRelativePath(root, fullPath);
                return rel;
            }
            catch
            {
                return fullPath;
            }
        }

        private static bool IsUnder(string fullPath, string root)
        {
            try
            {
                var full = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return full.StartsWith(r, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
