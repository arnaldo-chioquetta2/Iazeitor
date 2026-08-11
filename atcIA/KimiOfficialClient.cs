using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GptBolDll;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace atcIA
{
    public sealed class KimiOfficialClient : IAgentModelClientWithTimeout
    {
        public const string DefaultBaseUrl = "https://api.moonshot.ai/v1";

        private static readonly HttpClient Client = new HttpClient();
        private readonly Func<string> apiKeyProvider;
        private readonly string modelName;
        private readonly string baseUrl;
        private readonly Action<string> statusReporter;

        public KimiOfficialClient(
            Func<string> apiKeyProvider,
            string modelName,
            string baseUrl = null,
            Action<string> statusReporter = null)
        {
            this.apiKeyProvider = apiKeyProvider;
            this.modelName = string.IsNullOrWhiteSpace(modelName) ? "kimi-k2.6" : modelName.Trim();
            this.baseUrl = NormalizeBaseUrl(baseUrl);
            this.statusReporter = statusReporter;
        }

        public string ProviderName { get { return "Kimi Oficial / Moonshot"; } }
        public string ModelName { get { return modelName; } }
        public string DisplayName { get { return ProviderName + " / " + ModelName; } }

        public Task<string> AskAsync(string prompt)
        {
            return AskAsync(prompt, 0);
        }

        public async Task<string> AskAsync(string prompt, int timeoutMs)
        {
            string apiKey = apiKeyProvider == null ? string.Empty : apiKeyProvider();
            Log("[KIMI-OFICIAL] Provider selecionado.");
            Log("[KIMI-OFICIAL] Base URL configurada: " + baseUrl);
            Log("[KIMI-OFICIAL] Modelo: " + ModelName);
            Log("[KIMI-OFICIAL] API key configurada: " + (!string.IsNullOrWhiteSpace(apiKey)).ToString().ToLowerInvariant());
            Log("[KIMI-OFICIAL] EnableThinking nao enviado para provider oficial.");
            Log("[KIMI-OFICIAL] EnableSearch nao enviado para provider oficial.");

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Chave da API Kimi oficial nao configurada.");

            string url = baseUrl.TrimEnd('/') + "/chat/completions";
            var payload = new JObject
            {
                ["model"] = ModelName,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = "Voce e um assistente de programacao. Responda com precisao."
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = prompt ?? string.Empty
                    }
                },
                ["temperature"] = IsKimiK26(ModelName) ? 0.6 : 1.0,
                ["thinking"] = new JObject { ["type"] = "disabled" }
            };

            Log("[KIMI-OFICIAL] Enviando chat completion.");
            HttpResponseMessage response = null;
            string responseBody = string.Empty;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    request.Content = new StringContent(
                        JsonConvert.SerializeObject(payload),
                        Encoding.UTF8,
                        "application/json");

                    if (timeoutMs > 0)
                    {
                        using (var cancellation = new CancellationTokenSource(timeoutMs))
                        {
                            response = await Client.SendAsync(request, cancellation.Token).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        response = await Client.SendAsync(request).ConfigureAwait(false);
                    }

                    responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException ex)
            {
                Log("[KIMI-OFICIAL] Tempo esgotado ao consultar a Kimi oficial.");
                throw new InvalidOperationException("Tempo esgotado ao consultar a Kimi oficial.", ex);
            }
            catch (HttpRequestException ex)
            {
                Log("[KIMI-OFICIAL] Erro temporario de rede.");
                throw new InvalidOperationException("Indisponibilidade temporaria da Kimi oficial.", ex);
            }

            if (response == null)
                throw new InvalidOperationException("Indisponibilidade temporaria da Kimi oficial.");

            int status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                if (status == 401 || status == 403)
                {
                    Log("[KIMI-OFICIAL] Falha de autenticacao.");
                    throw new InvalidOperationException("Falha de autenticacao na Kimi oficial. Verifique a API key.");
                }

                if (status == 429)
                {
                    Log("[KIMI-OFICIAL] Limite de uso ou taxa excedido.");
                    throw new InvalidOperationException("Limite de uso ou taxa excedido na Kimi oficial.");
                }

                if (status == 408 || status == 500 || status == 502 || status == 503 || status == 504)
                {
                    Log("[KIMI-OFICIAL] Erro temporario: " + status);
                    throw new InvalidOperationException("Indisponibilidade temporaria da Kimi oficial.");
                }

                throw new InvalidOperationException("Falha ao consultar a Kimi oficial. HTTP " + status + ".");
            }

            string content = ExtractContent(responseBody);
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("A Kimi oficial retornou resposta vazia ou sem texto.");

            Log("[KIMI-OFICIAL] Resposta chars: " + content.Length);
            Log("[KIMI-OFICIAL] Consulta concluida.");
            return content;
        }

        private void Log(string message)
        {
            if (statusReporter != null)
                statusReporter(message);
        }

        private static string NormalizeBaseUrl(string value)
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? DefaultBaseUrl : value.Trim();
            int queryIndex = normalized.IndexOfAny(new[] { '?', '#' });
            if (queryIndex >= 0)
                normalized = normalized.Substring(0, queryIndex);

            normalized = normalized.TrimEnd('/');
            if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - "/chat/completions".Length).TrimEnd('/');
            if (!normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                normalized += "/v1";

            return normalized;
        }

        private static bool IsKimiK26(string model)
        {
            return string.Equals(model, "kimi-k2.6", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractContent(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("A Kimi oficial retornou uma resposta invalida.", ex);
            }

            JToken content = root["choices"]?.FirstOrDefault()?["message"]?["content"];
            if (content == null || content.Type == JTokenType.Null)
                content = root["choices"]?.FirstOrDefault()?["delta"]?["content"];
            if (content == null || content.Type == JTokenType.Null)
                content = root["choices"]?.FirstOrDefault()?["text"];

            if (content == null)
                return string.Empty;

            if (content.Type == JTokenType.String)
                return content.ToString();

            if (content.Type == JTokenType.Array)
                return string.Join(string.Empty, content.Children()
                    .Select(item => item["text"]?.ToString() ?? item.ToString()));

            return content.ToString(Formatting.None);
        }
    }
}