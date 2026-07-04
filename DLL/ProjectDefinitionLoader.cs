using System;
using System.IO;
using Newtonsoft.Json;

namespace GptBolDll
{
    public static class ProjectDefinitionLoader
    {
        public static string GetDefaultDefinitionPath(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                return null;

            return Path.Combine(projectRoot, "atcia.project.json");
        }

        public static ProjectDefinition LoadOrDefault(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new ArgumentException("projectRoot vazio.");

            var defPath = GetDefaultDefinitionPath(projectRoot);
            if (defPath == null || !File.Exists(defPath))
                return CreateDefault(projectRoot);

            var json = File.ReadAllText(defPath);
            var def = JsonConvert.DeserializeObject<ProjectDefinition>(json) ?? CreateDefault(projectRoot);

            if (string.IsNullOrWhiteSpace(def.Root))
                def.Root = projectRoot;

            return def;
        }

        public static ProjectDefinition CreateDefault(string projectRoot)
        {
            return new ProjectDefinition
            {
                Name = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Root = projectRoot,
                Include = { "**/*.cs", "**/*.csproj", "**/*.sln", "**/*.config", "**/*.json", "**/*.md", "**/*.txt" },
                Exclude = { "**/bin/**", "**/obj/**", "**/.git/**", "**/.vs/**", "**/packages/**" },
                ContextFiles = { "prompt.txt" },
            };
        }
    }
}
