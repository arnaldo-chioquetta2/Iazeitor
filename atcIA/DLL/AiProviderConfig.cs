using System;

namespace GptBolDll
{
    public sealed class AiProviderConfig
    {
        public const string DefaultProviderId = "gemini-default";
        public const string KimiProviderId = "kimi-openai-compatible";
        public const string KimiProviderType = "KimiOpenAICompatible";

        public string Id { get; set; }
        public string Nome { get; set; }
        public string ProviderType { get; set; }
        public string ModelName { get; set; }
        public string ApiKeyConfigName { get; set; }
        public string ApiKey { get; set; }
        public string PromptPath { get; set; }
        public string GitDiffPromptPath { get; set; }
        public int NivelMaximoSuportado { get; set; } = 6;
        public bool Ativo { get; set; } = true;
        public bool UseGitDiff { get; set; } = false;

        public static AiProviderConfig CreateDefaultGemini()
        {
            return new AiProviderConfig
            {
                Id = DefaultProviderId,
                Nome = "Gemini 2.5 Flash",
                ProviderType = "Gemini",
                ModelName = "gemini-2.5-flash",
                ApiKeyConfigName = "GeminiApiKey",
                ApiKey = "",
                PromptPath = string.Empty,
                GitDiffPromptPath = string.Empty,
                NivelMaximoSuportado = 6,
                Ativo = true,
                UseGitDiff = false
            };
        }

        public static AiProviderConfig CreateDefaultOpenAi()
        {
            return new AiProviderConfig
            {
                Id = "openai-gpt",
                Nome = "OpenAI GPT",
                ProviderType = "OpenAI",
                ModelName = "gpt-5.2",
                ApiKeyConfigName = "OpenAiApiKey",
                ApiKey = "",
                PromptPath = string.Empty,
                GitDiffPromptPath = string.Empty,
                NivelMaximoSuportado = 8,
                Ativo = false,
                UseGitDiff = false
            };
        }

        public static AiProviderConfig CreateDefaultGroq()
        {
            return new AiProviderConfig
            {
                Id = "groq-default",
                Nome = "Groq",
                ProviderType = "Groq",
                ModelName = "llama-3.3-70b-versatile",
                ApiKeyConfigName = "GroqApiKey",
                ApiKey = "",
                PromptPath = string.Empty,
                GitDiffPromptPath = string.Empty,
                NivelMaximoSuportado = 6,
                Ativo = false,
                UseGitDiff = false
            };
        }

        public static AiProviderConfig CreateDefaultMistral()
        {
            return new AiProviderConfig
            {
                Id = "mistral-codestral",
                Nome = "Mistral Codestral",
                ProviderType = "Mistral",
                ModelName = "codestral-latest",
                ApiKeyConfigName = "MistralApiKey",
                ApiKey = "",
                PromptPath = string.Empty,
                GitDiffPromptPath = string.Empty,
                NivelMaximoSuportado = 6,
                Ativo = false,
                UseGitDiff = false
            };
        }

        public static AiProviderConfig CreateDefaultDeepSeek()
        {
            return new AiProviderConfig
            {
                Id = "deepseek-default",
                Nome = "DeepSeek",
                ProviderType = "DeepSeek",
                ModelName = "deepseek-chat",
                ApiKeyConfigName = "DeepSeekApiKey",
                ApiKey = "",
                PromptPath = string.Empty,
                GitDiffPromptPath = string.Empty,
                NivelMaximoSuportado = 6,
                Ativo = false,
                UseGitDiff = false
            };
        }

        public static AiProviderConfig CreateDefaultKimi()
        {
            return new AiProviderConfig
            {
                Id = KimiProviderId,
                Nome = "Kimi / OpenAI Compatível",
                ProviderType = KimiProviderType,
                ModelName = "kimi-k2",
                ApiKeyConfigName = "KimiOpenAIApiKey",
                ApiKey = "",
                PromptPath = string.Empty,
                GitDiffPromptPath = string.Empty,
                NivelMaximoSuportado = 6,
                Ativo = false,
                UseGitDiff = false
            };
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Id))
                Id = DefaultProviderId;
            if (string.IsNullOrWhiteSpace(Nome))
                Nome = Id;
            if (string.IsNullOrWhiteSpace(ProviderType))
                ProviderType = "Gemini";
            if (string.IsNullOrWhiteSpace(ModelName))
                ModelName = string.Equals(ProviderType, KimiProviderType, StringComparison.OrdinalIgnoreCase)
                    ? "kimi-k2"
                    : "gemini-2.5-flash";
            if (string.IsNullOrWhiteSpace(ApiKeyConfigName))
                ApiKeyConfigName = string.Equals(ProviderType, KimiProviderType, StringComparison.OrdinalIgnoreCase)
                    ? "KimiOpenAIApiKey"
                    : "GeminiApiKey";
            if (ApiKey == null)
                ApiKey = "";
            if (PromptPath == null)
                PromptPath = string.Empty;
            if (GitDiffPromptPath == null)
                GitDiffPromptPath = string.Empty;

            NivelMaximoSuportado = ClampLevel(NivelMaximoSuportado);
        }

        public static int ClampLevel(int value)
        {
            if (value < 1)
                return 1;
            if (value > 10)
                return 10;
            return value;
        }
    }
}
