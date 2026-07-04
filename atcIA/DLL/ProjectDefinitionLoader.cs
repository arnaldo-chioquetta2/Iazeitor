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

            if (def.Include == null) def.Include = new System.Collections.Generic.List<string>();
            if (def.Exclude == null) def.Exclude = new System.Collections.Generic.List<string>();
            if (def.ContextFiles == null) def.ContextFiles = new System.Collections.Generic.List<string>();
            if (def.PrimaryFiles == null) def.PrimaryFiles = new System.Collections.Generic.List<string>();
            if (def.Instructions == null) def.Instructions = new System.Collections.Generic.List<string>();
            if (def.AllowedDosCommands == null) def.AllowedDosCommands = new System.Collections.Generic.List<string>();
            if (def.Credentials == null) def.Credentials = new System.Collections.Generic.List<ProjectCredential>();
            if (def.Database == null) def.Database = new DatabaseProfile();

            return def;
        }

        public static ProjectDefinition CreateDefault(string projectRoot)
        {
            return new ProjectDefinition
            {
                SchemaVersion = 1,
                Name = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Root = projectRoot,
                Description = "Definicao padrao de projeto para o atcIA.",
                Include = { "**/*.cs", "**/*.csproj", "**/*.sln", "**/*.config", "**/*.json", "**/*.md", "**/*.txt", "**/*.php", "**/*.phtml", "**/*.htaccess", "composer.json" },
                Exclude = { "**/bin/**", "**/obj/**", "**/.git/**", "**/.vs/**", "**/packages/**" },
                ContextFiles = { "prompt.txt" },
                PrimaryFiles = { "README.md", "prompt.txt" },
                Credentials = new System.Collections.Generic.List<ProjectCredential>(),
                Database = new DatabaseProfile(),
                Instructions =
                {
                    "Priorize alteracoes pequenas e seguras.",
                    "Sempre considere o contexto do projeto antes de sugerir mudancas.",
                    "Nao use git nem comandos destrutivos.",
                    "Use DOS e FTP somente quando solicitados ou explicitamente uteis."
                },
                AllowedDosCommands =
                {
                    "dir",
                    "type",
                    "copy",
                    "move",
                    "del",
                    "mkdir",
                    "rmdir",
                    "find",
                    "findstr",
                    "where",
                    "echo",
                    "powershell"
                },
            };
        }
    }
}
