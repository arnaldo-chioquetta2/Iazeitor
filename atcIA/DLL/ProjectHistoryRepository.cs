using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace GptBolDll
{
    public static class ProjectHistoryRepository
    {
        public static string GetHistoryDirectory()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dados", "historico");
        }

        public static string GetHistoryFile(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                throw new ArgumentException("projectId vazio.");

            return Path.Combine(GetHistoryDirectory(), MakeSafeFileName(projectId) + ".json");
        }

        public static ProjectHistory Load(string projectId, string projectName)
        {
            Directory.CreateDirectory(GetHistoryDirectory());

            var file = GetHistoryFile(projectId);
            if (!File.Exists(file))
            {
                return new ProjectHistory
                {
                    ProjectId = Guid.TryParse(projectId, out Guid id) ? id : Guid.Empty,
                    ProjectName = projectName ?? string.Empty
                };
            }

            try
            {
                var json = File.ReadAllText(file);
                var history = JsonConvert.DeserializeObject<ProjectHistory>(json) ?? new ProjectHistory();
                if (history.Entries == null)
                    history.Entries = new List<ProjectHistoryEntry>();
                if (string.IsNullOrWhiteSpace(history.ProjectName))
                    history.ProjectName = projectName ?? string.Empty;
                return history;
            }
            catch
            {
                return new ProjectHistory
                {
                    ProjectId = Guid.TryParse(projectId, out Guid id) ? id : Guid.Empty,
                    ProjectName = projectName ?? string.Empty
                };
            }
        }

        public static void Append(string projectId, string projectName, ProjectHistoryEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            var history = Load(projectId, projectName);
            history.ProjectName = projectName ?? history.ProjectName;
            history.Entries.Add(entry);

            Directory.CreateDirectory(GetHistoryDirectory());
            File.WriteAllText(GetHistoryFile(projectId), JsonConvert.SerializeObject(history, Formatting.Indented));
        }

        private static string MakeSafeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();

            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalidChars, chars[i]) >= 0)
                    chars[i] = '_';
            }

            return new string(chars);
        }
    }
}
