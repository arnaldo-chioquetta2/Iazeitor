using GptBolDll;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace atcIA
{
    public sealed class AiProviderConfigStore
    {
        public List<AiProviderConfig> Providers { get; set; } = new List<AiProviderConfig>();
    }

    public static class AiProviderRepository
    {
        public static string ProvidersFile
        {
            get { return Path.Combine(AppPaths.DadosDir, "ai-providers.json"); }
        }

        public static List<AiProviderConfig> Listar()
        {
            AppPaths.GarantirPastaDados();
            RegistrarLogConfiguracao("[CONFIG] Providers arquivo origem runtime: " + ProvidersFile);

            var store = LoadStore();
            EnsureDefaultProvider(store);
            Normalize(store);
            SaveStore(store);
            return store.Providers;
        }

        public static List<AiProviderConfig> ListarAtivos()
        {
            return Listar().Where(p => p.Ativo).ToList();
        }

        public static AiProviderConfig BuscarAtivoOuPadrao(string id)
        {
            var providers = Listar();
            var provider = providers.FirstOrDefault(p => p.Ativo && p.Id == id);
            if (provider != null)
                return provider;

            return providers.FirstOrDefault(p => p.Ativo && p.Id == AiProviderConfig.DefaultProviderId)
                ?? AiProviderConfig.CreateDefaultGemini();
        }

        public static AiProviderConfig BuscarPorId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            return Listar().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static void Salvar(AiProviderConfig provider)
        {
            if (provider == null)
                throw new System.ArgumentNullException(nameof(provider));

            RegistrarLogConfiguracao("[CONFIG-STORE] Entrada Salvar provider id: " + provider.Id);
            RegistrarLogConfiguracao("[CONFIG-STORE] Entrada GitDiffPromptPath vazio: " + (string.IsNullOrWhiteSpace(provider.GitDiffPromptPath) ? "true" : "false"));
            RegistrarLogConfiguracao("[CONFIG-STORE] Entrada GitDiffPromptPath: " + (provider.GitDiffPromptPath ?? string.Empty));

            provider.Normalize();

            if (!string.IsNullOrWhiteSpace(provider.ApiKey))
                provider.Ativo = true;

            var store = LoadStore();

            EnsureDefaultProvider(store);
            Normalize(store);

            var existente = store.Providers.FirstOrDefault(p => p.Id == provider.Id);

            if (existente == null)
                store.Providers.Add(provider);
            else
                Copiar(provider, existente);

            var existenteAposCopiar = store.Providers.FirstOrDefault(p => string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
            RegistrarLogConfiguracao("[CONFIG-STORE] Existente apos Copiar GitDiffPromptPath vazio: " + (existenteAposCopiar == null || string.IsNullOrWhiteSpace(existenteAposCopiar.GitDiffPromptPath) ? "true" : "false"));
            RegistrarLogConfiguracao("[CONFIG-STORE] Existente apos Copiar GitDiffPromptPath: " + (existenteAposCopiar == null ? string.Empty : existenteAposCopiar.GitDiffPromptPath ?? string.Empty));

            if (!store.Providers.Any(p => p.Ativo))
                throw new System.InvalidOperationException("Deve existir pelo menos uma IA ativa.");

            SaveStore(store);

            RegistrarLogConfiguracao("[CONFIG] Salvando provider: " + provider.Nome);
            RegistrarLogConfiguracao("[CONFIG] ProviderId salvo: " + provider.Id);
            RegistrarLogConfiguracao("[CONFIG] UseGitDiff salvo: " + (provider.UseGitDiff ? "true" : "false"));
            RegistrarLogConfiguracao("[CONFIG] PromptPath salvo vazio: " + (string.IsNullOrWhiteSpace(provider.PromptPath) ? "true" : "false"));
            RegistrarLogConfiguracao("[CONFIG] GitDiffPromptPath salvo vazio: " + (string.IsNullOrWhiteSpace(provider.GitDiffPromptPath) ? "true" : "false"));
            RegistrarLogConfiguracao("[CONFIG] GitDiffPromptPath salvo: " + (provider.GitDiffPromptPath ?? string.Empty));

            try
            {
                string json = File.ReadAllText(ProvidersFile);

                bool contemGitDiffPromptPath = json.IndexOf(
                    "GitDiffPromptPath",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;

                bool contemKimiProvider = json.IndexOf(
                    AiProviderConfig.KimiProviderId,
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;

                RegistrarLogConfiguracao(
                    "[CONFIG] Arquivo fisico contem GitDiffPromptPath: " +
                    (contemGitDiffPromptPath ? "true" : "false")
                );

                RegistrarLogConfiguracao(
                    "[CONFIG] Arquivo fisico contem provider kimi-openai-compatible: " +
                    (contemKimiProvider ? "true" : "false")
                );

                var storeReload = LoadStore();
                var recarregado = storeReload.Providers.FirstOrDefault(p => string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
                RegistrarLogConfiguracao("[CONFIG-STORE] Recarregado GitDiffPromptPath vazio: " + (recarregado == null || string.IsNullOrWhiteSpace(recarregado.GitDiffPromptPath) ? "true" : "false"));
                RegistrarLogConfiguracao("[CONFIG-STORE] Recarregado GitDiffPromptPath: " + (recarregado == null ? string.Empty : recarregado.GitDiffPromptPath ?? string.Empty));
            }
            catch
            {
            }
        }

        private static AiProviderConfigStore LoadStore()
        {
            if (!File.Exists(ProvidersFile))
                return new AiProviderConfigStore();

            try
            {
                var json = File.ReadAllText(ProvidersFile);
                return JsonConvert.DeserializeObject<AiProviderConfigStore>(json) ?? new AiProviderConfigStore();
            }
            catch
            {
                return new AiProviderConfigStore();
            }
        }

        private static void SaveStore(AiProviderConfigStore store)
        {
            AppPaths.GarantirPastaDados();
            RegistrarLogConfiguracao("[CONFIG] Providers arquivo destino: " + ProvidersFile);
            File.WriteAllText(ProvidersFile, JsonConvert.SerializeObject(store, Formatting.Indented));
        }

        private static void EnsureDefaultProvider(AiProviderConfigStore store)
        {
            if (store.Providers == null)
                store.Providers = new List<AiProviderConfig>();

            if (!store.Providers.Any(p => p.Id == AiProviderConfig.DefaultProviderId))
                store.Providers.Insert(0, AiProviderConfig.CreateDefaultGemini());
            if (!store.Providers.Any(p => p.Id == "openai-gpt"))
                store.Providers.Add(AiProviderConfig.CreateDefaultOpenAi());
            if (!store.Providers.Any(p => p.Id == "groq-default"))
                store.Providers.Add(AiProviderConfig.CreateDefaultGroq());
            if (!store.Providers.Any(p => p.Id == "mistral-codestral"))
                store.Providers.Add(AiProviderConfig.CreateDefaultMistral());
            if (!store.Providers.Any(p => p.Id == "deepseek-default"))
                store.Providers.Add(AiProviderConfig.CreateDefaultDeepSeek());
            if (!store.Providers.Any(p => p.Id == AiProviderConfig.KimiProviderId))
                store.Providers.Add(AiProviderConfig.CreateDefaultKimi());

            if (!store.Providers.Any(p => p.Ativo))
                store.Providers.First().Ativo = true;
        }

        private static void Normalize(AiProviderConfigStore store)
        {
            foreach (var provider in store.Providers)
                provider.Normalize();
        }

        private static void Copiar(AiProviderConfig origem, AiProviderConfig destino)
        {
            destino.Nome = origem.Nome;
            destino.ProviderType = origem.ProviderType;
            destino.ModelName = origem.ModelName;
            destino.ApiKeyConfigName = origem.ApiKeyConfigName;
            destino.ApiKey = origem.ApiKey;
            destino.PromptPath = origem.PromptPath;
            destino.GitDiffPromptPath = origem.GitDiffPromptPath;
            destino.NivelMaximoSuportado = origem.NivelMaximoSuportado;
            destino.Ativo = origem.Ativo;
            destino.UseGitDiff = origem.UseGitDiff;
        }

        private static void RegistrarLogConfiguracao(string mensagem)
        {
            try
            {
                string logPath = Path.Combine(AppPaths.DadosDir, "execucao.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? AppPaths.DadosDir);
                File.AppendAllText(logPath, mensagem + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
