using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GptBolDll
{
    public static class AgentResponseParser
    {
        public static AgentResponse Parse(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return CreateFallback(string.Empty);

            string json = ExtractJson(rawResponse);
            if (!string.IsNullOrWhiteSpace(json))
            {
                json = NormalizeNoActionTypes(json);
                var response = TryDeserialize(json);
                if (response != null)
                    return NormalizeWithNestedJsonFallback(response);

                var sanitizedJson = SanitizeJsonStringLiterals(json);
                if (!string.Equals(sanitizedJson, json, StringComparison.Ordinal))
                {
                    response = TryDeserialize(sanitizedJson);
                    if (response != null)
                        return NormalizeWithNestedJsonFallback(response);
                }

                response = TryParseActionsOnly(rawResponse, json);
                if (response != null)
                    return Normalize(response);
            }

            return CreateFallback(rawResponse.Trim());
        }

        private static string NormalizeNoActionTypes(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;

            string resultado = json;
            resultado = Regex.Replace(resultado, "\"tipo\"\\s*:\\s*\"(?:None|NoAction|SemAcao|SemAção|Nenhuma)\"", "\"tipo\":\"Nenhuma\"", RegexOptions.IgnoreCase);
            return resultado;
        }

        private static AgentResponse TryDeserialize(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<AgentResponse>(json);
            }
            catch
            {
                return null;
            }
        }

        private static AgentResponse Normalize(AgentResponse response)
        {
            if (response.Acoes == null)
                response.Acoes = new System.Collections.Generic.List<AgentAction>();

            return response;
        }

        private static AgentResponse NormalizeWithNestedJsonFallback(AgentResponse response)
        {
            response = Normalize(response);

            if (response.Acoes.Count > 0)
                return response;

            AgentResponse nested = TryParseNestedResponse(response.MensagemUsuario);
            if (nested == null || nested.Acoes == null || nested.Acoes.Count == 0)
                nested = TryParseNestedResponse(response.Explicacao);

            return nested != null && nested.Acoes != null && nested.Acoes.Count > 0
                ? Normalize(nested)
                : response;
        }

        private static AgentResponse TryParseNestedResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string json = ExtractJson(text);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var response = TryDeserialize(json);
            if (response != null)
                return Normalize(response);

            string sanitizedJson = SanitizeJsonStringLiterals(json);
            if (!string.Equals(sanitizedJson, json, StringComparison.Ordinal))
            {
                response = TryDeserialize(sanitizedJson);
                if (response != null)
                    return Normalize(response);
            }

            return null;
        }

        private static AgentResponse TryParseActionsOnly(string rawResponse, string json)
        {
            string actionsJson = ExtractActionsArray(json);
            if (string.IsNullOrWhiteSpace(actionsJson))
                actionsJson = ExtractActionsArray(rawResponse);

            if (string.IsNullOrWhiteSpace(actionsJson))
                return null;

            List<AgentAction> actions = TryDeserializeActions(actionsJson);
            if ((actions == null || actions.Count == 0))
            {
                string sanitizedActions = SanitizeJsonStringLiterals(actionsJson);
                if (!string.Equals(sanitizedActions, actionsJson, StringComparison.Ordinal))
                    actions = TryDeserializeActions(sanitizedActions);
            }

            if (actions == null || actions.Count == 0)
                return null;

            actions = NormalizeDirectPatchActions(actions);

            return new AgentResponse
            {
                MensagemUsuario = ExtractJsonStringProperty(json, "mensagem_usuario"),
                Explicacao = ExtractJsonStringProperty(json, "explicacao"),
                Acoes = actions,
                RequerConfirmacao = false
            };
        }

        private static List<AgentAction> TryDeserializeActions(string actionsJson)
        {
            try
            {
                return JArray.Parse(actionsJson).ToObject<List<AgentAction>>();
            }
            catch
            {
                return null;
            }
        }

        private static List<AgentAction> NormalizeDirectPatchActions(List<AgentAction> actions)
        {
            if (actions == null || actions.Count == 0)
                return actions;

            var normalized = new List<AgentAction>();

            foreach (AgentAction action in actions)
            {
                AgentAction normalizedAction = NormalizeDirectPatchAction(action);
                if (normalizedAction != null)
                    normalized.Add(normalizedAction);
            }

            return normalized;
        }

        private static AgentAction NormalizeDirectPatchAction(AgentAction action)
        {
            if (action == null)
                return null;

            JObject dados = action.Dados ?? new JObject();

            if (IsDirectPatchAction(dados, out string protocolo))
            {
                action.Tipo = AgentActionType.ArquivoLocal;
                action.Dados = new JObject
                {
                    ["protocolo"] = protocolo
                };
                action.Descricao = string.IsNullOrWhiteSpace(action.Descricao)
                    ? "Ação com campos diretos de patch normalizada para ArquivoLocal"
                    : action.Descricao;
                System.Console.WriteLine("[PARSER] Ação com campos diretos de patch normalizada para ArquivoLocal.");
                return action;
            }

            if (action.Tipo == AgentActionType.Nenhuma && !HasDirectActionFields(dados))
                return null;

            if (action.Tipo == AgentActionType.Nenhuma && HasDirectActionFields(dados))
            {
                action.Tipo = AgentActionType.ArquivoLocal;
                action.Dados = new JObject
                {
                    ["protocolo"] = BuildProtocolFromDirectFields(dados)
                };
                System.Console.WriteLine("[PARSER] Ação com campos diretos de patch normalizada para ArquivoLocal.");
                return action;
            }

            return action;
        }

        private static bool HasDirectActionFields(JObject dados)
        {
            if (dados == null)
                return false;

            return dados.Property("ARQ", StringComparison.OrdinalIgnoreCase) != null ||
                   dados.Property("SEARCH", StringComparison.OrdinalIgnoreCase) != null ||
                   dados.Property("REPLACE", StringComparison.OrdinalIgnoreCase) != null ||
                   dados.Property("SEARCH_BLOCK", StringComparison.OrdinalIgnoreCase) != null ||
                   dados.Property("REPLACE_BLOCK", StringComparison.OrdinalIgnoreCase) != null;
        }

        private static bool IsDirectPatchAction(JObject dados, out string protocolo)
        {
            protocolo = BuildProtocolFromDirectFields(dados);
            return !string.IsNullOrWhiteSpace(protocolo);
        }

        private static string BuildProtocolFromDirectFields(JObject dados)
        {
            if (dados == null)
                return string.Empty;

            string arq = GetStringValue(dados, "ARQ");
            if (string.IsNullOrWhiteSpace(arq))
                return string.Empty;

            string search = GetStringValue(dados, "SEARCH");
            string replace = GetStringValue(dados, "REPLACE");
            string searchBlock = GetStringValue(dados, "SEARCH_BLOCK");
            string replaceBlock = GetStringValue(dados, "REPLACE_BLOCK");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ARQ=" + arq.Trim());

            if (!string.IsNullOrWhiteSpace(searchBlock) || !string.IsNullOrWhiteSpace(replaceBlock))
            {
                if (string.IsNullOrWhiteSpace(searchBlock) || string.IsNullOrWhiteSpace(replaceBlock))
                    return string.Empty;

                sb.AppendLine("SEARCH_BLOCK");
                sb.AppendLine(searchBlock.Trim());
                sb.AppendLine("END_SEARCH");
                sb.AppendLine("REPLACE_BLOCK");
                sb.AppendLine(replaceBlock.Trim());
                sb.AppendLine("END_REPLACE");
                return sb.ToString().Trim();
            }

            if (!string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(replace))
            {
                if (string.IsNullOrWhiteSpace(search) || string.IsNullOrWhiteSpace(replace))
                    return string.Empty;

                sb.AppendLine("SEARCH=" + search.Trim());
                sb.AppendLine("REPLACE=" + replace.Trim());
                return sb.ToString().Trim();
            }

            return string.Empty;
        }

        private static string GetStringValue(JObject dados, string propertyName)
        {
            if (dados == null || string.IsNullOrWhiteSpace(propertyName))
                return string.Empty;

            JToken token = dados.Property(propertyName, StringComparison.OrdinalIgnoreCase)?.Value;
            if (token == null)
                return string.Empty;

            return token.Type == JTokenType.String ? token.Value<string>() ?? string.Empty : token.ToString(Formatting.None);
        }

        private static AgentResponse CreateFallback(string message)
        {
            return new AgentResponse
            {
                MensagemUsuario = message,
                Explicacao = string.Empty,
                RequerConfirmacao = false
            };
        }

        private static string ExtractJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var fenced = Regex.Match(text, "```(?:json)?\\s*(.*?)```", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (fenced.Success)
                return fenced.Groups[1].Value.Trim();

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
                return text.Substring(start, end - start + 1).Trim();

            return string.Empty;
        }

        private static string ExtractActionsArray(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var marker = Regex.Match(text, "\"acoes\"\\s*:", RegexOptions.IgnoreCase);
            if (!marker.Success)
                return string.Empty;

            int start = text.IndexOf('[', marker.Index + marker.Length);
            if (start < 0)
                return string.Empty;

            int end = FindBalancedEnd(text, start, '[', ']');
            return end > start ? text.Substring(start, end - start + 1).Trim() : string.Empty;
        }

        private static string ExtractJsonStringProperty(string json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            var marker = Regex.Match(json, "\"" + Regex.Escape(propertyName) + "\"\\s*:", RegexOptions.IgnoreCase);
            if (!marker.Success)
                return string.Empty;

            int start = json.IndexOf('"', marker.Index + marker.Length);
            if (start < 0)
                return string.Empty;

            int end = FindStringEnd(json, start);
            if (end <= start)
                return string.Empty;

            string value = json.Substring(start, end - start + 1);
            try
            {
                return JsonConvert.DeserializeObject<string>(value) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int FindBalancedEnd(string text, int start, char open, char close)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = start; i < text.Length; i++)
            {
                char current = text[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == open)
                    depth++;
                else if (current == close)
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static int FindStringEnd(string text, int start)
        {
            bool escaped = false;
            for (int i = start + 1; i < text.Length; i++)
            {
                char current = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                    return i;
            }

            return -1;
        }

        private static string SanitizeJsonStringLiterals(string json)
        {
            if (string.IsNullOrEmpty(json))
                return json;

            var sb = new System.Text.StringBuilder(json.Length);
            bool inString = false;

            for (int i = 0; i < json.Length; i++)
            {
                char current = json[i];

                if (!inString)
                {
                    sb.Append(current);
                    if (current == '"')
                        inString = true;
                    continue;
                }

                if (current == '"')
                {
                    sb.Append(current);
                    inString = false;
                    continue;
                }

                if (current == '\\')
                {
                    if (i + 1 >= json.Length)
                    {
                        sb.Append(@"\\");
                        continue;
                    }

                    char next = json[i + 1];
                    if (IsValidJsonEscape(next))
                    {
                        sb.Append(current);
                        sb.Append(next);
                        i++;
                    }
                    else
                    {
                        sb.Append(@"\\");
                    }

                    continue;
                }

                if (current == '\r')
                {
                    sb.Append(@"\r");
                    continue;
                }

                if (current == '\n')
                {
                    sb.Append(@"\n");
                    continue;
                }

                if (current == '\t')
                {
                    sb.Append(@"\t");
                    continue;
                }

                sb.Append(current);
            }

            return sb.ToString();
        }

        private static bool IsValidJsonEscape(char value)
        {
            return value == '"' ||
                   value == '\\' ||
                   value == '/' ||
                   value == 'b' ||
                   value == 'f' ||
                   value == 'n' ||
                   value == 'r' ||
                   value == 't' ||
                   value == 'u';
        }
    }
}
