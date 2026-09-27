using System;
using System.Text;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Threading;
using Iazeitor.PromptEngine.Contracts;
using Iazeitor.PromptEngine.Providers;

namespace GptBolDll
{
    public class GeminiClient : IAgentModelClientWithTimeout
    {
        internal const string ProviderKey = "Gemini";

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
            return await SendRequestBodyAsync(BuildRequestBodyJson(prompt), timeoutMs).ConfigureAwait(false);
        }

        /// <summary>Caminho tipado (Prompt Engine). Preserva System (systemInstruction) e User/Assistant (contents).</summary>
        public async Task<string> AskAsync(PromptRequest request)
        {
            return await AskAsync(request, 0).ConfigureAwait(false);
        }

        public async Task<string> AskAsync(PromptRequest request, int timeoutMs)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            PromptProviderMapping.EnsureProviderCompatible(request, ProviderKey, ProviderName);

            return await SendRequestBodyAsync(BuildRequestBodyJson(request), timeoutMs).ConfigureAwait(false);
        }

        /// <summary>
        /// Corpo JSON do caminho legado AskAsync(string). Puro: nao faz HTTP.
        /// Mantem exatamente o formato anterior: contents -> parts -> text.
        /// </summary>
        internal string BuildRequestBodyJson(string prompt)
        {
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

            return JsonConvert.SerializeObject(body);
        }

        /// <summary>
        /// Corpo JSON tipado. Puro: nao faz HTTP.
        /// System vai para 'systemInstruction' (unico, deterministico) e User/Assistant viram
        /// 'user'/'model' em contents. Assistant nunca e rebaixado para user.
        /// </summary>
        internal string BuildRequestBodyJson(PromptRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            PromptProviderMapping.EnsureProviderCompatible(request, ProviderKey, ProviderName);
            PromptProviderMapping.EnsureHasMessages(request);

            var body = new JObject();

            string systemInstruction = PromptProviderMapping.JoinSystemMessages(request);
            if (!string.IsNullOrEmpty(systemInstruction))
            {
                body["systemInstruction"] = new JObject
                {
                    ["parts"] = new JArray
                    {
                        new JObject { ["text"] = systemInstruction }
                    }
                };
            }

            var contents = new JArray();
            for (int i = 0; i < request.Messages.Count; i++)
            {
                PromptMessage message = request.Messages[i];
                if (message == null)
                    throw new ArgumentException("PromptRequest.Messages contem item null.", nameof(request));

                if (message.Role == PromptRole.System)
                    continue;

                contents.Add(new JObject
                {
                    ["role"] = PromptProviderMapping.ToGeminiRole(message.Role),
                    ["parts"] = new JArray
                    {
                        new JObject { ["text"] = message.Content ?? string.Empty }
                    }
                });
            }

            if (contents.Count == 0)
                throw new InvalidOperationException("PromptRequest sem mensagens de usuario ou assistente para o Gemini.");

            body["contents"] = contents;
            return JsonConvert.SerializeObject(body);
        }

        private async Task<string> SendRequestBodyAsync(string json, int timeoutMs)
        {
            string apiKey = apiKeyProvider?.Invoke();
            string url = $"https://generativelanguage.googleapis.com/v1/models/{ModelName}:generateContent?key={apiKey}";

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
