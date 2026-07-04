using GptBolDll;
using System;

namespace atcIA
{
    public static class AgentModelClientFactory
    {
        public static IAgentModelClient Create(AiProviderConfig providerConfig, Func<string, string> apiKeyProvider, Action<string> statusReporter = null)
        {
            if (providerConfig == null)
                throw new ArgumentNullException(nameof(providerConfig));

            providerConfig.Normalize();

            if (!providerConfig.Ativo)
                throw new InvalidOperationException("IA configurada esta inativa: " + providerConfig.Nome);

            if (string.Equals(providerConfig.ProviderType, "Gemini", StringComparison.OrdinalIgnoreCase))
            {
                return new GeminiClient(
                    () => !string.IsNullOrWhiteSpace(providerConfig.ApiKey)
                        ? providerConfig.ApiKey
                        : apiKeyProvider(providerConfig.ApiKeyConfigName),
                    providerConfig.ModelName);
            }

            if (string.Equals(providerConfig.ProviderType, "OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return new OpenAiClient(
                    () => !string.IsNullOrWhiteSpace(providerConfig.ApiKey)
                        ? providerConfig.ApiKey
                        : apiKeyProvider(providerConfig.ApiKeyConfigName),
                    providerConfig.ModelName);
            }

            if (string.Equals(providerConfig.ProviderType, AiProviderConfig.KimiProviderType, StringComparison.OrdinalIgnoreCase))
            {
                return new KimiOpenAICompatibleClient(
                    () => !string.IsNullOrWhiteSpace(providerConfig.ApiKey)
                        ? providerConfig.ApiKey
                        : apiKeyProvider(providerConfig.ApiKeyConfigName),
                    providerConfig.ModelName,
                    ConfigManager.GetKimiOpenAIBaseUrl(),
                    ConfigManager.GetKimiEnableSearch(),
                    ConfigManager.GetKimiEnableThinking(),
                    statusReporter);
            }

            if (string.Equals(providerConfig.ProviderType, "Groq", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(providerConfig.ProviderType, "Grog", StringComparison.OrdinalIgnoreCase))
            {
                return new GroqClient(
                    () => !string.IsNullOrWhiteSpace(providerConfig.ApiKey)
                        ? providerConfig.ApiKey
                        : apiKeyProvider(providerConfig.ApiKeyConfigName),
                    providerConfig.ModelName,
                    statusReporter);
            }

            if (string.Equals(providerConfig.ProviderType, "Mistral", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(providerConfig.ProviderType, "Mixtral", StringComparison.OrdinalIgnoreCase))
            {
                return new MistralClient(
                    () => !string.IsNullOrWhiteSpace(providerConfig.ApiKey)
                        ? providerConfig.ApiKey
                        : apiKeyProvider(providerConfig.ApiKeyConfigName),
                    providerConfig.ModelName);
            }

            if (string.Equals(providerConfig.ProviderType, "DeepSeek", StringComparison.OrdinalIgnoreCase))
            {
                return new DeepSeekClient(
                    () => !string.IsNullOrWhiteSpace(providerConfig.ApiKey)
                        ? providerConfig.ApiKey
                        : apiKeyProvider(providerConfig.ApiKeyConfigName),
                    providerConfig.ModelName);
            }

            throw new NotSupportedException("Provider de IA ainda nao implementado: " + providerConfig.ProviderType);
        }
    }
}
