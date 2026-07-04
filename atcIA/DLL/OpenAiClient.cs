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
    public sealed class OpenAiClient : IAgentModelClient
    {
        private readonly Func<string> apiKeyProvider;
        private readonly string modelName;
        private readonly HttpClient client = new HttpClient();

        public string ProviderName { get { return "OpenAI"; } }
        public string ModelName { get { return modelName; } }
        public string DisplayName { get { return ProviderName + " / " + ModelName; } }

        public OpenAiClient(Func<string> apiKeyProvider, string modelName)
        {
            this.apiKeyProvider = apiKeyProvider;
            this.modelName = string.IsNullOrWhiteSpace(modelName) ? "gpt-5.2" : modelName;
        }

        public async Task<string> AskAsync(string prompt)
        {
            string apiKey = apiKeyProvider?.Invoke();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Chave da OpenAI nao configurada.");

            var body = new
            {
                model = ModelName,
                input = prompt
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var result = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Falha ao consultar OpenAI: HTTP " + (int)response.StatusCode + " - " + result);

            return ExtractText(result);
        }

        private static string ExtractText(string json)
        {
            var obj = JObject.Parse(json);
            var outputText = obj["output_text"]?.ToString();
            if (!string.IsNullOrWhiteSpace(outputText))
                return outputText;

            var texts = obj["output"]?
                .SelectMany(item => item["content"] ?? new JArray())
                .Where(content => string.Equals(content["type"]?.ToString(), "output_text", StringComparison.OrdinalIgnoreCase))
                .Select(content => content["text"]?.ToString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            if (texts != null && texts.Count > 0)
                return string.Join(Environment.NewLine, texts);

            throw new InvalidOperationException("OpenAI retornou resposta vazia ou sem texto.");
        }
    }
}
