using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using GptBolDll.Configuration;

namespace GptBolDll
{
    public sealed class AgentCore
    {
        private const int MaxArquivosExtrasReprocessamento = 5;
        private const string FeatureMarker = "ExplicitPathFilter-v2;PatchPreflight-v1;PatchPlan-v1;PatchValidator-v1;PatchFailureClassifier-v1;PatchRetryContext-v1;InventedAnchorRetry-v1;PatchRetryDirect-v1;NoActionNotSuccess-v1;DirectPatchFields-v1;KimiOpenAICompatible-v1;ParserActGuard-v1;ActionParserContract-v1;ActionParserPipelineDiag-v1;ActionParserPreDispatch-v1;ActionParserRawJsonDiag-v1;ActionParserRawJsonRecover-v1;ActionParserCanonicalExec-v1;ActionParserNormalize-v1;ActionParserOutcome-v1;ToolDispatcherCanonical-v1;ActionParserFinal-v1;AiRawErrorLog-v1;GitDiffRuntimePrompt-v1;GitDiffDetect-v1;GitDiffAdapter-v1;GitDiffSafety-v1;GitDiffToProtocol-v1;GitDiffRuntimeSync-v1;GitDiffPromptPathPersistFix-v1;GitDiffConfigStoreFix-v1;GitDiffPromptFixedFallback-v1;KimiUnavailableFriendly-v1;KimiForcedGitDiff-v1;GitDiffDeleteOnly-v1;GitDiffCanonicalExec-v1;KimiModelRuntimeFix-v1;GitDiffJsonWrapper-v1;GitDiffJsonEscapedString-v1;ProgrammerTempRetrySlim-v1;GitDiffDeleteContextExpand-v1;GitDiffEmptyHunkIgnore-v1;GitDiffPriorityFlow-v1;GitDiffLiteralSearchRepair-v1;GitDiffNoAiRetryOnAnchor-v1;GitDiffTrimmedDeleteRepair-v1;GitDiffDeleteLiteralExpand-v1;GitDiffDropEmptyInvalidFile-v1;KimiRecoveryPathFix-v1;KimiSearchBlockDeleteRecover-v1";

        private readonly IAgentModelClient _modelClient;
        private readonly IAgentModelClient _analystModelClient;
        private readonly IProjectProvider _projectProvider;
        private readonly IContextBuilder _contextBuilder;
        private readonly Func<string, string> _loadPrompt;
        private readonly IToolDispatcher _toolDispatcher;
        private readonly Action<string> _statusReporter;
        private readonly ExecutionLogger _log;
        private readonly string _modelDisplayName;
        private readonly string _analystModelDisplayName;
        public LanguageProfile LanguageProfile { get; set; } = LanguageProfile.Geral;
        public string LanguageProfileId { get; set; } = "geral";
        public bool UseComplementPrompt { get; set; }
        public string ComplementPromptPath { get; set; }
        public string AnalystPromptPath { get; set; }
        public string KnowledgeIndexPath { get; set; } = string.Empty;
        public bool AutoVerificationEnabled { get; set; } = false;
        public bool UseGitDiff { get; set; } = false;
        public string AiProviderId { get; set; }
        public string AiProviderName { get; set; }
        public int NivelMaximoDificuldade { get; set; } = 6;
        public int NivelMaximoIa { get; set; } = 6;
        public int MaxPromptChars { get; set; } = 30000;

        public AgentCore(
            IAgentModelClient modelClient,
            IProjectProvider projectProvider,
            IContextBuilder contextBuilder,
            Func<string, string> loadPrompt,
            IToolDispatcher toolDispatcher = null,
            Action<string> statusReporter = null,
            ExecutionLogger log = null,
            string modelDisplayName = null,
            IAgentModelClient analystModelClient = null,
            string analystModelDisplayName = null)
        {
            _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
            _analystModelClient = analystModelClient ?? _modelClient;
            _projectProvider = projectProvider ?? throw new ArgumentNullException(nameof(projectProvider));
            _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
            _loadPrompt = loadPrompt ?? throw new ArgumentNullException(nameof(loadPrompt));
            _toolDispatcher = toolDispatcher;
            _statusReporter = statusReporter;
            _log = log ?? new ExecutionLogger();
            _modelDisplayName = string.IsNullOrWhiteSpace(modelDisplayName)
                ? (_modelClient.DisplayName ?? "IA")
                : modelDisplayName;
            _analystModelDisplayName = string.IsNullOrWhiteSpace(analystModelDisplayName)
                ? (_analystModelClient.DisplayName ?? _modelDisplayName)
                : analystModelDisplayName;
        }

        public async Task<AgentResult> ExecutarAsync(string entradaUsuario)
        {
            ValidarEntradaUsuario(entradaUsuario);

            string projectRoot = ObterProjectRootValido();

            RegistrarInicioProcessamento(projectRoot);

            string projectId;
            string projectName;
            ObterIdentificacaoProjeto(projectRoot, out projectId, out projectName);

            string promptBase = CarregarPromptBase(projectRoot);
            string contextoProjeto = MontarContextoProjeto(projectRoot);
            string systemPrompt = MontarSystemPrompt();

            string finalPrompt = PrepararPromptFinal(
                systemPrompt,
                promptBase,
                contextoProjeto,
                entradaUsuario,
                false);

            string respostaIA = await ConsultarModeloComEtapasAsync(
                systemPrompt,
                promptBase,
                contextoProjeto,
                entradaUsuario,
                finalPrompt).ConfigureAwait(false);

            bool semAcoesExecutaveis;
            int totalAcoes;
            AgentResponse structuredResponse = InterpretarRespostaIA(
                respostaIA,
                out totalAcoes,
                out semAcoesExecutaveis);
            ActionParseResult resultadoClassificacaoParser = ActionParserPipeline.Parse(structuredResponse, respostaIA);
            RegistrarResumoFinalParserAct(resultadoClassificacaoParser);

            if (semAcoesExecutaveis)
            {
                if (resultadoClassificacaoParser.Outcome == ActionParseOutcome.Executable &&
                    resultadoClassificacaoParser.CanonicalExecutableCount > 0)
                {
                    _log.Info("[PARSER-PIPELINE] Resultado: ações canônicas recuperadas para execução.");
                    semAcoesExecutaveis = false;
                }
                else if (resultadoClassificacaoParser.Outcome == ActionParseOutcome.GitDiffDetected)
                {
                    _log.Info("[GIT-DIFF] Resposta sem acoes JSON, mas GitDiff detectado.");
                    // O pré-dispatch fará parse, safety, conversão e criação das ações canônicas.
                    semAcoesExecutaveis = false;
                }
                else if (resultadoClassificacaoParser.Outcome == ActionParseOutcome.LegitimateNoAction &&
                         resultadoClassificacaoParser.Outcome != ActionParseOutcome.GitDiffDetected)
                {
                    _log.Info("[PARSER-PIPELINE] Resultado sem ação legítima: " + resultadoClassificacaoParser.GetUserFriendlyErrorMessage());
                    semAcoesExecutaveis = false;
                }
                else
                {
                    string mensagemAmigavel = resultadoClassificacaoParser.GetUserFriendlyErrorMessage();
                    if (!string.IsNullOrWhiteSpace(mensagemAmigavel))
                    {
                        RegistrarErroBrutoDaIAAtual(resultadoClassificacaoParser, respostaIA);
                        throw new InvalidOperationException(mensagemAmigavel);
                    }

                    var retryResult = await TentarForcarAcoesQuandoRespostaSemAcoesAsync(
                        systemPrompt,
                        promptBase,
                        contextoProjeto,
                        entradaUsuario,
                        respostaIA,
                        structuredResponse).ConfigureAwait(false);

                    if (retryResult == null)
                    {
                        retryResult = await TentarReprocessarRespostaSemAcoesAsync(
                            projectRoot,
                            systemPrompt,
                            promptBase,
                            contextoProjeto,
                            entradaUsuario,
                            respostaIA,
                            structuredResponse).ConfigureAwait(false);
                    }

                    if (retryResult != null)
                    {
                        contextoProjeto = retryResult.ContextoProjeto;
                        finalPrompt = retryResult.FinalPrompt;
                        respostaIA = retryResult.RespostaIA;
                        structuredResponse = retryResult.StructuredResponse;
                        totalAcoes = structuredResponse?.Acoes == null ? 0 : structuredResponse.Acoes.Count;
                        semAcoesExecutaveis = totalAcoes == 0;
                    }
                }
            }

            AgentResult result = CriarAgentResult(
                projectId,
                projectName,
                projectRoot,
                promptBase,
                contextoProjeto,
                finalPrompt,
                respostaIA,
                structuredResponse,
                resultadoClassificacaoParser == null ? string.Empty : resultadoClassificacaoParser.Outcome.ToString());

            ProjectHistoryEntry historyEntry = CriarHistoryEntry(
                entradaUsuario,
                respostaIA,
                structuredResponse,
                resultadoClassificacaoParser == null ? string.Empty : resultadoClassificacaoParser.Outcome.ToString());

            await ExecutarAcoesERegistrarHistoricoAsync(
                result,
                historyEntry,
                structuredResponse,
                projectRoot,
                systemPrompt,
                promptBase,
                entradaUsuario).ConfigureAwait(false);

            AtualizarStatusFinal(semAcoesExecutaveis);

            _log.Info("Fim do processamento da DLL.");

            return result;
        }

        public async Task<AgentResult> ExecutarGeracaoRequisicaoAsync(string entradaUsuario)
        {
            ValidarEntradaUsuario(entradaUsuario);

            if (!TaskDemandParser.TryParse(entradaUsuario, out TaskDemandRequest demanda, out string erroDemanda, out bool fallbackDemandaUsado))
                throw new InvalidOperationException(erroDemanda);

            if (fallbackDemandaUsado)
                _log.Info("Entrada livre detectada; demanda estruturada gerada automaticamente.");

            string projectRoot = ObterProjectRootValido();
            RegistrarInicioAnalista(projectRoot);

            string projectId;
            string projectName;
            ObterIdentificacaoProjeto(projectRoot, out projectId, out projectName);

            string contextoProjeto = MontarContextoProjeto(projectRoot);
            string contextoAnalista = contextoProjeto;
            string knowledgeIndex = ProjectKnowledgeIndexLoader.Load(KnowledgeIndexPath, _log);
            if (!string.IsNullOrWhiteSpace(knowledgeIndex))
            {
                contextoAnalista = knowledgeIndex;
                _log.Info("[INFO] Analista usando Ã­ndice de conhecimento como contexto. Caracteres: " + knowledgeIndex.Length);
            }
            else
            {
                _log.Info("[INFO] Analista usando contexto completo do projeto.");
            }

            int nivelEfetivo = CalcularNivelEfetivo();

            string analystPromptPath = AnalystPromptPath;
            if (!string.IsNullOrWhiteSpace(analystPromptPath))
                _log.Info("Prompt do Analista externo configurado: " + analystPromptPath);
            else
                _log.Info("Prompt do Analista externo nao configurado.");

            _log.Info("[DIAG] AnalystPromptPath recebido no AgentCore: " + (analystPromptPath ?? "(null)"));
            _log.Info("[DIAG] AnalystPromptPath vazio: " + string.IsNullOrWhiteSpace(analystPromptPath));

            if (!string.IsNullOrWhiteSpace(analystPromptPath))
            {
                bool arquivoExiste = System.IO.File.Exists(analystPromptPath);
                _log.Info("[DIAG] AnalystPromptPath arquivo existe: " + arquivoExiste);
            }

            string finalPrompt = PromptLoader.BuildSystemAnalystPrompt(nivelEfetivo, contextoAnalista, demanda, AnalystPromptPath);

            _log.Info("Demanda estruturada validada.");
            ReportStatus("Gerando requisicao de tarefa");

            string respostaIA;
            if (DeveUsarRequisicaoMinimaLocalParaAnalista(finalPrompt))
            {
                _log.Info("[WARN] Prompt do Analista excede limite seguro do Groq. Usando requisicao minima local.");
                respostaIA = CriarRespostaMinimaAnalistaLocal(demanda);
            }
            else
            {
                respostaIA = await ConsultarAnalistaComValidacaoAsync(finalPrompt, nivelEfetivo, demanda).ConfigureAwait(false);
            }

            TaskRequestParser.TryValidate(respostaIA, nivelEfetivo, out GeneratedTaskRequestInfo info, out string _);

            var result = new AgentResult
            {
                ProjectId = Guid.TryParse(projectId, out Guid parsedId) ? parsedId : Guid.Empty,
                ProjectName = projectName,
                ProjectRoot = projectRoot,
                ProjectContext = contextoProjeto,
                FinalPrompt = finalPrompt,
                ModelResponse = respostaIA,
                GeneratedTaskRequest = respostaIA,
                OperationMode = AgentOperationMode.AnalistaDeSistemas,
                NivelEfetivoUsado = nivelEfetivo,
                NivelAtribuido = info == null ? 0 : info.NivelAtribuido
            };

            RegistrarHistorico(result, CriarHistoryEntryAnalista(entradaUsuario, respostaIA, nivelEfetivo, info), projectRoot);
            _log.Info("Requisicao de tarefa gerada.");
            _log.Info("Nivel atribuido pela IA: " + result.NivelAtribuido);
            ReportStatus("Requisicao gerada");
            return result;
        }

        private bool DeveUsarRequisicaoMinimaLocalParaAnalista(string finalPrompt)
        {
            if (string.IsNullOrWhiteSpace(finalPrompt))
                return false;

            string provider = _analystModelClient?.ProviderName ?? string.Empty;
            if (!provider.Equals("Groq", StringComparison.OrdinalIgnoreCase))
                return false;

            return finalPrompt.Length > 120000;
        }

        private void RegistrarInicioAnalista(string projectRoot)
        {
            _log.Info("Modo de operacao: AnalistaDeSistemas");
            _log.Info("[INFO] Main FeatureMarker: AnalystTempFallback-v1;KimiUserMessageEncoding-v1");
            _log.Info("IA selecionada: " + (AiProviderName ?? _analystModelDisplayName));
            _log.Info("ProviderType: " + _analystModelClient.ProviderName);
            _log.Info("Modelo: " + _analystModelClient.ModelName);
            _log.Info("Projeto ativo: " + projectRoot);
            _log.Info("Nivel configurado na IA: " + NivelMaximoDificuldade);
            _log.Info("Nivel maximo suportado pela IA: " + NivelMaximoIa);
        }

        private int CalcularNivelEfetivo()
        {
            int nivelConfiguradoIa = AiProviderConfig.ClampLevel(NivelMaximoDificuldade);
            int nivelIa = AiProviderConfig.ClampLevel(NivelMaximoIa);
            int efetivo = Math.Min(nivelConfiguradoIa, nivelIa);

            _log.Info("Nivel configurado na IA: " + nivelConfiguradoIa);
            _log.Info("Nivel maximo suportado pela IA: " + nivelIa);
            _log.Info("Nivel efetivo usado: " + efetivo);
            return efetivo;
        }

        private async Task<string> ConsultarAnalistaComValidacaoAsync(string prompt, int nivelEfetivo, TaskDemandRequest demanda)
        {
            string resposta = null;
            string promptAtual = prompt;

            for (int tentativa = 1; tentativa <= 2; tentativa++)
            {
                try
                {
                    resposta = await AskAnalystModelWithDiagnosticsAsync(
                        tentativa == 1 ? "analista prompt principal" : "analista retry formato",
                        promptAtual,
                        tentativa == 1 ? 180000 : 120000).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsTemporaryAnalystFailure(ex))
                {
                    RegistrarFallbackAnalistaTemporario(ex);
                    return CriarRespostaMinimaAnalistaLocal(demanda);
                }

                if (TaskRequestParser.TryValidate(resposta, nivelEfetivo, out GeneratedTaskRequestInfo _, out string erro))
                    return resposta.Trim();

                _log.Error("Resposta fora do formato esperado. " + erro);
                promptAtual = prompt + Environment.NewLine + Environment.NewLine +
                    "A resposta anterior foi descartada por formato invalido: " + erro + Environment.NewLine +
                    "Reenvie exclusivamente no FORMATO EXATO DE SAIDA.";
            }

            _log.Error("Analista retornou formato invalido apos retry; requisicao minima gerada localmente.");
            return CriarRespostaMinimaAnalistaLocal(demanda);
        }

        private bool IsTemporaryAnalystFailure(Exception ex)
        {
            if (ex == null)
                return false;

            if (string.Equals(ex.GetType().Name, "ProviderUnavailableException", StringComparison.Ordinal))
                return true;

            if (ex is TaskCanceledException || ex is TimeoutException)
                return true;

            int? statusCode = TryGetIntProperty(ex, "StatusCode");
            if (statusCode.HasValue && IsTemporaryHttpStatus(statusCode.Value))
                return true;

            string message = ex.Message ?? string.Empty;
            if (ContainsTemporaryHttpMessage(message))
                return true;

            return IsTemporaryAnalystFailure(ex.InnerException);
        }

        private static bool IsTemporaryHttpStatus(int statusCode)
        {
            switch (statusCode)
            {
                case 408:
                case 429:
                case 500:
                case 502:
                case 503:
                case 504:
                    return true;
                default:
                    return false;
            }
        }

        private static bool ContainsTemporaryHttpMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("temporarily", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("gateway time-out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("gateway timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("status code: 504", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("status code: 503", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("status code: 502", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("status code: 500", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("status code: 429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("status code: 408", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RegistrarFallbackAnalistaTemporario(Exception ex)
        {
            string providerNome = _analystModelClient == null ? "Kimi / OpenAI Compatível" : _analystModelClient.ProviderName;
            string modelo = _analystModelClient == null ? string.Empty : _analystModelClient.ModelName;
            int? statusCode = TryGetIntProperty(ex, "StatusCode");
            string etapa = TryGetStringProperty(ex, "Stage");

            _log.Error("[ERRO] Analista falhou por erro temporário; requisicao minima gerada localmente.");
            _log.Info("[INFO] Motivo fallback analista: " + (ex == null ? string.Empty : ex.Message));
            _log.Info("[INFO] Etapa fallback analista: " + (string.IsNullOrWhiteSpace(etapa) ? "analista prompt principal" : etapa));
            _log.Info("[INFO] Status HTTP fallback analista: " + (statusCode.HasValue ? statusCode.Value.ToString() : "indisponivel"));
            _log.Info("[INFO] Provider fallback analista: " + providerNome);
            _log.Info("[INFO] Modelo fallback analista: " + modelo);
        }

        private static string TryGetStringProperty(Exception ex, string propertyName)
        {
            if (ex == null || string.IsNullOrWhiteSpace(propertyName))
                return string.Empty;

            try
            {
                var prop = ex.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null)
                    return string.Empty;

                object value = prop.GetValue(ex);
                return value == null ? string.Empty : value.ToString();
            }
            catch
            {
            }

            return string.Empty;
        }

        private static int? TryGetIntProperty(Exception ex, string propertyName)
        {
            if (ex == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                var prop = ex.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null)
                    return null;

                object value = prop.GetValue(ex);
                if (value is int i)
                    return i;

                int parsed;
                if (int.TryParse(value == null ? string.Empty : value.ToString(), out parsed))
                    return parsed;
            }
            catch
            {
            }

            return null;
        }

        private string CriarRespostaMinimaAnalistaLocal(TaskDemandRequest demanda)
        {
            string objetivo = demanda == null || string.IsNullOrWhiteSpace(demanda.Objetivo)
                ? "Executar a alteracao solicitada pelo usuario."
                : demanda.Objetivo.Trim();

            string escopo = demanda == null || string.IsNullOrWhiteSpace(demanda.Escopo)
                ? "Usar a demanda estruturada gerada a partir do texto do usuario."
                : demanda.Escopo.Trim();

            string restricoes = demanda == null || string.IsNullOrWhiteSpace(demanda.Restricoes)
                ? "Nao alterar fora do pedido do usuario."
                : demanda.Restricoes.Trim();

            string entrega = demanda == null || string.IsNullOrWhiteSpace(demanda.EntregaEsperada)
                ? "Manter o projeto compilando."
                : demanda.EntregaEsperada.Trim();

            return
                "TAREFA_ID: tarefa_auto" + Environment.NewLine +
                "NIVEL_ATRIBUIDO: 1" + Environment.NewLine +
                "TITULO: Requisicao minima gerada localmente" + Environment.NewLine +
                "OBJETIVO:" + Environment.NewLine +
                objetivo + Environment.NewLine +
                Environment.NewLine +
                "ESCOPO:" + Environment.NewLine +
                escopo + Environment.NewLine +
                Environment.NewLine +
                "ETAPAS:" + Environment.NewLine +
                "  1. OBJETIVO: Executar a alteracao solicitada pelo usuario." + Environment.NewLine +
                "     ESCOPO: Usar a demanda estruturada gerada a partir do texto do usuario." + Environment.NewLine +
                "     DEPENDENCIAS: Contexto do projeto." + Environment.NewLine +
                "     VERIFICACAO: Verificar se as acoes propostas atendem ao pedido." + Environment.NewLine +
                "VALIDACOES_MANUAIS_RECOMENDADAS:" + Environment.NewLine +
                "  - Revisar manualmente o resultado apos execucao." + Environment.NewLine +
                "RESTRICOES_GERAIS:" + Environment.NewLine +
                restricoes + Environment.NewLine +
                "FORMATO_EXECUCAO: sequencial" + Environment.NewLine +
                "TRIGGER_PROXIMO: PROGRAMADOR" + Environment.NewLine +
                Environment.NewLine +
                "ENTREGA_ESPERADA:" + Environment.NewLine +
                entrega;
        }

        private async Task<string> AskAnalystModelWithDiagnosticsAsync(string etapaConsulta, string prompt, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            int promptChars = prompt == null ? 0 : prompt.Length;
            _log.Info("Enviando " + etapaConsulta + " ao modelo analista. Provider=" + _analystModelClient.ProviderName + " Modelo=" + _analystModelClient.ModelName + " Caracteres=" + promptChars + (timeoutMs > 0 ? " TimeoutMs=" + timeoutMs : string.Empty));

            try
            {
                string resposta;
                var timeoutClient = _analystModelClient as IAgentModelClientWithTimeout;
                if (timeoutMs > 0 && timeoutClient != null)
                    resposta = await timeoutClient.AskAsync(prompt, timeoutMs).ConfigureAwait(false);
                else
                    resposta = await _analystModelClient.AskAsync(prompt).ConfigureAwait(false);

                sw.Stop();
                _log.Info("Consulta ao analista concluida. Etapa=" + etapaConsulta + " DuracaoMs=" + (long)sw.Elapsed.TotalMilliseconds + " RespostaChars=" + (resposta == null ? 0 : resposta.Length));
                return resposta;
            }
            catch (TaskCanceledException ex)
            {
                sw.Stop();
                _log.Error("Consulta ao analista cancelada. Etapa=" + etapaConsulta + " DuracaoMs=" + (long)sw.Elapsed.TotalMilliseconds + " PromptChars=" + promptChars);
                _log.Error("Tipo da excecao: " + ex.GetType().FullName);
                _log.Error("Mensagem: " + ex.Message);
                _log.Error("StackTrace parcial: " + TruncateForLog(ex.ToString(), 2000));
                throw new InvalidOperationException(
                    "A consulta ao analista " + (_analystModelClient.DisplayName ?? _analystModelClient.ModelName) +
                    " atingiu o tempo limite. Isso normalmente indica contexto grande demais, lentidao do provedor ou instabilidade de rede. " +
                    "Para continuar, reduza os arquivos no contexto, divida a demanda em partes menores ou use outra IA ativa para a etapa de analise. " +
                    "Detalhes: PromptChars=" + promptChars + "; DuracaoMs=" + (long)sw.Elapsed.TotalMilliseconds + "; TimeoutMs=" + timeoutMs + ".",
                    ex);
            }
            catch (GeminiResponseException ex) when (string.Equals(ex.FinishReason, "RECITATION", StringComparison.OrdinalIgnoreCase))
            {
                sw.Stop();
                LogGeminiException("Gemini bloqueou a resposta do analista por recitacao.", ex, prompt);
                throw new InvalidOperationException(BuildGeminiRecitationUserMessage(ex, prompt), ex);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _log.Error("Erro ao consultar analista. Etapa=" + etapaConsulta + " DuracaoMs=" + (long)sw.Elapsed.TotalMilliseconds + " PromptChars=" + promptChars);
                _log.Error("Tipo da excecao: " + ex.GetType().FullName);
                _log.Error("Mensagem: " + ex.Message);
                if (ex.InnerException != null)
                    _log.Error("InnerException: " + ex.InnerException.GetType().FullName + " - " + ex.InnerException.Message);
                _log.Error("StackTrace parcial: " + TruncateForLog(ex.ToString(), 2000));
                throw;
            }
        }

        private void ValidarEntradaUsuario(string entradaUsuario)
        {
            if (string.IsNullOrWhiteSpace(entradaUsuario))
                throw new ArgumentException("Entrada do usuario vazia.", nameof(entradaUsuario));
        }

        private string ObterProjectRootValido()
        {
            string projectRoot = _projectProvider.GetActiveProjectRoot();

            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
                throw new InvalidOperationException("A pasta do projeto nao esta configurada ou nao existe.");

            return projectRoot;
        }

        private void RegistrarInicioProcessamento(string projectRoot)
        {
            var assembly = typeof(AgentCore).Assembly;
            string assemblyLocation = assembly.Location;
            string informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
            string lastWriteTime = string.Empty;

            if (!string.IsNullOrWhiteSpace(assemblyLocation) && File.Exists(assemblyLocation))
            {
                try
                {
                    lastWriteTime = File.GetLastWriteTime(assemblyLocation).ToString("o");
                }
                catch
                {
                    lastWriteTime = "indisponivel";
                }
            }

            _log.Info("Inicio do processamento da DLL.");
            _log.Info("Versao programa principal: " + GetMainProgramVersion());
            _log.Info("Versao DLL: " + GetDllVersion());
            _log.Info("DLL Assembly FullName: " + assembly.FullName);
            _log.Info("DLL Location: " + (string.IsNullOrWhiteSpace(assemblyLocation) ? "indisponivel" : assemblyLocation));
            _log.Info("DLL LastWriteTime: " + (string.IsNullOrWhiteSpace(lastWriteTime) ? "indisponivel" : lastWriteTime));
            _log.Info("DLL InformationalVersion: " + (string.IsNullOrWhiteSpace(informationalVersion) ? "indisponivel" : informationalVersion));
            _log.Info("DLL FeatureMarker: " + FeatureMarker);
            _log.Info("Projeto ativo: " + projectRoot);
            _log.Info("Perfil de linguagem usado na execucao: " + LanguageProfile);
        }

        private void ObterIdentificacaoProjeto(
            string projectRoot,
            out string projectId,
            out string projectName)
        {
            projectId = _projectProvider.GetActiveProjectId();
            projectName = _projectProvider.GetActiveProjectName(projectRoot);

            if (string.IsNullOrWhiteSpace(projectName))
            {
                projectName = Path.GetFileName(
                    projectRoot.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar));
            }
        }

        private string CarregarPromptBase(string projectRoot)
        {
            ReportStatus("Carregando prompt base");

            string promptBase = string.Empty;
            if (UseComplementPrompt && !string.IsNullOrWhiteSpace(ComplementPromptPath))
            {
                promptBase = PromptLoader.LoadPrompt(projectRoot, ComplementPromptPath) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(promptBase))
                    _log.Info("Prompt de complemento de operacao carregado. Arquivo: " + ComplementPromptPath);
            }

            if (string.IsNullOrWhiteSpace(promptBase))
                promptBase = _loadPrompt(projectRoot) ?? string.Empty;

            _log.Info("Prompt base carregado. Caracteres: " + promptBase.Length);

            return promptBase;
        }

        private string MontarContextoProjeto(string projectRoot)
        {
            ReportStatus("Montando contexto do projeto");

            string contextoProjeto = _contextBuilder.Build(projectRoot) ?? string.Empty;

            _log.Info("Contexto do projeto montado. Caracteres: " + contextoProjeto.Length);

            return contextoProjeto;
        }

        private string MontarSystemPrompt()
        {
            ReportStatus("Montando instrucao do sistema");

            string systemPrompt = PromptLoader.BuildSystemPrompt() ?? string.Empty;

            string languageProfileInstruction =
                PromptLoader.BuildLanguageProfileInstruction(LanguageProfile) ?? string.Empty;

            string languageProfileIdEfetivo =
                LanguageProfileConfigRepository.ResolveId(LanguageProfileId, LanguageProfile);

            string languageSpecificPrompt =
                LanguageProfileConfigRepository.LoadPromptForLanguage(languageProfileIdEfetivo) ?? string.Empty;

            string promptComPerfil = string.Join(
                Environment.NewLine + Environment.NewLine,
                new[]
                {
            systemPrompt,
            languageProfileInstruction,
            languageSpecificPrompt
                }.Where(x => !string.IsNullOrWhiteSpace(x)));

            _log.Info("Instrucao do sistema montada. Caracteres: " + promptComPerfil.Length);
            _log.Info("Perfil de linguagem aplicado ao prompt: " + LanguageProfile);
            if (!string.IsNullOrWhiteSpace(languageSpecificPrompt))
                _log.Info("Prompt especifico de linguagem aplicado: " + languageProfileIdEfetivo);

            return promptComPerfil;
        }


        private string PrepararPromptFinal(
            string systemPrompt,
            string promptBase,
            string contextoProjeto,
            string entradaUsuario,
            bool reducedContext)
        {
            ReportStatus("Preparando entrada para o modelo");

            string finalPrompt = BuildFinalPrompt(
                systemPrompt,
                promptBase,
                contextoProjeto,
                entradaUsuario,
                reducedContext);

            if (reducedContext)
                _log.Info("Prompt reduzido preparado. Caracteres: " + finalPrompt.Length);
            else
                _log.Info("Prompt final preparado. Caracteres: " + finalPrompt.Length);

            return finalPrompt;
        }

        private async Task<string> ConsultarModeloComFallbackAsync(
            string systemPrompt,
            string promptBase,
            string contextoProjeto,
            string entradaUsuario,
            string finalPrompt)
        {
            string respostaIA = null;

            ReportStatus("Consultando a IA " + _modelDisplayName);

            try
            {
                respostaIA = await AskModelWithDiagnosticsAsync("prompt principal", finalPrompt).ConfigureAwait(false);
            }
            catch (GeminiResponseException ex) when (string.Equals(ex.FinishReason, "RECITATION", StringComparison.OrdinalIgnoreCase))
            {
                LogGeminiException("Gemini bloqueou a resposta por recitacao. Repetindo com contexto reduzido.", ex, finalPrompt);
                ReportStatus("IA bloqueada por recitacao; tentando contexto reduzido");

                string reducedPrompt = PrepararPromptFinal(
                    systemPrompt,
                    promptBase,
                    contextoProjeto,
                    entradaUsuario,
                    true);

                try
                {
                    respostaIA = await AskModelWithDiagnosticsAsync("prompt reduzido", reducedPrompt).ConfigureAwait(false);
                }
                catch (GeminiResponseException retryEx)
                {
                    LogGeminiException("Gemini falhou tambem com contexto reduzido.", retryEx, reducedPrompt);
                    ReportStatus("IA bloqueada novamente; tentando contexto minimo");

                    string minimalPrompt = BuildEmergencyPrompt(
                        systemPrompt,
                        contextoProjeto,
                        entradaUsuario,
                        12000);

                    _log.Info("Prompt minimo preparado. Caracteres: " + minimalPrompt.Length);

                    try
                    {
                        respostaIA = await AskModelWithDiagnosticsAsync("prompt minimo", minimalPrompt, 45000).ConfigureAwait(false);
                    }
                    catch (GeminiResponseException minimalEx)
                    {
                        LogGeminiException("Gemini falhou tambem com contexto minimo.", minimalEx, minimalPrompt);
                        throw new InvalidOperationException(
                            BuildGeminiRecitationUserMessage(minimalEx, minimalPrompt),
                            minimalEx);
                    }
                }
            }
            catch (InvalidOperationException ex) when (IsModelTimeoutException(ex))
            {
                _log.Error("Consulta principal ao modelo atingiu timeout. Tentando novamente com prompt minimo.");
                ReportStatus("Timeout no provedor; tentando contexto minimo");

                string minimalPrompt = BuildEmergencyPrompt(
                    systemPrompt,
                    contextoProjeto,
                    entradaUsuario,
                    12000);

                _log.Info("Prompt minimo por timeout preparado. Caracteres: " + minimalPrompt.Length);

                try
                {
                    respostaIA = await AskModelWithDiagnosticsAsync("prompt minimo por timeout", minimalPrompt, 35000).ConfigureAwait(false);
                }
                catch (Exception retryEx) when (IsModelTimeoutException(retryEx) || retryEx is TaskCanceledException)
                {
                    throw new InvalidOperationException(
                        BuildModelTimeoutUserMessage(_modelClient?.DisplayName, finalPrompt, retryEx, 35000),
                        retryEx);
                }
            }

            _log.Info("Resposta bruta recebida. Caracteres: " + (respostaIA == null ? 0 : respostaIA.Length));

            if (string.IsNullOrWhiteSpace(respostaIA))
                _log.Error("O modelo retornou resposta vazia.");

            return respostaIA;
        }

        private static bool IsModelTimeoutException(Exception ex)
        {
            if (ex == null)
                return false;

            if (ex is TaskCanceledException || ex is TimeoutException)
                return true;

            if (ex.InnerException != null && IsModelTimeoutException(ex.InnerException))
                return true;

            string message = ex.Message ?? string.Empty;
            return message.IndexOf("Consulta ao modelo cancelada", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("tarefa foi cancelada", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildModelTimeoutUserMessage(string modelDisplayName, string prompt, Exception ex, int retryTimeoutMs)
        {
            int promptChars = prompt == null ? 0 : prompt.Length;
            string model = string.IsNullOrWhiteSpace(modelDisplayName) ? "a IA selecionada" : modelDisplayName;
            string retryText = retryTimeoutMs > 0
                ? " A segunda tentativa usou prompt minimo e limite de " + (retryTimeoutMs / 1000) + "s."
                : string.Empty;

            return "A consulta ao modelo " + model + " atingiu o tempo limite mesmo apos uma tentativa automatica com prompt minimo." +
                   retryText + " " +
                   "Isso normalmente indica prompt grande demais, lentidao do provedor ou instabilidade de rede. " +
                   "Para continuar, reduza os arquivos no contexto, divida a tarefa em partes menores ou use outra IA ativa para esta etapa. " +
                   "Detalhes: PromptChars=" + promptChars + "; Erro=" + TruncateForLog(ex == null ? string.Empty : ex.Message, 220) + ".";
        }

        private async Task<string> AskModelWithDiagnosticsAsync(string etapaConsulta, string prompt, int timeoutMs = 0)
        {
            var sw = Stopwatch.StartNew();
            int promptChars = prompt == null ? 0 : prompt.Length;
            _log.Info("Enviando " + etapaConsulta + " ao modelo. Provider=" + _modelClient.ProviderName + " Modelo=" + _modelClient.ModelName + " Caracteres=" + promptChars + (timeoutMs > 0 ? " TimeoutMs=" + timeoutMs : string.Empty));

            try
            {
                string resposta;
                var timeoutClient = _modelClient as IAgentModelClientWithTimeout;
                if (timeoutMs > 0 && timeoutClient != null)
                    resposta = await timeoutClient.AskAsync(prompt, timeoutMs).ConfigureAwait(false);
                else
                    resposta = await _modelClient.AskAsync(prompt).ConfigureAwait(false);

                sw.Stop();
                _log.Info("Consulta ao modelo concluida. Etapa=" + etapaConsulta + " DuracaoMs=" + (long)sw.Elapsed.TotalMilliseconds + " RespostaChars=" + (resposta == null ? 0 : resposta.Length));
                return resposta;
            }
            catch (TaskCanceledException ex)
            {
                sw.Stop();
                _log.Error("Consulta ao modelo cancelada. Etapa=" + etapaConsulta + " DuracaoMs=" + (long)sw.Elapsed.TotalMilliseconds + " PromptChars=" + promptChars);
                _log.Error("Tipo da excecao: " + ex.GetType().FullName);
                _log.Error("Mensagem: " + ex.Message);
                if (ex.InnerException != null)
                    _log.Error("InnerException: " + ex.InnerException.GetType().FullName + " - " + ex.InnerException.Message);
                _log.Error("Possivel causa: timeout HTTP, cancelamento da tarefa, instabilidade de rede ou encerramento da requisicao pelo provedor.");
                _log.Error("StackTrace parcial: " + TruncateForLog(ex.ToString(), 2000));
                throw new InvalidOperationException("Consulta ao modelo cancelada durante " + etapaConsulta + ". Verifique timeout/rede/provedor. PromptChars=" + promptChars + " DuracaoMs=" + (long)sw.Elapsed.TotalMilliseconds + (timeoutMs > 0 ? " TimeoutMs=" + timeoutMs : string.Empty), ex);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _log.Error("Erro ao consultar modelo. Etapa=" + etapaConsulta + " DuracaoMs=" + (long)sw.Elapsed.TotalMilliseconds + " PromptChars=" + promptChars);
                _log.Error("Tipo da excecao: " + ex.GetType().FullName);
                _log.Error("Mensagem: " + ex.Message);
                if (ex.InnerException != null)
                    _log.Error("InnerException: " + ex.InnerException.GetType().FullName + " - " + ex.InnerException.Message);
                _log.Error("StackTrace parcial: " + TruncateForLog(ex.ToString(), 2000));
                throw;
            }
        }

        private void LogGeminiException(string message, GeminiResponseException ex, string prompt)
        {
            _log.Error(message);
            _log.Error("Gemini FinishReason: " + (ex == null ? "" : ex.FinishReason));
            _log.Error("Prompt enviado. Caracteres: " + (prompt == null ? 0 : prompt.Length));
            if (!string.IsNullOrWhiteSpace(ex?.RawResponse))
                _log.Error("Gemini resposta bruta: " + TruncateForLog(ex.RawResponse, 2000));
        }

        private static string BuildGeminiRecitationUserMessage(GeminiResponseException ex, string prompt)
        {
            string finishReason = ex == null ? string.Empty : ex.FinishReason;
            int promptChars = prompt == null ? 0 : prompt.Length;

            if (string.Equals(finishReason, "RECITATION", StringComparison.OrdinalIgnoreCase))
            {
                return "O Gemini bloqueou a resposta por recitacao mesmo apos as tentativas automaticas com contexto reduzido e minimo. " +
                       "Isso nao indica erro interno do atcIA; o provedor recusou gerar o texto. " +
                       "Tente novamente com uma instrucao menor, com menos arquivos no contexto, ou use outra IA ativa para esta etapa. " +
                       "Detalhes: FinishReason=RECITATION; PromptChars=" + promptChars + ".";
            }

            return "Gemini nao retornou texto. FinishReason=" + finishReason + ". PromptChars=" + promptChars + ".";
        }

        private async Task<string> ConsultarModeloComEtapasAsync(
            string systemPrompt,
            string promptBase,
            string contextoProjeto,
            string entradaUsuario,
            string finalPrompt)
        {
            int limite = ObterLimitePrompt();

            if (limite <= 0 || string.IsNullOrEmpty(finalPrompt) || finalPrompt.Length <= limite)
                return await ConsultarModeloComFallbackAsync(
                    systemPrompt,
                    promptBase,
                    contextoProjeto,
                    entradaUsuario,
                    finalPrompt).ConfigureAwait(false);

            _log.Info("Prompt final acima do limite configurado. Caracteres=" + finalPrompt.Length + " Limite=" + limite);
            ReportStatus("Prompt grande; dividindo em etapas");

            string promptSelecao = BuildPromptSelecaoContexto(systemPrompt, contextoProjeto, entradaUsuario, limite);
            _log.Info("Prompt etapa 1 preparado. Caracteres: " + promptSelecao.Length);

            ReportStatus("Etapa 1: selecionando contexto");

            string respostaSelecao;
            try
            {
                respostaSelecao = await ConsultarModeloComFallbackAsync(
                    systemPrompt,
                    string.Empty,
                    string.Empty,
                    entradaUsuario,
                    promptSelecao).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTemporaryProgrammerFailure(ex))
            {
                _log.Error("[PROGRAMADOR-RETRY] Erro temporário na etapa 1.");
                _log.Info("[PROGRAMADOR-RETRY] Retry enxuto ativado.");
                _log.Info("[PROGRAMADOR-RETRY] Prompt original chars: " + promptSelecao.Length);

                string contextoProjetoEnxuto = BuildMinimalContext(
                    contextoProjeto,
                    entradaUsuario,
                    Math.Max(12000, limite / 2));

                string promptSelecaoRetry = BuildPromptSelecaoContexto(systemPrompt, contextoProjetoEnxuto, entradaUsuario, Math.Max(12000, limite / 2));
                _log.Info("[PROGRAMADOR-RETRY] Prompt retry chars: " + promptSelecaoRetry.Length);

                try
                {
                    respostaSelecao = await ConsultarModeloComFallbackAsync(
                        systemPrompt,
                        string.Empty,
                        string.Empty,
                        entradaUsuario,
                        promptSelecaoRetry).ConfigureAwait(false);
                }
                catch (Exception retryEx) when (IsTemporaryProgrammerFailure(retryEx))
                {
                    throw new InvalidOperationException(
                        "A IA não respondeu ao selecionar o contexto do Programador. O endpoint pode estar instável ou o contexto pode estar grande demais.",
                        retryEx);
                }
            }

            string raizProjeto = ExtrairRaizDoContexto(contextoProjeto);
            _log.Info("[INFO] Verificando modo arquivo único explícito na demanda original.");
            _log.Info("[INFO] Fonte usada para extrair caminhos citados: demanda original");
            var arquivosNaoAlterar = ExtrairArquivosNaoAlterarExplicitamente(raizProjeto, entradaUsuario).ToList();
            _log.Info("[INFO] Arquivos marcados como nao alterar: " + arquivosNaoAlterar.Count);
            var arquivosCitadosExplicitamente = LocalizarArquivosCitadosExplicitamente(raizProjeto, entradaUsuario, arquivosNaoAlterar).ToList();
            bool modoArquivoUnicoExplicito = TentarAtivarModoArquivoUnicoExplicito(
                raizProjeto,
                entradaUsuario,
                arquivosCitadosExplicitamente,
                arquivosNaoAlterar,
                out string arquivoUnicoExplicito);

            List<string> arquivosSelecionados;
            if (modoArquivoUnicoExplicito)
            {
                _log.Info("[INFO] Arquivo único explícito identificado: " + MakeRelativeSafe(raizProjeto, arquivoUnicoExplicito));
                _log.Info(
                    "Modo arquivo unico explicito ativado: " +
                    MakeRelativeSafe(raizProjeto, arquivoUnicoExplicito));
                _log.Info("Selecao automatica por identificadores desativada por modo arquivo unico explicito.");
                arquivosSelecionados = new List<string> { arquivoUnicoExplicito };
            }
            else
            {
                arquivosSelecionados = ExtrairArquivosCitados(respostaSelecao, null)
                    .SelectMany(nome => LocalizarArquivosNoProjeto(raizProjeto, nome))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (arquivosCitadosExplicitamente.Count > 0)
                {
                    _log.Info(
                        "Arquivos adicionados por caminhos citados pelo usuario: " +
                        string.Join(", ", arquivosCitadosExplicitamente.Select(p => MakeRelativeSafe(raizProjeto, p))));

                    arquivosSelecionados = arquivosCitadosExplicitamente
                        .Concat(arquivosSelecionados)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                arquivosSelecionados = ExpandirArquivosRelacionados(
                    raizProjeto,
                    entradaUsuario,
                    respostaSelecao,
                    arquivosSelecionados)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                arquivosSelecionados = CorrigirSelecaoWinFormsPorDemanda(
                    raizProjeto,
                    entradaUsuario,
                    respostaSelecao,
                    arquivosSelecionados)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var arquivosPorIdentificadores = LocalizarArquivosPorIdentificadoresExplicitos(raizProjeto, entradaUsuario).ToList();
                if (arquivosPorIdentificadores.Count > 0)
                {
                    arquivosPorIdentificadores = FiltrarArquivosNaoAlterar(raizProjeto, arquivosPorIdentificadores, arquivosNaoAlterar);
                    _log.Info(
                        "Arquivos adicionados por identificadores citados pelo usuario: " +
                        string.Join(", ", arquivosPorIdentificadores.Select(p => MakeRelativeSafe(raizProjeto, p))));

                    arquivosSelecionados = arquivosPorIdentificadores
                        .Concat(arquivosSelecionados)
                        .Where(path => !EstaEmNaoAlterar(path, arquivosNaoAlterar))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }

            arquivosSelecionados = FiltrarArquivosNaoAlterar(raizProjeto, arquivosSelecionados, arquivosNaoAlterar);

            if (arquivosSelecionados.Count > 0)
            {
                _log.Info(
                    "Arquivos selecionados para etapa 2: " +
                    string.Join(", ", arquivosSelecionados.Select(p => MakeRelativeSafe(raizProjeto, p))));
            }

            string contextoFocado = MontarContextoFocadoPorResposta(
                contextoProjeto,
                respostaSelecao,
                arquivosSelecionados,
                limite);

            string arquivosPermitidosEtapa2 = arquivosSelecionados == null || arquivosSelecionados.Count == 0
                ? "(nenhum arquivo especifico selecionado; use somente arquivos presentes no contexto focado)"
                : string.Join(
                    Environment.NewLine,
                    arquivosSelecionados.Select(p => "- " + MakeRelativeSafe(raizProjeto, p)));

            string entradaEtapa2 = entradaUsuario +
                Environment.NewLine +
                Environment.NewLine +
                "[EXECUCAO EM ETAPAS]" +
                Environment.NewLine +
                "A etapa anterior selecionou o contexto necessario. Gere agora o JSON de acoes executaveis do sistema para executar toda a tarefa solicitada. Se a tarefa for grande, divida em varias acoes pequenas e sequenciais dentro da mesma resposta." +
                Environment.NewLine +
                Environment.NewLine +
                "[ARQUIVOS PERMITIDOS PARA ARQ=]" +
                Environment.NewLine +
                arquivosPermitidosEtapa2 +
                Environment.NewLine +
                Environment.NewLine +
                "Regras obrigatorias da etapa 2:" +
                Environment.NewLine +
                "- use ARQ= somente com os arquivos permitidos acima ou arquivos novos criados por REPLACE_BLOCK completo;" +
                Environment.NewLine +
                "- nunca invente MainForm.cs, Form1.cs, HomeController.cs, Program.cs ou outro nome generico;" +
                Environment.NewLine +
                "- antes de usar SEARCH, SEARCH_BLOCK ou DELETE, verifique que o arquivo alvo existe na lista de arquivos permitidos e que o trecho buscado existe exatamente no contexto focado;" +
                Environment.NewLine +
                "- nao use SEARCH ou SEARCH_BLOCK em arquivo que nao esteja listado em [ARQUIVOS PERMITIDOS PARA ARQ=];" +
                Environment.NewLine +
                "- se o arquivo permitido for Forms\\Inicial.cs, nao procure InitializeComponent nele a menos que esse metodo apareca explicitamente no contexto desse arquivo;" +
                Environment.NewLine +
                "- em WinForms, InitializeComponent normalmente fica no arquivo *.Designer.cs; o arquivo .cs principal contem handlers, metodos e logica da tela;" +
                Environment.NewLine +
                "- em WinForms, para adicionar botao, prefira criar metodo no .cs principal e adicionar o controle dinamicamente em painel existente, sem editar Designer.cs;" +
                Environment.NewLine +
                "- se precisar editar *.Designer.cs, use somente trechos que aparecem exatamente no contexto focado do *.Designer.cs;" +
                Environment.NewLine +
                "- nao misture regioes diferentes do InitializeComponent no mesmo SEARCH_BLOCK; declaracoes de controles e configuracoes visuais podem estar em pontos diferentes;" +
                Environment.NewLine +
                "- se nao houver ancora segura no contexto focado, retorne acoes vazia e explique a falta de ancora em explicacao.";

            string promptEtapa2 = BuildFinalPrompt(
                systemPrompt,
                promptBase,
                contextoFocado,
                entradaEtapa2,
                false);

            if (promptEtapa2.Length > limite)
            {
                contextoFocado = BuildRelevantReducedContext(
                    contextoFocado,
                    entradaUsuario + Environment.NewLine + respostaSelecao,
                    Math.Max(4000, limite - FixedPromptOverhead(systemPrompt, promptBase, entradaEtapa2)));

                promptEtapa2 = BuildFinalPrompt(
                    systemPrompt,
                    promptBase,
                    contextoFocado,
                    entradaEtapa2,
                    false);
            }

            _log.Info("Prompt etapa 2 preparado. Caracteres: " + promptEtapa2.Length);
            ReportStatus("Etapa 2: gerando acoes");

            try
            {
                return await ConsultarModeloComFallbackAsync(
                    systemPrompt,
                    promptBase,
                    contextoFocado,
                    entradaEtapa2,
                    promptEtapa2).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTemporaryProgrammerFailure(ex))
            {
                _log.Error("[PROGRAMADOR-RETRY] Erro temporário na etapa 2.");
                _log.Info("[PROGRAMADOR-RETRY] Retry enxuto de geração ativado.");

                string contextoFocadoEnxuto = BuildMinimalContext(
                    contextoFocado,
                    entradaUsuario + Environment.NewLine + respostaSelecao,
                    Math.Max(8000, limite / 2));

                string promptEtapa2Retry = BuildFinalPrompt(
                    systemPrompt,
                    promptBase,
                    contextoFocadoEnxuto,
                    entradaEtapa2,
                    false);

                if (!string.IsNullOrWhiteSpace(promptEtapa2Retry) && promptEtapa2Retry.Length > limite)
                {
                    contextoFocadoEnxuto = TruncateMiddle(contextoFocadoEnxuto, Math.Max(4000, limite / 2));
                    promptEtapa2Retry = BuildFinalPrompt(
                        systemPrompt,
                        promptBase,
                        contextoFocadoEnxuto,
                        entradaEtapa2,
                        false);
                }

                _log.Info("[PROGRAMADOR-RETRY] Prompt retry chars: " + promptEtapa2Retry.Length);

                try
                {
                    return await ConsultarModeloComFallbackAsync(
                        systemPrompt,
                        promptBase,
                        contextoFocadoEnxuto,
                        entradaEtapa2,
                        promptEtapa2Retry).ConfigureAwait(false);
                }
                catch (Exception retryEx) when (IsTemporaryProgrammerFailure(retryEx))
                {
                    throw new InvalidOperationException(
                        "A IA não respondeu ao gerar alterações no Programador. O endpoint pode estar instável ou o contexto pode estar grande demais.",
                        retryEx);
                }
            }
        }

        private bool IsTemporaryProgrammerFailure(Exception ex)
        {
            if (ex == null)
                return false;

            if (string.Equals(ex.GetType().Name, "ProviderUnavailableException", StringComparison.Ordinal))
                return true;

            if (ex is TaskCanceledException || ex is TimeoutException)
                return true;

            int? statusCode = TryGetIntProperty(ex, "StatusCode");
            if (statusCode.HasValue && IsTemporaryHttpStatus(statusCode.Value))
                return true;

            string message = ex.Message ?? string.Empty;
            if (ContainsTemporaryHttpMessage(message))
                return true;

            return IsTemporaryProgrammerFailure(ex.InnerException);
        }

        private static List<string> CorrigirSelecaoWinFormsPorDemanda(
            string raizProjeto,
            string entradaUsuario,
            string respostaSelecao,
            List<string> arquivosSelecionados)
        {
            var lista = arquivosSelecionados != null
                ? arquivosSelecionados.ToList()
                : new List<string>();

            if (string.IsNullOrWhiteSpace(raizProjeto) || !Directory.Exists(raizProjeto))
                return lista;

            if (!DemandaPedeInterfaceWinForms(entradaUsuario, respostaSelecao))
                return lista;

            bool jaTemFormPrincipal = lista.Any(PareceArquivoFormWinForms);
            bool selecionouServico = lista.Any(PareceArquivoServico);

            if (jaTemFormPrincipal && !selecionouServico)
                return lista;

            string formPrincipal = EncontrarFormularioPrincipalWinForms(raizProjeto);

            if (string.IsNullOrWhiteSpace(formPrincipal))
                return lista;

            lista.RemoveAll(PareceArquivoServico);

            if (!lista.Any(p => string.Equals(p, formPrincipal, StringComparison.OrdinalIgnoreCase)))
                lista.Insert(0, formPrincipal);

            return lista;
        }

        private static bool DemandaPedeInterfaceWinForms(string entradaUsuario, string respostaSelecao)
        {
            string texto = ((entradaUsuario ?? "") + " " + (respostaSelecao ?? "")).ToLowerInvariant();

            return texto.Contains("botao") ||
                   texto.Contains("botÃ£o") ||
                   texto.Contains("tela") ||
                   texto.Contains("interface") ||
                   texto.Contains("form") ||
                   texto.Contains("winforms") ||
                   texto.Contains("designer") ||
                   texto.Contains("click") ||
                   texto.Contains("btn") ||
                   texto.Contains("painel") ||
                   texto.Contains("panel") ||
                   texto.Contains("controls.add");
        }

        private static bool PareceArquivoServico(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string p = path.Replace('/', '\\');

            return p.IndexOf("\\Services\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   p.EndsWith("Service.cs", StringComparison.OrdinalIgnoreCase) ||
                   p.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase);
        }

        private static bool PareceArquivoFormWinForms(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string p = path.Replace('/', '\\');

            if (!p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return false;

            if (p.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                return false;

            return p.IndexOf("\\Forms\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   p.EndsWith("\\Inicial.cs", StringComparison.OrdinalIgnoreCase) ||
                   p.EndsWith("\\MainForm.cs", StringComparison.OrdinalIgnoreCase) ||
                   p.EndsWith("\\FormPrincipal.cs", StringComparison.OrdinalIgnoreCase) ||
                   p.EndsWith("\\FrmPrincipal.cs", StringComparison.OrdinalIgnoreCase);
        }

        private static string EncontrarFormularioPrincipalWinForms(string raizProjeto)
        {
            string porProgramCs = EncontrarFormularioPrincipalViaProgramCs(raizProjeto);
            if (!string.IsNullOrWhiteSpace(porProgramCs))
                return porProgramCs;

            string[] nomesPreferidos =
            {
        "Inicial.cs",
        "MainForm.cs",
        "FormPrincipal.cs",
        "FrmPrincipal.cs",
        "Principal.cs"
    };

            foreach (string nome in nomesPreferidos)
            {
                string encontrado = Directory
                    .GetFiles(raizProjeto, nome, SearchOption.AllDirectories)
                    .FirstOrDefault(p =>
                        p.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                        p.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                        !p.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(encontrado))
                    return encontrado;
            }

            return Directory
                .GetFiles(raizProjeto, "*.cs", SearchOption.AllDirectories)
                .FirstOrDefault(PareceArquivoFormWinForms);
        }

        private static string EncontrarFormularioPrincipalViaProgramCs(string raizProjeto)
        {
            var programFiles = Directory.GetFiles(raizProjeto, "Program.cs", SearchOption.AllDirectories);

            foreach (string programFile in programFiles)
            {
                if (programFile.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    programFile.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                string text;

                try
                {
                    text = File.ReadAllText(programFile);
                }
                catch
                {
                    continue;
                }

                var match = Regex.Match(
                    text,
                    @"Application\.Run\s*\(\s*new\s+(?<form>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
                    RegexOptions.IgnoreCase);

                if (!match.Success)
                    continue;

                string formName = match.Groups["form"].Value;
                string formFileName = formName + ".cs";

                string found = Directory
                    .GetFiles(raizProjeto, formFileName, SearchOption.AllDirectories)
                    .FirstOrDefault(p =>
                        p.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                        p.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                        !p.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(found))
                    return found;
            }

            return null;
        }

        private int ObterLimitePrompt()
        {
            if (MaxPromptChars < 12000)
                return 12000;
            if (MaxPromptChars > 200000)
                return 200000;
            return MaxPromptChars;
        }

        private AgentResponse InterpretarRespostaIA(
            string respostaIA,
            out int totalAcoes,
            out bool semAcoesExecutaveis)
        {
            ReportStatus("Interpretando resposta da IA");

            AgentResponse structuredResponse = AgentResponseParser.Parse(respostaIA);
            NormalizarAcoesNaoExecutaveis(ref structuredResponse);

            totalAcoes = structuredResponse?.Acoes == null ? 0 : structuredResponse.Acoes.Count;

            _log.Info("Resposta interpretada. Acoes: " + totalAcoes);
            RegistrarContratoCanonicoDiagnostico(respostaIA, structuredResponse, totalAcoes);
            ActionParseResult parseResult = RegistrarPipelineDiagnostico(respostaIA, structuredResponse);

            semAcoesExecutaveis = totalAcoes == 0;

            if (semAcoesExecutaveis &&
                parseResult != null &&
                parseResult.Outcome == ActionParseOutcome.GitDiffDetected)
            {
                _log.Info("[GIT-DIFF] GitDiffDetected priorizado sobre fluxo sem ações.");
                _log.Info("[GIT-DIFF] Ignorando validação antiga de zero ações para resposta GitDiff.");
                return structuredResponse;
            }

            if (semAcoesExecutaveis &&
                parseResult != null &&
                parseResult.Outcome == ActionParseOutcome.TechnicalSignalsWithoutActions &&
                UseGitDiff &&
                KimiActionDialectRecovery.IsPotentialDialectCandidate(respostaIA))
            {
                _log.Info("[KIMI-RECOVERY] Resposta sem GitDiff candidata a recuperação.");
                semAcoesExecutaveis = false;
            }

            if (semAcoesExecutaveis)
                RegistrarRespostaSemAcoes(respostaIA, structuredResponse);

            return structuredResponse;
        }

        private void NormalizarAcoesNaoExecutaveis(ref AgentResponse structuredResponse)
        {
            if (structuredResponse == null || structuredResponse.Acoes == null || structuredResponse.Acoes.Count == 0)
                return;

            int antes = structuredResponse.Acoes.Count;
            structuredResponse.Acoes = structuredResponse.Acoes
                .Where(IsAcaoExecutavel)
                .ToList();

            if (structuredResponse.Acoes.Count < antes)
                _log.Info("[PARSER] Ação do tipo Nenhuma normalizada para sem ações executáveis.");
        }

        private void RegistrarContratoCanonicoDiagnostico(string respostaIA, AgentResponse structuredResponse, int totalAcoes)
        {
            var parseResult = ActionParseResult.Create(
                respostaIA,
                structuredResponse,
                RespostaSemAcoesContemProtocoloExecutavelForte(respostaIA));

            _log.Info("[PARSER-PIPELINE] Contrato canônico disponível.");
            _log.Info("[PARSER-PIPELINE] Ações interpretadas pelo parser atual: " + totalAcoes);
            _log.Info("[PARSER-PIPELINE] Sinais técnicos: " + parseResult.HasTechnicalActionSignals.ToString().ToLowerInvariant());
            _log.Info("[PARSER-PIPELINE] Ações executáveis no contrato: " + parseResult.ExecutableCount);
            _log.Info("[PARSER-PIPELINE] Preview seguro chars: " + (parseResult.RawResponseSafePreview == null ? 0 : parseResult.RawResponseSafePreview.Length));
        }

        private ActionParseResult RegistrarPipelineDiagnostico(string respostaIA, AgentResponse structuredResponse)
        {
            ActionParseResult parseResult = ActionParserPipeline.Parse(structuredResponse, respostaIA);

            _log.Info("[PARSER-PIPELINE] Pipeline diagnóstico executado.");
            RegistrarResumoFinalParserAct(parseResult);
            _log.Info("[PARSER-PIPELINE] Ações canônicas: " + (parseResult.CanonicalActions == null ? 0 : parseResult.CanonicalActions.Count));
            _log.Info("[PARSER-PIPELINE] Ações executáveis: " + parseResult.CanonicalExecutableCount);
            _log.Info("[PARSER-PIPELINE] Diagnósticos: " + (parseResult.Diagnostics == null ? 0 : parseResult.Diagnostics.Count));
            _log.Info("[PARSER-PIPELINE] Sinais técnicos fortes: " + parseResult.HasTechnicalActionSignals.ToString().ToLowerInvariant());
            _log.Info("[PARSER-PIPELINE] Ações recuperáveis no JSON bruto: " + parseResult.RawRecoveredActionCount);
            _log.Info("[PARSER-PIPELINE] Candidatas executáveis no JSON bruto: " + parseResult.RawRecoveredExecutableCandidateCount);
            if (parseResult.Outcome == ActionParseOutcome.GitDiffDetected)
                _log.Info("[GIT-DIFF] Texto antes do primeiro diff descartado: " + parseResult.GitDiffPrefixDiscardedBeforeDiff.ToString().ToLowerInvariant());

            if (UseGitDiff && parseResult.Outcome == ActionParseOutcome.TechnicalSignalsWithoutActions)
            {
                if (KimiActionDialectRecovery.IsPotentialDialectCandidate(respostaIA))
                    _log.Info("[KIMI-RECOVERY] Resposta sem GitDiff candidata a recuperação.");
            }

            if (parseResult.RawDirectPatchFieldsFound)
                _log.Info("[PARSER-PIPELINE] Campos diretos encontrados no JSON bruto.");

            if (parseResult.TypedDeserializationLostFields)
                _log.Info("[PARSER-PIPELINE] Desserialização tipada perdeu campos executáveis do JSON bruto.");

            foreach (string line in parseResult.GetDiagnosticsLogLines())
                _log.Info(line);

            if (parseResult.Actions != null)
            {
                foreach (var action in parseResult.Actions)
                {
                    if (action == null)
                        continue;

                    if (!action.IsExecutable && string.Equals(action.Type, "Nenhuma", StringComparison.OrdinalIgnoreCase))
                        _log.Info("[PARSER-PIPELINE] Ação não executável detectada: Nenhuma");
                }
            }

            return parseResult;
        }

        private void RegistrarResumoFinalParserAct(ActionParseResult parseResult)
        {
            if (parseResult == null)
                return;

            _log.Info("[PARSER-PIPELINE] Resultado final: " + parseResult.Outcome);
            _log.Info("[PARSER-PIPELINE] Resumo: " + parseResult.BuildUserSafeSummary());
        }

        private void RegistrarDiagnosticoGitDiff(string rawResponse, ActionParseResult parseResult, GitDiffParseResult gitDiff = null, GitDiffSafetyResult safety = null)
        {
            if (parseResult == null)
                return;

            string gitDiffText = parseResult.GitDiffEffectiveText;
            if (gitDiff == null)
                gitDiff = GitDiffAdapter.Parse(string.IsNullOrWhiteSpace(gitDiffText) ? rawResponse : gitDiffText);
            if (safety == null)
                safety = ValidarGitDiffSeguranca(gitDiff, _projectProvider == null ? string.Empty : _projectProvider.GetActiveProjectRoot());

            _log.Info("[GIT-DIFF] Chars GitDiff efetivo: " + (string.IsNullOrWhiteSpace(gitDiffText) ? 0 : gitDiffText.Length));
            _log.Info("[GIT-DIFF] GitDiff efetivo contém header diff --git: " + (!string.IsNullOrWhiteSpace(gitDiffText) && gitDiffText.IndexOf("diff --git", StringComparison.OrdinalIgnoreCase) >= 0).ToString().ToLowerInvariant());
            _log.Info("[GIT-DIFF] GitDiff efetivo contém ---: " + (!string.IsNullOrWhiteSpace(gitDiffText) && gitDiffText.IndexOf("\n--- ", StringComparison.OrdinalIgnoreCase) >= 0).ToString().ToLowerInvariant());
            _log.Info("[GIT-DIFF] GitDiff efetivo contém +++: " + (!string.IsNullOrWhiteSpace(gitDiffText) && gitDiffText.IndexOf("\n+++ ", StringComparison.OrdinalIgnoreCase) >= 0).ToString().ToLowerInvariant());
            _log.Info("[GIT-DIFF] GitDiff efetivo contém @@: " + (!string.IsNullOrWhiteSpace(gitDiffText) && gitDiffText.IndexOf("@@", StringComparison.OrdinalIgnoreCase) >= 0).ToString().ToLowerInvariant());
            _log.Info("[GIT-DIFF] Adapter parse iniciado.");
            if (gitDiff.HadEmptyHunks)
                _log.Info("[GIT-DIFF] Hunk vazio detectado.");
            if (gitDiff.IgnoredTrailingEmptyHunks)
                _log.Info("[GIT-DIFF] Hunk vazio final ignorado com segurança.");
            if (gitDiff.ContainsOnlyEmptyHunks)
                _log.Info("[GIT-DIFF] Diff contém apenas hunks vazios.");
            if (gitDiff.HasEmptyHunkInMiddle)
                _log.Error("[GIT-DIFF] Conversão bloqueada: hunk vazio no meio do diff.");
            _log.Info("[GIT-DIFF] Arquivos parseados: " + gitDiff.Files.Count);
            _log.Info("[GIT-DIFF] Hunks parseados: " + gitDiff.TotalHunks);
            _log.Info("[GIT-DIFF] Validação de segurança iniciada.");
            _log.Info("[GIT-DIFF] Segurança: safe=" + safety.IsSafe.ToString().ToLowerInvariant());
            _log.Info("[GIT-DIFF] Arquivos validados: " + safety.FileCount);
            _log.Info("[GIT-DIFF] Hunks validados: " + safety.HunkCount);
            _log.Info("[GIT-DIFF] Erros de segurança: " + safety.Errors.Count);
            _log.Info("[GIT-DIFF] Warnings de segurança: " + safety.Warnings.Count);

            foreach (var file in gitDiff.Files)
            {
                if (file == null)
                    continue;

                int removed = file.Hunks.Sum(h => h == null || h.Lines == null ? 0 : h.Lines.Count(l => l != null && l.Kind == GitDiffLineKind.Removed));
                int added = file.Hunks.Sum(h => h == null || h.Lines == null ? 0 : h.Lines.Count(l => l != null && l.Kind == GitDiffLineKind.Added));
                int context = file.Hunks.Sum(h => h == null || h.Lines == null ? 0 : h.Lines.Count(l => l != null && l.Kind == GitDiffLineKind.Context));
                _log.Info("[GIT-DIFF] Arquivo parseado: " + (file.EffectivePath ?? string.Empty));
                _log.Info("[GIT-DIFF] Hunks: " + file.Hunks.Count);
                _log.Info("[GIT-DIFF] Linhas removidas: " + removed);
                _log.Info("[GIT-DIFF] Linhas adicionadas: " + added);
                _log.Info("[GIT-DIFF] Linhas contexto: " + context);
            }

            if (gitDiff.Warnings != null)
            {
                foreach (string warning in gitDiff.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)))
                    _log.Info("[GIT-DIFF] Warning: " + warning);
            }

            if (gitDiff.Errors != null)
            {
                foreach (string error in gitDiff.Errors.Where(e => !string.IsNullOrWhiteSpace(e)))
                    _log.Error("[GIT-DIFF] Erro: " + error);
            }

            if (safety.Warnings != null)
            {
                foreach (string warning in safety.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)))
                    _log.Info("[GIT-DIFF] Warning de segurança: " + warning);
            }

            if (safety.Errors != null)
            {
                foreach (string error in safety.Errors.Where(e => !string.IsNullOrWhiteSpace(e)))
                    _log.Error("[GIT-DIFF] Bloqueado: " + error);
            }
        }

        private GitDiffSafetyResult ValidarGitDiffSeguranca(GitDiffParseResult gitDiff, string projectRoot)
        {
            return GitDiffSafetyValidator.Validate(gitDiff, projectRoot);
        }

        private string ConstruirMensagemBloqueioGitDiff(string mensagemAmigavel, GitDiffSafetyResult safety)
        {
            if (safety == null || safety.IsSafe)
                return mensagemAmigavel;

            string motivo = safety.Errors != null && safety.Errors.Count > 0 ? safety.Errors[0] : "desconhecido";
            if (motivo.IndexOf("altera", StringComparison.OrdinalIgnoreCase) >= 0 &&
                motivo.IndexOf("aplic", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "A IA retornou GitDiff sem alteracoes aplicaveis.";
            }

            if (motivo.IndexOf("nenhum arquivo ou hunk valido foi encontrado", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "A IA retornou um GitDiff malformado e ele nao pode ser interpretado com seguranca. Motivo: nenhum arquivo ou hunk valido foi encontrado.";
            }

            if (motivo.IndexOf("hunk vazio no meio", StringComparison.OrdinalIgnoreCase) >= 0)
                return "A IA retornou GitDiff malformado. Motivo: hunk vazio no meio do diff.";

            if (motivo.IndexOf("apenas hunks vazios", StringComparison.OrdinalIgnoreCase) >= 0)
                return "A IA retornou GitDiff sem alterações aplicáveis.";

            return "A IA retornou GitDiff, mas o diff foi bloqueado pela validação de segurança. Motivo: " + motivo;
        }

        private List<AgentAction> CriarAcoesCanonicasGitDiff(GitDiffConversionResult conversion)
        {
            var actions = new List<AgentAction>();
            if (conversion == null || !conversion.Success || conversion.Operations == null)
                return actions;

            foreach (ConvertedGitDiffOperation operation in conversion.Operations)
            {
                if (operation == null || string.IsNullOrWhiteSpace(operation.FilePath) ||
                    string.IsNullOrWhiteSpace(operation.SearchBlock) || string.IsNullOrWhiteSpace(operation.ProtocolText))
                    throw new InvalidOperationException("Ação canônica GitDiff inválida antes do dispatcher.");

                var data = new JObject
                {
                    ["protocolo"] = operation.ProtocolText,
                    ["__canonical"] = true,
                    ["__source"] = "GitDiffAdapter",
                    ["isDeleteOnly"] = operation.IsDeleteOnly,
                    ["filePath"] = operation.FilePath,
                    ["hunkIndex"] = operation.HunkIndex,
                    ["searchBlock"] = operation.SearchBlock,
                    ["replaceBlock"] = operation.ReplaceBlock ?? string.Empty
                };

                var parsed = new ParsedAction
                {
                    Type = AgentActionType.ArquivoLocal.ToString(),
                    Description = "GitDiff convertido: " + operation.FilePath + " hunk=" + operation.HunkIndex,
                    ProtocolText = operation.ProtocolText,
                    Data = data,
                    Source = "GitDiffAdapter",
                    WasNormalized = true,
                    IsExecutable = true
                };

                if (!ValidarAcaoCanonicaAntesDoDispatcher(parsed, out string motivo))
                    throw new InvalidOperationException("Ação canônica GitDiff inválida antes do dispatcher: " + motivo);

                AgentAction action = ToAgentAction(parsed);
                if (action == null)
                    throw new InvalidOperationException("Ação canônica GitDiff não pôde ser criada.");

                actions.Add(action);
                _log.Info("[GIT-DIFF] Ação canônica criada: " + operation.FilePath + " hunk=" + operation.HunkIndex);
                _log.Info("[GIT-DIFF] Delete-only canônico: " + (operation.IsDeleteOnly ? "true" : "false"));
                if (operation.IsDeleteOnly)
                    _log.Info("[GIT-DIFF] EMPTY_REPLACE permitido por operação delete-only segura.");
            }

            return actions;
        }

        private void RegistrarDiagnosticoConversaoGitDiff(GitDiffConversionResult conversion)
        {
            if (conversion == null)
                return;

            _log.Info("[GIT-DIFF] Conversão para protocolo interno iniciada.");
            foreach (var op in conversion.Operations ?? Enumerable.Empty<ConvertedGitDiffOperation>())
            {
                if (op == null)
                    continue;

                _log.Info("[GIT-DIFF] Operação convertida: " + (op.FilePath ?? string.Empty) + " hunk=" + op.HunkIndex);
                _log.Info("[GIT-DIFF] SEARCH chars: " + (op.SearchBlock == null ? 0 : op.SearchBlock.Length));
                _log.Info("[GIT-DIFF] REPLACE chars: " + (op.ReplaceBlock == null ? 0 : op.ReplaceBlock.Length));
                if (op.IsDeleteOnly)
                {
                    _log.Info("[GIT-DIFF] Hunk de remoção pura detectado.");
                    _log.Info("[GIT-DIFF] Delete-only sem contexto: " + (op.ContextLines <= 0 ? "true" : "false"));
                    _log.Info("[GIT-DIFF] REPLACE_BLOCK vazio permitido para remoção pura.");
                    _log.Info("[GIT-DIFF] RemovedLines: " + op.RemovedLines);
                    _log.Info("[GIT-DIFF] Search chars: " + (op.SearchBlock == null ? 0 : op.SearchBlock.Length));
                }
            }
            _log.Info("[GIT-DIFF] Operações convertidas: " + (conversion.Operations == null ? 0 : conversion.Operations.Count));
            _log.Info("[GIT-DIFF] Erros de conversão: " + (conversion.Errors == null ? 0 : conversion.Errors.Count));
            _log.Info("[GIT-DIFF] Warnings de conversão: " + (conversion.Warnings == null ? 0 : conversion.Warnings.Count));

            if (conversion.Warnings != null)
            {
                foreach (string warning in conversion.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)))
                    _log.Info("[GIT-DIFF] Warning de conversão: " + warning);
            }

            if (conversion.Errors != null)
            {
                foreach (string error in conversion.Errors.Where(e => !string.IsNullOrWhiteSpace(e)))
                {
                    if (string.Equals(error, "hunk de remoção sem contexto suficiente", StringComparison.OrdinalIgnoreCase))
                        _log.Error("[GIT-DIFF] Conversão bloqueada: hunk de remoção sem contexto suficiente.");
                    _log.Error("[GIT-DIFF] Erro de conversão: " + error);
                }
            }
        }

        private void RegistrarErroBrutoDaIA(string provider, string model, ActionParseResult parseResult, string rawResponse)
        {
            if (!DeveRegistrarErroBrutoDaIA(parseResult))
                return;

            string safeResponse = ActionParseResult.SafePreview(rawResponse, 20000);
            int chars = string.IsNullOrWhiteSpace(rawResponse) ? 0 : rawResponse.Length;

            _log.Error("[AI-RAW-ERROR] Resposta bruta completa da IA abaixo.");
            _log.Error("[AI-RAW-ERROR] Provider: " + (string.IsNullOrWhiteSpace(provider) ? "(desconhecido)" : provider));
            _log.Error("[AI-RAW-ERROR] Modelo: " + (string.IsNullOrWhiteSpace(model) ? "(desconhecido)" : model));
            _log.Error("[AI-RAW-ERROR] Outcome: " + (parseResult == null ? "(desconhecido)" : parseResult.Outcome.ToString()));
            _log.Error("[AI-RAW-ERROR] Chars: " + chars);
            _log.Error("[AI-RAW-ERROR-BEGIN]");
            if (!string.IsNullOrWhiteSpace(safeResponse))
                _log.Error(safeResponse);
            if (!string.IsNullOrWhiteSpace(rawResponse) && rawResponse.Length > 20000)
                _log.Error("[AI-RAW-ERROR] Resposta truncada para 20000 caracteres.");
            _log.Error("[AI-RAW-ERROR-END]");
        }

        private void RegistrarErroBrutoDaIAAtual(ActionParseResult parseResult, string rawResponse)
        {
            RegistrarErroBrutoDaIA(
                _modelClient == null ? _modelDisplayName : _modelClient.ProviderName,
                _modelClient == null ? string.Empty : _modelClient.ModelName,
                parseResult,
                rawResponse);
        }

        private static bool DeveRegistrarErroBrutoDaIA(ActionParseResult parseResult)
        {
            if (parseResult == null)
                return false;

            switch (parseResult.Outcome)
            {
                case ActionParseOutcome.InvalidLocalFile:
                case ActionParseOutcome.TechnicalSignalsWithoutActions:
                case ActionParseOutcome.UnknownInvalidFormat:
                case ActionParseOutcome.NoExecutableActions:
                case ActionParseOutcome.NoOpOnly:
                case ActionParseOutcome.GitDiffDetected:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsAcaoExecutavel(AgentAction action)
        {
            if (action == null)
                return false;

            switch (action.Tipo)
            {
                case AgentActionType.ArquivoLocal:
                case AgentActionType.ComandoDos:
                case AgentActionType.Ftp:
                    return true;
                default:
                    return false;
            }
        }

        private void ValidarAcoesArquivoLocalAntesDoDispatcher(AgentResponse structuredResponse)
        {
            if (structuredResponse == null || structuredResponse.Acoes == null || structuredResponse.Acoes.Count == 0)
                return;

            for (int i = 0; i < structuredResponse.Acoes.Count; i++)
            {
                var action = structuredResponse.Acoes[i];
                if (action == null || action.Tipo != AgentActionType.ArquivoLocal)
                    continue;

                if (TemDadosExecutaveisArquivoLocal(action.Dados))
                    continue;

                string descricao = string.IsNullOrWhiteSpace(action.Descricao) ? "(sem descricao)" : action.Descricao.Trim();
                string campos = DescreverCamposPresentesArquivoLocal(action.Dados);
                _log.Info("[PARSER] ArquivoLocal sem protocolo executável rejeitado antes do dispatcher.");
                _log.Info("[PARSER] Tipo: " + action.Tipo);
                _log.Info("[PARSER] Descricao: " + descricao);
                _log.Info("[PARSER] Campos presentes: " + campos);
                throw new InvalidOperationException("A IA retornou ação de arquivo local sem dados executáveis.");
            }
        }

        private static bool TemDadosExecutaveisArquivoLocal(JObject dados)
        {
            if (dados == null)
                return false;

            if (TemTextoNaoVazio(dados, "protocolo", "comando", "bloco", "bloco_protocolo"))
                return true;

            if (TemArrayNaoVazia(dados["comandos"]) || TemArrayNaoVazia(dados["commands"]))
                return true;

            bool temArq = TemTextoNaoVazio(dados, "ARQ", "arq");
            bool temSearch = TemTextoNaoVazio(dados, "SEARCH", "search");
            bool temReplace = TemTextoNaoVazio(dados, "REPLACE", "replace");
            bool temSearchBlock = TemTextoNaoVazio(dados, "SEARCH_BLOCK", "search_block");
            bool temReplaceBlock = TemTextoNaoVazio(dados, "REPLACE_BLOCK", "replace_block");

            return temArq && ((temSearch && temReplace) || (temSearchBlock && temReplaceBlock));
        }

        private static bool TemTextoNaoVazio(JObject dados, params string[] propertyNames)
        {
            if (dados == null || propertyNames == null)
                return false;

            foreach (string propertyName in propertyNames)
            {
                var token = dados[propertyName];
                if (token != null && !string.IsNullOrWhiteSpace(token.ToString()))
                    return true;
            }

            return false;
        }

        private static bool TemArrayNaoVazia(JToken token)
        {
            return token != null && token.Type == JTokenType.Array && token.HasValues;
        }

        private static string DescreverCamposPresentesArquivoLocal(JObject dados)
        {
            if (dados == null)
                return "(nenhum)";

            var campos = new List<string>();

            if (dados["protocolo"] != null)
                campos.Add("protocolo");
            if (dados["comando"] != null)
                campos.Add("comando");
            if (dados["bloco"] != null)
                campos.Add("bloco");
            if (dados["bloco_protocolo"] != null)
                campos.Add("bloco_protocolo");
            if (TemArrayNaoVazia(dados["comandos"]))
                campos.Add("comandos");
            if (TemArrayNaoVazia(dados["commands"]))
                campos.Add("commands");
            if (dados["ARQ"] != null || dados["arq"] != null)
                campos.Add("ARQ");
            if (dados["SEARCH"] != null || dados["search"] != null)
                campos.Add("SEARCH");
            if (dados["REPLACE"] != null || dados["replace"] != null)
                campos.Add("REPLACE");
            if (dados["SEARCH_BLOCK"] != null || dados["search_block"] != null)
                campos.Add("SEARCH_BLOCK");
            if (dados["REPLACE_BLOCK"] != null || dados["replace_block"] != null)
                campos.Add("REPLACE_BLOCK");

            return campos.Count == 0 ? "(nenhum)" : string.Join(", ", campos);
        }

        private void RegistrarRespostaSemAcoes(
            string respostaIA,
            AgentResponse structuredResponse)
        {
            bool possuiProtocoloExecutavelForte = RespostaSemAcoesContemProtocoloExecutavelForte(respostaIA);
            bool possuiTermosTecnicos = !possuiProtocoloExecutavelForte && RespostaSemAcoesContemTermosTecnicosGenericos(respostaIA);
            bool semAcoesExecutaveis = structuredResponse?.Acoes == null || structuredResponse.Acoes.Count == 0;
            int respostaChars = string.IsNullOrEmpty(respostaIA) ? 0 : respostaIA.Length;

            if (possuiProtocoloExecutavelForte)
            {
                _log.Info("[DIAG] Zero ações com sinais técnicos detectados.");
                _log.Info("[DIAG] Sinais técnicos encontrados: " + string.Join(", ", ExtrairSinaisTecnicosFortes(respostaIA)));
                _log.Info("[DIAG] Resposta IA chars: " + respostaChars);
                _log.Info("[DIAG] structuredResponse null: " + (structuredResponse == null));
                _log.Info("[DIAG] structuredResponse.acoes count: " + (structuredResponse?.Acoes == null ? 0 : structuredResponse.Acoes.Count));
                _log.Info("[DIAG] semAcoesExecutaveis: " + semAcoesExecutaveis);
                LogRespostaBrutaDiagnostico(respostaIA);

                AgentResponse recuperada = TentarRecuperarArquivoLocalMalformado(respostaIA, structuredResponse);
                if (recuperada != null)
                {
                    structuredResponse.Acoes = recuperada.Acoes;
                    structuredResponse.RequerConfirmacao = recuperada.RequerConfirmacao;
                    if (!string.IsNullOrWhiteSpace(recuperada.MensagemUsuario))
                        structuredResponse.MensagemUsuario = recuperada.MensagemUsuario;
                    if (!string.IsNullOrWhiteSpace(recuperada.Explicacao))
                        structuredResponse.Explicacao = recuperada.Explicacao;

                    _log.Info("[ARQ] ArquivoLocal recuperado de resposta malformada.");
                    return;
                }

                string mensagemErro = "Resposta contem sinais de aÃ§ão, mas nenhuma ação executavel foi interpretada.";
                _log.Error(mensagemErro);
                throw new InvalidOperationException(mensagemErro);
            }

            if (RespostaSemAcoesEhConclusaoSemAlteracao(structuredResponse))
            {
                _log.Info("IA concluiu que nenhuma alteraÃ§Ã£o Ã© necessÃ¡ria.");
            }
            else if (possuiTermosTecnicos)
            {
                _log.Info("[INFO] Resposta sem aÃ§Ãµes contÃ©m termos tÃ©cnicos, mas nenhum protocolo executÃ¡vel forte.");
            }
            else
            {
                _log.Error("A IA retornou zero acoes executaveis.");
            }

            if (!string.IsNullOrWhiteSpace(structuredResponse?.MensagemUsuario))
                _log.Info("Mensagem da IA sem acoes: " + TruncateForLog(structuredResponse.MensagemUsuario, 500));

            if (!string.IsNullOrWhiteSpace(structuredResponse?.Explicacao))
                _log.Info("Explicacao da IA sem acoes: " + TruncateForLog(structuredResponse.Explicacao, 800));

            if (!string.IsNullOrWhiteSpace(respostaIA))
                _log.Info("Resposta bruta sem acoes (parcial): " + TruncateForLog(respostaIA, 1000));
        }

        private void LogRespostaBrutaDiagnostico(string respostaIA)
        {
            if (string.IsNullOrWhiteSpace(respostaIA))
                return;

            string respostaMascarada = MascararSegredosEmTexto(respostaIA);
            if (respostaMascarada.Length <= 8000)
            {
                _log.Info("[DIAG] Resposta IA bruta completa:\n" + respostaMascarada);
                return;
            }

            _log.Info("[DIAG] Resposta IA bruta parcial início:\n" + respostaMascarada.Substring(0, 4000));
            _log.Info("[DIAG] Resposta IA bruta parcial fim:\n" + respostaMascarada.Substring(respostaMascarada.Length - 4000));
        }

        private static string MascararSegredosEmTexto(string texto)
        {
            if (string.IsNullOrEmpty(texto))
                return string.Empty;

            string resultado = texto;
            resultado = Regex.Replace(resultado, @"sk-[A-Za-z0-9_\-]{8,}", "[REDACTED]", RegexOptions.IgnoreCase);
            resultado = Regex.Replace(resultado, @"Bearer\s+[A-Za-z0-9_\-\.\=]{8,}", "Bearer [REDACTED]", RegexOptions.IgnoreCase);
            resultado = Regex.Replace(resultado, "(?i)(OpenAiApiKey|ApiKey|Authorization)\\s*[:=]\\s*([^\\r\\n;,\\\"']+)", "$1: [REDACTED]", RegexOptions.IgnoreCase);
            return resultado;
        }

        private static IEnumerable<string> ExtrairSinaisTecnicosFortes(string respostaIA)
        {
            if (string.IsNullOrWhiteSpace(respostaIA))
                return Enumerable.Empty<string>();

            var sinais = new List<string>();
            string[] marcadores =
            {
                "ArquivoLocal",
                "ComandoDos",
                "SEARCH_BLOCK",
                "SEARCH=",
                "REPLACE_BLOCK",
                "REPLACE=",
                "END_REPLACE",
                "END_SEARCH",
                "dados.protocolo",
                "dados.bloco",
                "dados.comandos",
                "ARQ="
            };

            foreach (string marcador in marcadores)
            {
                if (respostaIA.IndexOf(marcador, StringComparison.OrdinalIgnoreCase) >= 0)
                    sinais.Add(marcador);
            }

            return sinais.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool RespostaSemAcoesEhConclusaoSemAlteracao(AgentResponse structuredResponse)
        {
            if (structuredResponse == null)
                return false;

            if (structuredResponse.RequerConfirmacao)
                return false;

            bool temTexto = !string.IsNullOrWhiteSpace(structuredResponse.MensagemUsuario) ||
                            !string.IsNullOrWhiteSpace(structuredResponse.Explicacao);

            return temTexto;
        }

        private static bool RespostaSemAcoesContemProtocoloExecutavelForte(string respostaIA)
        {
            if (string.IsNullOrWhiteSpace(respostaIA))
                return false;

            return respostaIA.IndexOf("ArquivoLocal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   respostaIA.IndexOf("ComandoDos", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   respostaIA.IndexOf("SEARCH_BLOCK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   respostaIA.IndexOf("SEARCH=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   respostaIA.IndexOf("REPLACE_BLOCK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   respostaIA.IndexOf("REPLACE=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   respostaIA.IndexOf("END_REPLACE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   respostaIA.IndexOf("dados.protocolo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   respostaIA.IndexOf("dados.bloco", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   respostaIA.IndexOf("dados.comandos", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   respostaIA.IndexOf("ARQ=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   respostaIA.IndexOf("END_SEARCH", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool RespostaSemAcoesContemTermosTecnicosGenericos(string respostaIA)
        {
            if (string.IsNullOrWhiteSpace(respostaIA))
                return false;

            string[] termos = new[]
            {
                "arquivo",
                "endpoint",
                "funÃ§Ã£o",
                "funcao",
                "versÃ£o",
                "versao",
                "provider",
                "steps",
                "errormessage",
                "success",
                "renderedimages",
                "validar",
                "alterar",
                "implementar",
                "verificar",
                "main.py",
                "fastapi"
            };

            return termos.Any(termo => respostaIA.IndexOf(termo, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private AgentResponse TentarRecuperarArquivoLocalMalformado(string respostaIA, AgentResponse structuredResponse)
        {
            if (string.IsNullOrWhiteSpace(respostaIA))
                return null;

            if (respostaIA.IndexOf("ArquivoLocal", StringComparison.OrdinalIgnoreCase) < 0 ||
                respostaIA.IndexOf("ARQ=", StringComparison.OrdinalIgnoreCase) < 0 ||
                respostaIA.IndexOf("SEARCH_BLOCK", StringComparison.OrdinalIgnoreCase) < 0 ||
                respostaIA.IndexOf("REPLACE_BLOCK", StringComparison.OrdinalIgnoreCase) < 0 ||
                respostaIA.IndexOf("END_REPLACE", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            string bloco = ExtrairBlocoArquivoLocalMalformado(respostaIA);
            if (string.IsNullOrWhiteSpace(bloco))
                return null;

            var dados = new JObject
            {
                ["protocolo"] = bloco
            };

            return new AgentResponse
            {
                MensagemUsuario = structuredResponse?.MensagemUsuario,
                Explicacao = structuredResponse?.Explicacao,
                RequerConfirmacao = structuredResponse?.RequerConfirmacao ?? false,
                Acoes = new List<AgentAction>
                {
                    new AgentAction
                    {
                        Tipo = AgentActionType.ArquivoLocal,
                        Descricao = "ArquivoLocal recuperado de resposta malformada",
                        Dados = dados,
                        RequerConfirmacao = structuredResponse?.RequerConfirmacao ?? false
                    }
                }
            };
        }

        private static string ExtrairBlocoArquivoLocalMalformado(string respostaIA)
        {
            int inicio = respostaIA.IndexOf("ARQ=", StringComparison.OrdinalIgnoreCase);
            if (inicio < 0)
                return null;

            int fim = respostaIA.IndexOf("END_REPLACE", inicio, StringComparison.OrdinalIgnoreCase);
            if (fim < 0)
                return null;

            fim += "END_REPLACE".Length;

            string bloco = respostaIA.Substring(inicio, fim - inicio);
            bloco = LimparTextoMalformadoDeProtocolo(bloco);

            if (bloco.IndexOf("ARQ=", StringComparison.OrdinalIgnoreCase) < 0 ||
                bloco.IndexOf("SEARCH_BLOCK", StringComparison.OrdinalIgnoreCase) < 0 ||
                bloco.IndexOf("REPLACE_BLOCK", StringComparison.OrdinalIgnoreCase) < 0 ||
                bloco.IndexOf("END_REPLACE", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            return bloco;
        }

        private static string LimparTextoMalformadoDeProtocolo(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return texto;

            var linhas = texto
                .Replace("\\\\", "\\")
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(linha => linha.Trim().TrimEnd(',').Trim())
                .Where(linha => !string.IsNullOrWhiteSpace(linha))
                .Select(linha => linha.Trim('"'))
                .ToList();

            string textoLimpo = string.Join(Environment.NewLine, linhas);

            textoLimpo = textoLimpo.Replace("\"SEARCH_BLOCK\"", "SEARCH_BLOCK")
                                   .Replace("\"REPLACE_BLOCK\"", "REPLACE_BLOCK")
                                   .Replace("\"END_SEARCH\"", "END_SEARCH")
                                   .Replace("\"END_REPLACE\"", "END_REPLACE")
                                   .Replace("\"ARQ=", "ARQ=");

            return textoLimpo.Trim();
        }

        private sealed class RetrySemAcoesResult
        {
            public AgentResponse StructuredResponse { get; set; }
            public string FinalPrompt { get; set; }
            public string RespostaIA { get; set; }
            public string ContextoProjeto { get; set; }
            public List<AgentAction> ExecutedActions { get; set; }
        }

        private sealed class PatchRetryContext
        {
            public bool HasPatchFailure { get; set; }
            public string Kind { get; set; }
            public string Code { get; set; }
            public string Title { get; set; }
            public string Message { get; set; }
            public string FilePath { get; set; }
            public int OperationIndex { get; set; }
            public string InvalidSearchPreview { get; set; }
            public string HintForRetry { get; set; }
            public string NearbyFileSnippet { get; set; }
            public string RetryInstruction { get; set; }
        }

        private async Task<RetrySemAcoesResult> TentarForcarAcoesQuandoRespostaSemAcoesAsync(
            string systemPrompt,
            string promptBase,
            string contextoProjeto,
            string entradaUsuario,
            string respostaIA,
            AgentResponse structuredResponse,
            string parserOutcome = null)
        {
            if (!RespostaSemAcoesParecePrometerImplementacao(respostaIA, structuredResponse))
                return null;

            ReportStatus("Reconsultando IA para gerar acoes executaveis");
            _log.Info("Resposta sem acoes descreveu implementacao/acoes, mas nao trouxe acoes executaveis. Reconsultando para forcar JSON com acoes.");

            if (RespostaContemCampoAcoes(respostaIA))
            {
                var repairResult = await TentarCorrigirJsonComAcoesAsync(
                    contextoProjeto,
                    entradaUsuario,
                    respostaIA).ConfigureAwait(false);

                if (repairResult != null)
                    return repairResult;
            }

            string entradaReprocessada = entradaUsuario +
                Environment.NewLine +
                Environment.NewLine +
                "[REPROCESSAMENTO AUTOMATICO - ZERO ACOES]" +
                Environment.NewLine +
                "A resposta anterior foi rejeitada porque o campo acoes veio vazio ou nao executavel, embora o texto tenha descrito implementacao, acoes futuras ou etapas pendentes." +
                Environment.NewLine +
                "Retorne agora somente JSON valido do sistema com acoes executaveis. Nao diga que ja implementou se nao houver acoes. Nao peca confirmacao para continuar. Se algo ja existir no codigo, gere apenas as acoes restantes.";

            int limite = ObterLimitePrompt();
            string contextoReduzido = BuildRelevantReducedContext(
                contextoProjeto,
                entradaUsuario + Environment.NewLine + respostaIA,
                Math.Max(8000, limite - FixedPromptOverhead(systemPrompt, promptBase, entradaReprocessada)));

            string retryPrompt = PrepararPromptFinal(
                systemPrompt,
                promptBase,
                contextoReduzido,
                entradaReprocessada,
                false);

            if (retryPrompt.Length > limite)
            {
                contextoReduzido = BuildRelevantReducedContext(
                    contextoProjeto,
                    entradaUsuario + Environment.NewLine + respostaIA,
                    Math.Max(4000, limite / 2));

                retryPrompt = PrepararPromptFinal(
                    systemPrompt,
                    promptBase,
                    contextoReduzido,
                    entradaReprocessada,
                    false);
            }

            _log.Info("Reprocessamento por zero acoes. PromptChars=" + retryPrompt.Length);
            string retryRawResponse = await ConsultarModeloComFallbackAsync(
                systemPrompt,
                promptBase,
                contextoReduzido,
                entradaReprocessada,
                retryPrompt).ConfigureAwait(false);

            int totalAcoes;
            bool semAcoes;
            AgentResponse retryResponse = InterpretarRespostaIA(
                retryRawResponse,
                out totalAcoes,
                out semAcoes);

            if (totalAcoes <= 0)
            {
                _log.Error("Reprocessamento por zero acoes tambem retornou zero acoes executaveis.");
                return null;
            }

            _log.Info("Reprocessamento por zero acoes gerou acoes executaveis: " + totalAcoes);
            return new RetrySemAcoesResult
            {
                StructuredResponse = retryResponse,
                FinalPrompt = retryPrompt,
                RespostaIA = retryRawResponse,
                ContextoProjeto = contextoReduzido
            };
        }

        private async Task<RetrySemAcoesResult> TentarCorrigirJsonComAcoesAsync(
            string contextoProjeto,
            string entradaUsuario,
            string respostaIA)
        {
            string repairPrompt =
                "A resposta abaixo contem um JSON do atcIA com o campo acoes, mas o parser local nao conseguiu interpretar nenhuma acao executavel." +
                Environment.NewLine +
                "Corrija somente o formato JSON. Nao altere a intencao das acoes. Nao explique. Nao use markdown." +
                Environment.NewLine +
                "Retorne exclusivamente um objeto JSON valido com: mensagem_usuario, explicacao, acoes, requer_confirmacao." +
                Environment.NewLine +
                "Dentro de dados.protocolo, preserve quebras de linha escapadas como \\n e aspas internas escapadas como \\\"." +
                Environment.NewLine +
                Environment.NewLine +
                "ENTRADA DO USUARIO:" +
                Environment.NewLine +
                TruncateForPrompt(entradaUsuario, 2500) +
                Environment.NewLine +
                Environment.NewLine +
                "RESPOSTA ANTERIOR A CORRIGIR:" +
                Environment.NewLine +
                TruncateForPrompt(respostaIA, 22000);

            _log.Info("Tentando corrigir JSON com acoes sem reenviar contexto do projeto. PromptChars=" + repairPrompt.Length);
            ReportStatus("Corrigindo formato JSON das acoes");

            string retryRawResponse;
            try
            {
                retryRawResponse = await AskModelWithDiagnosticsAsync("corrigir JSON com acoes", repairPrompt, 45000).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsModelTimeoutException(ex) || ex is TaskCanceledException)
            {
                _log.Error("Correcao curta do JSON com acoes falhou por timeout. " + ex.Message);
                return null;
            }

            int totalAcoes;
            bool semAcoes;
            AgentResponse retryResponse = InterpretarRespostaIA(
                retryRawResponse,
                out totalAcoes,
                out semAcoes);

            if (totalAcoes <= 0)
            {
                _log.Error("Correcao curta do JSON tambem retornou zero acoes executaveis.");
                return null;
            }

            _log.Info("Correcao curta do JSON gerou acoes executaveis: " + totalAcoes);
            return new RetrySemAcoesResult
            {
                StructuredResponse = retryResponse,
                FinalPrompt = repairPrompt,
                RespostaIA = retryRawResponse,
                ContextoProjeto = contextoProjeto
            };
        }

        private static bool RespostaSemAcoesParecePrometerImplementacao(string respostaIA, AgentResponse structuredResponse)
        {
            string texto = string.Join(
                Environment.NewLine,
                respostaIA ?? string.Empty,
                structuredResponse?.MensagemUsuario ?? string.Empty,
                structuredResponse?.Explicacao ?? string.Empty);

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            return texto.IndexOf("acoes a seguir", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   texto.IndexOf("aÃ§Ãµes a seguir", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   texto.IndexOf("serao realizadas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   texto.IndexOf("serÃ£o realizadas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   texto.IndexOf("foram implementadas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   texto.IndexOf("foi implementado", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   texto.IndexOf("foram realizadas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   texto.IndexOf("proxima resposta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   texto.IndexOf("prÃ³xima resposta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   texto.IndexOf("apos a confirmacao", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   texto.IndexOf("apÃ³s a confirmaÃ§Ã£o", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   texto.IndexOf("parcialmente coberta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   texto.IndexOf("parcialmente coberto", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool RespostaContemCampoAcoes(string respostaIA)
        {
            return !string.IsNullOrWhiteSpace(respostaIA) &&
                   Regex.IsMatch(respostaIA, "\"acoes\"\\s*:", RegexOptions.IgnoreCase);
        }

        private async Task<RetrySemAcoesResult> TentarReprocessarRespostaSemAcoesAsync(
            string projectRoot,
            string systemPrompt,
            string promptBase,
            string contextoProjeto,
            string entradaUsuario,
            string respostaIA,
            AgentResponse structuredResponse,
            string parserOutcome = null)
        {
            if (!RespostaIndicaArquivoFaltante(respostaIA, structuredResponse))
                return null;

            string respostaAtual = respostaIA;
            AgentResponse structuredAtual = structuredResponse;
            string contextoAtual = contextoProjeto;
            var arquivosJaIncluidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int tentativa = 1; tentativa <= 2; tentativa++)
            {
                var arquivos = SelecionarArquivosExtrasReprocessamento(
                    projectRoot,
                    entradaUsuario,
                    respostaAtual,
                    structuredAtual,
                    arquivosJaIncluidos).ToList();

                if (arquivos.Count == 0)
                {
                    _log.Info("Resposta sem acoes indica falta de arquivos, mas nenhum arquivo novo citado foi encontrado no projeto.");
                    return null;
                }

                ReportStatus("Incluindo arquivos solicitados pela IA");
                _log.Info("Arquivos citados pela IA encontrados (tentativa " + tentativa + "): " + string.Join(", ", arquivos.Select(p => MakeRelativeSafe(projectRoot, p))));

                string contextoAdicional = MontarContextoArquivosSolicitados(projectRoot, arquivos);
                if (string.IsNullOrWhiteSpace(contextoAdicional))
                    return null;

                string retryContext = string.Join(
                    Environment.NewLine + Environment.NewLine,
                    contextoAtual,
                    contextoAdicional);

                string entradaReprocessada =
                    entradaUsuario +
                    Environment.NewLine +
                    Environment.NewLine +
                    "[REPROCESSAMENTO AUTOMATICO]" +
                    Environment.NewLine +
                    "A resposta anterior informou que faltavam arquivos. Esses arquivos foram anexados ao contexto acima. Gere agora acoes executaveis no JSON do sistema. Nao retorne acoes vazia se a implementacao puder ser feita com os arquivos fornecidos. Priorize arquivos da aplicacao e ignore bibliotecas de terceiros como vendor, node_modules, packages, bin e obj.";

                string retryPrompt = PrepararPromptFinal(
                    systemPrompt,
                    promptBase,
                    retryContext,
                    entradaReprocessada,
                    false);

                int limite = ObterLimitePrompt();
                if (retryPrompt.Length > limite)
                {
                    _log.Error("Reprocessamento cancelado: contexto acima do limite. Caracteres=" + retryPrompt.Length + " Limite=" + limite);
                    return CriarRetrySemAcoesContextoGrande(retryPrompt, retryContext);
                }

                ReportStatus("Reconsultando a IA com arquivos solicitados");
                _log.Info("Reprocessamento direto com arquivos solicitados. PromptChars=" + retryPrompt.Length);
                string retryRawResponse = await ConsultarModeloComFallbackAsync(
                    systemPrompt,
                    promptBase,
                    retryContext,
                    entradaReprocessada,
                    retryPrompt).ConfigureAwait(false);

                int totalAcoes;
                bool semAcoes;
                AgentResponse retryResponse = InterpretarRespostaIA(
                    retryRawResponse,
                    out totalAcoes,
                    out semAcoes);

                if (totalAcoes > 0)
                {
                    _log.Info("Reprocessamento gerou acoes executaveis: " + totalAcoes);
                    return new RetrySemAcoesResult
                    {
                        StructuredResponse = retryResponse,
                        FinalPrompt = retryPrompt,
                        RespostaIA = retryRawResponse,
                        ContextoProjeto = retryContext
                    };
                }

                if (!RespostaIndicaArquivoFaltante(retryRawResponse, retryResponse))
                {
                    _log.Error("Reprocessamento com arquivos solicitados tambem retornou zero acoes.");
                    return null;
                }

                _log.Info("Reprocessamento ainda solicitou mais arquivos. Tentando nova inclusao automatica.");
                respostaAtual = retryRawResponse;
                structuredAtual = retryResponse;
                contextoAtual = retryContext;
            }

            _log.Error("Reprocessamento com arquivos solicitados tambem retornou zero acoes apos 2 tentativas.");
            return null;
        }

        private RetrySemAcoesResult CriarRetrySemAcoesContextoGrande(string retryPrompt, string retryContext)
        {
            const string mensagem = "Nao consegui reprocessar automaticamente porque o contexto ficou grande demais. Informe o arquivo especifico ou reduza o escopo do pedido.";
            const string explicacao = "O reprocessamento tentou incluir arquivos adicionais, mas o prompt continuou acima do limite configurado para o modelo. Nenhuma acao foi executada.";
            const string json = "{\"mensagem_usuario\":\"Nao consegui reprocessar automaticamente porque o contexto ficou grande demais. Informe o arquivo especifico ou reduza o escopo do pedido.\",\"explicacao\":\"O reprocessamento tentou incluir arquivos adicionais, mas o prompt continuou acima do limite configurado para o modelo. Nenhuma acao foi executada.\",\"acoes\":[],\"requer_confirmacao\":false}";

            return new RetrySemAcoesResult
            {
                StructuredResponse = new AgentResponse
                {
                    MensagemUsuario = mensagem,
                    Explicacao = explicacao,
                    Acoes = new List<AgentAction>(),
                    RequerConfirmacao = false
                },
                FinalPrompt = retryPrompt,
                RespostaIA = json,
                ContextoProjeto = retryContext
            };
        }

        private IEnumerable<string> SelecionarArquivosExtrasReprocessamento(
            string projectRoot,
            string entradaUsuario,
            string respostaIA,
            AgentResponse structuredResponse,
            HashSet<string> arquivosJaIncluidos)
        {
            var pontuados = new List<Tuple<string, int>>();
            var identificadores = ExtrairIdentificadoresExplicitos(entradaUsuario).ToList();
            bool demandaWinForms = PareceDemandaWinForms(entradaUsuario);
            bool demandaWeb = PareceDemandaWeb(entradaUsuario);

            var candidatos = new List<string>();
            candidatos.AddRange(LocalizarArquivosCitadosExplicitamente(projectRoot, entradaUsuario, ExtrairArquivosNaoAlterarExplicitamente(projectRoot, entradaUsuario)));
            candidatos.AddRange(LocalizarArquivosPorIdentificadoresExplicitos(projectRoot, entradaUsuario));
            candidatos.AddRange(ExpandirArquivosRelacionados(projectRoot, entradaUsuario, respostaIA, Enumerable.Empty<string>()));
            candidatos.AddRange(LocalizarArquivosSolicitados(projectRoot, respostaIA, structuredResponse));

            if (demandaWinForms)
                candidatos.AddRange(LocalizarArquivosProvaveisWinForms(projectRoot, entradaUsuario));
            if (demandaWeb)
                candidatos.AddRange(LocalizarArquivosProvaveisWeb(projectRoot, entradaUsuario));

            candidatos = candidatos
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string path in candidatos)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || arquivosJaIncluidos.Contains(path) || EhArquivoDeTerceiros(projectRoot, path))
                    continue;

                int score = ScoreArquivoAplicacao(projectRoot, path);
                string rel = MakeRelativeSafe(projectRoot, path);
                string nome = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
                string texto = string.Empty;

                try
                {
                    texto = File.ReadAllText(path);
                }
                catch
                {
                }

                foreach (string identificador in identificadores)
                {
                    if (ContemIdentificador(texto, identificador) || nome.IndexOf(identificador, StringComparison.OrdinalIgnoreCase) >= 0 || rel.IndexOf(identificador, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 1000;
                }

                if (path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                    score += 300;

                if (path.EndsWith(".blade.php", StringComparison.OrdinalIgnoreCase))
                    score += 220;

                if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".scss", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                    score += 180;

                if (RespostaIndicaArquivoFaltante(respostaIA, structuredResponse) && texto.Length > 0)
                    score += 20;

                if (ContemTermosGenericos(rel))
                    score -= 250;

                if (score > 0)
                    pontuados.Add(Tuple.Create(path, score));
            }

            return pontuados
                .OrderByDescending(x => x.Item2)
                .ThenBy(x => MakeRelativeSafe(projectRoot, x.Item1), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Item1)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxArquivosExtrasReprocessamento)
                .Where(path => arquivosJaIncluidos.Add(path))
                .ToList();
        }

        private static IEnumerable<string> LocalizarArquivosProvaveisWinForms(string projectRoot, string entradaUsuario)
        {
            var arquivos = new List<string>();
            foreach (string path in Directory.GetFiles(projectRoot, "*.Designer.cs", SearchOption.AllDirectories))
            {
                if (IsUnder(path, projectRoot) && !EhArquivoDeTerceiros(projectRoot, path))
                    arquivos.Add(path);
            }

            foreach (string path in Directory.GetFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (!path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) && IsUnder(path, projectRoot) && !EhArquivoDeTerceiros(projectRoot, path))
                {
                    if (PareceFormularioWinForms(path, entradaUsuario))
                        arquivos.Add(path);
                }
            }

            return arquivos;
        }

        private static IEnumerable<string> LocalizarArquivosProvaveisWeb(string projectRoot, string entradaUsuario)
        {
            var arquivos = new List<string>();
            bool pedeView = entradaUsuario.IndexOf("view", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            entradaUsuario.IndexOf("pagina", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            entradaUsuario.IndexOf("pÃ¡gina", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            entradaUsuario.IndexOf("blade", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!pedeView)
                return arquivos;

            foreach (string path in Directory.GetFiles(projectRoot, "*.blade.php", SearchOption.AllDirectories))
            {
                if (IsUnder(path, projectRoot) && !EhArquivoDeTerceiros(projectRoot, path))
                    arquivos.Add(path);
            }

            foreach (string path in Directory.GetFiles(projectRoot, "*.css", SearchOption.AllDirectories))
            {
                if (IsUnder(path, projectRoot) && !EhArquivoDeTerceiros(projectRoot, path))
                    arquivos.Add(path);
            }

            foreach (string path in Directory.GetFiles(projectRoot, "*.scss", SearchOption.AllDirectories))
            {
                if (IsUnder(path, projectRoot) && !EhArquivoDeTerceiros(projectRoot, path))
                    arquivos.Add(path);
            }

            foreach (string path in Directory.GetFiles(projectRoot, "*.js", SearchOption.AllDirectories))
            {
                if (IsUnder(path, projectRoot) && !EhArquivoDeTerceiros(projectRoot, path))
                    arquivos.Add(path);
            }

            return arquivos;
        }

        private static bool PareceDemandaWinForms(string texto)
        {
            texto = (texto ?? string.Empty).ToLowerInvariant();
            return texto.Contains("winforms") ||
                   texto.Contains("designer") ||
                   texto.Contains("label") ||
                   texto.Contains("botao") ||
                   texto.Contains("botÃ£o") ||
                   texto.Contains("tela") ||
                   texto.Contains("formulario") ||
                   texto.Contains("formulÃ¡rio") ||
                   texto.Contains("controle") ||
                   texto.Contains("location") ||
                   texto.Contains("size");
        }

        private static bool PareceFormularioWinForms(string path, string entradaUsuario)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string nome = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            string texto = (entradaUsuario ?? string.Empty).ToLowerInvariant();

            if (nome.IndexOf("form", StringComparison.OrdinalIgnoreCase) >= 0 ||
                nome.IndexOf("frm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                nome.IndexOf("inicial", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return texto.Contains("form") || texto.Contains("tela") || texto.Contains("designer");
        }

        private static bool PareceDemandaWeb(string texto)
        {
            texto = (texto ?? string.Empty).ToLowerInvariant();
            return texto.Contains("php") ||
                   texto.Contains("blade") ||
                   texto.Contains("css") ||
                   texto.Contains("js") ||
                   texto.Contains("view") ||
                   texto.Contains("pagina") ||
                   texto.Contains("pÃ¡gina") ||
                   texto.Contains("routes");
        }

        private static bool ContemTermosGenericos(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return true;

            string rel = relativePath.ToLowerInvariant();
            string[] genericos =
            {
                "controller", "view", "css", "js", "routes", "app", "config", "entrega", "pagamento"
            };

            return genericos.Any(term => rel.Contains(term));
        }

        private static bool RespostaIndicaArquivoFaltante(string respostaIA, AgentResponse structuredResponse)
        {
            string texto = string.Join(
                Environment.NewLine,
                respostaIA ?? string.Empty,
                structuredResponse?.MensagemUsuario ?? string.Empty,
                structuredResponse?.Explicacao ?? string.Empty);

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            bool mencionaArquivo =
                texto.IndexOf("arquivo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("arquivos", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("codigo fonte", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("cÃƒÂ³digo fonte", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("cÃ³digo-fonte", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("codigo-fonte", StringComparison.OrdinalIgnoreCase) >= 0 ||
                Regex.IsMatch(texto, @"\.(cs|php|blade\.php|js|css|html|htm|json|xml|config|txt)\b", RegexOptions.IgnoreCase);

            bool indicaFalta =
                texto.IndexOf("falta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("insuficiente", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("insuficientes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("essencial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("essenciais", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("preciso", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("precisa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("necessario", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("necessarios", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("necessÃ¡ria", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("necessÃ¡rias", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("requer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("requerido", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("requeridos", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("nÃ£o foi possÃ­vel avanÃ§ar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("nao foi possivel avancar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("nao inclui", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("nÃƒÂ£o inclui", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("nÃ£o contÃªm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("nao contem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("nÃ£o contÃ©m", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("nao contÃ©m", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("sem o codigo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("sem o cÃƒÂ³digo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("ter acesso", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("conteudo desses arquivos", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("conteÃƒÂºdo desses arquivos", StringComparison.OrdinalIgnoreCase) >= 0;

            return mencionaArquivo && indicaFalta;
        }

        private static IEnumerable<string> ExtrairArquivosCitados(string respostaIA, AgentResponse structuredResponse)
        {
            string texto = string.Join(
                Environment.NewLine,
                respostaIA ?? string.Empty,
                structuredResponse?.MensagemUsuario ?? string.Empty,
                structuredResponse?.Explicacao ?? string.Empty);

            var encontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in Regex.Matches(texto, @"[`'\""]?([A-Za-z0-9_.\-\\/]+?\.(?:blade\.php|csproj|resx|config|json|xml|txt|php|cs|js|css|html|htm))[`'\""]?", RegexOptions.IgnoreCase))
            {
                if (!match.Success || match.Groups.Count < 2)
                    continue;

                string arquivo = match.Groups[1].Value.Trim();
                if (arquivo.Length > 0)
                    encontrados.Add(arquivo);
            }

            return encontrados;
        }

        private IEnumerable<string> LocalizarArquivosCitadosExplicitamente(string projectRoot, string entradaUsuario)
        {
            return LocalizarArquivosCitadosExplicitamente(projectRoot, entradaUsuario, Enumerable.Empty<string>());
        }

        private IEnumerable<string> LocalizarArquivosCitadosExplicitamente(string projectRoot, string entradaUsuario, IEnumerable<string> arquivosNaoAlterar)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(entradaUsuario) || !Directory.Exists(projectRoot))
                return Enumerable.Empty<string>();

            _log?.Info("[INFO] Fonte usada para extrair caminhos citados: demanda original");

            var bloqueadosNaoAlterar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (arquivosNaoAlterar != null)
            {
                foreach (string arquivo in arquivosNaoAlterar)
                {
                    string normalizado = NormalizarCaminhoProjeto(projectRoot, arquivo);
                    if (!string.IsNullOrWhiteSpace(normalizado))
                        bloqueadosNaoAlterar.Add(normalizado);
                }
            }

            var candidatos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string caminho in ExtrairCaminhosArquivosCitadosExplicitamente(entradaUsuario))
            {
                if (EstaEmNaoAlterar(caminho, bloqueadosNaoAlterar))
                {
                    _log?.Info("[INFO] Arquivo ignorado por estar em Nao alterar: " + caminho);
                    continue;
                }

                if (IsBlockedContextPath(caminho))
                {
                    _log?.Info("[WARN] Caminho citado bloqueado antes da inclusão final: " + caminho);
                    continue;
                }

                if (Path.IsPathRooted(caminho))
                {
                    string caminhoAbsoluto = Path.GetFullPath(caminho);
                    if (!IsUnder(caminhoAbsoluto, projectRoot))
                    {
                        _log?.Info("[WARN] Caminho citado bloqueado antes da inclusão final: " + caminho);
                        continue;
                    }
                }

                bool encontrou = false;
                foreach (string path in LocalizarArquivosNoProjeto(projectRoot, caminho))
                {
                    if (File.Exists(path) && IsUnder(path, projectRoot) && !EhArquivoDeTerceiros(projectRoot, path))
                    {
                        if (EstaEmNaoAlterar(path, bloqueadosNaoAlterar))
                        {
                            _log?.Info("[INFO] Arquivo ignorado por estar em Nao alterar: " + MakeRelativeSafe(projectRoot, path));
                            continue;
                        }

                        if (IsBlockedContextPath(path))
                        {
                            _log?.Info("[WARN] Caminho citado bloqueado antes da inclusão final: " + MakeRelativeSafe(projectRoot, path));
                            continue;
                        }

                        if (candidatos.Add(path))
                        {
                            _log?.Info("[INFO] Arquivo citado explicitamente incluído no contexto: " + MakeRelativeSafe(projectRoot, path));
                        }
                        encontrou = true;
                    }
                }

                if (!encontrou)
                    _log?.Info("[WARN] Arquivo citado explicitamente não encontrado no projeto: " + caminho);
            }

            return candidatos
                .OrderBy(path => MakeRelativeSafe(projectRoot, path), StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
        }

        private static IEnumerable<string> ExtrairArquivosNaoAlterarExplicitamente(string projectRoot, string texto)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(texto))
                return Enumerable.Empty<string>();

            string secao = ExtrairSecaoNaoAlterar(texto);
            if (string.IsNullOrWhiteSpace(secao))
                return Enumerable.Empty<string>();

            return ExtrairCaminhosArquivosCitadosExplicitamente(secao);
        }

        private static string ExtrairSecaoNaoAlterar(string texto)
        {
            Match match = Regex.Match(
                texto,
                @"(?is)(?:^|\r?\n)\s*(?:nao alterar|não alterar)\s*:\s*(?<bloco>.*?)(?=(?:\r?\n\s*(?:arquivo unico para alterar|arquivo único para alterar|arquivo unico|arquivo único|alterar somente|somente alterar|apenas no arquivo|no arquivo|objetivo|escopo|etapas|criterio_aceite|criterio de aceite|criterio de aceite|critério de aceite|regras|fora do escopo|fora_escopo|validacoes manuais recomendadas|validações manuais recomendadas|validacoes|validações|trigger_proximo)\s*:)|\z)",
                RegexOptions.IgnoreCase);

            return match.Success ? match.Groups["bloco"].Value : string.Empty;
        }

        private static string NormalizarCaminhoProjeto(string projectRoot, string caminho)
        {
            if (string.IsNullOrWhiteSpace(caminho))
                return string.Empty;

            string normalizado = caminho.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            try
            {
                if (Path.IsPathRooted(normalizado))
                    return Path.GetFullPath(normalizado);

                if (!string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot))
                    return Path.GetFullPath(Path.Combine(projectRoot, normalizado));
            }
            catch
            {
                // Mantem o valor original normalizado quando a combinacao falhar.
            }

            return normalizado;
        }

        private static bool EstaEmNaoAlterar(string caminho, IEnumerable<string> arquivosNaoAlterar)
        {
            if (string.IsNullOrWhiteSpace(caminho) || arquivosNaoAlterar == null)
                return false;

            string caminhoNormalizado = caminho.Replace('/', '\\').Trim();
            foreach (string bloqueado in arquivosNaoAlterar)
            {
                if (string.IsNullOrWhiteSpace(bloqueado))
                    continue;

                string bloqueadoNormalizado = bloqueado.Replace('/', '\\').Trim();
                if (string.Equals(caminhoNormalizado, bloqueadoNormalizado, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (caminhoNormalizado.EndsWith("\\" + bloqueadoNormalizado.TrimStart('\\'), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private List<string> FiltrarArquivosNaoAlterar(
            string projectRoot,
            IEnumerable<string> caminhos,
            IEnumerable<string> arquivosNaoAlterar)
        {
            var resultado = new List<string>();
            if (caminhos == null)
                return resultado;

            var bloqueados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (arquivosNaoAlterar != null)
            {
                foreach (string arquivo in arquivosNaoAlterar)
                {
                    string normalizado = NormalizarCaminhoProjeto(projectRoot, arquivo);
                    if (!string.IsNullOrWhiteSpace(normalizado))
                        bloqueados.Add(normalizado);
                }
            }

            foreach (string caminho in caminhos)
            {
                if (EstaEmNaoAlterar(caminho, bloqueados))
                {
                    _log?.Info("[INFO] Arquivo ignorado por estar em Nao alterar: " + MakeRelativeSafe(projectRoot, caminho));
                    continue;
                }

                resultado.Add(caminho);
            }

            return resultado;
        }

        private static IEnumerable<string> ExtrairCaminhosArquivosCitadosExplicitamente(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                yield break;

            var encontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(texto, @"(?<![\w])(?:\.{1,2}[\\/])?(?:[A-Za-z]:[\\/])?(?:[A-Za-z0-9_.\-]+[\\/])+[A-Za-z0-9_.\-]+(?:\.[A-Za-z0-9_.\-]+)?", RegexOptions.IgnoreCase))
            {
                string caminho = match.Value.Trim().Trim('"', '\'', '`', ',', ';', ')', '(', '[', ']', '{', '}', '<', '>');
                if (caminho.Contains("//") || caminho.Contains("\\\\"))
                    caminho = caminho.Replace("//", "/").Replace("\\\\", "\\");

                if (IsBlockedContextPath(caminho))
                    continue;

                if (encontrados.Add(caminho))
                    yield return caminho;
            }
        }

        private static bool IsBlockedContextPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalizado = path.Replace('/', '\\').Trim();
            string lower = normalizado.ToLowerInvariant();

            string[] blockedSegments =
            {
                "\\.venv\\",
                "\\venv\\",
                "\\vendor\\",
                "\\node_modules\\",
                "\\site-packages\\",
                "\\storage\\",
                "\\cache\\",
                "\\backup\\"
            };

            if (blockedSegments.Any(segment => lower.Contains(segment)))
                return true;

            if (lower.StartsWith(".venv\\") || lower.StartsWith("venv\\") || lower.StartsWith("vendor\\") || lower.StartsWith("node_modules\\") ||
                lower.StartsWith("site-packages\\") || lower.StartsWith("storage\\") || lower.StartsWith("cache\\") || lower.StartsWith("backup\\"))
                return true;

            if (lower.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                lower.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                lower.Contains("://"))
                return true;

            if (lower.Contains("api.openai.com") || lower.Contains("googleapis.com") || lower.Contains("teletudo.com"))
                return true;

            if (lower.Contains("application/json") ||
                lower.Contains("image/png") ||
                lower.Contains("text/css") ||
                lower.Contains("text/javascript"))
                return true;

            if (lower.Contains("images/render") || lower.Contains("api/debug/version") || lower.Contains("settings/load") || lower.Contains("project/create"))
                return true;

            if (!Regex.IsMatch(normalizado, @"\.(py|php|js|css|html|blade\.php|json|txt|md|cs)$", RegexOptions.IgnoreCase))
                return true;

            return false;
        }

        private static bool DeveUsarModoArquivoUnicoExplicito(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return false;

            string lower = texto.ToLowerInvariant();
            string[] gatilhos =
            {
                "arquivo unico",
                "arquivo único",
                "arquivo unico para alterar",
                "arquivo único para alterar",
                "alterar somente",
                "somente alterar",
                "apenas no arquivo",
                "no arquivo"
            };

            return gatilhos.Any(gatilho => lower.Contains(gatilho));
        }

        private bool TentarAtivarModoArquivoUnicoExplicito(
            string projectRoot,
            string entradaUsuario,
            List<string> arquivosCitadosExplicitamente,
            IEnumerable<string> arquivosNaoAlterar,
            out string arquivoUnico)
        {
            arquivoUnico = null;

            if (arquivosCitadosExplicitamente == null)
                arquivosCitadosExplicitamente = new List<string>();

            var bloqueadosNaoAlterar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (arquivosNaoAlterar != null)
            {
                foreach (string arquivo in arquivosNaoAlterar)
                {
                    string normalizado = NormalizarCaminhoProjeto(projectRoot, arquivo);
                    if (!string.IsNullOrWhiteSpace(normalizado))
                        bloqueadosNaoAlterar.Add(normalizado);
                }
            }

            arquivosCitadosExplicitamente = arquivosCitadosExplicitamente
                .Where(path => !EstaEmNaoAlterar(path, bloqueadosNaoAlterar))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool fraseExplicita = DeveUsarModoArquivoUnicoExplicito(entradaUsuario);

            if (arquivosCitadosExplicitamente.Count > 1)
            {
                _log.Info("[INFO] Modo arquivo único não ativado: múltiplos arquivos citados.");
                return false;
            }

            if (arquivosCitadosExplicitamente.Count == 1)
            {
                arquivoUnico = arquivosCitadosExplicitamente[0];
                return true;
            }

            if (!fraseExplicita)
            {
                _log.Info("[INFO] Modo arquivo único não ativado: nenhum arquivo único válido encontrado.");
                return false;
            }

            var candidatos = ExtrairCaminhosArquivosCitadosExplicitamente(entradaUsuario)
                .SelectMany(caminho => LocalizarArquivosNoProjeto(projectRoot, caminho))
                .Where(path => File.Exists(path) && IsUnder(path, projectRoot) && !EhArquivoDeTerceiros(projectRoot, path) && !IsBlockedContextPath(path) && !EstaEmNaoAlterar(path, bloqueadosNaoAlterar))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            if (candidatos.Count == 1)
            {
                arquivoUnico = candidatos[0];
                return true;
            }

            if (candidatos.Count > 1)
            {
                _log.Info("[INFO] Modo arquivo único não ativado: múltiplos arquivos citados.");
                return false;
            }

            string caminhoExplicito = ExtrairCaminhosArquivosCitadosExplicitamente(entradaUsuario).FirstOrDefault();
            _log.Info("[WARN] Arquivo único explícito citado, mas não encontrado no projeto: " + (caminhoExplicito ?? string.Empty));
            return false;
        }

        private static IEnumerable<string> LocalizarArquivosSolicitados(string projectRoot, string respostaIA, AgentResponse structuredResponse)
        {
            var encontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string arquivo in ExtrairArquivosCitados(respostaIA, structuredResponse))
            {
                bool encontrouArquivo = false;
                foreach (string path in LocalizarArquivosNoProjeto(projectRoot, arquivo))
                {
                    encontrouArquivo = true;
                    if (!EhArquivoDeTerceiros(projectRoot, path) && encontrados.Add(path))
                        yield return path;
                }

                if (!encontrouArquivo)
                {
                    // O metodo e iterator; nao ha logger estatico aqui. O arquivo faltante sera inferido pelo log de arquivos encontrados.
                }
            }

            foreach (string diretorio in ExtrairDiretoriosCitados(respostaIA, structuredResponse))
            {
                foreach (string path in LocalizarArquivosPorDiretorio(projectRoot, diretorio))
                {
                    if (!EhArquivoDeTerceiros(projectRoot, path) && encontrados.Add(path))
                        yield return path;
                }
            }

            if (encontrados.Count > 0)
            {
                foreach (string path in ExpandirArquivosRelacionados(projectRoot, respostaIA, string.Empty, encontrados))
                {
                    if (!EhArquivoDeTerceiros(projectRoot, path) && encontrados.Add(path))
                        yield return path;
                }

                yield break;
            }

            foreach (string path in LocalizarArquivosDaAplicacaoPorAssunto(projectRoot, respostaIA, structuredResponse))
            {
                if (!EhArquivoDeTerceiros(projectRoot, path) && encontrados.Add(path))
                    yield return path;
            }
        }

        private static IEnumerable<string> ExtrairDiretoriosCitados(string respostaIA, AgentResponse structuredResponse)
        {
            string texto = string.Join(
                Environment.NewLine,
                respostaIA ?? string.Empty,
                structuredResponse?.MensagemUsuario ?? string.Empty,
                structuredResponse?.Explicacao ?? string.Empty);

            var encontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(texto, @"[`'\""]?((?:app|routes|database|config|resources|admin|includes)(?:[\\/][A-Za-z0-9_.\-]+)+)[`'\""]?", RegexOptions.IgnoreCase))
            {
                string dir = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(dir))
                    encontrados.Add(dir);
            }

            return encontrados;
        }

        private static IEnumerable<string> LocalizarArquivosNoProjeto(string projectRoot, string arquivoCitado)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(arquivoCitado) || !Directory.Exists(projectRoot))
                return Enumerable.Empty<string>();

            string normalizado = arquivoCitado.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
            var candidatos = new List<string>();
            bool caminhoEspecifico = normalizado.IndexOf(Path.DirectorySeparatorChar) >= 0;

            if (caminhoEspecifico)
            {
                string direto = Path.GetFullPath(Path.Combine(projectRoot, normalizado.TrimStart(Path.DirectorySeparatorChar)));
                if (File.Exists(direto) && IsUnder(direto, projectRoot))
                    candidatos.Add(direto);

                string rootName = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(rootName) &&
                    normalizado.StartsWith(rootName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    string semRaiz = normalizado.Substring(rootName.Length + 1);
                    string diretoSemRaiz = Path.GetFullPath(Path.Combine(projectRoot, semRaiz.TrimStart(Path.DirectorySeparatorChar)));
                    if (File.Exists(diretoSemRaiz) && IsUnder(diretoSemRaiz, projectRoot))
                        candidatos.Add(diretoSemRaiz);
                }

                candidatos.AddRange(FindByRelativeSuffix(projectRoot, normalizado));
            }

            string nomeArquivo = Path.GetFileName(normalizado);
            if (string.IsNullOrWhiteSpace(nomeArquivo))
                return OrdenarCandidatosArquivos(projectRoot, candidatos, normalizado);

            try
            {
                candidatos.AddRange(Directory.GetFiles(projectRoot, nomeArquivo, SearchOption.AllDirectories)
                    .Where(path => IsUnder(path, projectRoot)));
            }
            catch
            {
            }

            return OrdenarCandidatosArquivos(projectRoot, candidatos, normalizado);
        }

        private static IEnumerable<string> LocalizarArquivosPorIdentificadoresExplicitos(string projectRoot, string entradaUsuario)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(entradaUsuario) || !Directory.Exists(projectRoot))
                return Enumerable.Empty<string>();

            var identificadores = ExtrairIdentificadoresExplicitos(entradaUsuario).ToList();
            if (identificadores.Count == 0)
                return Enumerable.Empty<string>();

            bool demandaVisualWinForms = PareceDemandaVisualWinForms(entradaUsuario);
            var pontuados = new List<Tuple<string, int>>();

            foreach (string path in EnumerarArquivosBuscaveisParaIdentificadores(projectRoot))
            {
                string texto;
                try
                {
                    texto = File.ReadAllText(path);
                }
                catch
                {
                    continue;
                }

                int matches = 0;
                foreach (string identificador in identificadores)
                {
                    if (ContemIdentificador(texto, identificador))
                        matches++;
                }

                if (matches == 0)
                    continue;

                int score = matches * 1000 + ScoreArquivoAplicacao(projectRoot, path);

                if (demandaVisualWinForms)
                {
                    if (path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                        score += 500;

                    if (Path.GetFileName(path).IndexOf("Designer", StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 100;
                }

                pontuados.Add(Tuple.Create(path, score));
            }

            var ordenados = pontuados
                .OrderByDescending(x => x.Item2)
                .ThenBy(x => MakeRelativeSafe(projectRoot, x.Item1), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Item1)
                .Take(12)
                .ToList();

            if (demandaVisualWinForms)
            {
                var expandidos = new List<string>(ordenados);
                foreach (string path in ordenados.Where(p => p.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)))
                {
                    string codeBehind = path.Substring(0, path.Length - ".Designer.cs".Length) + ".cs";
                    if (File.Exists(codeBehind) && IsUnder(codeBehind, projectRoot) && !EhArquivoDeTerceiros(projectRoot, codeBehind))
                        expandidos.Add(codeBehind);
                }

                ordenados = expandidos
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return ordenados;
        }

        private static IEnumerable<string> ExtrairIdentificadoresExplicitos(string texto)
        {
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(texto ?? string.Empty, @"\b[A-Za-z_][A-Za-z0-9_]{2,}\b"))
            {
                string valor = match.Value;
                if (!PareceIdentificadorExplicito(valor))
                    continue;

                if (vistos.Add(valor))
                    yield return valor;
            }
        }

        private static bool PareceIdentificadorExplicito(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor) || valor.Length < 3)
                return false;

            if (valor.StartsWith("btn", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("lbl", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("txt", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("cbo", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("cmb", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("chk", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("rb", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("grid", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("dgv", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("lst", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("lv", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("pnl", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("panel", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("group", StringComparison.OrdinalIgnoreCase) ||
                valor.StartsWith("Frm", StringComparison.Ordinal) ||
                valor.StartsWith("Form", StringComparison.Ordinal))
            {
                return true;
            }

            return valor.Any(char.IsLower) && valor.Any(char.IsUpper);
        }

        private static bool PareceDemandaVisualWinForms(string texto)
        {
            texto = (texto ?? string.Empty).ToLowerInvariant();
            return texto.Contains("designer") ||
                   texto.Contains("label") ||
                   texto.Contains("botao") ||
                   texto.Contains("botÃ£o") ||
                   texto.Contains("controle") ||
                   texto.Contains("visual") ||
                   texto.Contains("location") ||
                   texto.Contains("size") ||
                   texto.Contains("tela") ||
                   texto.Contains("formulario") ||
                   texto.Contains("formulÃ¡rio") ||
                   texto.Contains("winforms");
        }

        private static IEnumerable<string> EnumerarArquivosBuscaveisParaIdentificadores(string projectRoot)
        {
            string[] extensoes =
            {
                ".cs", ".Designer.cs", ".vb", ".xaml", ".cshtml", ".razor",
                ".php", ".js", ".ts", ".tsx", ".jsx", ".html", ".css"
            };

            IEnumerable<string> files;
            try
            {
                files = Directory.GetFiles(projectRoot, "*.*", SearchOption.AllDirectories);
            }
            catch
            {
                return Enumerable.Empty<string>();
            }

            return files
                .Where(path => IsUnder(path, projectRoot))
                .Where(path => !EhArquivoDeTerceiros(projectRoot, path))
                .Where(path => extensoes.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        private static bool ContemIdentificador(string texto, string identificador)
        {
            if (string.IsNullOrEmpty(texto) || string.IsNullOrWhiteSpace(identificador))
                return false;

            return Regex.IsMatch(
                texto,
                @"(?<![A-Za-z0-9_])" + Regex.Escape(identificador) + @"(?![A-Za-z0-9_])",
                RegexOptions.CultureInvariant);
        }

        private static IEnumerable<string> FindByRelativeSuffix(string projectRoot, string normalizado)
        {
            var result = new List<string>();
            string suffix = normalizado.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string rootName = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(rootName) &&
                suffix.StartsWith(rootName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                suffix = suffix.Substring(rootName.Length + 1);
            }

            try
            {
                string fileName = Path.GetFileName(suffix);
                if (string.IsNullOrWhiteSpace(fileName))
                    return result;

                result.AddRange(Directory.GetFiles(projectRoot, fileName, SearchOption.AllDirectories)
                    .Where(path => IsUnder(path, projectRoot))
                    .Where(path => MakeRelativeSafe(projectRoot, path)
                        .Replace('/', Path.DirectorySeparatorChar)
                        .EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
            }

            return result;
        }

        private static IEnumerable<string> LocalizarArquivosPorDiretorio(string projectRoot, string diretorioCitado)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(diretorioCitado) || !Directory.Exists(projectRoot))
                return Enumerable.Empty<string>();

            string normalizado = diretorioCitado.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
            string rootName = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(rootName) &&
                normalizado.StartsWith(rootName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                normalizado = normalizado.Substring(rootName.Length + 1);

            string fullDir = Path.GetFullPath(Path.Combine(projectRoot, normalizado.TrimStart(Path.DirectorySeparatorChar)));
            if (!Directory.Exists(fullDir) || !IsUnder(fullDir, projectRoot))
                return Enumerable.Empty<string>();

            try
            {
                return Directory.GetFiles(fullDir, "*.php", SearchOption.AllDirectories)
                    .Where(path => IsUnder(path, projectRoot))
                    .Where(path => !EhArquivoDeTerceiros(projectRoot, path))
                    .OrderByDescending(path => ScoreArquivoAplicacao(projectRoot, path))
                    .ThenBy(path => MakeRelativeSafe(projectRoot, path), StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        private static IEnumerable<string> LocalizarArquivosDaAplicacaoPorAssunto(string projectRoot, string respostaIA, AgentResponse structuredResponse)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
                return Enumerable.Empty<string>();

            var roots = new[] { "app", "routes", "database", "config", "admin", "includes" }
                .Select(dir => Path.Combine(projectRoot, dir))
                .Where(Directory.Exists)
                .ToList();

            var files = new List<string>();
            foreach (string root in roots)
            {
                try
                {
                    files.AddRange(Directory.GetFiles(root, "*.php", SearchOption.AllDirectories)
                        .Where(path => IsUnder(path, projectRoot))
                        .Where(path => !EhArquivoDeTerceiros(projectRoot, path)));
                }
                catch
                {
                }
            }

            return files
                .OrderByDescending(path => ScoreArquivoAplicacao(projectRoot, path))
                .ThenBy(path => MakeRelativeSafe(projectRoot, path), StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList();
        }

        private static IEnumerable<string> ExpandirArquivosRelacionados(
            string projectRoot,
            string entradaUsuario,
            string textoReferencia,
            IEnumerable<string> arquivosBase)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
                return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in arquivosBase ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && IsUnder(path, projectRoot) && seen.Add(path))
                    result.Add(path);
            }

            string texto = string.Join(Environment.NewLine, entradaUsuario ?? string.Empty, textoReferencia ?? string.Empty);
            bool pareceLaravel = PareceProjetoLaravel(projectRoot) ||
                texto.IndexOf("Laravel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("routes/web.php", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("controlador", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("controller", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!pareceLaravel)
                return result;

            foreach (string rel in new[] { "routes/web.php", "routes/api.php", "config/app.php" })
                AdicionarArquivoSeExistir(projectRoot, rel, result, seen);

            var termos = ExtrairTermosDeRotaEAssunto(texto).ToList();
            foreach (string path in LocalizarArquivosPorTermos(projectRoot, termos))
            {
                if (seen.Add(path))
                    result.Add(path);
            }

            foreach (string path in LocalizarArquivosLaravelEssenciais(projectRoot, termos))
            {
                if (seen.Add(path))
                    result.Add(path);
            }

            return result.Take(40).ToList();
        }

        private static bool PareceProjetoLaravel(string projectRoot)
        {
            try
            {
                return File.Exists(Path.Combine(projectRoot, "artisan")) ||
                       File.Exists(Path.Combine(projectRoot, "composer.json")) && Directory.Exists(Path.Combine(projectRoot, "routes")) &&
                       Directory.Exists(Path.Combine(projectRoot, "app"));
            }
            catch
            {
                return false;
            }
        }

        private static void AdicionarArquivoSeExistir(string projectRoot, string relativePath, List<string> result, HashSet<string> seen)
        {
            try
            {
                string fullPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
                if (File.Exists(fullPath) && IsUnder(fullPath, projectRoot) && !EhArquivoDeTerceiros(projectRoot, fullPath) && seen.Add(fullPath))
                    result.Add(fullPath);
            }
            catch
            {
            }
        }

        private static IEnumerable<string> ExtrairTermosDeRotaEAssunto(string texto)
        {
            var termos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            texto = texto ?? string.Empty;

            foreach (Match match in Regex.Matches(texto, @"/([A-Za-z0-9_\-]{3,})"))
                termos.Add(match.Groups[1].Value);

            foreach (Match match in Regex.Matches(texto, @"\b([A-Za-z0-9_]*(?:painel|boy|motoboy|entrega|delivery|pedido|order|simulad|fake|teste)[A-Za-z0-9_]*)\b", RegexOptions.IgnoreCase))
                termos.Add(match.Groups[1].Value);

            foreach (string termo in new[] { "painelboy", "motoboy", "entrega", "delivery", "pedido", "simulada", "simulado" })
            {
                if (texto.IndexOf(termo, StringComparison.OrdinalIgnoreCase) >= 0)
                    termos.Add(termo);
            }

            return termos.Where(t => !string.IsNullOrWhiteSpace(t) && t.Length >= 3);
        }

        private static IEnumerable<string> LocalizarArquivosPorTermos(string projectRoot, List<string> termos)
        {
            if (termos == null || termos.Count == 0)
                return Enumerable.Empty<string>();

            var roots = new[] { "routes", "app", "resources", "config" }
                .Select(dir => Path.Combine(projectRoot, dir))
                .Where(Directory.Exists)
                .ToList();

            var result = new List<string>();
            foreach (var root in roots)
            {
                try
                {
                    foreach (var path in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                        .Where(path => IsUnder(path, projectRoot))
                        .Where(path => !EhArquivoDeTerceiros(projectRoot, path))
                        .Where(EhExtensaoAplicacaoWeb))
                    {
                        string rel = MakeRelativeSafe(projectRoot, path);
                        string name = Path.GetFileNameWithoutExtension(path);
                        string content = string.Empty;
                        try { content = File.ReadAllText(path); } catch { }

                        if (termos.Any(t =>
                            rel.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            content.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            result.Add(path);
                        }
                    }
                }
                catch
                {
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(path => ScoreArquivoAplicacao(projectRoot, path))
                .ThenBy(path => MakeRelativeSafe(projectRoot, path), StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList();
        }

        private static IEnumerable<string> LocalizarArquivosLaravelEssenciais(string projectRoot, List<string> termos)
        {
            var roots = new[] { "app/Http/Controllers", "resources/views" }
                .Select(dir => Path.Combine(projectRoot, dir.Replace('/', Path.DirectorySeparatorChar)))
                .Where(Directory.Exists)
                .ToList();

            var files = new List<string>();
            foreach (var root in roots)
            {
                try
                {
                    files.AddRange(Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                        .Where(path => IsUnder(path, projectRoot))
                        .Where(path => !EhArquivoDeTerceiros(projectRoot, path))
                        .Where(EhExtensaoAplicacaoWeb));
                }
                catch
                {
                }
            }

            return files
                .OrderByDescending(path => PontuarArquivoPorTermos(projectRoot, path, termos))
                .ThenBy(path => MakeRelativeSafe(projectRoot, path), StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
        }

        private static int PontuarArquivoPorTermos(string projectRoot, string path, List<string> termos)
        {
            int score = ScoreArquivoAplicacao(projectRoot, path);
            string rel = MakeRelativeSafe(projectRoot, path).Replace('\\', '/');
            foreach (var termo in termos ?? new List<string>())
            {
                if (rel.IndexOf(termo, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 200;
            }
            return score;
        }

        private static bool EhExtensaoAplicacaoWeb(string path)
        {
            string rel = path.Replace('\\', '/').ToLowerInvariant();
            return rel.EndsWith(".php") ||
                   rel.EndsWith(".blade.php") ||
                   rel.EndsWith(".js") ||
                   rel.EndsWith(".css") ||
                   rel.EndsWith(".env") ||
                   rel.EndsWith(".json");
        }

        private static IEnumerable<string> OrdenarCandidatosArquivos(string projectRoot, IEnumerable<string> candidatos, string arquivoCitado)
        {
            return (candidatos ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => EhArquivoDeTerceiros(projectRoot, path) ? 1 : 0)
                .ThenByDescending(path => PontuarCorrespondenciaCaminho(projectRoot, path, arquivoCitado))
                .ThenBy(path => MakeRelativeSafe(projectRoot, path), StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
        }

        private static int PontuarCorrespondenciaCaminho(string projectRoot, string fullPath, string arquivoCitado)
        {
            string rel = MakeRelativeSafe(projectRoot, fullPath).Replace('/', Path.DirectorySeparatorChar);
            string citado = (arquivoCitado ?? string.Empty).Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            string rootName = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(rootName) &&
                citado.StartsWith(rootName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                citado = citado.Substring(rootName.Length + 1);

            int score = 0;
            if (string.Equals(rel, citado, StringComparison.OrdinalIgnoreCase))
                score += 1000;
            if (rel.EndsWith(citado, StringComparison.OrdinalIgnoreCase))
                score += 500;
            if (!EhArquivoDeTerceiros(projectRoot, fullPath))
                score += 200;
            score += ScoreArquivoAplicacao(projectRoot, fullPath);
            return score;
        }

        private static int ScoreArquivoAplicacao(string projectRoot, string fullPath)
        {
            string rel = MakeRelativeSafe(projectRoot, fullPath).Replace('\\', '/').ToLowerInvariant();
            string name = Path.GetFileNameWithoutExtension(fullPath).ToLowerInvariant();
            int score = 0;

            if (rel.StartsWith("app/")) score += 80;
            if (rel.StartsWith("app/http/controllers/")) score += 120;
            if (rel.StartsWith("app/models/") || rel.StartsWith("app/model/")) score += 100;
            if (rel.StartsWith("routes/")) score += 90;
            if (rel.StartsWith("admin/")) score += 80;
            if (rel.StartsWith("includes/")) score += 60;

            string combined = rel + " " + name;
            foreach (string keyword in new[] { "pedido", "order", "payment", "pagamento", "motoboy", "notifica", "notification", "entrega", "delivery" })
            {
                if (combined.Contains(keyword))
                    score += 120;
            }

            return score;
        }

        private static bool EhArquivoDeTerceiros(string projectRoot, string fullPath)
        {
            string rel = MakeRelativeSafe(projectRoot, fullPath).Replace('\\', '/').ToLowerInvariant();
            return rel.StartsWith("vendor/") ||
                   rel.Contains("/vendor/") ||
                   rel.StartsWith("node_modules/") ||
                   rel.Contains("/node_modules/") ||
                   rel.StartsWith("packages/") ||
                   rel.Contains("/packages/") ||
                   rel.StartsWith("bin/") ||
                   rel.StartsWith("obj/");
        }

        private static string MontarContextoArquivosSolicitados(string projectRoot, List<string> arquivos)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[ARQUIVOS SOLICITADOS PELA IA - CONTEXTO ADICIONAL]");

            foreach (var path in arquivos)
            {
                string content;
                try { content = File.ReadAllText(path); }
                catch { continue; }

                if (string.IsNullOrWhiteSpace(content))
                    continue;

                string rel = MakeRelativeSafe(projectRoot, path);
                sb.AppendLine();
                sb.AppendLine("--- FILE: " + rel + " ---");
                sb.AppendLine(content);
                sb.AppendLine("--- END FILE: " + rel + " ---");
            }

            return sb.ToString();
        }

        private static string MakeRelativeSafe(string root, string fullPath)
        {
            try
            {
                var rootFull = Path.GetFullPath(root);
                if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString()) && !rootFull.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                    rootFull += Path.DirectorySeparatorChar;

                var rootUri = new Uri(rootFull, UriKind.Absolute);
                var fileUri = new Uri(Path.GetFullPath(fullPath), UriKind.Absolute);
                return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                return fullPath;
            }
        }

        private static bool IsUnder(string fullPath, string root)
        {
            try
            {
                var full = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return full.StartsWith(r, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private AgentResult CriarAgentResult(
            string projectId,
            string projectName,
            string projectRoot,
            string promptBase,
            string contextoProjeto,
            string finalPrompt,
            string respostaIA,
            AgentResponse structuredResponse,
            string parserOutcome)
        {
            return new AgentResult
            {
                ProjectId = Guid.TryParse(projectId, out Guid parsedId) ? parsedId : Guid.Empty,
                ProjectName = projectName,
                ProjectRoot = projectRoot,
                PromptBase = promptBase,
                ProjectContext = contextoProjeto,
                FinalPrompt = finalPrompt,
                ModelResponse = respostaIA,
                StructuredResponse = structuredResponse,
                ParserOutcome = parserOutcome ?? string.Empty
            };
        }

        private ProjectHistoryEntry CriarHistoryEntry(
            string entradaUsuario,
            string respostaIA,
            AgentResponse structuredResponse,
            string parserOutcome)
        {
            return new ProjectHistoryEntry
            {
                UserQuestion = entradaUsuario,
                VisibleAnswer = string.Join(
                    Environment.NewLine + Environment.NewLine,
                    new[] { structuredResponse?.MensagemUsuario, structuredResponse?.Explicacao }
                        .Where(x => !string.IsNullOrWhiteSpace(x))),
                ProposedActions = structuredResponse?.Acoes ?? new System.Collections.Generic.List<AgentAction>(),
                RawResponse = respostaIA,
                LanguageProfile = LanguageProfile.ToString(),
                AiProviderId = AiProviderId,
                AiProviderName = AiProviderName,
                NivelMaximoDificuldade = NivelMaximoDificuldade,
                NivelMaximoIa = NivelMaximoIa,
                NivelEfetivoUsado = Math.Min(AiProviderConfig.ClampLevel(NivelMaximoDificuldade), AiProviderConfig.ClampLevel(NivelMaximoIa)),
                OperationMode = AgentOperationMode.Desenvolvimento.ToString()
            };
        }

        private ProjectHistoryEntry CriarHistoryEntryAnalista(
            string entradaUsuario,
            string respostaIA,
            int nivelEfetivo,
            GeneratedTaskRequestInfo info)
        {
            return new ProjectHistoryEntry
            {
                UserQuestion = entradaUsuario,
                VisibleAnswer = respostaIA,
                RawResponse = respostaIA,
                LanguageProfile = LanguageProfile.ToString(),
                AiProviderId = AiProviderId,
                AiProviderName = AiProviderName,
                NivelMaximoDificuldade = NivelMaximoDificuldade,
                NivelMaximoIa = NivelMaximoIa,
                NivelEfetivoUsado = nivelEfetivo,
                NivelAtribuido = info == null ? 0 : info.NivelAtribuido,
                OperationMode = AgentOperationMode.AnalistaDeSistemas.ToString(),
                GeneratedTaskRequest = respostaIA
            };
        }

        private async Task ExecutarAcoesERegistrarHistoricoAsync(
            AgentResult result,
            ProjectHistoryEntry historyEntry,
            AgentResponse structuredResponse,
            string projectRoot,
            string systemPrompt,
            string promptBase,
            string entradaUsuario)
        {
            PreDispatchExecutionDecision decision = ValidarPreDispatchComPipeline(
                result,
                structuredResponse,
                projectRoot);

            if (_toolDispatcher != null &&
                decision != null &&
                decision.PodeExecutar &&
                decision.ResponseParaExecucao != null &&
                decision.ResponseParaExecucao.Acoes != null &&
                decision.ResponseParaExecucao.Acoes.Count > 0)
            {
                await ExecutarAcoesComDispatcherAsync(
                    result,
                    historyEntry,
                    decision.ResponseParaExecucao,
                    projectRoot,
                    systemPrompt,
                    promptBase,
                    entradaUsuario,
                    decision).ConfigureAwait(false);

                return;
            }

            RegistrarHistorico(result, historyEntry, projectRoot);
        }

        private PreDispatchExecutionDecision ValidarPreDispatchComPipeline(
            AgentResult result,
            AgentResponse structuredResponse,
            string projectRoot)
        {
            string respostaIA = result == null ? null : result.ModelResponse;
            ActionParseResult parseResult = ActionParserPipeline.Parse(structuredResponse, respostaIA);

            int totalErros = parseResult.Diagnostics == null
                ? 0
                : parseResult.Diagnostics.Count(d =>
                    d != null &&
                    string.Equals(d.Severity, "ERROR", StringComparison.OrdinalIgnoreCase));

            _log.Info("[PARSER-PIPELINE] Validação pré-dispatch iniciada.");
            _log.Info("[PARSER-PIPELINE] Executáveis: " + parseResult.CanonicalExecutableCount);
            _log.Info("[PARSER-PIPELINE] Erros: " + totalErros);
            RegistrarResumoFinalParserAct(parseResult);

            if (parseResult.Outcome == ActionParseOutcome.LegitimateNoAction &&
                parseResult.Outcome != ActionParseOutcome.GitDiffDetected)
            {
                _log.Info("[PARSER-PIPELINE] Resultado sem ação legítima: " + parseResult.GetUserFriendlyErrorMessage());
                return new PreDispatchExecutionDecision
                {
                    PodeExecutar = false,
                    UsouCanonico = false,
                    ParseResult = parseResult,
                    ResponseParaExecucao = structuredResponse
                };
            }

            if (parseResult.Outcome == ActionParseOutcome.NoOpOnly)
            {
                string mensagemAmigavel = parseResult.GetUserFriendlyErrorMessage();
                RegistrarErroBrutoDaIAAtual(parseResult, respostaIA);
                _log.Error("[PARSER-PIPELINE] Execução bloqueada pelo ParserAct: " + mensagemAmigavel);
                throw new InvalidOperationException(mensagemAmigavel);
            }

            if (parseResult.Outcome == ActionParseOutcome.TechnicalSignalsWithoutActions && UseGitDiff)
            {
                if (KimiActionDialectRecovery.TryRecoverDeleteOnly(
                    respostaIA,
                    projectRoot,
                    message => _log.Info(message),
                    out KimiDialectRecoveryResult kimiRecovery))
                {
                    _log.Info("[KIMI-RECOVERY] Encaminhando ação recuperada pelo pipeline seguro.");
                    return new PreDispatchExecutionDecision
                    {
                        PodeExecutar = true,
                        UsouCanonico = true,
                        ParseResult = parseResult,
                        ResponseParaExecucao = kimiRecovery.Response
                    };
                }

                if (KimiActionDialectRecovery.IsPotentialDialectCandidate(respostaIA))
                {
                    string motivo = kimiRecovery == null || string.IsNullOrWhiteSpace(kimiRecovery.ErrorMessage)
                        ? "Resposta Kimi sem GitDiff não pôde ser recuperada."
                        : kimiRecovery.ErrorMessage;
                    RegistrarErroBrutoDaIAAtual(parseResult, respostaIA);
                    _log.Error("[KIMI-RECOVERY] Recuperação bloqueada: " + motivo);
                    throw new InvalidOperationException(motivo);
                }
            }

            if (parseResult.Outcome == ActionParseOutcome.GitDiffDetected)
            {
                _log.Info("[GIT-DIFF] GitDiffDetected priorizado sobre fluxo sem ações.");
                _log.Info("[GIT-DIFF] Ignorando validação antiga de zero ações para resposta GitDiff.");
                _log.Info("[GIT-DIFF] JSON wrapper candidato detectado: " + parseResult.GitDiffJsonWrapperCandidateDetected.ToString().ToLowerInvariant());
                if (parseResult.GitDiffJsonWrapperFieldsFound != null && parseResult.GitDiffJsonWrapperFieldsFound.Count > 0)
                    _log.Info("[GIT-DIFF] Campo JSON de diff encontrado: " + parseResult.GitDiffJsonWrapperFieldsFound[0]);
                _log.Info("[GIT-DIFF] JSON wrapper incompleto recuperado: " + parseResult.GitDiffJsonWrapperIncompleteRecovered.ToString().ToLowerInvariant());
                _log.Info("[GIT-DIFF] GitDiff extraído de JSON string escapada: " + parseResult.GitDiffDiffEscapedTextDetected.ToString().ToLowerInvariant());
                _log.Info("[GIT-DIFF] Blocos GitDiff extraídos: " + parseResult.GitDiffExtractedBlockCount);
                _log.Info("[GIT-DIFF] Fallback por primeiro diff usado: " + parseResult.GitDiffUsedFirstDiffFallback.ToString().ToLowerInvariant());
                _log.Info("[GIT-DIFF] Texto antes do primeiro diff descartado: " + parseResult.GitDiffPrefixDiscardedBeforeDiff.ToString().ToLowerInvariant());
                _log.Info("[GIT-DIFF] Resposta sem acoes JSON, mas GitDiff detectado.");
                if (parseResult.GitDiffWrapperJsonDetected &&
                    !parseResult.GitDiffExtractedFromJsonWrapper &&
                    !parseResult.GitDiffJsonWrapperIncompleteRecovered &&
                    !parseResult.GitDiffUsedFirstDiffFallback)
                {
                    RegistrarErroBrutoDaIAAtual(parseResult, respostaIA);
                    throw new InvalidOperationException("A IA retornou um GitDiff malformado e ele nao pode ser interpretado com seguranca. Motivo: nenhum arquivo ou hunk valido foi encontrado.");
                }

                string gitDiffText = parseResult.GitDiffEffectiveText;
                var gitDiffParse = GitDiffAdapter.Parse(string.IsNullOrWhiteSpace(gitDiffText) ? respostaIA : gitDiffText);
                if (gitDiffParse == null || gitDiffParse.Files == null || gitDiffParse.Files.Count == 0 ||
                    (gitDiffParse.TotalHunks == 0 && !gitDiffParse.ContainsOnlyEmptyHunks))
                {
                    RegistrarErroBrutoDaIAAtual(parseResult, respostaIA);
                    throw new InvalidOperationException("A IA retornou um GitDiff malformado e ele nao pode ser interpretado com seguranca. Motivo: nenhum arquivo ou hunk valido foi encontrado.");
                }

                var gitDiffSafety = ValidarGitDiffSeguranca(gitDiffParse, projectRoot);
                RegistrarDiagnosticoGitDiff(respostaIA, parseResult, gitDiffParse, gitDiffSafety);

                if (gitDiffSafety == null || !gitDiffSafety.IsSafe)
                {
                    RegistrarErroBrutoDaIAAtual(parseResult, respostaIA);
                    throw new InvalidOperationException(ConstruirMensagemBloqueioGitDiff(parseResult.GetUserFriendlyErrorMessage(), gitDiffSafety));
                }

                GitDiffConversionResult conversion = GitDiffToProtocolConverter.Convert(gitDiffParse, projectRoot, message => _log.Info(message));
                RegistrarDiagnosticoConversaoGitDiff(conversion);
                if (conversion == null || !conversion.Success || conversion.Operations == null || conversion.Operations.Count == 0)
                {
                    RegistrarErroBrutoDaIAAtual(parseResult, respostaIA);
                    string motivoConversao = conversion != null && conversion.Errors != null && conversion.Errors.Count > 0
                        ? conversion.Errors[0]
                        : "nenhuma operação foi convertida";
                    throw new InvalidOperationException("A IA retornou GitDiff seguro, mas não foi possível convertê-lo para o protocolo interno. Motivo: " + motivoConversao);
                }

                List<AgentAction> gitDiffActions = CriarAcoesCanonicasGitDiff(conversion);
                if (gitDiffActions.Count == 0)
                    throw new InvalidOperationException("A IA retornou GitDiff seguro, mas nenhuma ação canônica executável foi criada.");

                _log.Info("[GIT-DIFF] GitDiff seguro convertido para ações canônicas.");
                _log.Info("[GIT-DIFF] SafetyResult: safe=true");
                _log.Info("[GIT-DIFF] ConversionResult: success=true");
                _log.Info("[GIT-DIFF] Ações GitDiff executáveis: " + gitDiffActions.Count);
                _log.Info("[GIT-DIFF] Encaminhando ações GitDiff pelo pipeline seguro existente.");

                return new PreDispatchExecutionDecision
                {
                    PodeExecutar = true,
                    UsouCanonico = true,
                    ParseResult = parseResult,
                    ResponseParaExecucao = new AgentResponse
                    {
                        MensagemUsuario = structuredResponse?.MensagemUsuario,
                        Explicacao = structuredResponse?.Explicacao,
                        RequerConfirmacao = structuredResponse?.RequerConfirmacao ?? false,
                        Acoes = gitDiffActions
                    }
                };
            }

            if (parseResult.Outcome == ActionParseOutcome.InvalidLocalFile)
            {
                string mensagemAmigavel = parseResult.GetUserFriendlyErrorMessage();
                RegistrarErroBrutoDaIAAtual(parseResult, respostaIA);
                _log.Error("[PARSER-PIPELINE] Execução bloqueada pelo ParserAct: " + mensagemAmigavel);
                throw new InvalidOperationException(mensagemAmigavel);
            }

            if (parseResult.Outcome == ActionParseOutcome.TechnicalSignalsWithoutActions)
            {
                string mensagemAmigavel = parseResult.GetUserFriendlyErrorMessage();
                RegistrarErroBrutoDaIAAtual(parseResult, respostaIA);
                _log.Error("[PARSER-PIPELINE] Execução bloqueada pelo ParserAct: " + mensagemAmigavel);
                throw new InvalidOperationException(mensagemAmigavel);
            }

            if (parseResult.Outcome == ActionParseOutcome.NoExecutableActions)
            {
                string mensagemAmigavel = parseResult.GetUserFriendlyErrorMessage();
                RegistrarErroBrutoDaIAAtual(parseResult, respostaIA);
                _log.Error("[PARSER-PIPELINE] Execução bloqueada pelo ParserAct: " + mensagemAmigavel);
                throw new InvalidOperationException(mensagemAmigavel);
            }

            if (parseResult.Outcome == ActionParseOutcome.UnknownInvalidFormat)
            {
                string mensagemAmigavel = parseResult.GetUserFriendlyErrorMessage();
                RegistrarErroBrutoDaIAAtual(parseResult, respostaIA);
                _log.Error("[PARSER-PIPELINE] Execução bloqueada pelo ParserAct: " + mensagemAmigavel);
                throw new InvalidOperationException(mensagemAmigavel);
            }

            List<AgentAction> canonicalActions = BuildCanonicalActionsForExecution(parseResult);
            bool temAcoesCanonicamenteExecutaveis = canonicalActions.Count > 0;
            bool typedHasExecutable = structuredResponse != null &&
                                      structuredResponse.Acoes != null &&
                                      structuredResponse.Acoes.Any(IsAcaoExecutavel);
            bool typedHasInvalidLocalFile = structuredResponse != null &&
                                            structuredResponse.Acoes != null &&
                                            structuredResponse.Acoes.Any(a =>
                                                a != null &&
                                                a.Tipo == AgentActionType.ArquivoLocal &&
                                                !TemDadosExecutaveisArquivoLocal(a.Dados));
            bool canonicalNeeded = temAcoesCanonicamenteExecutaveis &&
                                   parseResult != null &&
                                   (parseResult.TypedDeserializationLostFields ||
                                    parseResult.CanonicalActions.Any(a => a != null && (a.WasNormalized || string.Equals(a.Source, "RawJson", StringComparison.OrdinalIgnoreCase))) ||
                                    !typedHasExecutable || typedHasInvalidLocalFile || structuredResponse == null || structuredResponse.Acoes == null || structuredResponse.Acoes.Count == 0);

            _log.Info("[PARSER-PIPELINE] Preparando execução canônica.");

            if (canonicalNeeded)
            {
                var responseCanonica = new AgentResponse
                {
                    MensagemUsuario = structuredResponse?.MensagemUsuario,
                    Explicacao = structuredResponse?.Explicacao,
                    RequerConfirmacao = structuredResponse?.RequerConfirmacao ?? false,
                    Acoes = canonicalActions
                };

                _log.Info("[PARSER-PIPELINE] Execução usando ações canônicas.");
                _log.Info("[PARSER-PIPELINE] Ações canônicas executáveis usadas: " + canonicalActions.Count);

                return new PreDispatchExecutionDecision
                {
                    PodeExecutar = true,
                    UsouCanonico = true,
                    ParseResult = parseResult,
                    ResponseParaExecucao = responseCanonica
                };
            }

            if (totalErros > 0)
            {
                _log.Error("[PARSER-PIPELINE] Execução bloqueada: erro estrutural grave.");
                throw new InvalidOperationException("A resposta interpretada contém erro estrutural grave.");
            }

            if (parseResult.ExecutableCount == 0)
            {
                if (RespostaSemAcoesContemProtocoloExecutavelForte(respostaIA))
                {
                    _log.Error("[PARSER-PIPELINE] Execução bloqueada: sinais técnicos sem ação executável.");
                    throw new InvalidOperationException("Resposta contém sinais de ação, mas nenhuma ação executável foi interpretada.");
                }

                _log.Info("[PARSER-PIPELINE] Execução encerrada sem dispatcher: nenhuma ação executável legítima.");
                return new PreDispatchExecutionDecision
                {
                    PodeExecutar = false,
                    UsouCanonico = false,
                    ParseResult = parseResult,
                    ResponseParaExecucao = structuredResponse
                };
            }

            _log.Info("[PARSER-PIPELINE] Execução usando ações originais.");
            _log.Info("[PARSER-PIPELINE] Ações canônicas executáveis usadas: " + parseResult.ExecutableCount);
            return new PreDispatchExecutionDecision
            {
                PodeExecutar = true,
                UsouCanonico = false,
                ParseResult = parseResult,
                ResponseParaExecucao = structuredResponse
            };
        }

        private async Task ExecutarAcoesComDispatcherAsync(
            AgentResult result,
            ProjectHistoryEntry historyEntry,
            AgentResponse structuredResponse,
            string projectRoot,
            string systemPrompt,
            string promptBase,
            string entradaUsuario,
            PreDispatchExecutionDecision decision)
        {
            ReportStatus("Executando acoes propostas");

            ToolDispatchResult dispatchResult = null;
            bool recovered = false;
            AgentResponse responseParaExecucao = decision?.ResponseParaExecucao ?? structuredResponse;

            try
            {
                if (decision != null && decision.UsouCanonico)
                    _log.Info("[PARSER-PIPELINE] Enviando ação canônica ao ToolDispatcher.");

                if (decision != null && decision.UsouCanonico)
                    _log.Info("[PARSER-PIPELINE] Execução canônica confirmada para dispatcher.");

                int acoesParaDispatcher = responseParaExecucao?.Acoes == null ? 0 : responseParaExecucao.Acoes.Count;
                _log.Info("[PARSER-PIPELINE] Ações enviadas ao ToolDispatcher: " + acoesParaDispatcher);

                if (acoesParaDispatcher == 0)
                {
                    if (decision?.ParseResult != null && decision.ParseResult.Outcome == ActionParseOutcome.LegitimateNoAction)
                    {
                        _log.Info("[PARSER-PIPELINE] Execução encerrada sem dispatcher: nenhuma alteração necessária.");
                        return;
                    }

                    string mensagemBloqueio = decision?.ParseResult?.GetUserFriendlyErrorMessage() ?? "Ação inválida recebida pelo dispatcher. O ParserAct deveria ter bloqueado este formato.";
                    throw new InvalidOperationException(mensagemBloqueio);
                }

                ValidarAcoesArquivoLocalAntesDoDispatcher(responseParaExecucao);
                dispatchResult = _toolDispatcher.Dispatch(responseParaExecucao, projectRoot);
                result.ToolDispatchQueued = true;

                if (!string.IsNullOrWhiteSpace(dispatchResult.Error))
                {
                    if (IsVerificationFailureError(dispatchResult.Error))
                    {
                        _log.Error(dispatchResult.Error);
                        _log.Info("[INFO] Reprocessamento automÃ¡tico ignorado por falha de verificaÃ§Ã£o.");
                        result.ToolDispatchError = dispatchResult.Error;
                        historyEntry.Error = dispatchResult.Error;
                        return;
                    }

                    recovered = await TentarCorrigirFalhaExecucaoAsync(
                        result,
                        historyEntry,
                        dispatchResult,
                        projectRoot,
                        systemPrompt,
                        promptBase,
                        entradaUsuario).ConfigureAwait(false);

                    if (!recovered)
                    {
                        result.ToolDispatchError = dispatchResult.Error;
                        historyEntry.Error = dispatchResult.Error;
                        throw new InvalidOperationException("Falha ao executar acoes propostas: " + SummarizeDispatchError(dispatchResult.Error));
                    }
                }
                else
                {
                    await TentarContinuarConclusaoParcialAsync(
                        result,
                        historyEntry,
                        responseParaExecucao,
                        projectRoot,
                        systemPrompt,
                        promptBase,
                        entradaUsuario).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                historyEntry.Error = ex.ToString();
                throw;
            }
            finally
            {
                if (dispatchResult != null)
                {
                    if (historyEntry.ExecutedActions == null)
                        historyEntry.ExecutedActions = new System.Collections.Generic.List<AgentAction>();

                    if (historyEntry.ExecutedActions.Count == 0 && dispatchResult.ExecutedActions != null)
                        historyEntry.ExecutedActions.AddRange(dispatchResult.ExecutedActions);

                    result.ExecutedActions = historyEntry.ExecutedActions;

                    if (!recovered && !string.IsNullOrWhiteSpace(dispatchResult.Error))
                        historyEntry.Error = dispatchResult.Error;
                }

                RegistrarHistorico(result, historyEntry, projectRoot);
            }
        }

        private List<AgentAction> BuildCanonicalActionsForExecution(ActionParseResult parseResult)
        {
            var canonicalActions = new List<AgentAction>();

            if (parseResult?.CanonicalActions == null || parseResult.CanonicalActions.Count == 0)
                return canonicalActions;

            var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ParsedAction parsed in parseResult.CanonicalActions)
            {
                if (!ValidarAcaoCanonicaAntesDoDispatcher(parsed, out string motivoRejeicao))
                {
                    if (!string.IsNullOrWhiteSpace(motivoRejeicao))
                        _log.Error("[PARSER-PIPELINE] Ação canônica rejeitada antes do ToolDispatcher: " + motivoRejeicao);

                    throw new InvalidOperationException("Ação canônica inválida antes do dispatcher.");
                }

                AgentAction canonical = ToAgentAction(parsed);
                if (canonical == null || !IsAcaoExecutavel(canonical))
                    continue;

                string key = BuildCanonicalActionKey(canonical);
                if (!existentes.Add(key))
                    continue;

                canonicalActions.Add(canonical);
            }

            return canonicalActions;
        }

        private static string BuildCanonicalActionKey(AgentAction action)
        {
            if (action == null)
                return string.Empty;

            string tipo = action.Tipo.ToString();
            string descricao = action.Descricao ?? string.Empty;
            string dados = action.Dados == null ? string.Empty : action.Dados.ToString(Newtonsoft.Json.Formatting.None);
            return tipo + "|" + descricao + "|" + dados;
        }

        private static AgentAction ToAgentAction(ParsedAction parsed)
        {
            if (parsed == null || !parsed.IsExecutable)
                return null;

            AgentActionType tipo = AgentActionType.ArquivoLocal;
            if (string.Equals(parsed.Type, AgentActionType.ComandoDos.ToString(), StringComparison.OrdinalIgnoreCase))
                tipo = AgentActionType.ComandoDos;
            else if (string.Equals(parsed.Type, AgentActionType.Ftp.ToString(), StringComparison.OrdinalIgnoreCase))
                tipo = AgentActionType.Ftp;

            JObject dados = parsed.Data as JObject;
            if (dados == null && !string.IsNullOrWhiteSpace(parsed.ProtocolText))
            {
                dados = new JObject
                {
                    ["protocolo"] = parsed.ProtocolText
                };
            }

            if (dados == null)
                dados = new JObject();

            dados["__canonical"] = true;
            dados["__source"] = string.IsNullOrWhiteSpace(parsed.Source) ? "ActionParserPipeline" : parsed.Source;

            return new AgentAction
            {
                Tipo = tipo,
                Descricao = string.IsNullOrWhiteSpace(parsed.Description) ? "Ação canônica recuperada" : parsed.Description,
                Dados = (JObject)dados.DeepClone(),
                RequerConfirmacao = false
            };
        }

        private bool ValidarAcaoCanonicaAntesDoDispatcher(ParsedAction parsed, out string motivo)
        {
            motivo = string.Empty;

            if (parsed == null)
            {
                motivo = "acao nula";
                return false;
            }

            if (!string.Equals(parsed.Type, "ArquivoLocal", StringComparison.OrdinalIgnoreCase))
                return true;

            string protocol = parsed.ProtocolText;
            if (string.IsNullOrWhiteSpace(protocol) && parsed.Data != null)
                protocol = parsed.Data["protocolo"]?.ToString();

            if (string.IsNullOrWhiteSpace(protocol))
            {
                motivo = "protocolo ausente";
                return false;
            }

            if (RedactSensitiveText(protocol) != protocol)
            {
                motivo = "protocolo contem possivel segredo";
                return false;
            }

            if (protocol.IndexOf("ARQ=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                motivo = "ARQ ausente no protocolo";
                return false;
            }

            bool hasSimplePatch = protocol.IndexOf("SEARCH=", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                  protocol.IndexOf("REPLACE=", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasBlockPatch = protocol.IndexOf("SEARCH_BLOCK", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                 protocol.IndexOf("REPLACE_BLOCK", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hasSimplePatch && !hasBlockPatch)
            {
                motivo = "protocolo nao contem SEARCH/REPLACE nem SEARCH_BLOCK/REPLACE_BLOCK";
                return false;
            }

            return true;
        }

        private sealed class PreDispatchExecutionDecision
        {
            public bool PodeExecutar { get; set; }
            public bool UsouCanonico { get; set; }
            public ActionParseResult ParseResult { get; set; }
            public AgentResponse ResponseParaExecucao { get; set; }
        }

        private static string SummarizeDispatchError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return "erro nao informado.";

            var firstLine = error
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

            if (string.IsNullOrWhiteSpace(firstLine))
                firstLine = error.Trim();

            return TruncateForLog(firstLine, 500);
        }

        private static bool IsVerificationFailureError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return false;

            return error.IndexOf("[VERIFICATION_FAILED]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("VerificaÃ§Ã£o automÃ¡tica falhou", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("Verificacao automatica falhou", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task<bool> TentarCorrigirFalhaExecucaoAsync(
            AgentResult result,
            ProjectHistoryEntry historyEntry,
            ToolDispatchResult dispatchResult,
            string projectRoot,
            string systemPrompt,
            string promptBase,
            string entradaUsuario)
        {
            if (dispatchResult == null || string.IsNullOrWhiteSpace(dispatchResult.Error))
                return false;

            ReportStatus("Tentando corrigir falha de execucao");
            _log.Info("Iniciando reprocessamento por falha na execucao das acoes.");

            PatchRetryContext patchRetryContext = BuildPatchRetryContext(projectRoot, dispatchResult);

            if (IsGitDiffConvertedDispatchFailure(dispatchResult))
            {
                _log.Info("[GIT-DIFF] Retry com IA ignorado para GitDiff convertido com âncora inventada.");
                return false;
            }

            if (TryIsPatchRetryDirectContext(projectRoot, patchRetryContext))
            {
                RetrySemAcoesResult retryDireto = await ExecutarPatchRetryDiretoAsync(
                    projectRoot,
                    entradaUsuario,
                    patchRetryContext).ConfigureAwait(false);

                if (retryDireto == null)
                    return false;

                if (historyEntry.ExecutedActions == null)
                    historyEntry.ExecutedActions = new System.Collections.Generic.List<AgentAction>();

                if (dispatchResult.ExecutedActions != null)
                    historyEntry.ExecutedActions.AddRange(dispatchResult.ExecutedActions);

                if (retryDireto.ExecutedActions != null)
                    historyEntry.ExecutedActions.AddRange(retryDireto.ExecutedActions);

                int acoesExecutadasDireto = retryDireto.ExecutedActions == null ? 0 : retryDireto.ExecutedActions.Count;
                if (acoesExecutadasDireto <= 0)
                {
                    _log.Info("[FLOW] Retry retornou apenas ações não executáveis.");
                    _log.Info("[FLOW] Nenhum arquivo foi alterado.");
                    _log.Info("[FLOW] Mantendo falha original.");
                    return false;
                }

                result.ExecutedActions = historyEntry.ExecutedActions;

                historyEntry.Error = null;
                result.ToolDispatchError = null;
                result.StructuredResponse = retryDireto.StructuredResponse;
                result.ModelResponse = retryDireto.RespostaIA;
                result.ProjectContext = retryDireto.ContextoProjeto;
                result.FinalPrompt = retryDireto.FinalPrompt;

                _log.Info("Correcao automatica executada com sucesso. Acoes: " + acoesExecutadasDireto);
                return true;
            }

            string contextoAtual = patchRetryContext != null && patchRetryContext.HasPatchFailure
                ? BuildPatchRetryProjectContext(projectRoot, patchRetryContext)
                : MontarContextoProjeto(projectRoot);
            string blocosProibidos = BuildForbiddenFailedSearchBlocks(dispatchResult);
            string entradaCorrecao = BuildExecutionFailureRetryInput(entradaUsuario, dispatchResult, patchRetryContext, blocosProibidos);

            string promptCorrecao = PrepararPromptFinal(
                systemPrompt,
                promptBase,
                contextoAtual,
                entradaCorrecao,
                false);

            string respostaCorrecao = await ConsultarModeloComEtapasAsync(
                systemPrompt,
                promptBase,
                contextoAtual,
                entradaCorrecao,
                promptCorrecao).ConfigureAwait(false);

            int totalAcoes;
            bool semAcoes;
            AgentResponse responseCorrecao = InterpretarRespostaIA(
                respostaCorrecao,
                out totalAcoes,
                out semAcoes);

            if (responseCorrecao == null || responseCorrecao.Acoes == null || responseCorrecao.Acoes.Count == 0)
            {
                _log.Error("Correcao automatica nao retornou acoes.");
                return false;
            }

            ReportStatus("Executando acoes de correcao");
            ToolDispatchResult retryDispatch = _toolDispatcher.Dispatch(responseCorrecao, projectRoot);
            if (!string.IsNullOrWhiteSpace(retryDispatch.Error))
            {
                _log.Error("Correcao automatica tambem falhou: " + retryDispatch.Error);
                return false;
            }

            if (historyEntry.ExecutedActions == null)
                historyEntry.ExecutedActions = new System.Collections.Generic.List<AgentAction>();

            if (dispatchResult.ExecutedActions != null)
                historyEntry.ExecutedActions.AddRange(dispatchResult.ExecutedActions);

            if (retryDispatch.ExecutedActions != null)
                historyEntry.ExecutedActions.AddRange(retryDispatch.ExecutedActions);

            int acoesExecutadas = retryDispatch.ExecutedActions == null ? 0 : retryDispatch.ExecutedActions.Count;
            if (acoesExecutadas <= 0)
            {
                _log.Info("[FLOW] Retry retornou apenas ações não executáveis.");
                _log.Info("[FLOW] Nenhum arquivo foi alterado.");
                _log.Info("[FLOW] Mantendo falha original.");
                return false;
            }

            result.ExecutedActions = historyEntry.ExecutedActions;
            historyEntry.Error = null;
            result.ToolDispatchError = null;
            result.StructuredResponse = responseCorrecao;
            result.ModelResponse = respostaCorrecao;
            result.ProjectContext = contextoAtual;
            result.FinalPrompt = promptCorrecao;

            _log.Info("Correcao automatica executada com sucesso. Acoes: " + acoesExecutadas);
            return true;
        }

        private static bool TryIsPatchRetryDirectContext(string projectRoot, PatchRetryContext patchRetryContext)
        {
            if (patchRetryContext == null || !patchRetryContext.HasPatchFailure)
                return false;

            if (string.Equals(patchRetryContext.Kind, "Idempotent", StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(projectRoot) ||
                string.IsNullOrWhiteSpace(patchRetryContext.FilePath) ||
                string.IsNullOrWhiteSpace(patchRetryContext.NearbyFileSnippet))
                return false;

            return true;
        }

        private static bool IsGitDiffConvertedDispatchFailure(ToolDispatchResult dispatchResult)
        {
            if (dispatchResult?.FailedAction?.Dados == null)
                return false;

            try
            {
                var source = dispatchResult.FailedAction.Dados["__source"]?.ToString();
                return string.Equals(source, "GitDiffAdapter", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
            }

            return false;
        }

        private async Task<RetrySemAcoesResult> ExecutarPatchRetryDiretoAsync(
            string projectRoot,
            string entradaUsuario,
            PatchRetryContext patchRetryContext)
        {
            string arquivoRelativo = MakeRelativeSafe(projectRoot, patchRetryContext.FilePath).Replace('\\', '/');
            _log.Info("[PATCH-RETRY-DIRECT] Retry direto ativado.");
            _log.Info("[PATCH-RETRY-DIRECT] Tipo: " + patchRetryContext.Kind);
            _log.Info("[PATCH-RETRY-DIRECT] Arquivo: " + arquivoRelativo);
            _log.Info("[PATCH-RETRY-DIRECT] Snippet chars: " + (patchRetryContext.NearbyFileSnippet == null ? 0 : patchRetryContext.NearbyFileSnippet.Length));
            if (!string.Equals(arquivoRelativo, patchRetryContext.FilePath, StringComparison.OrdinalIgnoreCase))
                _log.Info("[PATCH-RETRY-DIRECT] Caminho absoluto convertido para relativo: " + arquivoRelativo);

            string promptDireto = BuildPatchRetryDirectPrompt(
                entradaUsuario,
                patchRetryContext,
                arquivoRelativo);

            int limite = ObterLimitePrompt();
            if (limite > 0 && promptDireto.Length > limite)
            {
                string snippetReduzido = patchRetryContext.NearbyFileSnippet;
                while (limite > 0 && promptDireto.Length > limite && !string.IsNullOrWhiteSpace(snippetReduzido) && snippetReduzido.Length > 1000)
                {
                    snippetReduzido = TruncateForPrompt(snippetReduzido, Math.Max(1000, snippetReduzido.Length / 2));
                    promptDireto = BuildPatchRetryDirectPrompt(entradaUsuario, patchRetryContext, arquivoRelativo, snippetReduzido);
                }
            }

            _log.Info("[PATCH-RETRY-DIRECT] Prompt direto chars: " + promptDireto.Length);

            if (limite > 0 && promptDireto.Length > limite)
            {
                _log.Info("[PATCH-RETRY-DIRECT] Prompt direto permaneceu acima do limite apos reducao; seguindo com o menor prompt construido.");
            }

            string respostaDireta = await AskModelWithDiagnosticsAsync("patch retry direto", promptDireto, 45000).ConfigureAwait(false);

            int totalAcoes;
            bool semAcoes;
            AgentResponse responseDireta = InterpretarRespostaIA(
                respostaDireta,
                out totalAcoes,
                out semAcoes);

            if (responseDireta == null || responseDireta.Acoes == null || responseDireta.Acoes.Count == 0)
            {
                _log.Info("[PATCH-RETRY-DIRECT] Retry direto retornou zero ações.");
                return null;
            }

            ReportStatus("Executando acoes de correcao");
            ToolDispatchResult retryDispatch = _toolDispatcher.Dispatch(responseDireta, projectRoot);
            if (!string.IsNullOrWhiteSpace(retryDispatch.Error))
            {
                _log.Error("Correcao automatica tambem falhou: " + retryDispatch.Error);
                return null;
            }

            return new RetrySemAcoesResult
            {
                StructuredResponse = responseDireta,
                FinalPrompt = promptDireto,
                RespostaIA = respostaDireta,
                ContextoProjeto = patchRetryContext.NearbyFileSnippet,
                ExecutedActions = retryDispatch.ExecutedActions == null
                    ? null
                    : retryDispatch.ExecutedActions.ToList()
            };
        }

        private static string BuildPatchRetryDirectPrompt(
            string entradaUsuario,
            PatchRetryContext patchRetryContext,
            string arquivoRelativo,
            string nearbyFileSnippet = null)
        {
            string snippet = nearbyFileSnippet ?? patchRetryContext?.NearbyFileSnippet ?? string.Empty;
            string retryInstruction = patchRetryContext?.RetryInstruction ?? string.Empty;
            string invalidAnchor = patchRetryContext?.InvalidSearchPreview ?? string.Empty;
            string hint = patchRetryContext?.HintForRetry ?? string.Empty;
            string demandSummary = TruncateForPrompt(entradaUsuario ?? string.Empty, 2500);
            string fileAllowed = string.IsNullOrWhiteSpace(arquivoRelativo)
                ? RedactSensitiveText(patchRetryContext?.FilePath ?? string.Empty)
                : arquivoRelativo;

            var sb = new StringBuilder();
            sb.AppendLine("Voce esta corrigindo uma falha de patch classificada.");
            sb.AppendLine("Nao reanalise o projeto.");
            sb.AppendLine("Nao selecione contexto.");
            sb.AppendLine("Nao use outro arquivo.");
            sb.AppendLine();
            sb.AppendLine("Demanda original resumida:");
            sb.AppendLine(demandSummary);
            sb.AppendLine();
            sb.AppendLine("Falha:");
            sb.AppendLine("Tipo: " + (patchRetryContext?.Kind ?? string.Empty));
            sb.AppendLine("Codigo: " + (patchRetryContext?.Code ?? string.Empty));
            sb.AppendLine("Titulo: " + (patchRetryContext?.Title ?? string.Empty));
            sb.AppendLine("Arquivo permitido:");
            sb.AppendLine(fileAllowed);
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(invalidAnchor))
            {
                sb.AppendLine("Âncora inválida:");
                sb.AppendLine(RedactSensitiveText(invalidAnchor));
                sb.AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(hint))
            {
                sb.AppendLine("Dica de retry:");
                sb.AppendLine(RedactSensitiveText(hint));
                sb.AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(retryInstruction))
            {
                sb.AppendLine("Instrução obrigatória:");
                sb.AppendLine(RedactSensitiveText(retryInstruction));
                sb.AppendLine();
            }
            sb.AppendLine("Trecho real do arquivo:");
            sb.AppendLine(snippet);
            sb.AppendLine();
            sb.AppendLine("Regra obrigatória:");
            sb.AppendLine("Use somente texto copiado literalmente do trecho real acima.");
            sb.AppendLine("Nao reutilize a ancora invalida.");
            sb.AppendLine("Nao use caminho absoluto.");
            sb.AppendLine("Se nao houver ancora segura, retorne acoes=[].");
            sb.AppendLine();
            sb.AppendLine("Resposta obrigatoria:");
            sb.AppendLine("JSON com acoes.");
            sb.AppendLine("Cada acao deve usar ARQ=" + fileAllowed.Replace('\\', '/'));
            sb.AppendLine("Use SEARCH/REPLACE ou SEARCH_BLOCK/REPLACE_BLOCK.");
            return sb.ToString();
        }

        private static string BuildForbiddenFailedSearchBlocks(ToolDispatchResult dispatchResult)
        {
            string dados = dispatchResult?.FailedAction?.Dados == null
                ? string.Empty
                : dispatchResult.FailedAction.Dados.ToString(Newtonsoft.Json.Formatting.None);

            if (string.IsNullOrWhiteSpace(dados))
                return string.Empty;

            string normalized = dados
                .Replace("\\r\\n", Environment.NewLine)
                .Replace("\\n", Environment.NewLine)
                .Replace("\\r", Environment.NewLine);

            var forbidden = new List<string>();
            foreach (Match match in Regex.Matches(normalized, @"SEARCH_BLOCK\s*=?\s*(?:\r?\n)?(?<line>[^\r\n""]+)", RegexOptions.IgnoreCase))
            {
                string line = (match.Groups["line"].Value ?? string.Empty).Trim();
                if (line.Length > 0 && !forbidden.Contains(line, StringComparer.OrdinalIgnoreCase))
                    forbidden.Add(line);
            }

            if (forbidden.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("SEARCH_BLOCKS_PROIBIDOS_NA_CORRECAO:");
            foreach (string item in forbidden.Take(5))
                sb.AppendLine("- " + item);
            sb.AppendLine("Nao use nenhum SEARCH_BLOCK acima na resposta de correcao. Se o membro nao existe, escolha um membro real listado no diagnostico como ancora e faca INSERT/REPLACE ao redor dele, ou altere o metodo real existente que contem a chamada relacionada.");
            return sb.ToString();
        }

        private PatchRetryContext BuildPatchRetryContext(string projectRoot, ToolDispatchResult dispatchResult)
        {
            string error = dispatchResult == null ? string.Empty : dispatchResult.Error;
            if (string.IsNullOrWhiteSpace(error))
            {
                _log.Info("[PATCH-RETRY] Contexto de retry de patch não gerado: erro vazio.");
                return null;
            }

            PatchRetryContext context = ParsePatchRetryContext(error);
            if (context == null || !context.HasPatchFailure)
            {
                _log.Info("[PATCH-RETRY] Contexto de retry de patch não gerado: falha sem classificação de patch.");
                return null;
            }

            if (string.Equals(context.Kind, "Idempotent", StringComparison.OrdinalIgnoreCase))
            {
                _log.Info("[PATCH-RETRY] Contexto de retry de patch não gerado: falha idempotente.");
                return null;
            }

            context.NearbyFileSnippet = BuildPatchRetryNearbySnippet(projectRoot, context.FilePath, context.InvalidSearchPreview);
            context.RetryInstruction = BuildPatchRetryInstruction(context);

            _log.Info("[PATCH-RETRY] Contexto de retry de patch criado.");
            _log.Info("[PATCH-RETRY] Tipo: " + context.Kind);
            _log.Info("[PATCH-RETRY] Codigo: " + context.Code);
            _log.Info("[PATCH-RETRY] Arquivo: " + RedactSensitiveText(context.FilePath));
            _log.Info("[PATCH-RETRY] Operacao: " + context.OperationIndex);
            _log.Info("[PATCH-RETRY] Snippet chars: " + (context.NearbyFileSnippet == null ? 0 : context.NearbyFileSnippet.Length));
            _log.Info("[PATCH-RETRY] Retry enxuto habilitado: " + (!string.IsNullOrWhiteSpace(context.NearbyFileSnippet)).ToString().ToLowerInvariant());
            return context;
        }

        private static PatchRetryContext ParsePatchRetryContext(string error)
        {
            string kind = ExtractTaggedValue(error, "[PATCH-CLASSIFIER] Tipo:");
            if (string.IsNullOrWhiteSpace(kind))
                return null;

            int operationIndex = 0;
            int.TryParse(ExtractTaggedValue(error, "[PATCH-CLASSIFIER] Operacao:"), out operationIndex);

            return new PatchRetryContext
            {
                HasPatchFailure = true,
                Kind = kind,
                Code = ExtractTaggedValue(error, "[PATCH-CLASSIFIER] Codigo:"),
                Title = ExtractTaggedValue(error, "[PATCH-CLASSIFIER] Titulo:"),
                Message = ExtractPlainPatchClassifierMessage(error),
                FilePath = ExtractTaggedValue(error, "[PATCH-CLASSIFIER] Arquivo:"),
                OperationIndex = operationIndex,
                InvalidSearchPreview = ExtractTaggedValue(error, "[PATCH-CLASSIFIER] Preview:"),
                HintForRetry = ExtractTaggedValue(error, "[PATCH-CLASSIFIER] Dica retry:")
            };
        }

        private string BuildPatchRetryNearbySnippet(string projectRoot, string filePath, string invalidSearchPreview)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(filePath))
                return string.Empty;

            string resolvedPath = ResolveRetryFilePath(projectRoot, filePath);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
                return string.Empty;

            try
            {
                var lines = File.ReadAllLines(resolvedPath).ToList();
                if (lines.Count == 0)
                    return string.Empty;

                int center = FindRetrySnippetCenter(lines, invalidSearchPreview);
                int start = Math.Max(0, center - 40);
                int end = Math.Min(lines.Count - 1, center + 39);
                var snippetLines = new List<string>();
                for (int i = start; i <= end; i++)
                    snippetLines.Add((i + 1).ToString().PadLeft(4) + ": " + lines[i]);

                string snippet = string.Join(Environment.NewLine, snippetLines);
                snippet = RedactSensitiveText(snippet);
                if (snippet.Length > 12000)
                    snippet = snippet.Substring(0, 12000);
                return snippet;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string BuildPatchRetryProjectContext(string projectRoot, PatchRetryContext context)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ARQUIVO_ALVO:");
            sb.AppendLine(RedactSensitiveText(context.FilePath));
            sb.AppendLine();
            sb.AppendLine("FALHA_CLASSIFICADA:");
            sb.AppendLine("Tipo=" + context.Kind + " Codigo=" + context.Code + " Operacao=" + context.OperationIndex);
            if (!string.IsNullOrWhiteSpace(context.InvalidSearchPreview))
            {
                sb.AppendLine("ANCORA_INVALIDA:");
                sb.AppendLine(RedactSensitiveText(context.InvalidSearchPreview));
            }
            if (!string.IsNullOrWhiteSpace(context.NearbyFileSnippet))
            {
                sb.AppendLine();
                sb.AppendLine("TRECHO_REAL_DO_ARQUIVO:");
                sb.AppendLine(context.NearbyFileSnippet);
            }

            return sb.ToString();
        }

        private string BuildExecutionFailureRetryInput(string entradaUsuario, ToolDispatchResult dispatchResult, PatchRetryContext patchRetryContext, string blocosProibidos)
        {
            if (patchRetryContext != null && patchRetryContext.HasPatchFailure)
            {
                var sb = new StringBuilder();
                sb.AppendLine(entradaUsuario ?? string.Empty);
                sb.AppendLine();
                sb.AppendLine("[CORRECAO AUTOMATICA DE FALHA DE PATCH]");
                sb.AppendLine("A falha foi classificada. Nao gere contexto grande nem reutilize a ancora invalida.");
                sb.AppendLine("Tipo: " + patchRetryContext.Kind);
                sb.AppendLine("Codigo: " + patchRetryContext.Code);
                sb.AppendLine("Titulo: " + patchRetryContext.Title);
                sb.AppendLine("Arquivo permitido: " + RedactSensitiveText(patchRetryContext.FilePath));
                sb.AppendLine("Operacao com falha: " + patchRetryContext.OperationIndex);
                if (!string.IsNullOrWhiteSpace(patchRetryContext.InvalidSearchPreview))
                {
                    sb.AppendLine("SEARCH/SEARCH_BLOCK invalido:");
                    sb.AppendLine(RedactSensitiveText(patchRetryContext.InvalidSearchPreview));
                }
                if (!string.IsNullOrWhiteSpace(patchRetryContext.HintForRetry))
                {
                    sb.AppendLine("Dica:");
                    sb.AppendLine(RedactSensitiveText(patchRetryContext.HintForRetry));
                }
                if (!string.IsNullOrWhiteSpace(patchRetryContext.RetryInstruction))
                {
                    sb.AppendLine("Instrucao de retry:");
                    sb.AppendLine(RedactSensitiveText(patchRetryContext.RetryInstruction));
                }
                if (!string.IsNullOrWhiteSpace(patchRetryContext.NearbyFileSnippet))
                {
                    sb.AppendLine("Trecho real do arquivo:");
                    sb.AppendLine(patchRetryContext.NearbyFileSnippet);
                }
                if (!string.IsNullOrWhiteSpace(blocosProibidos))
                    sb.AppendLine(blocosProibidos);
                sb.AppendLine("Responda somente com acoes executaveis. Nao reutilize a ancora invalida. Se nao houver ancora segura, retorne acoes=[].");
                return sb.ToString();
            }

            return entradaUsuario +
                Environment.NewLine +
                Environment.NewLine +
                "[CORRECAO AUTOMATICA DE FALHA]" +
                Environment.NewLine +
                "Algumas acoes ja podem ter sido aplicadas ao projeto. Continue a partir do estado atual dos arquivos." +
                Environment.NewLine +
                "Erro do executor:" +
                Environment.NewLine +
                dispatchResult.Error +
                Environment.NewLine +
                blocosProibidos +
                Environment.NewLine +
                Environment.NewLine +
                "Gere somente as acoes necessarias para corrigir o erro e concluir a tarefa original. Nao repita alteracoes que ja aparecem no contexto atual. Se o erro disser que o arquivo nao existe e trouxer 'Arquivos existentes candidatos no projeto', escolha um desses arquivos reais; nao tente alterar novamente o mesmo caminho inexistente e nao crie arquivo novo para SEARCH/DELETE. Se o erro trouxer 'Trechos parecidos encontrados no arquivo atual', use essas linhas como fonte de verdade para montar o novo SEARCH/SEARCH_BLOCK. Se o erro trouxer 'Membros C# encontrados' e o metodo buscado nao existir, insira o novo metodo perto de um membro real existente em vez de substituir um SEARCH_BLOCK inexistente. Nao repita o mesmo bloco que falhou. Garanta que toda linha SEARCH= tenha REPLACE=, REPLACE_BLOCK ou DELETE imediatamente depois. Para remover blocos, prefira SEARCH_BLOCK com DELETE_BLOCK.";
        }

        private static string BuildPatchRetryInstruction(PatchRetryContext context)
        {
            switch ((context.Kind ?? string.Empty).Trim())
            {
                case "InventedAnchor":
                    return "Nao reutilize o SEARCH/SEARCH_BLOCK invalido. Use somente texto copiado literalmente do trecho real do arquivo abaixo. Se nao houver ancora segura, retorne acoes=[].";
                case "DuplicateAnchor":
                    return "Nao reutilize a mesma ancora em multiplas operacoes. Consolide a alteracao em um unico SEARCH_BLOCK/REPLACE_BLOCK atomico.";
                case "FragmentedPatch":
                    return "Nao envie REPLACE_BLOCK com fragmentos soltos. Gere bloco completo, sintaticamente valido e com indentacao correta.";
                case "WrongLanguage":
                    return "O arquivo e Python. Use somente sintaxe Python real. Nao use chaves isoladas, Provider com maiuscula inventado, public/private/function/var/let/const.";
                case "InvalidFormat":
                    return "Use somente formato suportado: ARQ + SEARCH/REPLACE ou ARQ + SEARCH_BLOCK/REPLACE_BLOCK.";
                case "EmptyAnchor":
                    return "A ancora usada esta vazia ou invalida. Use uma ancora literal existente no arquivo permitido.";
                case "EmptyReplace":
                    return "Nao envie REPLACE vazio. Use DELETE explicito para remocao ou forneca REPLACE completo.";
                case "PreflightFailed":
                    return "Use somente texto copiado literalmente do arquivo e gere um patch minimo, atomico e consistente com o trecho real.";
                default:
                    return string.Empty;
            }
        }

        private static string ResolveRetryFilePath(string projectRoot, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return string.Empty;

            string trimmed = filePath.Trim();
            try
            {
                if (Path.IsPathRooted(trimmed))
                    return Path.GetFullPath(trimmed);

                return Path.GetFullPath(Path.Combine(projectRoot, trimmed.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int FindRetrySnippetCenter(List<string> lines, string preview)
        {
            if (lines == null || lines.Count == 0)
                return 0;

            string normalizedPreview = NormalizeRetrySearch(preview);
            if (!string.IsNullOrWhiteSpace(normalizedPreview))
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    if (NormalizeRetrySearch(lines[i]).IndexOf(normalizedPreview, StringComparison.OrdinalIgnoreCase) >= 0)
                        return i;
                }
            }

            return Math.Min(39, lines.Count - 1);
        }

        private static string NormalizeRetrySearch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        }

        private static string ExtractTaggedValue(string text, string prefix)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(prefix))
                return string.Empty;

            string escapedPrefix = Regex.Escape(prefix);
            Match match = Regex.Match(text, escapedPrefix + @"\s*(?<value>.+?)(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
                return match.Groups["value"].Value.Trim();

            foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                int idx = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    return line.Substring(idx + prefix.Length).Trim();
            }

            return string.Empty;
        }

        private static string ExtractPlainPatchClassifierMessage(string text)
        {
            var lines = (text ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Where(line => !line.StartsWith("[PATCH-CLASSIFIER]", StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();

            return string.Join(" ", lines);
        }

        private static string RedactSensitiveText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string result = text;
            result = System.Text.RegularExpressions.Regex.Replace(result, @"sk-[A-Za-z0-9_\-]{8,}", "[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            result = System.Text.RegularExpressions.Regex.Replace(result, @"Bearer\s+[A-Za-z0-9_\-\.\=]{8,}", "Bearer [REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            result = System.Text.RegularExpressions.Regex.Replace(result, "(?i)(OpenAiApiKey|ApiKey|Authorization)\\s*[:=]\\s*([^\\r\\n;,\\\"']+)", "$1: [REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return result;
        }

        private async Task<bool> TentarContinuarConclusaoParcialAsync(
            AgentResult result,
            ProjectHistoryEntry historyEntry,
            AgentResponse ultimaResposta,
            string projectRoot,
            string systemPrompt,
            string promptBase,
            string entradaUsuario)
        {
            if (!RespostaIndicaConclusaoParcial(ultimaResposta))
                return false;

            bool executouContinuacao = false;

            for (int tentativa = 1; tentativa <= 3; tentativa++)
            {
                ReportStatus("Continuando etapas pendentes");
                _log.Info("Resposta indica conclusao parcial. Solicitando continuacao automatica. Tentativa " + tentativa + "/3.");

                string contextoAtual = MontarContextoProjeto(projectRoot);
                string entradaContinuacao = entradaUsuario +
                    Environment.NewLine +
                    Environment.NewLine +
                    "[CONTINUACAO AUTOMATICA]" +
                    Environment.NewLine +
                    "A resposta anterior concluiu apenas uma etapa e pediu confirmacao/proxima etapa. Nao aguarde confirmacao manual. Continue a partir do estado atual dos arquivos e gere as acoes restantes para concluir integralmente a tarefa original." +
                    Environment.NewLine +
                    "Nao repita alteracoes que ja aparecem no contexto atual. Se tudo ja estiver completo, retorne acoes vazia e explique objetivamente.";

                string promptContinuacao = PrepararPromptFinal(
                    systemPrompt,
                    promptBase,
                    contextoAtual,
                    entradaContinuacao,
                    false);

                string respostaContinuacao = await ConsultarModeloComEtapasAsync(
                    systemPrompt,
                    promptBase,
                    contextoAtual,
                    entradaContinuacao,
                    promptContinuacao).ConfigureAwait(false);

                int totalAcoes;
                bool semAcoes;
                AgentResponse responseContinuacao = InterpretarRespostaIA(
                    respostaContinuacao,
                    out totalAcoes,
                    out semAcoes);

                if (semAcoes)
                {
                    var retryContinuacao = await TentarReprocessarRespostaSemAcoesAsync(
                        projectRoot,
                        systemPrompt,
                        promptBase,
                        contextoAtual,
                        entradaContinuacao,
                        respostaContinuacao,
                        responseContinuacao).ConfigureAwait(false);

                    if (retryContinuacao != null)
                    {
                        contextoAtual = retryContinuacao.ContextoProjeto;
                        promptContinuacao = retryContinuacao.FinalPrompt;
                        respostaContinuacao = retryContinuacao.RespostaIA;
                        responseContinuacao = retryContinuacao.StructuredResponse;
                        totalAcoes = responseContinuacao?.Acoes == null ? 0 : responseContinuacao.Acoes.Count;
                        semAcoes = totalAcoes == 0;
                    }
                }

                if (responseContinuacao == null || responseContinuacao.Acoes == null || responseContinuacao.Acoes.Count == 0)
                {
                    _log.Info("Continuacao automatica retornou sem acoes. Encerrando continuacao.");
                    result.StructuredResponse = responseContinuacao;
                    result.ModelResponse = respostaContinuacao;
                    result.ProjectContext = contextoAtual;
                    result.FinalPrompt = promptContinuacao;
                    return executouContinuacao;
                }

                ReportStatus("Executando acoes de continuacao");
                ToolDispatchResult dispatchContinuacao = _toolDispatcher.Dispatch(responseContinuacao, projectRoot);
                if (!string.IsNullOrWhiteSpace(dispatchContinuacao.Error))
                {
                    _log.Error("Continuacao automatica falhou: " + dispatchContinuacao.Error);

                    bool recovered = await TentarCorrigirFalhaExecucaoAsync(
                        result,
                        historyEntry,
                        dispatchContinuacao,
                        projectRoot,
                        systemPrompt,
                        promptBase,
                        entradaContinuacao).ConfigureAwait(false);

                    if (!recovered)
                    {
                        result.ToolDispatchError = dispatchContinuacao.Error;
                        historyEntry.Error = dispatchContinuacao.Error;
                        throw new InvalidOperationException("Falha ao executar continuacao automatica: " + SummarizeDispatchError(dispatchContinuacao.Error));
                    }

                    executouContinuacao = true;
                    return true;
                }

                if (historyEntry.ExecutedActions == null)
                    historyEntry.ExecutedActions = new System.Collections.Generic.List<AgentAction>();

                if (dispatchContinuacao.ExecutedActions != null)
                    historyEntry.ExecutedActions.AddRange(dispatchContinuacao.ExecutedActions);

                result.ExecutedActions = historyEntry.ExecutedActions;
                result.StructuredResponse = responseContinuacao;
                result.ModelResponse = respostaContinuacao;
                result.ProjectContext = contextoAtual;
                result.FinalPrompt = promptContinuacao;
                executouContinuacao = true;

                if (!RespostaIndicaConclusaoParcial(responseContinuacao))
                    return true;

                ultimaResposta = responseContinuacao;
            }

            _log.Error("Continuacao automatica atingiu o limite de 3 ciclos ainda indicando etapa pendente.");
            return executouContinuacao;
        }

        private static bool RespostaIndicaConclusaoParcial(AgentResponse response)
        {
            string texto = string.Join(
                Environment.NewLine,
                response?.MensagemUsuario ?? string.Empty,
                response?.Explicacao ?? string.Empty);

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            bool possuiAcoes = response?.Acoes != null && response.Acoes.Count > 0;
            bool possuiCheckEtapas = texto.IndexOf("CHECK_ETAPAS", StringComparison.OrdinalIgnoreCase) >= 0;
            bool declaraCobertura =
                texto.IndexOf("coberta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("cobertas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("coberto", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("cobertos", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("todas as etapas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("todas etapas", StringComparison.OrdinalIgnoreCase) >= 0;
            bool pendenciaExplicita =
                texto.IndexOf("pendente", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("pendentes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("faltante", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("faltantes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("nao foi implement", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("nao implement", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("nÃƒÂ£o foi implement", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("nÃƒÂ£o implement", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("proxima etapa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("prÃƒÂ³xima etapa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("prÃƒÆ’Ã‚Â³xima etapa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("aguarda_confirmacao", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("aguarda confirmaÃƒÂ§ÃƒÂ£o", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("aguarda confirmacao", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("confirme para prosseguir", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("confirmar para prosseguir", StringComparison.OrdinalIgnoreCase) >= 0;

            if (possuiAcoes && possuiCheckEtapas && declaraCobertura && !pendenciaExplicita)
                return false;

            bool indicaEtapa =
                texto.IndexOf("etapa 1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("primeira etapa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("proxima etapa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("prÃƒÂ³xima etapa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("prosseguir", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("confirm", StringComparison.OrdinalIgnoreCase) >= 0;

            bool indicaPendente =
                pendenciaExplicita ||
                texto.IndexOf("proxima", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("prÃƒÂ³xima", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("prosseguir", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("confirm", StringComparison.OrdinalIgnoreCase) >= 0;

            return indicaEtapa && indicaPendente;
        }

        private void RegistrarHistorico(
            AgentResult result,
            ProjectHistoryEntry historyEntry,
            string projectRoot)
        {
            ReportStatus("Registrando historico");

            ProjectHistoryRepository.Append(
                result.ProjectId != Guid.Empty ? result.ProjectId.ToString() : projectRoot,
                result.ProjectName,
                historyEntry);
        }

        private void AtualizarStatusFinal(bool semAcoesExecutaveis)
        {
            if (semAcoesExecutaveis)
                ReportStatus("Erro: IA nao retornou acoes");
            else
                ReportStatus("Concluido");
        }



        private void ReportStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return;

            _statusReporter?.Invoke(status);
        }

        private static string GetMainProgramVersion()
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                    return StripBuildMetadata(info);

                var version = assembly.GetName().Version;
                return version == null ? "desconhecida" : version.ToString();
            }
            catch
            {
                return "desconhecida";
            }
        }

        private static string GetDllVersion()
        {
            try
            {
                var assembly = typeof(AgentCore).Assembly;
                var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                    return StripBuildMetadata(info);

                var version = assembly.GetName().Version;
                return version == null ? "desconhecida" : version.ToString();
            }
            catch
            {
                return "desconhecida";
            }
        }

        private static string StripBuildMetadata(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return "desconhecida";

            var index = version.IndexOf('+');
            return index > 0 ? version.Substring(0, index) : version;
        }

        private static string TruncateForLog(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0 || text.Length <= maxChars)
                return text ?? string.Empty;

            return text.Substring(0, maxChars) + "...";
        }

        private static string TruncateForPrompt(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0 || text.Length <= maxChars)
                return text ?? string.Empty;

            int head = Math.Max(1, maxChars * 3 / 4);
            int tail = Math.Max(1, maxChars - head - 120);
            if (tail <= 0)
                return text.Substring(0, maxChars);

            return text.Substring(0, head) +
                   Environment.NewLine +
                   "[... conteudo omitido para manter prompt curto ...]" +
                   Environment.NewLine +
                   text.Substring(text.Length - tail);
        }

        private static string BuildFinalPrompt(string systemPrompt, string promptBase, string contextoProjeto, string entradaUsuario, bool reducedContext)
        {
            var contextToUse = contextoProjeto ?? string.Empty;
            if (reducedContext)
            {
                contextToUse = BuildRelevantReducedContext(contextToUse, entradaUsuario, 12000);
            }

            if (reducedContext)
            {
                contextToUse = string.Join(
                    Environment.NewLine + Environment.NewLine,
                    new[]
                    {
                        "[MODO REDUZIDO]",
                        "Use apenas o contexto essencial para evitar bloqueio por recitacao.",
                        contextToUse
                    }.Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            return string.Join(
                Environment.NewLine + Environment.NewLine,
                new[] { systemPrompt, promptBase, contextToUse, entradaUsuario }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static string BuildPromptSelecaoContexto(string systemPrompt, string contextoProjeto, string entradaUsuario, int limite)
        {
            string inventario = BuildContextInventory(contextoProjeto, Math.Max(4000, limite - FixedPromptOverhead(systemPrompt, string.Empty, entradaUsuario) - 2500));
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                new[]
                {
            systemPrompt,
            "[ETAPA 1 - SELECAO DE CONTEXTO]",
            "O prompt completo excedeu o limite configurado. Analise a solicitacao e o inventario do projeto. Retorne somente uma lista curta dos arquivos necessarios e uma sequencia objetiva de etapas. Nao gere codigo nesta etapa.",
            "Se o projeto for Laravel e a demanda mencionar uma URL/rota, inclua routes/web.php, o controller associado, views blade relacionadas e arquivos que contenham o nome da rota.",
            "Escolha somente arquivos listados no inventario. Nao invente arquivos genericos como MainForm.cs, Form1.cs, HomeController.cs ou Program.cs se eles nao aparecerem no inventario.",
            "Para WinForms, identifique a tela principal pelos arquivos reais listados. Se existir Forms\\Inicial.cs, use esse arquivo em vez de inventar MainForm.cs.",
            "Para WinForms, selecione *.Designer.cs somente se for inevitavel editar InitializeComponent; caso contrario, prefira o arquivo .cs principal.",
            "Formato obrigatorio:",
            "ARQUIVOS_NECESSARIOS:",
            "- caminho/ou/NomeArquivo.cs",
            "ETAPAS:",
            "- descricao curta",
            inventario,
            entradaUsuario
                }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static string BuildContextInventory(string contextoProjeto, int maxChars)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[INVENTARIO DO CONTEXTO]");

            string root = ExtrairRaizDoContexto(contextoProjeto);
            if (!string.IsNullOrWhiteSpace(root))
                sb.AppendLine("Raiz: " + root);

            var sections = ExtractFileSections(contextoProjeto).ToList();
            if (sections.Count > 0)
            {
                sb.AppendLine("Arquivos disponiveis:");
                foreach (var section in sections)
                    sb.AppendLine("- " + section.RelativePath);
            }
            else if (!string.IsNullOrWhiteSpace(contextoProjeto))
            {
                sb.AppendLine(TruncateMiddle(contextoProjeto, maxChars));
            }

            var result = sb.ToString();
            if (result.Length > maxChars)
                return TruncateMiddle(result, maxChars);
            return result;
        }

        private static string MontarContextoFocadoPorResposta(string contextoProjeto, string respostaSelecao, List<string> arquivosSelecionados, int limite)
        {
            string root = ExtrairRaizDoContexto(contextoProjeto);
            string contextoArquivos = arquivosSelecionados == null || arquivosSelecionados.Count == 0
                ? string.Empty
                : MontarContextoArquivosSolicitados(root, arquivosSelecionados);

            if (string.IsNullOrWhiteSpace(contextoArquivos))
                contextoArquivos = BuildRelevantReducedContext(contextoProjeto, respostaSelecao, Math.Max(8000, limite / 2));

            string cabecalho = string.Join(
                Environment.NewLine,
                "[CONTEXTO FOCADO PARA EXECUCAO]",
                "Resumo da etapa de selecao:",
                TruncateMiddle(respostaSelecao ?? string.Empty, 3000));

            return string.Join(
                Environment.NewLine + Environment.NewLine,
                new[] { cabecalho, contextoArquivos }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static int FixedPromptOverhead(string systemPrompt, string promptBase, string entradaUsuario)
        {
            return (systemPrompt ?? string.Empty).Length +
                   (promptBase ?? string.Empty).Length +
                   (entradaUsuario ?? string.Empty).Length +
                   1000;
        }

        private static string ExtrairRaizDoContexto(string contextoProjeto)
        {
            if (string.IsNullOrWhiteSpace(contextoProjeto))
                return string.Empty;

            foreach (var line in contextoProjeto.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (line.StartsWith("Raiz:", StringComparison.OrdinalIgnoreCase))
                    return line.Substring("Raiz:".Length).Trim();
            }

            return string.Empty;
        }

        private static string BuildMinimalContext(string context, string userInput, int maxChars)
        {
            string reduced = BuildRelevantReducedContext(context, userInput, maxChars);
            if (string.IsNullOrWhiteSpace(reduced) || reduced.Length <= maxChars)
                return reduced ?? string.Empty;

            return TruncateMiddle(reduced, maxChars);
        }

        private static string BuildEmergencyPrompt(string systemPrompt, string contextoProjeto, string entradaUsuario, int maxChars)
        {
            string compactSchema = string.Join(
                Environment.NewLine,
                "Responda somente JSON valido, sem markdown e sem texto fora do JSON.",
                "Schema:",
                "{\"mensagem_usuario\":\"string\",\"explicacao\":\"string\",\"acoes\":[{\"tipo\":\"ArquivoLocal\",\"descricao\":\"string\",\"requer_confirmacao\":true,\"dados\":{\"protocolo\":\"ARQ=caminho\\nSEARCH=texto\\nREPLACE=texto\"}}],\"requer_confirmacao\":false}");

            string instructions = string.Join(
                Environment.NewLine,
                "[MODO MINIMO ANTI-RECITACAO]",
                compactSchema,
                "Gere acoes ArquivoLocal pequenas e executaveis.",
                "Nao substitua arquivos inteiros.",
                "Nao copie metodos inteiros.",
                "Use SEARCH_BLOCK com no maximo 6 linhas.",
                "Toda linha SEARCH= deve ter REPLACE=, REPLACE_BLOCK ou DELETE imediatamente depois.",
                "Para remover multiplas linhas, use SEARCH_BLOCK seguido de DELETE_BLOCK.",
                "Nao recite trechos longos do contexto; use apenas ancoras curtas.");

            int fixedSize =
                instructions.Length +
                (entradaUsuario ?? string.Empty).Length +
                1200;

            int contextBudget = Math.Max(1500, maxChars - fixedSize);
            string minimalContext = BuildMinimalContext(contextoProjeto, entradaUsuario, contextBudget);

            string prompt = string.Join(
                Environment.NewLine + Environment.NewLine,
                new[] { instructions, minimalContext, entradaUsuario }.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (prompt.Length <= maxChars)
                return prompt;

            contextBudget = Math.Max(500, contextBudget - (prompt.Length - maxChars) - 500);
            minimalContext = BuildMinimalContext(contextoProjeto, entradaUsuario, contextBudget);

            prompt = string.Join(
                Environment.NewLine + Environment.NewLine,
                new[] { instructions, minimalContext, entradaUsuario }.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (prompt.Length <= maxChars)
                return prompt;

            return TruncateMiddle(prompt, maxChars);
        }

        private static string BuildRelevantReducedContext(string context, string userInput, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(context) || maxChars <= 0 || context.Length <= maxChars)
                return context ?? string.Empty;

            var fileSections = ExtractFileSections(context).ToList();
            if (fileSections.Count == 0)
                return TruncateMiddle(context, maxChars);

            var firstFileIndex = context.IndexOf("--- FILE:", StringComparison.OrdinalIgnoreCase);
            var preamble = firstFileIndex > 0 ? context.Substring(0, firstFileIndex) : string.Empty;
            if (preamble.Length > 7000)
                preamble = TruncateMiddle(preamble, 7000);

            var keywords = BuildReductionKeywords(userInput).ToList();
            var selected = fileSections
                .Select(section => new { Section = section, Score = ScoreSection(section, keywords) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Section.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Section)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine(preamble.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("[CONTEXTO REDUZIDO SELETIVO]");
            sb.AppendLine("Arquivos abaixo foram escolhidos por relevancia com a instrucao do usuario.");

            foreach (var section in selected)
            {
                if (sb.Length + section.Text.Length + 4 > maxChars)
                {
                    string snippet = BuildSnippetSection(section, keywords, Math.Max(1200, Math.Min(5000, maxChars - sb.Length - 4)));
                    if (!string.IsNullOrWhiteSpace(snippet) && sb.Length + snippet.Length + 4 <= maxChars)
                    {
                        sb.AppendLine();
                        sb.AppendLine(snippet.TrimEnd());
                    }

                    continue;
                }

                sb.AppendLine();
                sb.AppendLine(section.Text.TrimEnd());
            }

            if (selected.Count == 0 || sb.Length < Math.Min(maxChars, 12000))
            {
                foreach (var section in fileSections)
                {
                    if (selected.Any(x => string.Equals(x.RelativePath, section.RelativePath, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (sb.Length + section.Text.Length + 4 > maxChars)
                    {
                        string snippet = BuildSnippetSection(section, keywords, Math.Max(1000, Math.Min(3500, maxChars - sb.Length - 4)));
                        if (!string.IsNullOrWhiteSpace(snippet) && sb.Length + snippet.Length + 4 <= maxChars)
                        {
                            sb.AppendLine();
                            sb.AppendLine(snippet.TrimEnd());
                        }

                        continue;
                    }

                    sb.AppendLine();
                    sb.AppendLine(section.Text.TrimEnd());
                }
            }

            var reduced = sb.ToString();
            if (reduced.Length > maxChars)
                return TruncateMiddle(reduced, maxChars);

            return reduced;
        }

        private static string BuildSnippetSection(FileContextSection section, List<string> keywords, int maxChars)
        {
            if (section == null || string.IsNullOrWhiteSpace(section.Text) || maxChars < 500)
                return string.Empty;

            string body = ExtractSectionBody(section.Text, section.RelativePath);
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            var lines = body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
                return string.Empty;

            var interestingLines = new SortedSet<int>();
            var normalizedKeywords = (keywords ?? new List<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k) && k.Trim().Length >= 4)
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] ?? string.Empty;
                bool match = normalizedKeywords.Any(k => line.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match && IsLikelyImportantCodeLine(line))
                    match = true;

                if (!match)
                    continue;

                int from = Math.Max(0, i - 4);
                int to = Math.Min(lines.Length - 1, i + 8);
                for (int j = from; j <= to; j++)
                    interestingLines.Add(j);
            }

            if (interestingLines.Count == 0)
            {
                int fallbackLines = Math.Min(lines.Length, 60);
                for (int i = 0; i < fallbackLines; i++)
                    interestingLines.Add(i);
            }

            var sb = new StringBuilder();
            sb.AppendLine("--- FILE_SNIPPETS: " + section.RelativePath + " ---");
            sb.AppendLine("[Arquivo omitido parcialmente. Trechos relevantes com numeros de linha.]");

            int previous = -2;
            foreach (var lineIndex in interestingLines)
            {
                if (sb.Length > maxChars - 120)
                    break;

                if (previous >= 0 && lineIndex > previous + 1)
                    sb.AppendLine("...");

                string numberedLine = (lineIndex + 1).ToString("0000") + ": " + (lines[lineIndex] ?? string.Empty);
                if (sb.Length + numberedLine.Length + 2 > maxChars - 80)
                    break;

                sb.AppendLine(numberedLine);
                previous = lineIndex;
            }

            sb.AppendLine("--- END FILE_SNIPPETS: " + section.RelativePath + " ---");
            return sb.ToString();
        }

        private static string ExtractSectionBody(string sectionText, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(sectionText))
                return string.Empty;

            string startMarker = "--- FILE: " + (relativePath ?? string.Empty) + " ---";
            string endMarker = "--- END FILE: " + (relativePath ?? string.Empty) + " ---";
            int start = sectionText.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return sectionText;

            start += startMarker.Length;
            int end = sectionText.IndexOf(endMarker, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
                end = sectionText.Length;

            return sectionText.Substring(start, end - start).Trim('\r', '\n');
        }

        private static bool IsLikelyImportantCodeLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string trimmed = line.Trim();
            return Regex.IsMatch(trimmed, @"^(public|private|protected|internal)\s+") ||
                   trimmed.StartsWith("class ", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("partial class ", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("namespace ", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.IndexOf("InitializeComponent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   trimmed.IndexOf("+=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   trimmed.IndexOf("Click", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   trimmed.IndexOf("Name =", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<FileContextSection> ExtractFileSections(string context)
        {
            var regex = new Regex(
                @"--- FILE:\s*(?<path>.+?)\s*---(?<body>.*?)(--- END FILE:\s*\k<path>\s*---)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in regex.Matches(context ?? string.Empty))
            {
                if (!match.Success)
                    continue;

                yield return new FileContextSection
                {
                    RelativePath = match.Groups["path"].Value.Trim(),
                    Text = match.Value
                };
            }
        }

        private static IEnumerable<string> BuildReductionKeywords(string userInput)
        {
            var text = (userInput ?? string.Empty).ToLowerInvariant();
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in Regex.Matches(text, @"[a-z0-9_\\/\.-]{4,}"))
                result.Add(match.Value.Trim('.', ',', ';', ':'));

            if (text.Contains("prioridade"))
            {
                result.Add("prioridade");
                result.Add("priority");
            }

            if (text.Contains("nova tarefa") || text.Contains("cadastro de nova tarefa") || text.Contains("tarefa"))
            {
                result.Add("addtask");
                result.Add("taskform");
                result.Add("taskmodel");
                result.Add("tarefa");
            }

            if (text.Contains("nota") || text.Contains("notas"))
            {
                result.Add("tasknote");
                result.Add("note");
                result.Add("nota");
            }

            if (text.Contains("formulario") || text.Contains("formulÃƒÂ¡rio") || text.Contains("tela") || text.Contains("visual"))
            {
                result.Add("form");
                result.Add("designer");
                result.Add("winforms");
            }

            return result.Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private static int ScoreSection(FileContextSection section, List<string> keywords)
        {
            if (section == null || keywords == null || keywords.Count == 0)
                return 0;

            var path = (section.RelativePath ?? string.Empty).ToLowerInvariant();
            var text = (section.Text ?? string.Empty).ToLowerInvariant();
            var score = 0;

            foreach (var keyword in keywords)
            {
                var k = (keyword ?? string.Empty).ToLowerInvariant();
                if (k.Length < 4)
                    continue;

                if (path.Contains(k))
                    score += 100;
                else if (text.Contains(k))
                    score += 5;
            }

            if (path.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
                score += 10;
            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                score += 20;

            return score;
        }

        private static string TruncateMiddle(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text) || maxChars <= 0 || text.Length <= maxChars)
                return text ?? string.Empty;

            int keepHead = maxChars / 2;
            int keepTail = maxChars - keepHead - 32;
            if (keepTail < 0)
                keepTail = 0;

            var head = text.Substring(0, keepHead);
            var tail = keepTail > 0 ? text.Substring(text.Length - keepTail) : string.Empty;

            return head + Environment.NewLine + "<... contexto reduzido ...>" + Environment.NewLine + tail;
        }

        private sealed class FileContextSection
        {
            public string RelativePath { get; set; }
            public string Text { get; set; }
        }
    }
}
