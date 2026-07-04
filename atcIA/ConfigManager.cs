using Newtonsoft.Json;
using System.IO;

namespace atcIA
{
    public class AppConfig
    {
        public string GeminiApiKey { get; set; }
        public string GeminiModel { get; set; }
        public string PromptFile { get; set; }
        public string AnalystPromptPath { get; set; }
        public string ComplementPromptPath { get; set; }
        public string KimiOpenAIBaseUrl { get; set; }
        public string KimiOpenAIApiKey { get; set; }
        public string KimiOpenAIModel { get; set; }
        public bool KimiEnableSearch { get; set; } = true;
        public bool KimiEnableThinking { get; set; } = true;
        public string ProjectRoot { get; set; }
        public string LastProjectId { get; set; }
        public bool LogVisible { get; set; } = true;
        public int MaxPromptChars { get; set; } = 30000;
    }

    public static class ConfigManager
    {
        static ConfigManager()
        {
            AppPaths.GarantirPastaDados();
            AppPaths.MigrarArquivoLegadoSeNecessario("config.json");
        }

        private static AppConfig LoadConfig()
        {
            if (!File.Exists(AppPaths.ConfigFile))
                return new AppConfig();

            try
            {
                var json = File.ReadAllText(AppPaths.ConfigFile);
                return JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        private static void SaveConfig(AppConfig config)
        {
            AppPaths.GarantirPastaDados();
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(AppPaths.ConfigFile, json);
        }

        public static string GetApiKey()
        {
            return LoadConfig().GeminiApiKey;
        }

        public static void SaveApiKey(string apiKey)
        {
            var config = LoadConfig();
            config.GeminiApiKey = apiKey;
            SaveConfig(config);
        }

        public static string GetGeminiModel()
        {
            return LoadConfig().GeminiModel;
        }

        public static void SaveGeminiModel(string model)
        {
            var config = LoadConfig();
            config.GeminiModel = model;
            SaveConfig(config);
        }

        public static string GetPromptFile()
        {
            return LoadConfig().PromptFile;
        }

        public static void SavePromptFile(string promptFile)
        {
            var config = LoadConfig();
            config.PromptFile = promptFile;
            SaveConfig(config);
        }

        public static string GetAnalystPromptPath()
        {
            return LoadConfig().AnalystPromptPath;
        }

        public static void SaveAnalystPromptPath(string promptFile)
        {
            var config = LoadConfig();
            config.AnalystPromptPath = promptFile;
            SaveConfig(config);
        }

        public static string GetComplementPromptPath()
        {
            return LoadConfig().ComplementPromptPath;
        }

        public static void SaveComplementPromptPath(string promptFile)
        {
            var config = LoadConfig();
            config.ComplementPromptPath = promptFile;
            SaveConfig(config);
        }

        public static string GetKimiOpenAIBaseUrl()
        {
            return string.IsNullOrWhiteSpace(LoadConfig().KimiOpenAIBaseUrl)
                ? "https://servidorapi.duckdns.org/v1"
                : LoadConfig().KimiOpenAIBaseUrl;
        }

        public static void SaveKimiOpenAIBaseUrl(string baseUrl)
        {
            var config = LoadConfig();
            config.KimiOpenAIBaseUrl = baseUrl;
            SaveConfig(config);
        }

        public static string GetKimiOpenAIApiKey()
        {
            return LoadConfig().KimiOpenAIApiKey ?? string.Empty;
        }

        public static void SaveKimiOpenAIApiKey(string apiKey)
        {
            var config = LoadConfig();
            config.KimiOpenAIApiKey = apiKey;
            SaveConfig(config);
        }

        public static string GetKimiOpenAIModel()
        {
            return string.IsNullOrWhiteSpace(LoadConfig().KimiOpenAIModel)
                ? "kimi-k2"
                : LoadConfig().KimiOpenAIModel;
        }

        public static void SaveKimiOpenAIModel(string model)
        {
            var config = LoadConfig();
            config.KimiOpenAIModel = model;
            SaveConfig(config);
        }

        public static bool GetKimiEnableSearch()
        {
            return LoadConfig().KimiEnableSearch;
        }

        public static void SaveKimiEnableSearch(bool enabled)
        {
            var config = LoadConfig();
            config.KimiEnableSearch = enabled;
            SaveConfig(config);
        }

        public static bool GetKimiEnableThinking()
        {
            return LoadConfig().KimiEnableThinking;
        }

        public static void SaveKimiEnableThinking(bool enabled)
        {
            var config = LoadConfig();
            config.KimiEnableThinking = enabled;
            SaveConfig(config);
        }

        public static string GetProjectRoot()
        {
            return LoadConfig().ProjectRoot;
        }

        public static void SaveProjectRoot(string projectRoot)
        {
            var config = LoadConfig();
            config.ProjectRoot = projectRoot;
            SaveConfig(config);
        }

        public static string GetLastProjectId()
        {
            return LoadConfig().LastProjectId;
        }

        public static void SaveLastProjectId(string projectId)
        {
            var config = LoadConfig();
            config.LastProjectId = projectId;
            SaveConfig(config);
        }

        public static bool GetLogVisible()
        {
            return LoadConfig().LogVisible;
        }

        public static void SaveLogVisible(bool visible)
        {
            var config = LoadConfig();
            config.LogVisible = visible;
            SaveConfig(config);
        }

        public static int GetMaxPromptChars()
        {
            var value = LoadConfig().MaxPromptChars;
            if (value < 12000)
                return 12000;
            if (value > 200000)
                return 200000;
            return value;
        }

        public static void SaveMaxPromptChars(int maxPromptChars)
        {
            var config = LoadConfig();
            if (maxPromptChars < 12000)
                maxPromptChars = 12000;
            if (maxPromptChars > 200000)
                maxPromptChars = 200000;
            config.MaxPromptChars = maxPromptChars;
            SaveConfig(config);
        }
    }
}
