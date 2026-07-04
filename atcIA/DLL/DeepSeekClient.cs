using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GptBolDll
{
    public sealed class DeepSeekClient : IAgentModelClientWithTimeout
    {
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

            using (var cts = timeoutMs > 0 ? new System.Threading.CancellationTokenSource(timeoutMs) : null)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

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
