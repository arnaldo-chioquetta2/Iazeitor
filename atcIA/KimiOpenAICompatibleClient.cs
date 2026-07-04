using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GptBolDll;

namespace atcIA
{
    public sealed class ProviderUnavailableException : InvalidOperationException
    {
        public string ProviderName { get; }
        public string ModelName { get; }
        public string BaseUrl { get; }
        public int? StatusCode { get; }
        public string Stage { get; }
        public bool EnableSearch { get; }
        public bool EnableThinking { get; }

        public ProviderUnavailableException(
            string message,
            string providerName,
            string modelName,
            string baseUrl,
            int? statusCode,
            string stage,
            bool enableSearch,
            bool enableThinking,
            Exception innerException = null)
            : base(message, innerException)
        {
            ProviderName = providerName ?? string.Empty;
            ModelName = modelName ?? string.Empty;
            BaseUrl = baseUrl ?? string.Empty;
            StatusCode = statusCode;
            Stage = stage ?? string.Empty;
            EnableSearch = enableSearch;
            EnableThinking = enableThinking;
        }
    }

    public sealed class KimiOpenAICompatibleClient : IAgentModelClientWithTimeout
    {
        private static readonly HttpClient Client = new HttpClient();
        private readonly Func<string> apiKeyProvider;
        private readonly string modelName;
        private readonly string baseUrl;
        private readonly bool enableSearch;
        private readonly bool enableThinking;
        private readonly Action<string> statusReporter;

        public string ProviderName { get { return "Kimi / OpenAI Compatível"; } }
        public string ModelName { get { return modelName; } }
        public string DisplayName { get { return ProviderName + " / " + ModelName; } }

        public KimiOpenAICompatibleClient(
            Func<string> apiKeyProvider,
            string modelName,
            string baseUrl,
            bool enableSearch,
            bool enableThinking,
            Action<string> statusReporter = null)
        {
            this.apiKeyProvider = apiKeyProvider;
            this.modelName = string.IsNullOrWhiteSpace(modelName) ? ConfigManager.GetKimiOpenAIModel() : modelName;
            this.baseUrl = NormalizeBaseUrl(baseUrl, statusReporter);
            this.enableSearch = enableSearch;
            this.enableThinking = enableThinking;
            this.statusReporter = statusReporter;
        }

        public Task<string> AskAsync(string prompt)
        {
            return AskAsync(prompt, 0);
        }

        public async Task<string> AskAsync(string prompt, int timeoutMs)
        {
            string apiKey = apiKeyProvider?.Invoke() ?? string.Empty;
            Log("[KIMI] Provider OpenAI-compatible selecionado.");
            Log("[KIMI] Base URL configurada: " + baseUrl);
            Log("[KIMI] Modelo: " + ModelName);
            Log("[KIMI] EnableSearch: " + enableSearch.ToString().ToLowerInvariant());
            Log("[KIMI] EnableThinking: " + enableThinking.ToString().ToLowerInvariant());
            Log("[KIMI] API key configurada: " + (!string.IsNullOrWhiteSpace(apiKey)).ToString().ToLowerInvariant());

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Chave da API Kimi nao configurada.");

            string url = baseUrl.TrimEnd('/') + "/chat/completions";
            string responseText = await SendRequestAsync(url, prompt, apiKey, timeoutMs, includeFlags: true).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                Log("[KIMI] Resposta chars: " + responseText.Length);
                return responseText;
            }

            throw new InvalidOperationException("Kimi retornou resposta vazia ou sem texto.");
        }

        private async Task<string> SendRequestAsync(string url, string prompt, string apiKey, int timeoutMs, bool includeFlags)
        {
            var body = new JObject
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
                ["temperature"] = 0.2
            };

            if (includeFlags && enableSearch)
                body["enable_search"] = true;
            if (includeFlags && enableThinking)
                body["enable_thinking"] = true;

            HttpResponseMessage response;
            string result;

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

                try
                {
                    response = await SendAsync(request, timeoutMs).ConfigureAwait(false);
                    result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (ProviderUnavailableException)
                {
                    throw;
                }
                catch (TaskCanceledException ex)
                {
                    throw CriarProviderIndisponivelException("timeout", null, ex);
                }
                catch (HttpRequestException ex)
                {
                    throw CriarProviderIndisponivelException("rede", null, ex);
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                if (includeFlags && (enableSearch || enableThinking) && DeveTentarNovamenteSemFlags(response.StatusCode, result))
                {
                    if ((int)response.StatusCode == 400)
                        Log("[KIMI] API recusou flags extras; repetindo sem enable_search/enable_thinking.");
                    else
                        Log("[KIMI] Erro temporário com flags extras; repetindo sem enable_search/enable_thinking. Status=" + (int)response.StatusCode);

                    string retryResult = await SendRequestAsync(url, prompt, apiKey, timeoutMs, includeFlags: false).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(retryResult))
                        return retryResult;

                    throw CriarProviderIndisponivelException("timeout", (int)response.StatusCode, null);
                }

                if (EhErroTemporario((int)response.StatusCode))
                    throw CriarProviderIndisponivelException("http", (int)response.StatusCode, null);

                throw new InvalidOperationException("Falha ao consultar Kimi: HTTP " + (int)response.StatusCode + " - " + Truncate(result, 4000));
            }

            return ExtractText(result);
        }

        private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, int timeoutMs)
        {
            if (timeoutMs <= 0)
                return Client.SendAsync(request);

            var cts = new CancellationTokenSource(timeoutMs);
            return Client.SendAsync(request, cts.Token);
        }

        private ProviderUnavailableException CriarProviderIndisponivelException(string etapa, int? statusCode, Exception innerException)
        {
            string mensagem = BuildUnavailableMessage(statusCode);
            Log("[KIMI] Endpoint indisponível ou timeout.");
            Log("[KIMI] Base URL: " + baseUrl);
            Log("[KIMI] Modelo: " + ModelName);
            Log("[KIMI] EnableSearch: " + enableSearch.ToString().ToLowerInvariant());
            Log("[KIMI] EnableThinking: " + enableThinking.ToString().ToLowerInvariant());
            Log("[KIMI] Status HTTP: " + (statusCode.HasValue ? statusCode.Value.ToString() : "indisponivel"));
            Log("[KIMI] Etapa: " + etapa);
            return new ProviderUnavailableException(
                mensagem,
                ProviderName,
                ModelName,
                baseUrl,
                statusCode,
                etapa,
                enableSearch,
                enableThinking,
                innerException);
        }

        private string BuildUnavailableMessage(int? statusCode)
        {
            var sb = new StringBuilder();
            sb.Append("Servidor Kimi indisponível ou não respondeu dentro do tempo esperado.");
            sb.Append(" Verifique se o endpoint configurado está ativo: ");
            sb.Append(baseUrl);
            sb.Append(". Também confira se o modelo configurado está disponível: ");
            sb.Append(ModelName);
            sb.Append(". Como este endpoint é externo/não oficial, ele pode estar temporariamente desligado ou instável.");
            sb.Append(" Tente novamente mais tarde ou selecione outra IA.");

            if (enableSearch || enableThinking)
            {
                sb.Append(" Se necessário, tente desativar Search/Thinking.");
            }

            if (statusCode.HasValue)
                sb.Append(" Status HTTP: ").Append(statusCode.Value).Append('.');

            return sb.ToString();
        }

        private static string ExtractText(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            JObject obj = JObject.Parse(json);
            string content = obj["choices"]?
                .FirstOrDefault()?["message"]?["content"]?
                .ToString();

            if (!string.IsNullOrWhiteSpace(content))
                return content;

            var deltas = obj["choices"]?
                .Select(choice => choice["delta"]?["content"]?.ToString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            if (deltas != null && deltas.Count > 0)
                return string.Join(Environment.NewLine, deltas);

            var texts = obj["choices"]?
                .Select(choice => choice["text"]?.ToString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            if (texts != null && texts.Count > 0)
                return string.Join(Environment.NewLine, texts);

            throw new InvalidOperationException("Kimi retornou resposta vazia ou sem texto.");
        }

        private static string NormalizeBaseUrl(string baseUrl, Action<string> statusReporter)
        {
            string normalized = string.IsNullOrWhiteSpace(baseUrl)
                ? ConfigManager.GetKimiOpenAIBaseUrl()
                : baseUrl.Trim();

            bool changed = false;
            if (normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.TrimEnd('/');
                changed = true;
            }

            if (!normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.TrimEnd('/') + "/v1";
                changed = true;
            }

            if (changed)
                statusReporter?.Invoke("[KIMI] Base URL normalizada com /v1.");

            return normalized;
        }

        private static bool PareceRecusaDeFlagsExtras(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return false;

            string lower = responseText.ToLowerInvariant();
            return lower.Contains("enable_search") ||
                   lower.Contains("enable_thinking") ||
                   lower.Contains("unknown field") ||
                   lower.Contains("additional properties") ||
                   lower.Contains("unrecognized") ||
                   lower.Contains("unexpected");
        }

        private static bool DeveTentarNovamenteSemFlags(HttpStatusCode statusCode, string responseText)
        {
            int code = (int)statusCode;
            if (code == 400)
                return PareceRecusaDeFlagsExtras(responseText);

            return EhErroTemporario(code);
        }

        private static bool EhErroTemporario(int statusCode)
        {
            return statusCode == 408 ||
                   statusCode == 429 ||
                   statusCode == 500 ||
                   statusCode == 502 ||
                   statusCode == 503 ||
                   statusCode == 504;
        }

        private void Log(string message)
        {
            statusReporter?.Invoke(message);
        }

        private static string Truncate(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length <= maxChars)
                return text ?? string.Empty;

            return text.Substring(0, maxChars) + "...";
        }
    }
}
