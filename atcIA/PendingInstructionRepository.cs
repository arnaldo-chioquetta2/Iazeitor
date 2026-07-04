using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace atcIA
{
    public static class PendingInstructionRepository
    {
        private static string FilePath
        {
            get { return Path.Combine(AppPaths.DadosDir, "instrucoes_pendentes.json"); }
        }

        public static string Load(Guid projectId)
        {
            if (projectId == Guid.Empty)
                return string.Empty;

            var all = LoadAll();
            return all.TryGetValue(projectId.ToString(), out string text) ? text ?? string.Empty : string.Empty;
        }

        public static void Save(Guid projectId, string instruction)
        {
            if (projectId == Guid.Empty)
                return;

            var all = LoadAll();
            if (string.IsNullOrWhiteSpace(instruction))
                all.Remove(projectId.ToString());
            else
                all[projectId.ToString()] = instruction;

            SaveAll(all);
        }

        public static void Clear(Guid projectId)
        {
            Save(projectId, string.Empty);
        }

        private static Dictionary<string, string> LoadAll()
        {
            AppPaths.GarantirPastaDados();
            if (!File.Exists(FilePath))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var json = File.ReadAllText(FilePath);
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SaveAll(Dictionary<string, string> all)
        {
            AppPaths.GarantirPastaDados();
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(all, Formatting.Indented));
        }
    }
}
