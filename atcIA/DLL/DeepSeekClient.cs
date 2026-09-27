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
    public sealed class DeepSeekClient : IAgentModelClientWithTimeout
    {
        internal const string ProviderKey = "DeepSeek";

        private readonly Func<string> apiKeyProvider;
        private readonly string modelName;
        private readonly HttpClient client = new HttpClient();

        public string ProviderName { get { return "DeepSeek"; } }
        public string ModelName { get { return modelName; } }
        public string DisplayName { get { return ProviderName + " / " + ModelName; } }

        public DeepSeekClient(Func<string> apiKeyProvider, string modelName)
        {
            this.apiKeyProvider = apiKeyProvider;
            this.modelName = string.IsNullOrWhiteSpace(modelName) ? "deepseek-chat" : modelName;
        }

        public Task<string> AskAsync(string prompt)
        {
            return AskAsync(prompt, 0);
        }

        public async Task<string> AskAsync(string prompt, int timeoutMs)
        {
            string apiKey = apiKeyProvider?.Invoke();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Chave da DeepSeek nao configurada.");

            return await SendRequestBodyAsync(BuildRequestBodyJson(prompt), apiKey, timeoutMs).ConfigureAwait(false);
        }

        /// <summary>Caminho tipado (Prompt Engine). Preserva system/user/assistant.</summary>
        public async Task<string> AskAsync(PromptRequest request)
        {
            return await AskAsync(request, 0).ConfigureAwait(false);
        }

        public async Task<string> AskAsync(PromptRequest request, int timeoutMs)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            PromptProviderMapping.EnsureProviderCompatible(request, ProviderKey, ProviderName);

            string apiKey = apiKeyProvider?.Invoke();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Chave da DeepSeek nao configurada.");

            return await SendRequestBodyAsync(BuildRequestBodyJson(request), apiKey, timeoutMs).ConfigureAwait(false);
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
                },
                stream = false
            };

            return JsonConvert.SerializeObject(body);
        }

        /// <summary>Corpo JSON tipado. Puro: nao faz HTTP.</summary>
        internal string BuildRequestBodyJson(PromptRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            PromptProviderMapping.EnsureProviderCompatible(request, ProviderKey, ProviderName);

            var body = new
            {
                model = PromptProviderMapping.ResolveModel(request, ModelName),
                messages = PromptProviderMapping.BuildOpenAiMessages(request),
                stream = false
            };

            return JsonConvert.SerializeObject(body);
        }

        private async Task<string> SendRequestBodyAsync(string bodyJson, string apiKey, int timeoutMs)
        {

            using (var cts = timeoutMs > 0 ? new System.Threading.CancellationTokenSource(timeoutMs) : null)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

                var response = cts == null
                    ? await client.SendAsync(request).ConfigureAwait(false)
                    : await client.SendAsync(request, cts.Token).ConfigureAwait(false);

                var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("Falha ao consultar DeepSeek: HTTP " + (int)response.StatusCode + " - " + result);

                return ExtractText(result);
            }
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

            throw new InvalidOperationException("DeepSeek retornou resposta vazia ou sem texto.");
        }
    }
}
