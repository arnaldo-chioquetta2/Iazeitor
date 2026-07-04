using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GptBolDll
{
    public enum ActionParseOutcome
    {
        Executable,
        LegitimateNoAction,
        NoExecutableActions,
        TechnicalSignalsWithoutActions,
        InvalidLocalFile,
        NoOpOnly,
        GitDiffDetected,
        UnknownInvalidFormat
    }

    public sealed class ActionParseResult
    {
        public List<ParsedAction> Actions { get; } = new List<ParsedAction>();
        public List<ParsedAction> RawActions { get; } = new List<ParsedAction>();
        public List<ParsedAction> CanonicalActions { get; } = new List<ParsedAction>();
        public List<ActionParseDiagnostic> Diagnostics { get; } = new List<ActionParseDiagnostic>();
        public ActionParseOutcome Outcome { get; set; } = ActionParseOutcome.UnknownInvalidFormat;
        public string OutcomeReason { get; set; }
        public string RawResponseSafePreview { get; set; }
        public string GitDiffEffectiveText { get; set; }
        public bool GitDiffWrapperJsonDetected { get; set; }
        public bool GitDiffExtractedFromJsonWrapper { get; set; }
        public bool GitDiffJsonWrapperCandidateDetected { get; set; }
        public bool GitDiffJsonWrapperIncompleteRecovered { get; set; }
        public bool GitDiffPrefixDiscardedBeforeDiff { get; set; }
        public bool GitDiffUsedFirstDiffFallback { get; set; }
        public bool GitDiffDiffEscapedTextDetected { get; set; }
        public int GitDiffWrapperFieldCount { get; set; }
        public int GitDiffExtractedBlockCount { get; set; }
        public List<string> GitDiffJsonWrapperFieldsFound { get; } = new List<string>();
        public bool HasTechnicalActionSignals { get; set; }
        public bool HasGitDiffFormat { get; set; }
        public int RawRecoveredActionCount { get; set; }
        public int RawRecoveredExecutableCandidateCount { get; set; }
        public bool RawJsonExtracted { get; set; }
        public bool RawDirectPatchFieldsFound { get; set; }
        public bool TypedDeserializationLostFields { get; set; }

        public bool HasExecutableActions => Actions.Any(a => a != null && a.IsExecutable);
        public int ExecutableCount => Actions.Count(a => a != null && a.IsExecutable);
        public bool HasCanonicalExecutableActions => CanonicalActions.Any(a => a != null && a.IsExecutable);
        public int CanonicalExecutableCount => CanonicalActions.Count(a => a != null && a.IsExecutable);
        public bool HasNoOpActions => Actions.Any(a => a != null && a.IsNoOp) ||
                                      RawActions.Any(a => a != null && a.IsNoOp) ||
                                      CanonicalActions.Any(a => a != null && a.IsNoOp);
        public int NoOpCount => Actions.Count(a => a != null && a.IsNoOp) +
                                RawActions.Count(a => a != null && a.IsNoOp) +
                                CanonicalActions.Count(a => a != null && a.IsNoOp);
        public bool HasErrors => Diagnostics.Any(d => string.Equals(d?.Severity, "ERROR", StringComparison.OrdinalIgnoreCase));

        public string BuildUserSafeSummary()
        {
            string outcome = Outcome.ToString();
            string mainError = Diagnostics.FirstOrDefault(d => string.Equals(d?.Severity, "ERROR", StringComparison.OrdinalIgnoreCase))?.Message;
            if (string.IsNullOrWhiteSpace(mainError))
                mainError = OutcomeReason;

            string mainDiagnostic = Diagnostics.FirstOrDefault()?.Message;
            if (string.IsNullOrWhiteSpace(mainDiagnostic))
                mainDiagnostic = OutcomeReason;

            var parts = new List<string>
            {
                "ParserAct: " + outcome + ".",
                "Ações recebidas: " + Actions.Count + ".",
                "Ações canônicas: " + CanonicalActions.Count + ".",
                "Ações executáveis: " + ExecutableCount + "."
            };

            if (!string.IsNullOrWhiteSpace(mainError))
                parts.Add("Erro principal: " + mainError.Trim());
            else if (!string.IsNullOrWhiteSpace(mainDiagnostic))
                parts.Add("Diagnóstico principal: " + mainDiagnostic.Trim());

            return string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        public string GetUserFriendlyErrorMessage()
        {
            switch (Outcome)
            {
                case ActionParseOutcome.TechnicalSignalsWithoutActions:
                    return "Resposta contém sinais de ação, mas nenhuma ação executável foi interpretada.";
                case ActionParseOutcome.InvalidLocalFile:
                    return "A IA retornou ação de arquivo local sem dados executáveis.";
                case ActionParseOutcome.NoOpOnly:
                    return "A IA retornou apenas patch sem efeito.";
                case ActionParseOutcome.GitDiffDetected:
                    return "A IA retornou diff unificado GitDiff, que ainda não é suportado nesta etapa.";
                case ActionParseOutcome.UnknownInvalidFormat:
                    return "A IA retornou formato de ação inválido.";
                case ActionParseOutcome.NoExecutableActions:
                    return "A IA retornou ações, mas nenhuma delas é executável.";
                case ActionParseOutcome.LegitimateNoAction:
                    return "Nenhuma alteração necessária.";
                case ActionParseOutcome.Executable:
                    return null;
                default:
                    return OutcomeReason;
            }
        }

        public IEnumerable<string> GetDiagnosticsLogLines()
        {
            foreach (var diagnostic in Diagnostics)
            {
                if (diagnostic == null)
                    continue;

                string severity = string.IsNullOrWhiteSpace(diagnostic.Severity) ? "INFO" : diagnostic.Severity.Trim().ToUpperInvariant();
                string code = string.IsNullOrWhiteSpace(diagnostic.Code) ? "UNKNOWN" : diagnostic.Code.Trim();
                string message = string.IsNullOrWhiteSpace(diagnostic.Message) ? string.Empty : diagnostic.Message.Trim();
                yield return "[PARSER-PIPELINE] " + severity + " " + code + ": " + message;
            }
        }

        public static ActionParseResult Create(string rawResponse, AgentResponse response, bool hasTechnicalSignals = false)
        {
            var result = new ActionParseResult
            {
                RawResponseSafePreview = SafePreview(rawResponse, 4000),
                HasTechnicalActionSignals = hasTechnicalSignals
            };

            if (response?.Acoes != null)
            {
                foreach (var action in response.Acoes)
                {
                    result.Actions.Add(new ParsedAction
                    {
                        Type = action?.Tipo.ToString(),
                        IsExecutable = action != null && action.Tipo == AgentActionType.ArquivoLocal ||
                                       action != null && action.Tipo == AgentActionType.ComandoDos ||
                                       action != null && action.Tipo == AgentActionType.Ftp,
                        Data = action?.Dados,
                        ProtocolText = action?.Dados == null ? string.Empty : action.Dados.ToString(Newtonsoft.Json.Formatting.None),
                        Description = action?.Descricao,
                        RawActionJson = action?.Dados == null ? string.Empty : action.Dados.ToString(Newtonsoft.Json.Formatting.None)
                    });
                }
            }

            return result;
        }

        public static string SafePreview(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string result = text;
            result = Regex.Replace(result, @"sk-[A-Za-z0-9_\-]{8,}", "[REDACTED]", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"Bearer\s+[A-Za-z0-9_\-\.\=]{8,}", "Bearer [REDACTED]", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "(?i)(OpenAiApiKey|ApiKey|Authorization|KimiOpenAIApiKey|OPENAI_API_KEY|KIMI_API_KEY|api_key|authorization)\\s*[:=]\\s*(\"[^\"]*\"|'[^']*'|[^\\r\\n;,\\\"']+)", "$1: [REDACTED]", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "(?i)(\"(?:OpenAiApiKey|ApiKey|Authorization|KimiOpenAIApiKey|OPENAI_API_KEY|KIMI_API_KEY|api_key|authorization)\"\\s*:\\s*)(\"[^\"]*\"|'[^']*'|[^\\r\\n,}]+)", "$1[REDACTED]", RegexOptions.IgnoreCase);

            if (maxChars > 0 && result.Length > maxChars)
                return result.Substring(0, maxChars);

            return result;
        }
    }
}
