using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GptBolDll
{
    public sealed class ProjectContextBuilder : IContextBuilder
    {
        public string Build(string projectRoot)
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
            if (!string.IsNullOrWhiteSpace(definition.Description))
                sb.AppendLine("Descricao: " + definition.Description);
            sb.AppendLine("SchemaVersion: " + definition.SchemaVersion);
            sb.AppendLine("Raiz: " + projectRoot);

            var csprojFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.AllDirectories)
                .Select(path => MakeRelative(projectRoot, path))
                .ToList();

            if (csprojFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[PROJETO C#]");
                sb.AppendLine("Arquivo(s) de projeto: " + string.Join(", ", csprojFiles));
                sb.AppendLine("Regra: se criar arquivo novo de codigo, atualize o .csproj correspondente quando o projeto nao usar inclusao automatica.");

                foreach (var csproj in csprojFiles.Take(2))
                {
                    var fullPath = Path.Combine(projectRoot, csproj);
                    if (!File.Exists(fullPath))
                        continue;

                    var content = ReadTextLimited(fullPath, 12000);
                    if (string.IsNullOrWhiteSpace(content))
                        continue;

                    sb.AppendLine();
                    sb.AppendLine($"--- CSPROJ: {csproj} ---");
                    sb.AppendLine(content);
                    sb.AppendLine($"--- END CSPROJ: {csproj} ---");
                }
            }

            if (definition.Instructions != null && definition.Instructions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[DIRETRIZES]");
                foreach (var instruction in definition.Instructions)
                {
                    if (!string.IsNullOrWhiteSpace(instruction))
                        sb.AppendLine("- " + instruction.Trim());
                }
            }

            if (definition.AllowedDosCommands != null && definition.AllowedDosCommands.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[DOS PERMITIDOS]");
                sb.AppendLine(string.Join(", ", definition.AllowedDosCommands.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)));
            }

            var allFiles = EnumerateFilesSafe(projectRoot);

            var selected = FilterByGlobs(allFiles, projectRoot, definition.Include, includeByDefault: true);
            selected = FilterByGlobs(selected, projectRoot, definition.Exclude, includeByDefault: false);

            var legacyCompileIncludes = BuildLegacyCompileIncludeSet(projectRoot, csprojFiles);
            if (legacyCompileIncludes.Count > 0)
            {
                selected = selected.Where(path => ShouldIncludeFileInLegacyProject(path, projectRoot, legacyCompileIncludes));

                sb.AppendLine();
                sb.AppendLine("[FILTRO C# LEGADO]");
                sb.AppendLine("Arquivos .cs fora de <Compile Include=...> foram omitidos do contexto para evitar uso de codigo nao compilado.");
            }

            // prioriza ContextFiles e PrimaryFiles
            var prioritized = new List<string>();
            var priorityFiles = new List<string>();
            priorityFiles.AddRange(definition.ContextFiles ?? new List<string>());
            priorityFiles.AddRange(definition.PrimaryFiles ?? new List<string>());
            foreach (var rel in priorityFiles.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
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
                if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(fullPath))
                    return fullPath;

                var rootFull = Path.GetFullPath(root);
                if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString()) && !rootFull.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                    rootFull += Path.DirectorySeparatorChar;

                var fileFull = Path.GetFullPath(fullPath);
                var rootUri = new Uri(rootFull, UriKind.Absolute);
                var fileUri = new Uri(fileFull, UriKind.Absolute);
                var relUri = rootUri.MakeRelativeUri(fileUri);
                var rel = Uri.UnescapeDataString(relUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
                return rel;
            }
            catch
            {
                return fullPath;
            }
        }

        private static string ReadTextLimited(string path, int maxChars)
        {
            try
            {
                if (maxChars <= 0)
                    return string.Empty;

                var text = File.ReadAllText(path);
                if (string.IsNullOrEmpty(text))
                    return string.Empty;

                if (text.Length <= maxChars)
                    return text;

                return text.Substring(0, maxChars) + Environment.NewLine + "<... truncado ...>";
            }
            catch
            {
                return string.Empty;
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

        private static HashSet<string> BuildLegacyCompileIncludeSet(string projectRoot, List<string> csprojFiles)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(projectRoot) || csprojFiles == null || csprojFiles.Count == 0)
                return result;

            foreach (var csproj in csprojFiles)
            {
                var csprojPath = Path.Combine(projectRoot, csproj);
                if (!File.Exists(csprojPath))
                    continue;

                string projectText;
                try { projectText = File.ReadAllText(csprojPath); }
                catch { continue; }

                if (IsSdkStyleProject(projectText))
                    continue;

                var csprojDir = Path.GetDirectoryName(csprojPath) ?? projectRoot;
                foreach (Match match in Regex.Matches(projectText, "<Compile\\s+Include\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase))
                {
                    if (!match.Success || match.Groups.Count < 2)
                        continue;

                    var include = match.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(include))
                        continue;

                    var fullPath = Path.GetFullPath(Path.Combine(csprojDir, include));
                    if (!IsUnder(fullPath, projectRoot))
                        continue;

                    result.Add(NormalizePath(MakeRelative(projectRoot, fullPath)));
                }
            }

            return result;
        }

        private static bool ShouldIncludeFileInLegacyProject(string path, string projectRoot, HashSet<string> compileIncludes)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (!string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
                return true;

            var relative = NormalizePath(MakeRelative(projectRoot, path));
            return compileIncludes.Contains(relative);
        }

        private static bool IsSdkStyleProject(string projectText)
        {
            if (string.IsNullOrWhiteSpace(projectText))
                return false;

            return projectText.IndexOf("<Project Sdk=", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
