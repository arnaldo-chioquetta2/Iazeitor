using Newtonsoft.Json.Linq;
using System.Linq;
using System;
using System.IO;

namespace GptBolDll
{
    public sealed class ToolDispatcher : IToolDispatcher
    {
        private ExecutionLogger _log;
        private readonly Func<AgentAction, string, bool> _confirm;
        private readonly Func<string, FtpProfile> _ftpProfileProvider;
        public bool AutoVerificationEnabled { get; set; }

        public ToolDispatcher(
            ExecutionLogger log = null,
            Func<AgentAction, string, bool> confirm = null,
            Func<string, FtpProfile> ftpProfileProvider = null)
        {
            _log = log ?? new ExecutionLogger();
            _confirm = confirm;
            _ftpProfileProvider = ftpProfileProvider;
        }

        public void SetLogger(ExecutionLogger logger)
        {
            _log = logger ?? new ExecutionLogger();
        }

        public ToolDispatchResult Dispatch(AgentResponse response, string projectRoot)
        {
            var result = new ToolDispatchResult();

            if (response == null || response.Acoes == null || response.Acoes.Count == 0)
            {
                _log.Info("Nenhuma acao para executar.");
                return result;
            }

            for (int i = 0; i < response.Acoes.Count; i++)
            {
                var action = response.Acoes[i];
                try
                {
                    _log.Info("Executando acao " + (i + 1) + "/" + response.Acoes.Count + ": " + FormatActionSummary(action));
                    if (Dispatch(action, projectRoot))
                        result.ExecutedActions.Add(action);
                }
                catch (Exception ex)
                {
                    result.FailedActionIndex = i;
                    result.FailedAction = action;
                    result.Error = "Falha na acao " + (i + 1) + "/" + response.Acoes.Count + ": " + ex.Message + Environment.NewLine + FormatActionDetails(action);
                    _log.Error(result.Error);
                    _log.Error(ex.ToString());
                    break;
                }
            }

            return result;
        }

        public bool Dispatch(AgentAction action, string projectRoot)
        {
            if (action == null)
                return false;

            if (NeedsConfirmation(action) && !Confirm(action, projectRoot))
            {
                _log.PermissionDenied("Acao cancelada pelo usuario.");
                return false;
            }

            switch (action.Tipo)
            {
                case AgentActionType.ArquivoLocal:
                    return DispatchLocalFile(action, projectRoot);
                case AgentActionType.ComandoDos:
                    DispatchDos(action, projectRoot);
                    return true;
                case AgentActionType.Ftp:
                    DispatchFtp(action, projectRoot);
                    return true;
                default:
                    _log.Info("Nenhuma acao executada.");
                    return false;
            }
        }

        private bool DispatchLocalFile(AgentAction action, string projectRoot)
        {
            if (action == null)
                throw new InvalidOperationException("Ação inválida recebida pelo dispatcher. O ParserAct deveria ter bloqueado este formato.");

            if (action.Dados == null)
            {
                _log.Info("[PARSER] Ação ArquivoLocal sem dados foi rejeitada antes da execução.");
                throw new InvalidOperationException("Ação ArquivoLocal inválida: dados ausentes.");
            }

            bool isCanonical = IsCanonicalPayload(action.Dados);
            if (isCanonical)
            {
                _log.Info("[TOOL-DISPATCHER] Ação canônica recebida do ActionParserPipeline.");

                string canonicalProtocol = GetString(action.Dados, "protocolo");
                if (string.IsNullOrWhiteSpace(canonicalProtocol))
                    throw new InvalidOperationException("Ação canônica inválida: protocolo ausente.");

                if (canonicalProtocol.IndexOf("ARQ=", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidOperationException("Ação canônica inválida: ARQ ausente no protocolo.");

                _log.Info("[TOOL-DISPATCHER] Protocolo canônico validado.");
                canonicalProtocol = NormalizarDeleteOnlyGitDiffCanonico(action.Dados, canonicalProtocol);
                _log.Info("Executando protocolo de arquivo local.");
                var canonicalEngine = new AtcIaProtocolEngine(message => _log.Info(message));
                canonicalEngine.Apply(canonicalProtocol, projectRoot);

                if (canonicalEngine.LastChangedFiles == null || canonicalEngine.LastChangedFiles.Count == 0)
                {
                    _log.Info("Acao ArquivoLocal executada, mas nenhum arquivo teve alteracao real.");
                    return false;
                }

                return true;
            }

            if (!isCanonical && (string.IsNullOrWhiteSpace(GetString(action.Dados, "protocolo")) ||
                action.Dados["comando"] != null || action.Dados["bloco"] != null || TemArrayNaoVazia(action.Dados["comandos"]) ||
                TemArrayNaoVazia(action.Dados["commands"]) || action.Dados["ARQ"] != null || action.Dados["arq"] != null ||
                action.Dados["SEARCH"] != null || action.Dados["search"] != null || action.Dados["REPLACE"] != null || action.Dados["replace"] != null ||
                action.Dados["SEARCH_BLOCK"] != null || action.Dados["search_block"] != null || action.Dados["REPLACE_BLOCK"] != null || action.Dados["replace_block"] != null))
                _log.Info("[WARN] [TOOL-DISPATCHER] Compatibilidade antiga usada para normalizar ArquivoLocal. Migrar para ActionParserPipeline.");

            if (action == null || action.Dados == null || !TemDadosExecutaveis(action.Dados))
            {
                _log.Info("[PARSER] AÃ§Ã£o ArquivoLocal sem dados foi rejeitada antes da execuÃ§Ã£o.");
                throw new InvalidOperationException("Ação inválida recebida pelo dispatcher. O ParserAct deveria ter bloqueado este formato.");
            }

            var protocol = GetString(action.Dados, "protocolo");
            if (string.IsNullOrWhiteSpace(protocol))
                protocol = GetString(action.Dados, "comando");

            if (string.IsNullOrWhiteSpace(protocol))
                protocol = BuildProtocolFromCommandList(action.Dados);

            if (string.IsNullOrWhiteSpace(protocol))
                protocol = BuildProtocolFromStructuredData(action.Dados);

            protocol = NormalizarProtocoloArquivoLocal(action.Dados, protocol);

            if (string.IsNullOrWhiteSpace(protocol))
                throw new InvalidOperationException("Acao de arquivo local sem protocolo.");

            _log.Info("Executando protocolo de arquivo local.");
            var engine = new AtcIaProtocolEngine(message => _log.Info(message));
            engine.Apply(protocol, projectRoot);

            if (engine.LastChangedFiles == null || engine.LastChangedFiles.Count == 0)
            {
                _log.Info("Acao ArquivoLocal executada, mas nenhum arquivo teve alteracao real.");
                return false;
            }

            return true;
        }
        private string NormalizarProtocoloArquivoLocal(JObject data, string protocol)
        {
            if (data == null)
            {
                _log.Info("[PARSER] Ação ArquivoLocal sem dados foi rejeitada antes da execução.");
                throw new InvalidOperationException("Ação ArquivoLocal inválida: dados ausentes.");
            }

            string protocoloAtual = protocol ?? string.Empty;
            string arqSeparado = GetStringAny(data, "ARQ", "arq");
            string blocoSeparado = GetStringAny(data, "bloco", "bloco_protocolo");

            if (protocoloAtual.IndexOf("ARQ=", StringComparison.OrdinalIgnoreCase) >= 0)
                return protocoloAtual;

            if (string.IsNullOrWhiteSpace(arqSeparado))
            {
                if (!string.IsNullOrWhiteSpace(blocoSeparado))
                {
                    string blocoAtual = blocoSeparado.Trim();
                    if (blocoAtual.IndexOf("ARQ=", StringComparison.OrdinalIgnoreCase) >= 0)
                        return blocoAtual;
                }

                return protocoloAtual;
            }

            if (!string.IsNullOrWhiteSpace(protocoloAtual))
            {
                string trimmed = protocoloAtual.TrimStart();
                if (trimmed.StartsWith("SEARCH", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("REPLACE", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("SEARCH_BLOCK", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("REPLACE_BLOCK", StringComparison.OrdinalIgnoreCase))
                {
                    _log.Info("[ARQ] Campo ARQ separado incorporado ao protocolo.");
                    return "ARQ=" + arqSeparado.Trim() + Environment.NewLine + protocoloAtual;
                }
            }

            if (!string.IsNullOrWhiteSpace(blocoSeparado))
            {
                string blocoAtual = blocoSeparado.Trim();
                if (blocoAtual.IndexOf("ARQ=", StringComparison.OrdinalIgnoreCase) >= 0)
                    return blocoAtual;

                _log.Info("[ARQ] Protocolo montado a partir de dados.bloco.");
                return "ARQ=" + arqSeparado.Trim() + Environment.NewLine + blocoAtual;
            }

            string blocoBusca = ExtrairBlocoParaProtocolo(data, "SEARCH_BLOCK", "search_block", "lines", "lines_search");
            string blocoReplaces = ExtrairBlocoParaProtocolo(data, "REPLACE_BLOCK", "replace_block", "new_lines", "new_lines_search");

            if (string.IsNullOrWhiteSpace(blocoBusca) || string.IsNullOrWhiteSpace(blocoReplaces))
                return protocoloAtual;

            _log.Info("[ARQ] Protocolo montado a partir de campos SEARCH_BLOCK/lines/new_lines.");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ARQ=" + arqSeparado.Trim());
            sb.AppendLine("SEARCH_BLOCK");
            sb.AppendLine(blocoBusca);
            sb.AppendLine("END_SEARCH");
            sb.AppendLine("REPLACE_BLOCK");
            sb.AppendLine(blocoReplaces);
            sb.AppendLine("END_REPLACE");
            return sb.ToString();
        }

        private static string ExtrairBlocoParaProtocolo(JObject data, params string[] propertyNames)
        {
            if (data == null || propertyNames == null)
                return null;

            foreach (string propertyName in propertyNames)
            {
                var token = data[propertyName];
                if (token == null)
                    continue;

                string text = TokenToProtocolText(token);
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }

            return null;
        }

        private void DispatchDos(AgentAction action, string projectRoot)
        {
            var command = GetString(action.Dados, "comando");
            if (string.IsNullOrWhiteSpace(command))
                throw new InvalidOperationException("Acao DOS sem comando.");

            bool comandoSeguro = AllowedCommandPolicy.IsSafeVerificationCommand(command);
            if (comandoSeguro && !AutoVerificationEnabled)
                _log.Info("[INFO] Verificacao automatica desabilitada; comando seguira politica padrao.");
            else if (!comandoSeguro)
                _log.Info("[WARN] Comando nao classificado como verificacao segura: " + command);

            var definition = ProjectDefinitionLoader.LoadOrDefault(projectRoot);
            var policy = new AllowedCommandPolicy(definition.AllowedDosCommands);
            var tool = new DosCommandTool(policy, _log);
            var result = tool.Execute(command, projectRoot, AutoVerificationEnabled);

            if (result != null && result.ExitCode != 0)
            {
                if (comandoSeguro && AutoVerificationEnabled)
                {
                    throw new InvalidOperationException(
                        "[VERIFICATION_FAILED] Verificação automática falhou: " + command +
                        Environment.NewLine +
                        "ExitCode=" + result.ExitCode +
                        Environment.NewLine +
                        "Arquivos podem ter sido alterados antes da falha de verificação." +
                        (string.IsNullOrWhiteSpace(result.StdErr) ? string.Empty : Environment.NewLine + result.StdErr));
                }

                throw new InvalidOperationException(
                    "Falha ao executar comando DOS: " + command +
                    Environment.NewLine +
                    "ExitCode=" + result.ExitCode +
                    (string.IsNullOrWhiteSpace(result.StdErr) ? string.Empty : Environment.NewLine + result.StdErr));
            }
        }

        private bool NeedsConfirmation(AgentAction action)
        {
            if (action == null)
                return false;

            if (action.Tipo != AgentActionType.ArquivoLocal && action.RequerConfirmacao)
                return true;

            switch (action.Tipo)
            {
                case AgentActionType.ArquivoLocal:
                    return false;
                case AgentActionType.ComandoDos:
                    return true;
                case AgentActionType.Ftp:
                    var operacao = GetString(action.Dados, "operacao");
                    return string.Equals(operacao, "PUT", StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

        private bool Confirm(AgentAction action, string projectRoot)
        {
            if (_confirm == null)
                return false;

            return _confirm(action, projectRoot);
        }

        private void DispatchFtp(AgentAction action, string projectRoot)
        {
            var definition = ProjectDefinitionLoader.LoadOrDefault(projectRoot);
            var ftpProfile = _ftpProfileProvider == null ? null : _ftpProfileProvider(projectRoot);
            if (ftpProfile == null || string.IsNullOrWhiteSpace(ftpProfile.Host))
                ftpProfile = definition.Ftp;

            if (ftpProfile == null || string.IsNullOrWhiteSpace(ftpProfile.Host))
                throw new InvalidOperationException("FTP nao configurado no projeto.");

            var ftpTool = new FtpTool(ftpProfile, projectRoot, _log);
            var ftpAction = FtpAction.From(action);
            var result = ftpTool.Execute(ftpAction);
            if (result == null)
                throw new InvalidOperationException("Falha ao executar FTP.");
        }

        private static string ResolveLocalPath(string projectRoot, string relativeOrAbsolute)
        {
            if (Path.IsPathRooted(relativeOrAbsolute))
                return relativeOrAbsolute;

            return Path.GetFullPath(Path.Combine(projectRoot, relativeOrAbsolute));
        }

        private static string GetString(JObject data, string propertyName)
        {
            if (data == null)
                return null;

            return data[propertyName]?.ToString();
        }
        private static bool IsCanonicalPayload(JObject data)
        {
            if (data == null)
                return false;

            var token = data["__canonical"] ?? data["__Canonical"];
            return token != null && token.Type == JTokenType.Boolean && token.Value<bool>();
        }

        private string NormalizarDeleteOnlyGitDiffCanonico(JObject data, string protocol)
        {
            if (data == null || string.IsNullOrWhiteSpace(protocol))
                return protocol;

            string source = GetString(data, "__source");
            JToken deleteToken = data["isDeleteOnly"] ?? data["IsDeleteOnly"];
            bool isDeleteOnly = deleteToken != null &&
                                deleteToken.Type == JTokenType.Boolean &&
                                deleteToken.Value<bool>();
            if (!isDeleteOnly || !string.Equals(source, "GitDiffAdapter", StringComparison.Ordinal))
                return protocol;

            string replaceBlock = GetString(data, "replaceBlock");
            if (!string.IsNullOrEmpty(replaceBlock))
                return protocol;

            string filePath = GetString(data, "filePath");
            string searchBlock = GetString(data, "searchBlock");
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(searchBlock))
                throw new InvalidOperationException("Ação canônica GitDiff delete-only inválida: arquivo ou SEARCH_BLOCK ausente.");

            var sb = new System.Text.StringBuilder();
            sb.Append("ARQ=").Append(filePath).AppendLine();
            sb.AppendLine("SEARCH_BLOCK");
            sb.AppendLine(searchBlock);
            sb.AppendLine("END_SEARCH");
            sb.Append("DELETE_BLOCK");

            _log.Info("[GIT-DIFF] EMPTY_REPLACE permitido por operação delete-only segura.");
            return sb.ToString();
        }

        private static string BuildProtocolFromStructuredData(JObject data)
        {
            if (data == null)
                return null;

            string file = GetStringAny(data, "ARQ", "arq", "arquivo", "file", "path");
            if (string.IsNullOrWhiteSpace(file))
                return null;

            string replaceFile = GetStringAny(data, "REPLACE_FILE", "replace_file", "conteudo", "content");
            string replaceBlock = GetStringAny(data, "REPLACE_BLOCK", "replace_block");
            string replace = GetStringAny(data, "REPLACE", "replace");
            string search = GetStringAny(data, "SEARCH", "search");
            string searchBlock = GetStringAny(data, "SEARCH_BLOCK", "search_block");
            bool delete = HasPropertyAny(data, "DELETE", "delete");
            bool deleteBlock = HasPropertyAny(data, "DELETE_BLOCK", "delete_block");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ARQ=" + file);

            if (!string.IsNullOrEmpty(searchBlock))
            {
                sb.AppendLine("SEARCH_BLOCK=");
                sb.AppendLine(searchBlock);
                sb.AppendLine("END_SEARCH");
            }
            else if (!string.IsNullOrEmpty(search))
            {
                sb.AppendLine("SEARCH=" + search);
            }

            if (!string.IsNullOrEmpty(replaceBlock))
            {
                if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(searchBlock))
                {
                    if (LooksLikeCSharpFragment(replaceBlock) && !LooksLikeCompleteCSharpFile(replaceBlock))
                        throw new InvalidOperationException("Acao estruturada tentou substituir arquivo inteiro por fragmento C#. Informe SEARCH/SEARCH_BLOCK ou gere o arquivo completo.");

                    sb.AppendLine("REPLACE=");
                    sb.AppendLine(replaceBlock);
                }
                else
                {
                    sb.AppendLine("REPLACE_BLOCK=");
                    sb.AppendLine(replaceBlock);
                    sb.AppendLine("END_REPLACE");
                }
            }
            else if (!string.IsNullOrEmpty(replaceFile))
            {
                if (LooksLikeCSharpFragment(replaceFile) && !LooksLikeCompleteCSharpFile(replaceFile))
                    throw new InvalidOperationException("Acao estruturada tentou substituir arquivo inteiro por fragmento C#. Informe SEARCH/SEARCH_BLOCK ou gere o arquivo completo.");

                sb.AppendLine("REPLACE=");
                sb.AppendLine(replaceFile);
            }
            else if (!string.IsNullOrEmpty(replace))
            {
                sb.AppendLine("REPLACE=" + replace);
            }
            else if (deleteBlock)
            {
                sb.AppendLine("DELETE_BLOCK");
            }
            else if (delete)
            {
                sb.AppendLine("DELETE");
            }

            string protocol = sb.ToString();
            return protocol.IndexOf("REPLACE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   protocol.IndexOf("DELETE", StringComparison.OrdinalIgnoreCase) >= 0
                ? protocol
                : null;
        }

        private string BuildProtocolFromCommandList(JObject data)
        {
            if (data == null)
                return null;

            var token = data["comandos"] ?? data["commands"];
            if (token == null)
                return null;

            string commandFieldProtocol = BuildProtocolFromCommandFieldList(token);
            if (!string.IsNullOrWhiteSpace(commandFieldProtocol))
                return commandFieldProtocol;

            string objectProtocol = BuildProtocolFromCommandObjectList(token);
            if (!string.IsNullOrWhiteSpace(objectProtocol))
                return objectProtocol;

            string protocol = TokenToProtocolText(token);
            if (string.IsNullOrWhiteSpace(protocol))
                return null;

            return protocol.IndexOf("ARQ=", StringComparison.OrdinalIgnoreCase) >= 0
                ? protocol
                : null;
        }

        private string BuildProtocolFromCommandFieldList(JToken token)
        {
            if (token == null || token.Type != JTokenType.Array)
                return null;

            var items = token.Children().ToList();
            if (items.Count == 0)
                return null;

            var sb = new System.Text.StringBuilder();
            bool encontrouLinha = false;

            foreach (var item in items)
            {
                if (item == null)
                    return null;

                string commandText = null;

                if (item.Type == JTokenType.String)
                {
                    commandText = item.ToString();
                }
                else if (item.Type == JTokenType.Object)
                {
                    commandText = GetStringAny((JObject)item, "comando", "command");
                }
                else
                {
                    commandText = TokenToProtocolText(item);
                }

                if (string.IsNullOrWhiteSpace(commandText))
                    return null;

                encontrouLinha = true;
                sb.AppendLine(commandText.Trim());
            }

            if (!encontrouLinha)
                return null;

            _log.Info("[ARQ] Protocolo montado a partir de comandos campo comando.");
            return sb.ToString();
        }

        private string BuildProtocolFromCommandObjectList(JToken token)
        {
            if (token == null || token.Type != JTokenType.Array)
                return null;

            var items = token.Children().ToList();
            if (items.Count == 0)
                return null;

            var sb = new System.Text.StringBuilder();
            bool encontrouObjetoValido = false;

            foreach (var item in items)
            {
                if (item == null || item.Type != JTokenType.Object)
                    return null;

                string arq = GetStringAny((JObject)item, "ARQ", "arq");
                string search = GetStringAny((JObject)item, "SEARCH", "search");
                string replace = GetStringAny((JObject)item, "REPLACE", "replace");
                string searchBlock = GetStringAny((JObject)item, "SEARCH_BLOCK", "search_block");
                string replaceBlock = GetStringAny((JObject)item, "REPLACE_BLOCK", "replace_block");

                bool hasSearchReplace = !string.IsNullOrWhiteSpace(search) && !string.IsNullOrWhiteSpace(replace);
                bool hasSearchReplaceBlock = !string.IsNullOrWhiteSpace(searchBlock) && !string.IsNullOrWhiteSpace(replaceBlock);

                if (string.IsNullOrWhiteSpace(arq) || (!hasSearchReplace && !hasSearchReplaceBlock))
                    return null;

                encontrouObjetoValido = true;

                sb.AppendLine("ARQ=" + arq.Trim());

                if (hasSearchReplaceBlock)
                {
                    sb.AppendLine("SEARCH_BLOCK");
                    sb.AppendLine(searchBlock);
                    sb.AppendLine("END_SEARCH");
                    sb.AppendLine("REPLACE_BLOCK");
                    sb.AppendLine(replaceBlock);
                    sb.AppendLine("END_REPLACE");
                }
                else
                {
                    sb.AppendLine("SEARCH=" + search);
                    sb.AppendLine("REPLACE=" + replace);
                }
            }

            if (!encontrouObjetoValido)
                return null;

            _log.Info("[ARQ] Protocolo montado a partir de comandos objeto block.");
            return sb.ToString();
        }

        private static string GetStringAny(JObject data, params string[] propertyNames)
        {
            if (data == null)
            {
                System.Diagnostics.Trace.WriteLine("[PARSER] GetStringAny recebeu dados nulos.");
                return null;
            }

            foreach (string propertyName in propertyNames)
            {
                var token = data[propertyName];
                if (token != null)
                    return TokenToProtocolText(token);
            }

            return null;
        }

        private static bool TemDadosExecutaveis(JObject data)
        {
            if (data == null)
                return false;

            string protocolo = GetStringAny(data, "protocolo", "comando", "bloco", "bloco_protocolo");
            if (!string.IsNullOrWhiteSpace(protocolo))
                return true;

            bool temComandos = TemArrayNaoVazia(data["comandos"]) || TemArrayNaoVazia(data["commands"]);
            if (temComandos)
                return true;

            bool temArq = !string.IsNullOrWhiteSpace(GetStringAny(data, "ARQ", "arq"));
            bool temSearch = !string.IsNullOrWhiteSpace(GetStringAny(data, "SEARCH", "search"));
            bool temReplace = !string.IsNullOrWhiteSpace(GetStringAny(data, "REPLACE", "replace"));
            bool temSearchBlock = !string.IsNullOrWhiteSpace(GetStringAny(data, "SEARCH_BLOCK", "search_block"));
            bool temReplaceBlock = !string.IsNullOrWhiteSpace(GetStringAny(data, "REPLACE_BLOCK", "replace_block"));

            return temArq && ((temSearch && temReplace) || (temSearchBlock && temReplaceBlock));
        }

        private static string TokenToProtocolText(JToken token)
        {
            if (token == null)
                return null;

            if (token.Type == JTokenType.Array)
            {
                return string.Join(
                    Environment.NewLine,
                    token.Children().Select(child => child.Type == JTokenType.String ? child.ToString() : child.ToString(Newtonsoft.Json.Formatting.None)));
            }

            if (token.Type == JTokenType.String)
                return token.ToString();

            return token.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static bool TemArrayNaoVazia(JToken token)
        {
            return token != null && token.Type == JTokenType.Array && token.HasValues;
        }

        private static bool LooksLikeCSharpFragment(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string trimmed = text.TrimStart();
            return trimmed.StartsWith("private ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("public ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("protected ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("internal ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("void ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("async ", StringComparison.Ordinal);
        }

        private static bool LooksLikeCompleteCSharpFile(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.IndexOf(" class ", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf(" partial class ", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("namespace ", StringComparison.Ordinal) >= 0;
        }

        private static bool HasPropertyAny(JObject data, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (data[propertyName] != null)
                    return true;
            }

            return false;
        }

        private static string FormatActionSummary(AgentAction action)
        {
            if (action == null)
                return "<acao nula>";

            string descricao = string.IsNullOrWhiteSpace(action.Descricao) ? "" : " | " + action.Descricao.Trim();
            return action.Tipo + descricao;
        }

        private static string FormatActionDetails(AgentAction action)
        {
            if (action == null)
                return "Acao: <nula>";

            string dados = action.Dados == null ? "" : action.Dados.ToString(Newtonsoft.Json.Formatting.None);
            return "Tipo: " + action.Tipo +
                   Environment.NewLine +
                   "Descricao: " + (action.Descricao ?? "") +
                   Environment.NewLine +
                   "Dados: " + Truncate(dados, 1200);
        }

        private static string Truncate(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
                return text ?? string.Empty;

            return text.Substring(0, maxChars) + "...";
        }

    }
}
