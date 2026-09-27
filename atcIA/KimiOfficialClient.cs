using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GptBolDll;
using Iazeitor.PromptEngine.Contracts;
using Iazeitor.PromptEngine.Providers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace atcIA
{
    public sealed class ProviderRateLimitException : InvalidOperationException
    {
        public string ProviderName { get; }
        public string ModelName { get; }
        public string Stage { get; }
        public int? StatusCode { get; }

        public ProviderRateLimitException(string providerName, string modelName, string stage, int? statusCode, Exception innerException = null)
            : base("Limite de uso ou taxa excedido na Kimi oficial.", innerException)
        {
            ProviderName = providerName ?? string.Empty;
            ModelName = modelName ?? string.Empty;
            Stage = stage ?? string.Empty;
            StatusCode = statusCode;
        }
    }
    public sealed class KimiOfficialClient : IAgentModelClientWithTimeout
    {
        public const string DefaultBaseUrl = "https://api.moonshot.cn/v1";

        internal const string ProviderKey = "KimiOfficialMoonshot";

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

        public Task<string> AskAsync(string prompt, int timeoutMs)
        {
            return AskAsyncInternal(JsonConvert.SerializeObject(BuildLegacyPayload(prompt)), timeoutMs);
        }

        /// <summary>
        /// Caminho tipado (Prompt Engine). Preserva system/user/assistant e nao injeta o
        /// system legado hardcoded quando o request ja traz as proprias mensagens.
        /// </summary>
        public Task<string> AskAsync(PromptRequest request)
        {
            return AskAsync(request, 0);
        }

        public Task<string> AskAsync(PromptRequest request, int timeoutMs)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            PromptProviderMapping.EnsureProviderCompatible(request, ProviderKey, ProviderName);

            return AskAsyncInternal(JsonConvert.SerializeObject(BuildTypedPayload(request)), timeoutMs);
        }

        private async Task<string> AskAsyncInternal(string payloadJson, int timeoutMs)
        {
            string rawApiKey = apiKeyProvider == null ? string.Empty : apiKeyProvider();
            string apiKey = SanitizeApiKey(rawApiKey, out ApiKeyDiagnostics keyDiag);

            Log("[KIMI-OFICIAL] Provider selecionado.");
            Log("[KIMI-OFICIAL] Base URL configurada: " + baseUrl);
            Log("[KIMI-OFICIAL] Modelo: " + ModelName);
            Log("[KIMI-OFICIAL] API key configurada: " + keyDiag.HasValue.ToString().ToLowerInvariant());
            Log("[KIMI-OFICIAL] API key normalizada para envio: " + keyDiag.Normalized.ToString().ToLowerInvariant());
            Log("[KIMI-OFICIAL] API key contem quebra de linha: " + keyDiag.HasNewLine.ToString().ToLowerInvariant());
            Log("[KIMI-OFICIAL] API key contem espacos: " + keyDiag.HasSpaces.ToString().ToLowerInvariant());
            Log("[KIMI-OFICIAL] Authorization scheme: Bearer");
            Log("[KIMI-OFICIAL] Authorization token configurado: " + keyDiag.HasValue.ToString().ToLowerInvariant());
            Log("[KIMI-OFICIAL] Authorization token possui prefixo Bearer no valor salvo: " + keyDiag.HasBearerPrefix.ToString().ToLowerInvariant());
            if (keyDiag.HasValue)
            {
                Log("[KIMI-OFICIAL] Tamanho da API key: " + keyDiag.Length);
                Log("[KIMI-OFICIAL] Prefixo da API key: " + keyDiag.Prefix);
                Log("[KIMI-OFICIAL] Sufixo da API key: " + keyDiag.Suffix);
            }
            Log("[KIMI-OFICIAL] EnableThinking nao enviado para provider oficial.");
            Log("[KIMI-OFICIAL] EnableSearch nao enviado para provider oficial.");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Log("[KIMI-OFICIAL] API key vazia apos normalizacao.");
                throw new InvalidOperationException("Chave da API Kimi oficial nao configurada.");
            }

            string url = baseUrl.TrimEnd('/') + "/chat/completions";
            Log("[KIMI-OFICIAL] Base URL efetiva: " + baseUrl);
            Log("[KIMI-OFICIAL] Endpoint efetivo: /chat/completions");
            Log("[KIMI-OFICIAL] URL final sem query sensivel: " + url);

            Log("[KIMI-OFICIAL] Enviando chat completion.");

            HttpResponseMessage response = null;
            string responseBody = string.Empty;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Add("User-Agent", "atcIA/1.0");
                    request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

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
                Log("[KIMI-OFICIAL] HTTP status: " + status);
                Log("[KIMI-OFICIAL] ReasonPhrase: " + (response.ReasonPhrase ?? string.Empty));
                Log("[KIMI-OFICIAL] Erro autenticacao: " + (status == 401 || status == 403 ? "true" : "false"));
                Log("[KIMI-OFICIAL] Base URL efetiva: " + baseUrl);
                Log("[KIMI-OFICIAL] Endpoint efetivo: /chat/completions");
                Log("[KIMI-OFICIAL] Modelo enviado: " + ModelName);

                ApiErrorDetails errorDetails = ExtractSafeErrorDetails(responseBody);
                if (!string.IsNullOrWhiteSpace(errorDetails.Type))
                    Log("[KIMI-OFICIAL] Erro API tipo: " + errorDetails.Type);
                if (!string.IsNullOrWhiteSpace(errorDetails.Code))
                    Log("[KIMI-OFICIAL] Erro API codigo: " + errorDetails.Code);
                if (!string.IsNullOrWhiteSpace(errorDetails.Message))
                    Log("[KIMI-OFICIAL] Erro API mensagem: " + errorDetails.Message);

                if (status == 401)
                {
                    Log("[KIMI-OFICIAL] Falha de autenticacao (401).");
                    throw new InvalidOperationException(
                        "Falha de autenticacao na Kimi oficial. Verifique se a chave pertence a Moonshot/Kimi API oficial e se foi copiada corretamente." +
                        (baseUrl.Contains("moonshot.cn") ? string.Empty : " Se sua chave foi criada na plataforma chinesa, verifique se a Base URL correta e https://api.moonshot.cn/v1."));
                }

                if (status == 403)
                {
                    Log("[KIMI-OFICIAL] Falha de permissao (403).");
                    throw new InvalidOperationException(
                        "A chave da Kimi oficial foi reconhecida, mas nao possui permissao para este recurso ou modelo.");
                }

                if (status == 429)
                {
                    Log("[KIMI-OFICIAL] Limite de uso ou taxa excedido.");
                    Log("[KIMI] Lançando ProviderRateLimitException");
                    throw new ProviderRateLimitException(ProviderName, ModelName, "http", status);
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

        /// <summary>
        /// Payload do caminho legado AskAsync(string). Puro: nao faz HTTP.
        /// Mantem exatamente o system hardcoded, temperature, max_tokens e thinking anteriores.
        /// </summary>
        internal JObject BuildLegacyPayload(string prompt)
        {
            return new JObject
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
                ["max_tokens"] = 1200,
                ["thinking"] = new JObject { ["type"] = "disabled" }
            };
        }

        /// <summary>
        /// Payload tipado. Puro: nao faz HTTP.
        /// Usa apenas as mensagens do PromptRequest: nenhum system adicional e injetado.
        /// </summary>
        internal JObject BuildTypedPayload(PromptRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            PromptProviderMapping.EnsureProviderCompatible(request, ProviderKey, ProviderName);

            return new JObject
            {
                ["model"] = PromptProviderMapping.ResolveModel(request, ModelName),
                ["messages"] = PromptProviderMapping.BuildOpenAiMessages(request),
                ["temperature"] = IsKimiK26(ModelName) ? 0.6 : 1.0,
                ["max_tokens"] = 1200,
                ["thinking"] = new JObject { ["type"] = "disabled" }
            };
        }

        /// <summary>
        /// Testa autenticacao via GET /models sem iniciar um chat completo.
        /// Destinado a uso manual/diagnostico futuro; nao e chamado automaticamente.
        /// </summary>
        public async Task<bool> TestAuthenticationAsync(int timeoutMs = 30000)
        {
            string rawApiKey = apiKeyProvider == null ? string.Empty : apiKeyProvider();
            string apiKey = SanitizeApiKey(rawApiKey, out ApiKeyDiagnostics keyDiag);

            Log("[KIMI-OFICIAL-TEST] Iniciando teste de autenticacao.");
            Log("[KIMI-OFICIAL-TEST] Base URL: " + baseUrl);
            Log("[KIMI-OFICIAL-TEST] API key configurada: " + keyDiag.HasValue.ToString().ToLowerInvariant());

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Log("[KIMI-OFICIAL-TEST] API key ausente.");
                return false;
            }

            string url = baseUrl.TrimEnd('/') + "/models";
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Add("User-Agent", "atcIA/1.0");

                    HttpResponseMessage response;
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

                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    int status = (int)response.StatusCode;
                    Log("[KIMI-OFICIAL-TEST] HTTP status: " + status);

                    if (response.IsSuccessStatusCode)
                    {
                        Log("[KIMI-OFICIAL-TEST] Autenticacao OK.");
                        return true;
                    }

                    ApiErrorDetails errorDetails = ExtractSafeErrorDetails(body);
                    if (!string.IsNullOrWhiteSpace(errorDetails.Type))
                        Log("[KIMI-OFICIAL-TEST] Erro API tipo: " + errorDetails.Type);
                    if (!string.IsNullOrWhiteSpace(errorDetails.Code))
                        Log("[KIMI-OFICIAL-TEST] Erro API codigo: " + errorDetails.Code);
                    if (!string.IsNullOrWhiteSpace(errorDetails.Message))
                        Log("[KIMI-OFICIAL-TEST] Erro API mensagem: " + errorDetails.Message);

                    return false;
                }
            }
            catch (Exception ex)
            {
                Log("[KIMI-OFICIAL-TEST] Excecao: " + ex.GetType().Name);
                return false;
            }
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

        private static string SanitizeApiKey(string raw, out ApiKeyDiagnostics diag)
        {
            diag = new ApiKeyDiagnostics();
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string trimmed = raw.Trim();
            diag.HasBearerPrefix = trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
            if (diag.HasBearerPrefix)
                trimmed = trimmed.Substring("Bearer ".Length).Trim();

            diag.HasNewLine = trimmed.IndexOfAny(new[] { '\r', '\n' }) >= 0;
            diag.HasSpaces = trimmed.Contains(" ");
            diag.Length = trimmed.Length;
            diag.Prefix = trimmed.Length >= 3 ? trimmed.Substring(0, 3) : trimmed;
            diag.Suffix = trimmed.Length >= 3 ? trimmed.Substring(trimmed.Length - 3) : trimmed;
            diag.HasValue = true;

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                diag.Normalized = false;
                return string.Empty;
            }

            if (trimmed.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                diag.Normalized = false;
                return string.Empty;
            }

            diag.Normalized = true;
            return trimmed;
        }

        private static ApiErrorDetails ExtractSafeErrorDetails(string json)
        {
            var result = new ApiErrorDetails();
            if (string.IsNullOrWhiteSpace(json))
                return result;

            try
            {
                JObject root = JObject.Parse(json);
                JToken error = root["error"];
                if (error != null)
                {
                    result.Type = SanitizeCredential(error["type"]?.ToString());
                    result.Code = SanitizeCredential(error["code"]?.ToString());
                    result.Message = SanitizeCredential(error["message"]?.ToString());
                }
                else
                {
                    result.Message = SanitizeCredential(root["message"]?.ToString())
                        ?? SanitizeCredential(root.ToString(Formatting.None));
                }
            }
            catch
            {
                result.Message = SanitizeCredential(json);
            }

            return result;
        }

        private static string SanitizeCredential(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string sanitized = value;
            string[] patterns = new[]
            {
                @"(?i)(api[_\-]?key\s*[:=]\s*)['""]?[\w\-\.]+['""]?",
                @"(?i)(authorization\s*[:=]\s*)['""]?[\w\-\.]+['""]?",
                @"(?i)(bearer\s+)['""]?[\w\-\.]+['""]?",
                @"(?i)(token\s*[:=]\s*)['""]?[\w\-\.]+['""]?",
                @"(?i)(senha\s*[:=]\s*)['""]?[\w\-\.]+['""]?",
                @"(?i)(secret\s*[:=]\s*)['""]?[\w\-\.]+['""]?",
            };

            foreach (var pattern in patterns)
            {
                sanitized = Regex.Replace(sanitized, pattern, "$1***REDACTED***", RegexOptions.IgnoreCase);
            }

            return sanitized;
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

        private struct ApiKeyDiagnostics
        {
            public bool HasValue;
            public bool Normalized;
            public bool HasNewLine;
            public bool HasSpaces;
            public bool HasBearerPrefix;
            public int Length;
            public string Prefix;
            public string Suffix;
        }

        private struct ApiErrorDetails
        {
            public string Type;
            public string Code;
            public string Message;
        }
    }
}
