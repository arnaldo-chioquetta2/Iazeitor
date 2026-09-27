using GptBolDll;
using System;

namespace atcIA
{
    public sealed class ProviderInactiveException : InvalidOperationException
    {
        public string ProviderId { get; private set; }
        public string ProviderName { get; private set; }

        public ProviderInactiveException(string providerId, string providerName)
            : base("IA configurada esta inativa: " + providerName)
        {
            ProviderId = providerId ?? string.Empty;
            ProviderName = providerName ?? string.Empty;
        }
    }

    public static class AgentModelClientFactory
    {
        private const string FeatureMarker = "AgentFactoryInactiveGuard-v1;KimiOfficialAuthDiag-v1;KimiOfficialMoonshot-v1;PreFactoryProviderGuard-v1;KimiOfficialGenericFields-v1;KimiOfficialClientAlignedWithWorkingService-v1";
        public static IAgentModelClient Create(AiProviderConfig providerConfig, Func<string, string> apiKeyProvider, Action<string> statusReporter = null)
        {
            if (providerConfig == null)
                throw new ArgumentNullException(nameof(providerConfig));

            providerConfig.Normalize();

            AiProviderConfig providerPersistido = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(providerConfig.Id))
                    providerPersistido = AiProviderRepository.BuscarPorId(providerConfig.Id);
            }
            catch (Exception ex)
            {
                statusReporter?.Invoke("[FACTORY-IA] Falha ao recarregar provider por ID: " + ex.GetType().Name);
            }

            if (providerPersistido != null)
            {
                providerConfig.Ativo = providerPersistido.Ativo;
                if (!string.IsNullOrWhiteSpace(providerPersistido.ApiKey))
                    providerConfig.ApiKey = providerPersistido.ApiKey;
            }

            if (!providerConfig.Ativo)
            {
                statusReporter?.Invoke("[FACTORY-IA] AgentModelClientFactory em execucao.");
                statusReporter?.Invoke("[FACTORY-IA] FeatureMarker: " + FeatureMarker);
                statusReporter?.Invoke("[FACTORY-IA] BaseDirectory: " + AppDomain.CurrentDomain.BaseDirectory);
                statusReporter?.Invoke("[FACTORY-IA] Providers path: " + AppPaths.GetAiProvidersPath());
                statusReporter?.Invoke("[FACTORY-IA] Provider solicitado ID: " + (providerConfig.Id ?? string.Empty));
                statusReporter?.Invoke("[FACTORY-IA] Provider solicitado Nome: " + (providerConfig.Nome ?? string.Empty));
                statusReporter?.Invoke("[FACTORY-IA] Provider solicitado Ativo: false");
                statusReporter?.Invoke("[FACTORY-IA] Provider solicitado API key configurada: " + (!string.IsNullOrWhiteSpace(providerConfig.ApiKey) ? "true" : "false"));
                statusReporter?.Invoke("[FACTORY-IA] Provider solicitado Modelo configurado: " + (!string.IsNullOrWhiteSpace(providerConfig.ModelName) ? "true" : "false"));
                statusReporter?.Invoke("[FACTORY-IA] Provider resolvido por ID: " + (providerPersistido != null ? "true" : "false"));
                statusReporter?.Invoke("[FACTORY-IA] Provider resolvido Ativo: false");
                // A validacao esperada deve ocorrer antes da factory; esta excecao e apenas defesa interna.
                throw new ProviderInactiveException(providerConfig.Id, providerConfig.Nome);
            }
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

            if (string.Equals(providerConfig.ProviderType, AiProviderConfig.KimiOfficialProviderType, StringComparison.OrdinalIgnoreCase))
            {
                return new KimiOfficialClient(
                    () => !string.IsNullOrWhiteSpace(providerConfig.ApiKey)
                        ? providerConfig.ApiKey
                        : apiKeyProvider(providerConfig.ApiKeyConfigName),
                    providerConfig.ModelName,
                    KimiOfficialClient.DefaultBaseUrl,
                    statusReporter);
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
