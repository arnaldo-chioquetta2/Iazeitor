using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Iazeitor.PromptEngine.Contracts;
using Iazeitor.PromptEngine.Providers;

namespace GptBolDll
{
    public sealed class MistralClient : IAgentModelClient
    {
        internal const string ProviderKey = "Mistral";

        private readonly Func<string> apiKeyProvider;
        private readonly string modelName;
        private readonly HttpClient client = new HttpClient();

        public string ProviderName { get { return "Mistral"; } }
        public string ModelName { get { return modelName; } }
        public string DisplayName { get { return ProviderName + " / " + ModelName; } }

        public MistralClient(Func<string> apiKeyProvider, string modelName)
        {
            this.apiKeyProvider = apiKeyProvider;
            this.modelName = string.IsNullOrWhiteSpace(modelName) ? "codestral-latest" : modelName;
        }

        public async Task<string> AskAsync(string prompt)
        {
            string apiKey = apiKeyProvider?.Invoke();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Chave da Mistral nao configurada.");

            return await SendRequestBodyAsync(BuildRequestBodyJson(prompt), apiKey).ConfigureAwait(false);
        }

        public async Task<string> AskAsync(PromptRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            PromptProviderMapping.EnsureProviderCompatible(request, ProviderKey, ProviderName, "Mixtral");

            string apiKey = apiKeyProvider?.Invoke();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Chave da Mistral nao configurada.");

            return await SendRequestBodyAsync(BuildRequestBodyJson(request), apiKey).ConfigureAwait(false);
        }

        /// <summary>Corpo JSON do caminho legado AskAsync(string). Puro: nao faz HTTP.</summary>
        internal string BuildRequestBodyJson(string prompt)
        {
            var body = new
            {
                model = ModelName,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                }
            };

            return JsonConvert.SerializeObject(body);
        }

        /// <summary>Corpo JSON tipado. Puro: nao faz HTTP. Preserva system/user/assistant.</summary>
        internal string BuildRequestBodyJson(PromptRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            PromptProviderMapping.EnsureProviderCompatible(request, ProviderKey, ProviderName, "Mixtral");

            var body = new
            {
                model = PromptProviderMapping.ResolveModel(request, ModelName),
                messages = PromptProviderMapping.BuildOpenAiMessages(request)
            };

            return JsonConvert.SerializeObject(body);
        }

        private async Task<string> SendRequestBodyAsync(string bodyJson, string apiKey)
        {

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mistral.ai/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request).ConfigureAwait(false);
            var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Falha ao consultar Mistral: HTTP " + (int)response.StatusCode + " - " + result);

            return ExtractText(result);
        }

        private static string ExtractText(string json)
        {
            var obj = JObject.Parse(json);
            var texts = obj["choices"]?
                .Select(choice => choice["message"]?["content"]?.ToString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            if (texts != null && texts.Count > 0)
                return string.Join(Environment.NewLine, texts);

            throw new InvalidOperationException("Mistral retornou resposta vazia ou sem texto.");
        }
    }
}
