using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GptBolDll
{
    public static class ActionParserPipeline
    {
        public static ActionParseResult Parse(AgentResponse response, string rawResponse)
        {
            GitDiffTextExtractionResult gitDiffExtraction = GitDiffTextExtractor.Extract(rawResponse);
            var result = new ActionParseResult
            {
                RawResponseSafePreview = ActionParseResult.SafePreview(rawResponse, 4000),
                HasTechnicalActionSignals = HasTechnicalSignals(rawResponse),
                HasGitDiffFormat = HasGitDiffFormat(gitDiffExtraction?.EffectiveText) || HasGitDiffFormat(rawResponse),
                GitDiffEffectiveText = gitDiffExtraction != null && !string.IsNullOrWhiteSpace(gitDiffExtraction.EffectiveText)
                    ? (gitDiffExtraction.EffectiveText ?? string.Empty)
                    : (rawResponse ?? string.Empty),
                GitDiffWrapperJsonDetected = gitDiffExtraction != null && gitDiffExtraction.HasWrapperJson,
                GitDiffExtractedFromJsonWrapper = gitDiffExtraction != null && gitDiffExtraction.ExtractedFromJsonWrapper,
                GitDiffJsonWrapperCandidateDetected = gitDiffExtraction != null && gitDiffExtraction.JsonWrapperCandidateDetected,
                GitDiffJsonWrapperIncompleteRecovered = gitDiffExtraction != null && gitDiffExtraction.JsonWrapperIncompleteRecovered,
                GitDiffPrefixDiscardedBeforeDiff = gitDiffExtraction != null && gitDiffExtraction.PrefixDiscardedBeforeDiff,
                GitDiffUsedFirstDiffFallback = gitDiffExtraction != null && gitDiffExtraction.UsedFirstDiffFallback,
                GitDiffDiffEscapedTextDetected = gitDiffExtraction != null && gitDiffExtraction.DiffEscapedTextDetected,
                GitDiffWrapperFieldCount = gitDiffExtraction == null ? 0 : gitDiffExtraction.FieldCount,
                GitDiffExtractedBlockCount = gitDiffExtraction == null ? 0 : gitDiffExtraction.ExtractedBlockCount
            };

            if (gitDiffExtraction != null && gitDiffExtraction.JsonWrapperFieldsFound != null)
            {
                foreach (string field in gitDiffExtraction.JsonWrapperFieldsFound.Where(field => !string.IsNullOrWhiteSpace(field)))
                {
                    if (!result.GitDiffJsonWrapperFieldsFound.Contains(field))
                        result.GitDiffJsonWrapperFieldsFound.Add(field);
                }
            }

            if (response?.Acoes != null)
            {
                foreach (var action in response.Acoes)
                    result.Actions.Add(ParseTypedAction(action, result));
            }

            JObject rawJson = TryExtractRawJson(rawResponse, result);
            if (rawJson != null)
            {
                result.RawJsonExtracted = true;
                ParseRawJson(rawJson, response, result);
            }

            BuildCanonicalActions(response, rawResponse, rawJson, result);
            ClassifyOutcome(response, rawResponse, result);

            return result;
        }

        private static ParsedAction ParseTypedAction(AgentAction action, ActionParseResult result)
        {
            var parsed = new ParsedAction
            {
                Type = action?.Tipo.ToString() ?? string.Empty,
                Description = action?.Descricao ?? string.Empty,
                Data = action?.Dados == null ? null : (JObject)action.Dados.DeepClone(),
                RawActionJson = MaskSafe(action?.Dados == null ? string.Empty : action.Dados.ToString(Formatting.None)),
                IsExecutable = false,
                Source = "TypedResponse",
                WasNormalized = false
            };

            if (action == null)
            {
                parsed.Errors.Add("Ação nula.");
                result.Diagnostics.Add(new ActionParseDiagnostic
                {
                    Severity = "ERROR",
                    Code = "NULL_ACTION",
                    Message = "Ação nula.",
                    ActionType = string.Empty,
                    SafePreview = string.Empty
                });
                return parsed;
            }

            string type = (action.Tipo.ToString() ?? string.Empty).Trim();
            if (IsNoActionType(type))
            {
                parsed.IsExecutable = false;
                parsed.Warnings.Add("Ação não executável normalizada.");
                result.Diagnostics.Add(new ActionParseDiagnostic
                {
                    Severity = "INFO",
                    Code = "NO_ACTION",
                    Message = "Ação não executável normalizada.",
                    ActionType = type,
                    SafePreview = MaskSafe(parsed.RawActionJson)
                });
                return parsed;
            }

            if (action.Tipo == AgentActionType.ArquivoLocal)
            {
                bool temDadosExecutaveis = TemDadosExecutaveisArquivoLocal(action.Dados);
                parsed.IsExecutable = temDadosExecutaveis;

                if (temDadosExecutaveis)
                {
                    parsed.Warnings.Add("Ação ArquivoLocal executável identificada.");
                    result.Diagnostics.Add(new ActionParseDiagnostic
                    {
                        Severity = "INFO",
                        Code = "LOCAL_FILE_EXECUTABLE",
                        Message = "Ação ArquivoLocal executável identificada.",
                        ActionType = type,
                        SafePreview = MaskSafe(parsed.RawActionJson)
                    });
                }
                else
                {
                    parsed.Errors.Add("Ação ArquivoLocal sem dados executáveis.");
                    result.Diagnostics.Add(new ActionParseDiagnostic
                    {
                        Severity = "ERROR",
                        Code = "LOCAL_FILE_EMPTY",
                        Message = "Ação ArquivoLocal sem dados executáveis.",
                        ActionType = type,
                        SafePreview = MaskSafe(parsed.RawActionJson)
                    });
                }

                if (TemCamposDiretosPatch(action.Dados))
                {
                    parsed.Warnings.Add("Campos diretos de patch detectados.");
                    result.Diagnostics.Add(new ActionParseDiagnostic
                    {
                        Severity = "INFO",
                        Code = "DIRECT_PATCH_FIELDS",
                        Message = "Campos diretos de patch detectados.",
                        ActionType = type,
                        SafePreview = MaskSafe(parsed.RawActionJson)
                    });
                }
            }
            else
            {
                parsed.IsExecutable = action.Tipo == AgentActionType.ComandoDos ||
                                      action.Tipo == AgentActionType.Ftp;
            }

            return parsed;
        }

        private static void ParseRawJson(JObject rawJson, AgentResponse typedResponse, ActionParseResult result)
        {
            JArray actionsArray;
            string arrayName;
            if (!TryGetActionsArray(rawJson, out actionsArray, out arrayName) || actionsArray == null)
                return;

            result.RawRecoveredActionCount = actionsArray.Count;
            result.Diagnostics.Add(new ActionParseDiagnostic
            {
                Severity = "INFO",
                Code = "RAW_ACTIONS_FOUND",
                Message = "Ações encontradas no JSON bruto: " + actionsArray.Count,
                ActionType = arrayName ?? string.Empty,
                SafePreview = ActionParseResult.SafePreview(actionsArray.ToString(Formatting.None), 4000)
            });

            bool rawDirectFieldsFound = false;
            int rawExecutableCandidates = 0;

            foreach (JToken item in actionsArray)
            {
                JObject rawAction = item as JObject;
                if (rawAction == null)
                    continue;

                var parsed = BuildRawParsedAction(rawAction, result);
                result.RawActions.Add(parsed);
                result.Diagnostics.Add(new ActionParseDiagnostic
                {
                    Severity = "INFO",
                    Code = "RAW_ACTION_FIELDS",
                    Message = "Raw action " + result.RawActions.Count + " fields: " + string.Join(", ", GetSafeFieldNames(rawAction)),
                    ActionType = parsed.Type,
                    SafePreview = string.Empty
                });

                JObject payload = GetActionPayloadObject(rawAction);
                bool hasDirectFields = TemCamposDiretosPatchBruto(payload);
                if (hasDirectFields)
                {
                    rawDirectFieldsFound = true;
                    result.Diagnostics.Add(new ActionParseDiagnostic
                    {
                        Severity = "INFO",
                        Code = "RAW_DIRECT_PATCH_FIELDS",
                        Message = "Campos diretos de patch encontrados no JSON bruto.",
                        ActionType = parsed.Type,
                        SafePreview = MaskSafe(rawAction.ToString(Formatting.None))
                    });
                }

                if (parsed.IsExecutable)
                    rawExecutableCandidates++;
            }

            result.RawRecoveredExecutableCandidateCount = rawExecutableCandidates;
            result.RawDirectPatchFieldsFound = rawDirectFieldsFound;
            result.Diagnostics.Add(new ActionParseDiagnostic
            {
                Severity = "INFO",
                Code = "RAW_RECOVERABLE_ACTIONS",
                Message = "Ações recuperáveis no JSON bruto: " + result.RawRecoveredActionCount,
                ActionType = arrayName ?? string.Empty,
                SafePreview = string.Empty
            });
            result.Diagnostics.Add(new ActionParseDiagnostic
            {
                Severity = "INFO",
                Code = "RAW_EXECUTABLE_CANDIDATES",
                Message = "Candidatas executáveis no JSON bruto: " + result.RawRecoveredExecutableCandidateCount,
                ActionType = arrayName ?? string.Empty,
                SafePreview = string.Empty
            });

            bool typedLostFields = ResponseTypedLostRawFields(typedResponse, rawDirectFieldsFound);
            result.TypedDeserializationLostFields = typedLostFields;
            if (typedLostFields)
            {
                result.Diagnostics.Add(new ActionParseDiagnostic
                {
                    Severity = "WARN",
                    Code = "TYPED_DESERIALIZATION_LOST_FIELDS",
                    Message = "A desserialização tipada perdeu campos executáveis presentes no JSON bruto.",
                    ActionType = string.Empty,
                    SafePreview = string.Empty
                });
            }
        }

        private static bool ResponseTypedLostRawFields(AgentResponse typedResponse, bool rawDirectFieldsFound)
        {
            if (!rawDirectFieldsFound || typedResponse?.Acoes == null || typedResponse.Acoes.Count == 0)
                return false;

            return typedResponse.Acoes.Any(action =>
                action != null &&
                action.Tipo == AgentActionType.ArquivoLocal &&
                !TemDadosExecutaveisArquivoLocal(action.Dados));
        }

        private static ParsedAction BuildRawParsedAction(JObject rawAction, ActionParseResult result)
        {
            string type = GetFirstText(rawAction, "tipo", "type", "action", "acao") ?? "RawJson";
            string description = GetFirstText(rawAction, "descricao", "description", "mensagem", "message");
            string rawText = MaskSafe(rawAction.ToString(Formatting.None));
            JObject payload = GetActionPayloadObject(rawAction);
            bool hasDirectPatchFields = TemCamposDiretosPatchBruto(payload);
            bool hasExecutablePayload = (TemDadosExecutaveisArquivoLocalBruto(payload) || hasDirectPatchFields) &&
                                        !IsNoOpArquivoLocal(payload, BuildProtocolFromDirectFieldsRaw(payload));

            var parsed = new ParsedAction
            {
                Type = hasExecutablePayload ? "ArquivoLocal" : type,
                Description = description ?? string.Empty,
                Data = payload ?? rawAction,
                ProtocolText = hasDirectPatchFields ? BuildProtocolFromDirectFieldsRaw(payload) : GetFirstText(payload ?? rawAction, "protocolo", "comando", "bloco", "bloco_protocolo", "protocol", "command", "block") ?? string.Empty,
                RawActionJson = rawText,
                IsExecutable = hasExecutablePayload,
                Source = "RawJson",
                IsNoOp = !hasExecutablePayload && IsNoOpArquivoLocal(payload, GetFirstText(payload ?? rawAction, "protocolo", "comando", "bloco", "bloco_protocolo", "protocol", "command", "block") ?? string.Empty)
            };

            if (hasDirectPatchFields)
                parsed.Warnings.Add("Campos diretos de patch encontrados no JSON bruto.");
            else if (TemDadosExecutaveisArquivoLocalBruto(payload))
                parsed.Warnings.Add("Ação recuperável no JSON bruto.");

            return parsed;
        }

        private static JObject GetActionPayloadObject(JObject rawAction)
        {
            if (rawAction == null)
                return null;

            JToken payload = FindPropertyIgnoreCase(rawAction, "dados") ??
                             FindPropertyIgnoreCase(rawAction, "data") ??
                             FindPropertyIgnoreCase(rawAction, "payload");

            if (payload is JObject payloadObject)
                return payloadObject;

            return rawAction;
        }

        private static IEnumerable<string> GetSafeFieldNames(JObject rawAction)
        {
            if (rawAction == null)
                return new[] { "(nenhum)" };

            var names = new List<string>();
            foreach (var property in rawAction.Properties())
            {
                if (property == null || string.IsNullOrWhiteSpace(property.Name))
                    continue;

                string name = property.Name.Trim();
                if (name.IndexOf("sk-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("bearer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("authorization", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("apikey", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                names.Add(name);
            }

            if (names.Count == 0)
                return new[] { "(nenhum)" };

            return names;
        }

        private static string BuildProtocolFromDirectFieldsRaw(JObject dados)
        {
            if (dados == null)
                return string.Empty;

            string arq = GetFirstText(dados, "ARQ", "arq", "arquivo", "file", "path");
            if (string.IsNullOrWhiteSpace(arq))
                return string.Empty;

            string search = GetFirstText(dados, "SEARCH", "search");
            string replace = GetFirstText(dados, "REPLACE", "replace");
            string searchBlock = GetFirstText(dados, "SEARCH_BLOCK", "search_block");
            string replaceBlock = GetFirstText(dados, "REPLACE_BLOCK", "replace_block");

            var sb = new StringBuilder();
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

        private static bool TryGetActionsArray(JToken root, out JArray actionsArray, out string arrayName)
        {
            actionsArray = null;
            arrayName = string.Empty;

            if (root == null)
                return false;

            if (root.Type == JTokenType.Array)
            {
                actionsArray = root as JArray;
                arrayName = "(root)";
                return actionsArray != null;
            }

            JObject obj = root as JObject;
            if (obj == null)
                return false;

            foreach (string name in new[] { "acoes", "ações", "Acoes", "Actions", "actions" })
            {
                JToken token = FindPropertyIgnoreCase(obj, name);
                if (token != null && token.Type == JTokenType.Array)
                {
                    actionsArray = token as JArray;
                    arrayName = name;
                    return actionsArray != null;
                }
            }

            return false;
        }

        private static JObject TryExtractRawJson(string rawResponse, ActionParseResult result)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                result.Diagnostics.Add(new ActionParseDiagnostic
                {
                    Severity = "WARN",
                    Code = "RAW_JSON_NOT_FOUND",
                    Message = "Não foi possível extrair JSON bruto da resposta.",
                    ActionType = string.Empty,
                    SafePreview = string.Empty
                });
                return null;
            }

            JToken parsed;
            if (TryParseJsonToken(rawResponse, out parsed))
                return parsed as JObject ?? (parsed is JArray ? new JObject { ["acoes"] = parsed } : null);

            foreach (int start in FindJsonStartPositions(rawResponse))
            {
                string slice = ExtractBalancedJsonSlice(rawResponse, start);
                if (string.IsNullOrWhiteSpace(slice))
                    continue;

                if (TryParseJsonToken(slice, out parsed))
                    return parsed as JObject ?? (parsed is JArray ? new JObject { ["acoes"] = parsed } : null);
            }

            result.Diagnostics.Add(new ActionParseDiagnostic
            {
                Severity = "WARN",
                Code = "RAW_JSON_NOT_FOUND",
                Message = "Não foi possível extrair JSON bruto da resposta.",
                ActionType = string.Empty,
                SafePreview = ActionParseResult.SafePreview(rawResponse, 4000)
            });
            return null;
        }

        private static bool TryParseJsonToken(string text, out JToken token)
        {
            token = null;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            try
            {
                token = JToken.Parse(text.Trim());
                return token != null;
            }
            catch
            {
                token = null;
                return false;
            }
        }

        private static IEnumerable<int> FindJsonStartPositions(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            int brace = text.IndexOf('{');
            int bracket = text.IndexOf('[');

            if (brace >= 0 && bracket >= 0)
            {
                yield return Math.Min(brace, bracket);
                yield break;
            }

            if (brace >= 0)
                yield return brace;

            if (bracket >= 0)
                yield return bracket;
        }

        private static string ExtractBalancedJsonSlice(string text, int startIndex)
        {
            if (string.IsNullOrWhiteSpace(text) || startIndex < 0 || startIndex >= text.Length)
                return null;

            char open = text[startIndex];
            char close;
            if (open == '{')
                close = '}';
            else if (open == '[')
                close = ']';
            else
                return null;

            int depth = 0;
            bool inString = false;
            bool escape = false;

            for (int i = startIndex; i < text.Length; i++)
            {
                char c = text[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }

                    if (c == '"')
                        inString = false;

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == open)
                {
                    depth++;
                    continue;
                }

                if (c == close)
                {
                    depth--;
                    if (depth == 0)
                        return text.Substring(startIndex, i - startIndex + 1);
                }
            }

            return null;
        }

        private static bool TemCamposDiretosPatch(JObject dados)
        {
            if (dados == null)
                return false;

            return TemTextoNaoVazio(dados, "ARQ", "arq") &&
                   ((TemTextoNaoVazio(dados, "SEARCH", "search") && TemTextoNaoVazio(dados, "REPLACE", "replace")) ||
                    (TemTextoNaoVazio(dados, "SEARCH_BLOCK", "search_block") && TemTextoNaoVazio(dados, "REPLACE_BLOCK", "replace_block")));
        }

        private static bool TemCamposDiretosPatchBruto(JObject dados)
        {
            if (dados == null)
                return false;

            return TemTextoNaoVazio(dados, "ARQ", "arq", "arquivo", "file", "path") &&
                   ((TemTextoNaoVazio(dados, "SEARCH", "search") && TemTextoNaoVazio(dados, "REPLACE", "replace")) ||
                    (TemTextoNaoVazio(dados, "SEARCH_BLOCK", "search_block") && TemTextoNaoVazio(dados, "REPLACE_BLOCK", "replace_block")));
        }

        private static bool TemDadosExecutaveisArquivoLocal(JObject dados)
        {
            if (dados == null)
                return false;

            if (TemTextoNaoVazio(dados, "protocolo", "comando", "bloco", "bloco_protocolo"))
                return true;

            if (TemArrayNaoVazia(dados["comandos"]) || TemArrayNaoVazia(dados["commands"]))
                return true;

            return TemCamposDiretosPatch(dados);
        }

        private static bool TemDadosExecutaveisArquivoLocalBruto(JObject dados)
        {
            if (dados == null)
                return false;

            if (TemTextoNaoVazio(dados, "protocolo", "comando", "bloco", "bloco_protocolo"))
                return true;

            if (TemArrayNaoVazia(dados["comandos"]) || TemArrayNaoVazia(dados["commands"]))
                return true;

            return TemCamposDiretosPatchBruto(dados);
        }

        private static bool TemTextoNaoVazio(JObject dados, params string[] propertyNames)
        {
            if (dados == null || propertyNames == null)
                return false;

            foreach (string propertyName in propertyNames)
            {
                var token = FindPropertyIgnoreCase(dados, propertyName);
                if (token != null && !string.IsNullOrWhiteSpace(token.ToString()))
                    return true;
            }

            return false;
        }

        private static JToken FindPropertyIgnoreCase(JObject obj, string propertyName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            string normalizedTarget = NormalizeKey(propertyName);
            foreach (var property in obj.Properties())
            {
                if (property == null)
                    continue;

                if (string.Equals(NormalizeKey(property.Name), normalizedTarget, StringComparison.Ordinal))
                    return property.Value;
            }

            return null;
        }

        private static string GetFirstText(JObject obj, params string[] propertyNames)
        {
            if (obj == null || propertyNames == null)
                return null;

            foreach (string propertyName in propertyNames)
            {
                var token = FindPropertyIgnoreCase(obj, propertyName);
                if (token != null && !string.IsNullOrWhiteSpace(token.ToString()))
                    return token.ToString().Trim();
            }

            return null;
        }

        private static string NormalizeKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string normalized = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);

            foreach (char c in normalized)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static bool TemArrayNaoVazia(JToken token)
        {
            return token != null && token.Type == JTokenType.Array && token.HasValues;
        }

        private static bool HasTechnicalSignals(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return false;

            string[] signals =
            {
                "SEARCH_BLOCK",
                "REPLACE_BLOCK",
                "END_SEARCH",
                "END_REPLACE",
                "ARQ=",
                "ARQ",
                "SEARCH",
                "REPLACE",
                "dados",
                "protocolo",
                "comandos",
                "\"ARQ\"",
                "\"SEARCH\"",
                "\"REPLACE\""
            };

            return signals.Any(signal => rawResponse.IndexOf(signal, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool HasGitDiffFormat(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return false;

            int hits = 0;
            string[] signals =
            {
                "diff --git a/",
                "--- a/",
                "+++ b/",
                "@@"
            };

            foreach (string signal in signals)
            {
                if (rawResponse.IndexOf(signal, StringComparison.OrdinalIgnoreCase) >= 0)
                    hits++;
            }

            return hits >= 2;
        }

        private static string MaskSafe(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string result = text;
            result = Regex.Replace(result, @"sk-[A-Za-z0-9_\-]{8,}", "[REDACTED]", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"Bearer\s+[A-Za-z0-9_\-\.\=]{8,}", "Bearer [REDACTED]", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "(?i)(OpenAiApiKey|ApiKey|Authorization|KimiOpenAIApiKey)\\s*[:=]\\s*([^\\r\\n;,\\\"']+)", "$1: [REDACTED]", RegexOptions.IgnoreCase);
            return result;
        }

        private static void BuildCanonicalActions(AgentResponse response, string rawResponse, JObject rawJson, ActionParseResult result)
        {
            if (result == null)
                return;

            result.CanonicalActions.Clear();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (response?.Acoes != null)
            {
                foreach (var action in response.Acoes)
                {
                    var canonical = CanonicalizeTypedAction(action, result);
                    AddCanonicalAction(result, canonical, seen);
                }
            }

            if (result.RawActions != null)
            {
                foreach (var action in result.RawActions)
                {
                    var canonical = CanonicalizeParsedAction(action, result);
                    AddCanonicalAction(result, canonical, seen);
                }
            }
        }

        private static void ClassifyOutcome(AgentResponse response, string rawResponse, ActionParseResult result)
        {
            if (result == null)
                return;

            if (result.HasGitDiffFormat)
            {
                result.Diagnostics.Add(new ActionParseDiagnostic
                {
                    Severity = "ERROR",
                    Code = "GIT_DIFF_DETECTED",
                    Message = "A resposta contém diff unificado GitDiff.",
                    ActionType = string.Empty,
                    SafePreview = ActionParseResult.SafePreview(rawResponse, 1200)
                });
                SetOutcome(result, ActionParseOutcome.GitDiffDetected, "A resposta contém diff unificado GitDiff.");
                return;
            }

            bool hasExecutable = result.CanonicalActions.Any(a => a != null && a.IsExecutable);
            bool hasErrors = result.Diagnostics.Any(d => d != null && string.Equals(d.Severity, "ERROR", StringComparison.OrdinalIgnoreCase));
            bool hasInvalidLocalFile = result.Diagnostics.Any(d =>
                d != null &&
                string.Equals(d.Severity, "ERROR", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.Code, "LOCAL_FILE_EMPTY", StringComparison.OrdinalIgnoreCase));
            bool hasNoOp = result.CanonicalActions.Any(a => a != null && a.IsNoOp) ||
                           result.Diagnostics.Any(d => d != null && string.Equals(d.Code, "NO_OP_PATCH", StringComparison.OrdinalIgnoreCase));
            bool hasTechnicalSignals = result.HasTechnicalActionSignals;
            bool responseHasActions = response?.Acoes != null && response.Acoes.Count > 0;
            bool hasNoActionType = responseHasActions && response.Acoes.Any(a => a != null && IsNoActionType(a.Tipo.ToString()));
            bool legitNoActionMessage = HasLegitimateNoActionMessage(response, rawResponse);
            bool hasAnySignalText = HasAnyTechnicalSignalText(rawResponse);

            if (hasExecutable && !hasErrors)
            {
                SetOutcome(result, ActionParseOutcome.Executable, "Há pelo menos uma ação canônica executável sem erro estrutural bloqueante.");
                return;
            }

            if (hasInvalidLocalFile)
            {
                SetOutcome(result, ActionParseOutcome.InvalidLocalFile, "Existe ação ArquivoLocal sem dados executáveis.");
                return;
            }

            if (hasNoOp && !hasExecutable)
            {
                SetOutcome(result, ActionParseOutcome.NoOpOnly, "As ações detectadas não produzem alteração efetiva.");
                return;
            }

            if (hasTechnicalSignals && !hasExecutable)
            {
                SetOutcome(result, ActionParseOutcome.TechnicalSignalsWithoutActions, "A resposta contém sinais técnicos de patch, mas nenhuma ação executável foi gerada.");
                return;
            }

            if (!hasExecutable && !hasTechnicalSignals && !hasErrors && legitNoActionMessage && (!responseHasActions || hasNoActionType))
            {
                SetOutcome(result, ActionParseOutcome.LegitimateNoAction, "A resposta indica explicitamente que nenhuma alteração é necessária.");
                return;
            }

            if (!hasExecutable && !hasTechnicalSignals && !hasErrors && !hasAnySignalText)
            {
                SetOutcome(result, ActionParseOutcome.NoExecutableActions, "Há ações interpretadas, mas nenhuma é executável.");
                return;
            }

            SetOutcome(result, ActionParseOutcome.UnknownInvalidFormat, "A resposta contém sinais ou estruturas inconsistentes que não resultaram em execução segura.");
        }

        private static void SetOutcome(ActionParseResult result, ActionParseOutcome outcome, string reason)
        {
            if (result == null)
                return;

            result.Outcome = outcome;
            result.OutcomeReason = reason ?? string.Empty;
        }

        private static bool HasLegitimateNoActionMessage(AgentResponse response, string rawResponse)
        {
            var texts = new[]
            {
                response?.MensagemUsuario,
                response?.Explicacao,
                rawResponse
            };

            foreach (string text in texts)
            {
                if (ContainsNoActionPhrase(text))
                    return true;
            }

            return false;
        }

        private static bool ContainsNoActionPhrase(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = NormalizeForComparison(text);
            string[] phrases =
            {
                "nenhuma alteracao necessaria",
                "nao ha alteracao necessaria",
                "nao e necessario alterar",
                "sem alteracao",
                "nada precisa ser feito",
                "nenhuma mudanca necessaria",
                "sem modificacao",
                "nao ha necessidade de alterar",
                "nao ha nada a fazer",
                "no changes",
                "nothing to change"
            };

            return phrases.Any(phrase => normalized.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool HasAnyTechnicalSignalText(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return false;

            string[] signals =
            {
                "ArquivoLocal",
                "ComandoDos",
                "SEARCH_BLOCK",
                "REPLACE_BLOCK",
                "END_SEARCH",
                "END_REPLACE",
                "ARQ=",
                "SEARCH=",
                "REPLACE=",
                "\"acoes\"",
                "\"tipo\""
            };

            return signals.Any(signal => rawResponse.IndexOf(signal, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string NormalizeForComparison(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string normalized = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (char c in normalized)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static void AddCanonicalAction(ActionParseResult result, ParsedAction action, HashSet<string> seen)
        {
            if (result == null || action == null)
                return;

            string key = BuildCanonicalActionKey(action);
            if (!seen.Add(key))
                return;

            result.CanonicalActions.Add(action);
        }

        private static string BuildCanonicalActionKey(ParsedAction action)
        {
            if (action == null)
                return string.Empty;

            return (action.Type ?? string.Empty) + "|" +
                   (action.ProtocolText ?? string.Empty) + "|" +
                   (action.Description ?? string.Empty);
        }

        private static ParsedAction CanonicalizeTypedAction(AgentAction action, ActionParseResult result)
        {
            if (action == null)
            {
                return new ParsedAction
                {
                    Type = string.Empty,
                    IsExecutable = false,
                    Source = "TypedResponse"
                };
            }

            string type = NormalizeActionTypeName(action.Tipo.ToString());
            if (IsNoActionType(type))
            {
                return new ParsedAction
                {
                    Type = "Nenhuma",
                    Description = action.Descricao ?? string.Empty,
                    Data = action.Dados == null ? null : (JObject)action.Dados.DeepClone(),
                    RawActionJson = MaskSafe(action.Dados == null ? string.Empty : action.Dados.ToString(Formatting.None)),
                    ProtocolText = string.Empty,
                    IsExecutable = false,
                    Source = "TypedResponse",
                    WasNormalized = !string.Equals(type, action.Tipo.ToString(), StringComparison.OrdinalIgnoreCase)
                };
            }

            if (type == "ArquivoLocal")
                return CanonicalizeArquivoLocal(action.Dados, action.Descricao, "TypedResponse", result);

            return new ParsedAction
            {
                Type = type,
                Description = action.Descricao ?? string.Empty,
                Data = action.Dados == null ? null : (JObject)action.Dados.DeepClone(),
                RawActionJson = MaskSafe(action.Dados == null ? string.Empty : action.Dados.ToString(Formatting.None)),
                ProtocolText = string.Empty,
                IsExecutable = action.Tipo == AgentActionType.ComandoDos || action.Tipo == AgentActionType.Ftp,
                Source = "TypedResponse",
                WasNormalized = !string.Equals(type, action.Tipo.ToString(), StringComparison.OrdinalIgnoreCase)
            };
        }

        private static ParsedAction CanonicalizeParsedAction(ParsedAction parsed, ActionParseResult result)
        {
            if (parsed == null)
                return null;

            string type = NormalizeActionTypeName(parsed.Type);
            if (IsNoActionType(type))
            {
                return new ParsedAction
                {
                    Type = "Nenhuma",
                    Description = parsed.Description ?? string.Empty,
                    Data = parsed.Data == null ? null : (JObject)parsed.Data.DeepClone(),
                    RawActionJson = MaskSafe(parsed.RawActionJson),
                    ProtocolText = string.Empty,
                    IsExecutable = false,
                    Source = parsed.Source ?? "RawJson",
                    WasNormalized = parsed.WasNormalized || !string.Equals(type, parsed.Type ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                };
            }

            if (type == "ArquivoLocal" || HasDirectPatchLikeFields(parsed.Data))
                return CanonicalizeArquivoLocal(parsed.Data, parsed.Description, parsed.Source ?? "RawJson", result);

            return new ParsedAction
            {
                Type = type,
                Description = parsed.Description ?? string.Empty,
                Data = parsed.Data == null ? null : (JObject)parsed.Data.DeepClone(),
                RawActionJson = MaskSafe(parsed.RawActionJson),
                ProtocolText = parsed.ProtocolText ?? string.Empty,
                IsExecutable = parsed.IsExecutable,
                Source = parsed.Source ?? "RawJson",
                WasNormalized = parsed.WasNormalized || !string.Equals(type, parsed.Type ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            };
        }

        private static ParsedAction CanonicalizeArquivoLocal(JObject dados, string descricao, string source, ActionParseResult result)
        {
            if (TryNormalizeArquivoLocal(dados, out string protocolo, out string motivo, out bool foiNormalizado))
            {
                bool isNoOp = IsNoOpArquivoLocal(dados, protocolo);
                if (isNoOp)
                {
                    if (result != null)
                    {
                        result.Diagnostics.Add(new ActionParseDiagnostic
                        {
                            Severity = "WARN",
                            Code = "NO_OP_PATCH",
                            Message = "Patch sem efeito detectado: SEARCH e REPLACE idênticos.",
                            ActionType = "ArquivoLocal",
                            SafePreview = MaskSafe(dados == null ? string.Empty : dados.ToString(Formatting.None))
                        });
                    }

                    return new ParsedAction
                    {
                        Type = "ArquivoLocal",
                        Description = descricao ?? string.Empty,
                        Data = new JObject
                        {
                            ["protocolo"] = protocolo
                        },
                        RawActionJson = MaskSafe(dados == null ? string.Empty : dados.ToString(Formatting.None)),
                        ProtocolText = protocolo,
                        IsExecutable = false,
                        IsNoOp = true,
                        Source = source ?? "RawJson",
                        WasNormalized = foiNormalizado
                    };
                }

                if (result != null)
                {
                    result.Diagnostics.Add(new ActionParseDiagnostic
                    {
                        Severity = "INFO",
                        Code = motivo,
                        Message = ObterMensagemNormalizacao(motivo),
                        ActionType = "ArquivoLocal",
                        SafePreview = MaskSafe(dados == null ? string.Empty : dados.ToString(Formatting.None))
                    });
                }

                return new ParsedAction
                {
                    Type = "ArquivoLocal",
                    Description = descricao ?? string.Empty,
                    Data = new JObject
                    {
                        ["protocolo"] = protocolo
                    },
                    RawActionJson = MaskSafe(dados == null ? string.Empty : dados.ToString(Formatting.None)),
                    ProtocolText = protocolo,
                    IsExecutable = true,
                    IsNoOp = false,
                    Source = source ?? "RawJson",
                    WasNormalized = foiNormalizado
                };
            }

            if (result != null)
            {
                result.Diagnostics.Add(new ActionParseDiagnostic
                {
                    Severity = "ERROR",
                    Code = "LOCAL_FILE_EMPTY",
                    Message = "Ação ArquivoLocal sem dados executáveis.",
                    ActionType = "ArquivoLocal",
                    SafePreview = MaskSafe(dados == null ? string.Empty : dados.ToString(Formatting.None))
                });
            }

            return new ParsedAction
            {
                Type = "ArquivoLocal",
                Description = descricao ?? string.Empty,
                Data = dados == null ? null : (JObject)dados.DeepClone(),
                RawActionJson = MaskSafe(dados == null ? string.Empty : dados.ToString(Formatting.None)),
                ProtocolText = string.Empty,
                IsExecutable = false,
                IsNoOp = false,
                Source = source ?? "RawJson",
                WasNormalized = false
            };
        }

        private static bool TryNormalizeArquivoLocal(JObject dados, out string protocolo, out string motivo, out bool foiNormalizado)
        {
            protocolo = string.Empty;
            motivo = string.Empty;
            foiNormalizado = false;

            if (dados == null)
                return false;

            string arq = GetFirstText(dados, "ARQ", "arq", "arquivo", "Arquivo", "file", "path");
            string protocoloDireto = GetFirstText(dados, "protocolo", "protocol");
            string comando = GetFirstText(dados, "comando", "command");
            string bloco = GetFirstText(dados, "bloco", "block");
            string search = GetFirstText(dados, "SEARCH", "search");
            string replace = GetFirstText(dados, "REPLACE", "replace");
            string searchBlock = GetFirstText(dados, "SEARCH_BLOCK", "search_block", "searchBlock");
            string replaceBlock = GetFirstText(dados, "REPLACE_BLOCK", "replace_block", "replaceBlock");
            JToken comandos = GetTokenByNames(dados, "comandos", "commands");

            if (!string.IsNullOrWhiteSpace(protocoloDireto))
            {
                protocolo = protocoloDireto.Trim();
                motivo = "LOCAL_FILE_PROTOCOL_NORMALIZED";
                foiNormalizado = false;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(comando))
            {
                protocolo = comando.Trim();
                motivo = "LOCAL_FILE_COMMAND_NORMALIZED";
                foiNormalizado = true;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(bloco))
            {
                protocolo = bloco.Trim();
                motivo = "LOCAL_FILE_BLOCK_NORMALIZED";
                foiNormalizado = true;
                return true;
            }

            if (comandos != null && comandos.Type == JTokenType.Array && comandos.HasValues)
            {
                protocolo = BuildProtocolFromCommandsToken((JArray)comandos);
                if (!string.IsNullOrWhiteSpace(protocolo))
                {
                    motivo = "LOCAL_FILE_COMMANDS_NORMALIZED";
                    foiNormalizado = true;
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(arq) &&
                (!string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(replace) ||
                 !string.IsNullOrWhiteSpace(searchBlock) || !string.IsNullOrWhiteSpace(replaceBlock)))
            {
                protocolo = BuildProtocolFromDirectFieldsAny(dados);
                if (!string.IsNullOrWhiteSpace(protocolo))
                {
                    motivo = "DIRECT_PATCH_FIELDS_NORMALIZED";
                    foiNormalizado = true;
                    return true;
                }
            }

            return false;
        }

        private static string BuildProtocolFromCommandsToken(JArray commands)
        {
            if (commands == null || commands.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (JToken item in commands)
            {
                if (item == null)
                    continue;

                if (item.Type == JTokenType.String)
                {
                    string texto = item.Value<string>();
                    if (!string.IsNullOrWhiteSpace(texto))
                        sb.AppendLine(texto.Trim());
                    continue;
                }

                JObject obj = item as JObject;
                if (obj == null)
                    continue;

                string comando = GetFirstText(obj, "comando", "command");
                if (!string.IsNullOrWhiteSpace(comando))
                {
                    sb.AppendLine(comando.Trim());
                    continue;
                }

                string fragmento = BuildProtocolFromDirectFieldsAny(obj);
                if (!string.IsNullOrWhiteSpace(fragmento))
                {
                    sb.AppendLine(fragmento);
                    continue;
                }

                string protocolo = GetFirstText(obj, "protocolo", "protocol", "bloco", "block");
                if (!string.IsNullOrWhiteSpace(protocolo))
                    sb.AppendLine(protocolo.Trim());
            }

            return sb.ToString().Trim();
        }

        private static string BuildProtocolFromDirectFieldsAny(JObject dados)
        {
            if (dados == null)
                return string.Empty;

            string arq = GetFirstText(dados, "ARQ", "arq", "arquivo", "Arquivo", "file", "path");
            if (string.IsNullOrWhiteSpace(arq))
                return string.Empty;

            string search = GetFirstText(dados, "SEARCH", "search");
            string replace = GetFirstText(dados, "REPLACE", "replace");
            string searchBlock = GetFirstText(dados, "SEARCH_BLOCK", "search_block", "searchBlock");
            string replaceBlock = GetFirstText(dados, "REPLACE_BLOCK", "replace_block", "replaceBlock");

            var sb = new StringBuilder();
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

        private static bool IsNoOpArquivoLocal(JObject dados, string protocolo)
        {
            if (dados != null)
            {
                string search = GetFirstText(dados, "SEARCH", "search");
                string replace = GetFirstText(dados, "REPLACE", "replace");
                if (!string.IsNullOrWhiteSpace(search) && !string.IsNullOrWhiteSpace(replace) &&
                    string.Equals(NormalizeForComparison(search), NormalizeForComparison(replace), StringComparison.OrdinalIgnoreCase))
                    return true;

                string searchBlock = GetFirstText(dados, "SEARCH_BLOCK", "search_block", "searchBlock");
                string replaceBlock = GetFirstText(dados, "REPLACE_BLOCK", "replace_block", "replaceBlock");
                if (!string.IsNullOrWhiteSpace(searchBlock) && !string.IsNullOrWhiteSpace(replaceBlock) &&
                    string.Equals(NormalizeBlockForComparison(searchBlock), NormalizeBlockForComparison(replaceBlock), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (string.IsNullOrWhiteSpace(protocolo))
                return false;

            if (TryExtractSearchReplaceFromProtocol(protocolo, out string searchProto, out string replaceProto) &&
                string.Equals(NormalizeForComparison(searchProto), NormalizeForComparison(replaceProto), StringComparison.OrdinalIgnoreCase))
                return true;

            if (TryExtractBlockReplaceFromProtocol(protocolo, out string searchBlockProto, out string replaceBlockProto) &&
                string.Equals(NormalizeBlockForComparison(searchBlockProto), NormalizeBlockForComparison(replaceBlockProto), StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static string NormalizeBlockForComparison(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            return Regex.Replace(normalized, @"\s+", " ");
        }

        private static bool TryExtractSearchReplaceFromProtocol(string protocolo, out string search, out string replace)
        {
            search = string.Empty;
            replace = string.Empty;

            if (string.IsNullOrWhiteSpace(protocolo))
                return false;

            Match searchMatch = Regex.Match(protocolo, @"SEARCH\s*=\s*(?<v>.*?)(?:\r?\nREPLACE\s*=|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            Match replaceMatch = Regex.Match(protocolo, @"REPLACE\s*=\s*(?<v>.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!searchMatch.Success || !replaceMatch.Success)
                return false;

            search = searchMatch.Groups["v"].Value.Trim();
            replace = replaceMatch.Groups["v"].Value.Trim();
            return true;
        }

        private static bool TryExtractBlockReplaceFromProtocol(string protocolo, out string searchBlock, out string replaceBlock)
        {
            searchBlock = string.Empty;
            replaceBlock = string.Empty;

            if (string.IsNullOrWhiteSpace(protocolo))
                return false;

            Match searchMatch = Regex.Match(protocolo, @"SEARCH_BLOCK\s*(?<v>.*?)(?:\r?\nEND_SEARCH|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            Match replaceMatch = Regex.Match(protocolo, @"REPLACE_BLOCK\s*(?<v>.*?)(?:\r?\nEND_REPLACE|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!searchMatch.Success || !replaceMatch.Success)
                return false;

            searchBlock = searchMatch.Groups["v"].Value.Trim();
            replaceBlock = replaceMatch.Groups["v"].Value.Trim();
            return true;
        }

        private static JToken GetTokenByNames(JObject dados, params string[] propertyNames)
        {
            if (dados == null || propertyNames == null)
                return null;

            foreach (string propertyName in propertyNames)
            {
                var token = FindPropertyIgnoreCase(dados, propertyName);
                if (token != null)
                    return token;
            }

            return null;
        }

        private static string NormalizeActionTypeName(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return string.Empty;

            string normalized = type.Trim().Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
            switch (normalized)
            {
                case "arquivolocal":
                case "localfile":
                case "fileedit":
                case "editfile":
                case "patchfile":
                    return "ArquivoLocal";
                case "nenhuma":
                case "none":
                case "noaction":
                case "semacao":
                case "semacão":
                case "noop":
                    return "Nenhuma";
                case "comandodos":
                    return "ComandoDos";
                case "ftp":
                    return "Ftp";
                default:
                    return type.Trim();
            }
        }

        private static bool IsNoActionType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return true;

            switch (NormalizeActionTypeName(type).ToLowerInvariant())
            {
                case "nenhuma":
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasDirectPatchLikeFields(JObject dados)
        {
            if (dados == null)
                return false;

            return !string.IsNullOrWhiteSpace(GetFirstText(dados, "ARQ", "arq", "arquivo", "Arquivo", "file", "path")) &&
                   ((!string.IsNullOrWhiteSpace(GetFirstText(dados, "SEARCH", "search")) &&
                     !string.IsNullOrWhiteSpace(GetFirstText(dados, "REPLACE", "replace"))) ||
                    (!string.IsNullOrWhiteSpace(GetFirstText(dados, "SEARCH_BLOCK", "search_block", "searchBlock")) &&
                     !string.IsNullOrWhiteSpace(GetFirstText(dados, "REPLACE_BLOCK", "replace_block", "replaceBlock"))));
        }

        private static string ObterMensagemNormalizacao(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo))
                return string.Empty;

            switch (codigo)
            {
                case "LOCAL_FILE_PROTOCOL_NORMALIZED":
                    return "ArquivoLocal normalizado por dados.protocolo.";
                case "LOCAL_FILE_COMMAND_NORMALIZED":
                    return "ArquivoLocal normalizado por dados.comando.";
                case "LOCAL_FILE_BLOCK_NORMALIZED":
                    return "ArquivoLocal normalizado por dados.bloco.";
                case "LOCAL_FILE_COMMANDS_NORMALIZED":
                    return "ArquivoLocal normalizado por dados.comandos.";
                case "DIRECT_PATCH_FIELDS_NORMALIZED":
                    return "Ação com campos diretos normalizada para ArquivoLocal.";
                default:
                    return "Ação ArquivoLocal executável identificada.";
            }
        }
    }
}
