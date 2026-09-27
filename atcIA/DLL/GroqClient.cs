using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Iazeitor.PromptEngine.Contracts;
using Iazeitor.PromptEngine.Providers;

namespace GptBolDll
{
    public sealed class GroqClient : IAgentModelClient
    {
        internal const string ProviderKey = "Groq";

        private readonly Func<string> apiKeyProvider;
        private readonly string modelName;
        private readonly Action<string> statusReporter;
        private readonly HttpClient client = new HttpClient();

        public string ProviderName { get { return "Groq"; } }
        public string ModelName { get { return modelName; } }
        public string DisplayName { get { return ProviderName + " / " + ModelName; } }

        public GroqClient(Func<string> apiKeyProvider, string modelName, Action<string> statusReporter = null)
        {
            this.apiKeyProvider = apiKeyProvider;
            this.modelName = string.IsNullOrWhiteSpace(modelName) ? "llama-3.3-70b-versatile" : modelName;
            this.statusReporter = statusReporter;
        }

        public async Task<string> AskAsync(string prompt)
        {
            string apiKey = apiKeyProvider?.Invoke();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Chave da Groq nao configurada.");

            return await AskOnceWithRetryAsync(BuildRequestBodyJson(prompt), apiKey).ConfigureAwait(false);
        }

        public async Task<string> AskAsync(PromptRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            PromptProviderMapping.EnsureProviderCompatible(request, ProviderKey, ProviderName, "Grog");

            string apiKey = apiKeyProvider?.Invoke();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Chave da Groq nao configurada.");

            return await AskOnceWithRetryAsync(BuildRequestBodyJson(request), apiKey).ConfigureAwait(false);
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

            PromptProviderMapping.EnsureProviderCompatible(request, ProviderKey, ProviderName, "Grog");

            var body = new
            {
                model = PromptProviderMapping.ResolveModel(request, ModelName),
                messages = PromptProviderMapping.BuildOpenAiMessages(request)
            };

            return JsonConvert.SerializeObject(body);
        }

        private async Task<string> AskOnceWithRetryAsync(string bodyJson, string apiKey)
        {
            string result = null;
            HttpResponseMessage response = null;

            for (int attempt = 1; attempt <= 4; attempt++)
            {
                response = await SendRequestAsync(bodyJson, apiKey).ConfigureAwait(false);
                result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    return ExtractText(result);

                if ((int)response.StatusCode != 429)
                    break;

                bool isDailyLimit = IsDailyTokenLimit(result);
                TimeSpan delay = isDailyLimit ? ExtractRetryDelayLong(result) : ExtractRetryDelay(result);

                if (isDailyLimit && delay >= TimeSpan.FromMinutes(1))
                    throw new InvalidOperationException(BuildDailyLimitMessage(result));

                if (!isDailyLimit && attempt >= 4)
                    break;

                await DelayWithStatusAsync(delay, attempt).ConfigureAwait(false);
            }

            if ((int)response.StatusCode == 429)
            {
                if (IsDailyTokenLimit(result))
                    throw new InvalidOperationException(BuildDailyLimitMessage(result));

                throw new InvalidOperationException(BuildRateLimitMessage(result));
            }

            throw new InvalidOperationException("Falha ao consultar Groq: HTTP " + (int)response.StatusCode + " - " + result);
        }

        private async Task<HttpResponseMessage> SendRequestAsync(string bodyJson, string apiKey)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            return await client.SendAsync(request).ConfigureAwait(false);
        }

        private static string ExtractText(string result)
        {
            var obj = JObject.Parse(result);
            var text = obj["choices"]?[0]?["message"]?["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Groq retornou resposta vazia ou sem texto.");

            return text;
        }

        private static TimeSpan ExtractRetryDelay(string responseText)
        {
            var text = responseText ?? string.Empty;

            var millisecondsMatch = Regex.Match(text, @"try again in\s+(?<milliseconds>[0-9]+(?:\.[0-9]+)?)ms", RegexOptions.IgnoreCase);
            if (millisecondsMatch.Success &&
                double.TryParse(millisecondsMatch.Groups["milliseconds"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double milliseconds))
            {
                return TimeSpan.FromMilliseconds(Math.Min(60000, Math.Max(1000, milliseconds + 500)));
            }

            var minuteSecondMatch = Regex.Match(text, @"try again in\s+(?<minutes>[0-9]+)m(?<seconds>[0-9]+(?:\.[0-9]+)?)s", RegexOptions.IgnoreCase);
            if (minuteSecondMatch.Success &&
                int.TryParse(minuteSecondMatch.Groups["minutes"].Value, out int minutes) &&
                double.TryParse(minuteSecondMatch.Groups["seconds"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds))
            {
                return TimeSpan.FromMilliseconds(Math.Min(60000, Math.Max(1000, (minutes * 60 + seconds + 1) * 1000)));
            }

            var secondsMatch = Regex.Match(text, @"try again in\s+(?<seconds>[0-9]+(?:\.[0-9]+)?)s", RegexOptions.IgnoreCase);
            if (secondsMatch.Success &&
                double.TryParse(secondsMatch.Groups["seconds"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds))
            {
                return TimeSpan.FromMilliseconds(Math.Min(60000, Math.Max(1000, (seconds + 1) * 1000)));
            }

            return TimeSpan.FromSeconds(10);
        }

        private static bool IsDailyTokenLimit(string responseText)
        {
            return (responseText ?? string.Empty).IndexOf("tokens per day", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (responseText ?? string.Empty).IndexOf("(TPD)", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildRateLimitMessage(string responseText)
        {
            var delay = ExtractRetryDelay(responseText);
            var localTime = DateTime.Now.Add(delay);
            return "Limite temporario da Groq atingido. Tente novamente por volta de " +
                   localTime.ToString("HH:mm:ss") +
                   ". Detalhes: " + ExtractGroqErrorMessage(responseText);
        }

        private static string BuildDailyLimitMessage(string responseText)
        {
            var delay = ExtractRetryDelayLong(responseText);
            if (delay < TimeSpan.FromMinutes(1))
            {
                return "A Groq recusou a requisicao por limite de tokens mesmo apos 4 tentativas automaticas. Aguarde cerca de 1 minuto e tente novamente.";
            }

            var localTime = DateTime.Now.Add(delay);
            string horario = delay < TimeSpan.FromHours(1)
                ? localTime.ToString("HH:mm:ss")
                : localTime.ToString("HH:mm");

            return "O limite diario da Groq foi atingido. O limite da Groq sera renovado por volta de " +
                   horario +
                   " de " +
                   localTime.ToString("dd/MM/yyyy") +
                   ".";
        }

        private async Task DelayWithStatusAsync(TimeSpan delay, int attempt)
        {
            if (delay < TimeSpan.FromSeconds(1))
                delay = TimeSpan.FromSeconds(1);

            DateTime end = DateTime.Now.Add(delay);
            while (true)
            {
                TimeSpan remaining = end - DateTime.Now;
                if (remaining <= TimeSpan.Zero)
                    break;

                statusReporter?.Invoke("Aguardando Groq " + remaining.ToString(@"mm\:ss") + " (tentativa " + (attempt + 1) + "/4)");

                TimeSpan step = remaining > TimeSpan.FromSeconds(1)
                    ? TimeSpan.FromSeconds(1)
                    : remaining;

                await Task.Delay(step).ConfigureAwait(false);
            }
        }

        private static TimeSpan ExtractRetryDelayLong(string responseText)
        {
            var text = responseText ?? string.Empty;

            var minuteSecondMatch = Regex.Match(text, @"try again in\s+(?<minutes>[0-9]+)m(?<seconds>[0-9]+(?:\.[0-9]+)?)s", RegexOptions.IgnoreCase);
            if (minuteSecondMatch.Success &&
                int.TryParse(minuteSecondMatch.Groups["minutes"].Value, out int minutes) &&
                double.TryParse(minuteSecondMatch.Groups["seconds"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds))
            {
                return TimeSpan.FromMilliseconds(Math.Max(1000, (minutes * 60 + seconds + 1) * 1000));
            }

            return ExtractRetryDelay(text);
        }

        private static string ExtractGroqErrorMessage(string responseText)
        {
            try
            {
                var obj = JObject.Parse(responseText ?? string.Empty);
                var message = obj["error"]?["message"]?.ToString();
                if (!string.IsNullOrWhiteSpace(message))
                    return message;
            }
            catch
            {
            }

            return responseText ?? string.Empty;
        }
    }
}
