using System;
using System.Text;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace GptBolDll
{
    public class GeminiClient : IAgentModelClientWithTimeout
    {
        public string ProviderName { get { return "Gemini"; } }

        public string ModelName { get { return modelName; } }

        public string DisplayName { get { return ProviderName + " / " + ModelName; } }

        private readonly Func<string> apiKeyProvider;
        private readonly string modelName;
        private readonly HttpClient client = new HttpClient();

        public GeminiClient(string apiKey)
            : this(() => apiKey)
        {
        }

        public GeminiClient(Func<string> apiKeyProvider)
            : this(apiKeyProvider, "gemini-2.5-flash")
        {
        }

        public GeminiClient(Func<string> apiKeyProvider, string modelName)
        {
            this.apiKeyProvider = apiKeyProvider;
            this.modelName = string.IsNullOrWhiteSpace(modelName) ? "gemini-2.5-flash" : modelName;
        }

        public async Task<string> AskAsync(string prompt)
        {
            return await AskAsync(prompt, 0).ConfigureAwait(false);
        }

        public async Task<string> AskAsync(string prompt, int timeoutMs)
        {
            string apiKey = apiKeyProvider?.Invoke();
            string url = $"https://generativelanguage.googleapis.com/v1/models/{ModelName}:generateContent?key={apiKey}";

            var body = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(body);

            HttpResponseMessage response;
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            {
                if (timeoutMs > 0)
                {
                    using (var cts = new CancellationTokenSource(timeoutMs))
                    {
                        response = await client.PostAsync(url, content, cts.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    response = await client.PostAsync(url, content).ConfigureAwait(false);
                }
            }

            var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Falha ao consultar Gemini: HTTP " + (int)response.StatusCode + " - " + result);

            var obj = JObject.Parse(result);
            var text = obj["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                var finishReason = obj["candidates"]?[0]?["finishReason"]?.ToString();
                var promptBlockReason = obj["promptFeedback"]?["blockReason"]?.ToString();
                if (!string.IsNullOrWhiteSpace(promptBlockReason) && string.IsNullOrWhiteSpace(finishReason))
                    finishReason = "PROMPT_BLOCK_" + promptBlockReason;

                if (!string.IsNullOrWhiteSpace(finishReason))
                    throw new GeminiResponseException("Gemini nao retornou texto. FinishReason=" + finishReason, finishReason, result);

                throw new GeminiResponseException("Gemini retornou resposta vazia ou sem candidato de texto.", string.Empty, result);
            }

            return text;

        }
    }
}
