using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace atcIA
{
    public sealed class VersionIncrementResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; }
        public string OldVersion { get; set; }
        public string NewVersion { get; set; }
        public string Message { get; set; }
    }

    public static class ProjectVersionIncrementer
    {
        public static VersionIncrementResult Incrementar(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            {
                return new VersionIncrementResult
                {
                    Success = false,
                    Message = "Pasta do projeto destino invalida."
                };
            }

            var assemblyInfo = Directory
                .GetFiles(projectRoot, "AssemblyInfo.cs", SearchOption.AllDirectories)
                .FirstOrDefault(p => !EhPastaIgnorada(p));

            if (!string.IsNullOrWhiteSpace(assemblyInfo))
            {
                var result = IncrementarAssemblyInfo(assemblyInfo);
                if (result.Success)
                    return result;
            }

            var csproj = Directory
                .GetFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(csproj))
            {
                var result = IncrementarCsproj(csproj);
                if (result.Success)
                    return result;
            }

            return new VersionIncrementResult
            {
                Success = false,
                Message = "Nenhum AssemblyInfo.cs ou .csproj com versao foi encontrado."
            };
        }

        private static VersionIncrementResult IncrementarAssemblyInfo(string filePath)
        {
            string text = File.ReadAllText(filePath);

            var regex = new Regex(
                @"\[assembly:\s*Assembly(?<kind>Version|FileVersion|InformationalVersion)\s*\(\s*""(?<version>\d+(?:\.\d+){1,3})(?<suffix>[^""]*)""\s*\)\s*\]",
                RegexOptions.IgnoreCase);

            var matches = regex.Matches(text);
            if (matches.Count == 0)
            {
                return new VersionIncrementResult
                {
                    Success = false,
                    FilePath = filePath,
                    Message = "AssemblyInfo.cs encontrado, mas sem versao."
                };
            }

            Match baseMatch = matches
                .Cast<Match>()
                .FirstOrDefault(m => string.Equals(m.Groups["kind"].Value, "FileVersion", StringComparison.OrdinalIgnoreCase))
                ?? matches.Cast<Match>().First();

            string oldVersion = baseMatch.Groups["version"].Value;
            string newVersion = IncrementarVersao(oldVersion);

            string newText = regex.Replace(text, m =>
            {
                return SubstituirGrupo(m, "version", newVersion);
            });

            if (newText == text)
            {
                return new VersionIncrementResult
                {
                    Success = false,
                    FilePath = filePath,
                    Message = "Nenhuma alteracao de versao aplicada."
                };
            }

            File.WriteAllText(filePath, newText);

            return new VersionIncrementResult
            {
                Success = true,
                FilePath = filePath,
                OldVersion = oldVersion,
                NewVersion = newVersion,
                Message = "Versao incrementada em AssemblyInfo.cs."
            };
        }

        private static VersionIncrementResult IncrementarCsproj(string filePath)
        {
            string text = File.ReadAllText(filePath);

            var regex = new Regex(
                @"<(?<tag>Version|AssemblyVersion|FileVersion|InformationalVersion)>(?<version>\d+(?:\.\d+){1,3})(?<suffix>[^<]*)</\k<tag>>",
                RegexOptions.IgnoreCase);

            var matches = regex.Matches(text);
            if (matches.Count == 0)
            {
                return new VersionIncrementResult
                {
                    Success = false,
                    FilePath = filePath,
                    Message = ".csproj encontrado, mas sem tag de versao."
                };
            }

            Match baseMatch = matches
                .Cast<Match>()
                .FirstOrDefault(m => string.Equals(m.Groups["tag"].Value, "FileVersion", StringComparison.OrdinalIgnoreCase))
                ?? matches.Cast<Match>().First();

            string oldVersion = baseMatch.Groups["version"].Value;
            string newVersion = IncrementarVersao(oldVersion);

            string newText = regex.Replace(text, m =>
            {
                return SubstituirGrupo(m, "version", newVersion);
            });

            if (newText == text)
            {
                return new VersionIncrementResult
                {
                    Success = false,
                    FilePath = filePath,
                    Message = "Nenhuma alteracao de versao aplicada."
                };
            }

            File.WriteAllText(filePath, newText);

            return new VersionIncrementResult
            {
                Success = true,
                FilePath = filePath,
                OldVersion = oldVersion,
                NewVersion = newVersion,
                Message = "Versao incrementada no .csproj."
            };
        }

        private static string IncrementarVersao(string version)
        {
            var partes = version.Split('.')
                .Select(p =>
                {
                    int n;
                    return int.TryParse(p, out n) ? n : 0;
                })
                .ToList();

            while (partes.Count < 3)
                partes.Add(0);

            int tamanhoOriginal = partes.Count;

            partes[2]++;

            if (partes[2] > 9)
            {
                partes[2] = 0;
                partes[1]++;
            }

            if (partes[1] > 9)
            {
                partes[1] = 0;
                partes[0]++;
            }

            for (int i = 3; i < partes.Count; i++)
                partes[i] = 0;

            return string.Join(".", partes.Take(tamanhoOriginal));
        }

        private static string SubstituirGrupo(Match match, string groupName, string novoValor)
        {
            Group group = match.Groups[groupName];

            int inicioRelativo = group.Index - match.Index;
            string antes = match.Value.Substring(0, inicioRelativo);
            string depois = match.Value.Substring(inicioRelativo + group.Length);

            return antes + novoValor + depois;
        }

        private static bool EhPastaIgnorada(string path)
        {
            string normalizado = path.Replace('/', '\\');

            return normalizado.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizado.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizado.IndexOf("\\.git\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizado.IndexOf("\\.vs\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizado.IndexOf("\\packages\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}