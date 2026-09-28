using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using GptBolDll.Configuration;
using GptBolDll.Core.Contracts;
using GptBolDll.Core.Services;
using Iazeitor.KnowledgeEngine.Contracts.Gateway;
using Iazeitor.KnowledgeEngine.Contracts.Persistence;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Query;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace GptBolDll
{
    public sealed class AgentCore
    {
        private const string FeatureMarker = "KimiOfficialMoonshot-v1;ProviderRateLimitFallback-v1;LogCleanup-v1;KimiOfficialConfigTrace-v1;ProviderDuplicateGuard-v1;AiProviderPathUnify-v1;PreFactoryProviderGuard-v1;ExplicitPathFilter-v2;PatchPreflight-v1;PatchPlan-v1;PatchValidator-v1;PatchFailureClassifier-v1;PatchRetryContext-v1;InventedAnchorRetry-v1;PatchRetryDirect-v1;NoActionNotSuccess-v1;DirectPatchFields-v1;KimiOpenAICompatible-v1;ParserActGuard-v1;ActionParserContract-v1;ActionParserPipelineDiag-v1;ActionParserPreDispatch-v1;ActionParserRawJsonDiag-v1;ActionParserRawJsonRecover-v1;ActionParserCanonicalExec-v1;ActionParserNormalize-v1;ActionParserOutcome-v1;ToolDispatcherCanonical-v1;ActionParserFinal-v1;CanonicalSecretDiag-v1;CanonicalSecretValidationFix-v1;CanonicalActionExpansionDiag-v1;CanonicalDiagEntry-v1;AiRawErrorLog-v1;GitDiffRuntimePrompt-v1;GitDiffDetect-v1;GitDiffAdapter-v1;GitDiffSafety-v1;GitDiffToProtocol-v1;GitDiffRuntimeSync-v1;GitDiffPromptPathPersistFix-v1;GitDiffConfigStoreFix-v1;GitDiffPromptFixedFallback-v1;KimiUnavailableFriendly-v1;KimiForcedGitDiff-v1;GitDiffDeleteOnly-v1;GitDiffCanonicalExec-v1;KimiModelRuntimeFix-v1;GitDiffJsonWrapper-v1;GitDiffJsonEscapedString-v1;ProgrammerTempRetrySlim-v1;GitDiffDeleteContextExpand-v1;GitDiffEmptyHunkIgnore-v1;GitDiffPriorityFlow-v1;GitDiffLiteralSearchRepair-v1;GitDiffNoAiRetryOnAnchor-v1;GitDiffTrimmedDeleteRepair-v1;GitDiffDeleteLiteralExpand-v1;GitDiffDropEmptyInvalidFile-v1;KimiRecoveryPathFix-v1;KimiSearchBlockDeleteRecover-v1";

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
        private readonly RequestExecutionEngine _requestExecutionEngine;
        private readonly IKnowledgeGateway _knowledgeGateway;
        private readonly IProjectContextProvider _projectContextProvider;
        private readonly IProjectKnowledgeBindingStore _projectKnowledgeBindingStore;
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
        public KnowledgeEngineMode KnowledgeEngineMode { get; set; } = KnowledgeEngineMode.Observe;
        public int KnowledgeGatewayTimeoutMilliseconds { get; set; } = 5000;
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
            string analystModelDisplayName = null,
            RequestExecutionEngine requestExecutionEngine = null,
            IKnowledgeGateway knowledgeGateway = null,
            IProjectContextProvider projectContextProvider = null,
            IProjectKnowledgeBindingStore projectKnowledgeBindingStore = null)
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
            _requestExecutionEngine = requestExecutionEngine ?? throw new ArgumentNullException(nameof(requestExecutionEngine));
            _knowledgeGateway = knowledgeGateway;
            _projectContextProvider = projectContextProvider ?? new ProjectContextProvider();
            _projectKnowledgeBindingStore = projectKnowledgeBindingStore;
        }

        private IReadOnlyList<KnowledgeIdentifier> LoadBoundPackIds(string normalizedProjectId)
        {
            if (_projectKnowledgeBindingStore == null || String.IsNullOrWhiteSpace(normalizedProjectId))
            {
                return null;
            }

            try
            {
                ProjectKnowledgeBindingContract binding = _projectKnowledgeBindingStore.Load(normalizedProjectId);
                if (binding == null || binding.PackIds == null || binding.PackIds.Count == 0)
                {
                    return null;
                }

                return binding.PackIds;
            }
            catch (Exception)
            {
                // Associacao ausente/ilegivel nao inventa Packs: o Gateway/Reader
                // classifica o que existir e nao ha associacao cruzada entre projetos.
                return null;
            }
        }

        public async Task<AgentResult> ExecutarAsync(string entradaUsuario)
        {
            string knowledgeOperationId = Guid.NewGuid().ToString("N");
            DateTime knowledgeStartedAt = DateTime.UtcNow;
            bool knowledgeTimeoutOccurred = false;
            bool knowledgeFallbackUsed = false;
            bool knowledgeConversionFailed = false;
            bool knowledgeFailedStatus = false;
            bool knowledgeCancelledStatus = false;
            string knowledgeExceptionType = string.Empty;
            string knowledgeFallbackReason = string.Empty;
            string knowledgeStatus = string.Empty;
            string knowledgePipelineStage = string.Empty;
            string projectRootForContext = _projectProvider.GetActiveProjectRoot();
            ProjectContextSnapshot projectContextSnapshot = null;
            if (!string.IsNullOrWhiteSpace(projectRootForContext) && Directory.Exists(projectRootForContext))
            {
                projectContextSnapshot = _projectContextProvider.Build(
                    projectRootForContext,
                    _contextBuilder,
                    ReportStatus,
                    _log,
                    knowledgeOperationId);
            }
            AdditionalContext additionalContext = null;
            if (_knowledgeGateway != null && KnowledgeEngineMode != KnowledgeEngineMode.Disabled)
            {
                // Identidade estavel do projeto (Projeto.Id persistido). Quando nao ha
                // identidade confiavel, a Session permanece sem Identifier e nenhum
                // binding e usado (fail-closed: sem associacao cruzada entre projetos).
                string activeProjectId = null;
                try
                {
                    activeProjectId = _projectProvider.GetActiveProjectId();
                }
                catch (Exception)
                {
                    activeProjectId = null;
                }

                string normalizedProjectId = null;
                KnowledgeIdentifier sessionIdentifier = null;
                IReadOnlyList<KnowledgeIdentifier> boundPackIds = null;
                if (ProjectKnowledgeBindingContract.TryNormalizeProjectId(activeProjectId, out normalizedProjectId))
                {
                    sessionIdentifier = new KnowledgeIdentifier(normalizedProjectId);
                    boundPackIds = LoadBoundPackIds(normalizedProjectId);
                }

                var shadowDemand = new KnowledgeDemand
                {
                    Objective = entradaUsuario,
                    Context = projectContextSnapshot == null ? string.Empty : projectContextSnapshot.Context
                };
                var shadowSession = new SessionContract
                {
                    Identifier = sessionIdentifier,
                    Demand = shadowDemand,
                    Context = projectContextSnapshot == null ? string.Empty : projectContextSnapshot.Context,
                    KnowledgePackIds = boundPackIds
                };
                var shadowQuery = new QueryContract
                {
                    Demand = shadowDemand
                };
                KnowledgeGatewayResult shadowResult = null;
                try
                {
                    Task<KnowledgeGatewayResult> gatewayTask = Task.Run(() => _knowledgeGateway.Execute(
                        shadowDemand,
                        shadowSession,
                        shadowQuery));
                    int timeoutMilliseconds = KnowledgeGatewayTimeoutMilliseconds > 0
                        ? KnowledgeGatewayTimeoutMilliseconds
                        : 5000;

                    if (gatewayTask.Wait(timeoutMilliseconds))
                    {
                        shadowResult = gatewayTask.GetAwaiter().GetResult();
                        knowledgeStatus = shadowResult == null ? string.Empty : shadowResult.Status ?? string.Empty;
                        knowledgePipelineStage = shadowResult == null ? string.Empty : shadowResult.PipelineStage ?? string.Empty;
                        knowledgeFailedStatus = string.Equals(knowledgeStatus, "Failed", StringComparison.OrdinalIgnoreCase);
                        knowledgeCancelledStatus = string.Equals(knowledgeStatus, "Cancelled", StringComparison.OrdinalIgnoreCase);
                        if (knowledgeFailedStatus)
                        {
                            knowledgeFallbackUsed = true;
                            knowledgeFallbackReason = "FailedStatus";
                        }
                        else if (knowledgeCancelledStatus)
                        {
                            knowledgeFallbackUsed = true;
                            knowledgeFallbackReason = "CancelledStatus";
                        }

                        if (KnowledgeEngineMode == KnowledgeEngineMode.Assist ||
                            KnowledgeEngineMode == KnowledgeEngineMode.Enabled)
                        {
                    additionalContext = KnowledgeGatewayResultToAdditionalContextConverter.Convert(
                        shadowResult,
                        shadowDemand,
                        projectContextSnapshot == null ? null : projectContextSnapshot.Context);
                        }
                    }
                    else
                    {
                        knowledgeTimeoutOccurred = true;
                        knowledgeFallbackUsed = true;
                        knowledgeFallbackReason = "Timeout";
                    }
                }
                catch (Exception ex)
                {
                    knowledgeConversionFailed = shadowResult != null &&
                        (KnowledgeEngineMode == KnowledgeEngineMode.Assist ||
                         KnowledgeEngineMode == KnowledgeEngineMode.Enabled);
                    if (knowledgeConversionFailed)
                        knowledgeFallbackReason = "ConversionFailed";
                    else if (knowledgeTimeoutOccurred)
                        knowledgeFallbackReason = "Timeout";
                    else
                        knowledgeFallbackReason = "GatewayException";

                    knowledgeFallbackUsed = true;
                    knowledgeExceptionType = ex.GetType().FullName ?? ex.GetType().Name;
                    additionalContext = null;
                }
            }

            DateTime knowledgeFinishedAt = DateTime.UtcNow;
            try
            {
                _log.Info(
                    "[KNOWLEDGE-ENGINE] " +
                    "OperationId=" + knowledgeOperationId +
                    ";Mode=" + KnowledgeEngineMode +
                    ";StartedAtUtc=" + knowledgeStartedAt.ToString("o") +
                    ";FinishedAtUtc=" + knowledgeFinishedAt.ToString("o") +
                    ";DurationMs=" + (knowledgeFinishedAt - knowledgeStartedAt).TotalMilliseconds.ToString("0") +
                    ";TimeoutOccurred=" + knowledgeTimeoutOccurred +
                    ";ExceptionType=" + knowledgeExceptionType +
                    ";FallbackUsed=" + knowledgeFallbackUsed +
                    ";FallbackReason=" + knowledgeFallbackReason +
                    ";ConversionFailed=" + knowledgeConversionFailed +
                    ";FailedStatus=" + knowledgeFailedStatus +
                    ";CancelledStatus=" + knowledgeCancelledStatus +
                    ";Status=" + knowledgeStatus +
                    ";PipelineStage=" + knowledgePipelineStage);
            }
            catch (Exception)
            {
            }

            RequestExecutionEngine.ProgrammerQueryResult queryResult =
                await _requestExecutionEngine.PrepararEConsultarProgrammerAsync(
                    entradaUsuario,
                    _projectProvider,
                    _contextBuilder,
                    _loadPrompt,
                    UseComplementPrompt,
                    ComplementPromptPath,
                    LanguageProfile,
                    LanguageProfileId,
                    ReportStatus,
                    RegistrarInicioProcessamento,
                    additionalContext,
                    projectContextSnapshot).ConfigureAwait(false);

            string projectRoot = queryResult.ProjectRoot;
            string projectId = queryResult.ProjectId;
            string projectName = queryResult.ProjectName;
            string promptBase = queryResult.PromptBase;
            string contextoProjeto = queryResult.ContextoProjeto;
            string systemPrompt = queryResult.SystemPrompt;
            string finalPrompt = queryResult.FinalPrompt;
            string respostaIA = queryResult.RespostaIA;

            bool semAcoesExecutaveis;
            int totalAcoes;
            AgentResponse structuredResponse = _requestExecutionEngine.InterpretarRespostaIA(
                respostaIA,
                out totalAcoes,
                out semAcoesExecutaveis,
                UseGitDiff,
                RespostaSemAcoesContemProtocoloExecutavelForte,
                RespostaSemAcoesContemTermosTecnicosGenericos,
                ExtrairSinaisTecnicosFortes,
                LogRespostaBrutaDiagnostico,
                RespostaSemAcoesEhConclusaoSemAlteracao,
                ReportStatus);
            ActionParseResult resultadoClassificacaoParser = ActionParserPipeline.Parse(structuredResponse, respostaIA);
            _requestExecutionEngine.RegistrarResumoFinalParserAct(resultadoClassificacaoParser);

            if (semAcoesExecutaveis)
            {
                if (resultadoClassificacaoParser.Outcome == ActionParseOutcome.Executable &&
                    resultadoClassificacaoParser.CanonicalExecutableCount > 0)
                {
                    _log.Info("[PARSER-PIPELINE] Resultado: aÃ§Ãµes canÃ´nicas recuperadas para execuÃ§Ã£o.");
                    semAcoesExecutaveis = false;
                }
                else if (resultadoClassificacaoParser.Outcome == ActionParseOutcome.GitDiffDetected)
                {
                    _log.Info("[GIT-DIFF] Resposta sem acoes JSON, mas GitDiff detectado.");
                    // O prÃ©-dispatch farÃ¡ parse, safety, conversÃ£o e criaÃ§Ã£o das aÃ§Ãµes canÃ´nicas.
                    semAcoesExecutaveis = false;
                }
                else if (resultadoClassificacaoParser.Outcome == ActionParseOutcome.LegitimateNoAction &&
                         resultadoClassificacaoParser.Outcome != ActionParseOutcome.GitDiffDetected)
                {
                    _log.Info("[PARSER-PIPELINE] Resultado sem aÃ§Ã£o legÃ­tima: " + resultadoClassificacaoParser.GetUserFriendlyErrorMessage());
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

                    var retryResult = await _requestExecutionEngine.TentarForcarAcoesQuandoRespostaSemAcoesAsync(
                        systemPrompt,
                        promptBase,
                        contextoProjeto,
                        entradaUsuario,
                        respostaIA,
                        structuredResponse,
                        null,
                        TruncateForPrompt,
                        UseGitDiff,
                        RespostaSemAcoesContemProtocoloExecutavelForte,
                        RespostaSemAcoesContemTermosTecnicosGenericos,
                        ExtrairSinaisTecnicosFortes,
                        LogRespostaBrutaDiagnostico,
                        RespostaSemAcoesEhConclusaoSemAlteracao,
                        ReportStatus).ConfigureAwait(false);

                    if (retryResult == null)
                    {
                        retryResult = await _requestExecutionEngine.TentarReprocessarRespostaSemAcoesAsync(
                            projectRoot,
                            systemPrompt,
                            promptBase,
                            contextoProjeto,
                            entradaUsuario,
                            respostaIA,
                            structuredResponse,
                            null,
                            UseGitDiff,
                            RespostaSemAcoesContemProtocoloExecutavelForte,
                            RespostaSemAcoesContemTermosTecnicosGenericos,
                            ExtrairSinaisTecnicosFortes,
                            LogRespostaBrutaDiagnostico,
                            RespostaSemAcoesEhConclusaoSemAlteracao,
                            ReportStatus).ConfigureAwait(false);
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

            AgentResult result = _requestExecutionEngine.CriarAgentResult(
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
            RequestExecutionEngine.AnalystQueryResult queryResult =
                await _requestExecutionEngine.PrepararEConsultarAnalystAsync(
                    entradaUsuario,
                    _projectProvider,
                    _contextBuilder,
                    KnowledgeIndexPath,
                    AnalystPromptPath,
                    NivelMaximoDificuldade,
                    NivelMaximoIa,
                    ReportStatus,
                    RegistrarInicioAnalista,
                    DeveUsarRequisicaoMinimaLocalParaAnalista,
                    CriarRespostaMinimaAnalistaLocal,
                    ConsultarAnalistaComValidacaoAsync).ConfigureAwait(false);

            TaskDemandRequest demanda = queryResult.Demanda;
            string projectRoot = queryResult.ProjectRoot;
            string projectId = queryResult.ProjectId;
            string projectName = queryResult.ProjectName;
            string contextoProjeto = queryResult.ContextoProjeto;
            int nivelEfetivo = queryResult.NivelEfetivo;
            string finalPrompt = queryResult.FinalPrompt;
            string respostaIA = queryResult.RespostaIA;

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
            _log.Info("IA selecionada: " + (AiProviderName ?? _analystModelDisplayName));
            _log.Info("ProviderType: " + _analystModelClient.ProviderName);
            _log.Info("Modelo: " + _analystModelClient.ModelName);
            _log.Info("Projeto ativo: " + projectRoot);
            _log.Info("Nivel configurado na IA: " + NivelMaximoDificuldade);
            _log.Info("Nivel maximo suportado pela IA: " + NivelMaximoIa);
        }

        private async Task<string> ConsultarAnalistaComValidacaoAsync(string prompt, int nivelEfetivo, TaskDemandRequest demanda)
        {
            string resposta = null;
            string promptAtual = prompt;

            for (int tentativa = 1; tentativa <= 2; tentativa++)
            {
                try
                {
                    resposta = await _requestExecutionEngine.AskAnalystModelWithDiagnosticsAsync(
                        tentativa == 1 ? "analista prompt principal" : "analista retry formato",
                        promptAtual,
                        tentativa == 1 ? 180000 : 120000).ConfigureAwait(false);
                }
                catch (Exception ex) when (RequestExecutionEngine.IsTemporaryAnalystFailure(ex))
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

        private void RegistrarFallbackAnalistaTemporario(Exception ex)
        {
            string providerNome = _analystModelClient == null ? "Kimi / OpenAI CompatÃ­vel" : _analystModelClient.ProviderName;
            string modelo = _analystModelClient == null ? string.Empty : _analystModelClient.ModelName;
            int? statusCode = RequestExecutionEngine.TryGetIntProperty(ex, "StatusCode");
            string etapa = RequestExecutionEngine.TryGetStringProperty(ex, "Stage");

            _log.Error("[ERRO] Analista falhou por erro temporÃ¡rio; requisicao minima gerada localmente.");
            _log.Info("[INFO] Motivo fallback analista: " + (ex == null ? string.Empty : ex.Message));
            _log.Info("[INFO] Etapa fallback analista: " + (string.IsNullOrWhiteSpace(etapa) ? "analista prompt principal" : etapa));
            _log.Info("[INFO] Status HTTP fallback analista: " + (statusCode.HasValue ? statusCode.Value.ToString() : "indisponivel"));
            _log.Info("[INFO] Provider fallback analista: " + providerNome);
            _log.Info("[INFO] Modelo fallback analista: " + modelo);
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

        private void RegistrarInicioProcessamento(string projectRoot)
        {
            _log.Info("[INFO] VersÃ£o programa: " + GetMainProgramVersion());
            _log.Info("[INFO] VersÃ£o DLL: " + GetDllVersion());
            _log.Info("[INFO] FeatureMarker: " + FeatureMarker);
        }

        private int ObterLimitePrompt()
        {
            if (MaxPromptChars < 12000)
                return 12000;
            if (MaxPromptChars > 200000)
                return 200000;
            return MaxPromptChars;
        }

        private void RegistrarDiagnosticoGitDiff(string rawResponse, ActionParseResult parseResult, GitDiffParseResult gitDiff = null, GitDiffSafetyResult safety = null)
        {
            if (parseResult == null)
                return;

            string gitDiffText = parseResult.GitDiffEffectiveText;
            if (gitDiff == null)
                gitDiff = _requestExecutionEngine.ParseGitDiff(rawResponse, parseResult);
            if (safety == null)
                safety = _requestExecutionEngine.ValidarGitDiffSeguranca(gitDiff, _projectProvider == null ? string.Empty : _projectProvider.GetActiveProjectRoot());

            _log.Info("[GIT-DIFF] Chars GitDiff efetivo: " + (string.IsNullOrWhiteSpace(gitDiffText) ? 0 : gitDiffText.Length));
            _log.Info("[GIT-DIFF] GitDiff efetivo contÃ©m header diff --git: " + (!string.IsNullOrWhiteSpace(gitDiffText) && gitDiffText.IndexOf("diff --git", StringComparison.OrdinalIgnoreCase) >= 0).ToString().ToLowerInvariant());
            _log.Info("[GIT-DIFF] GitDiff efetivo contÃ©m ---: " + (!string.IsNullOrWhiteSpace(gitDiffText) && gitDiffText.IndexOf("\n--- ", StringComparison.OrdinalIgnoreCase) >= 0).ToString().ToLowerInvariant());
            _log.Info("[GIT-DIFF] GitDiff efetivo contÃ©m +++: " + (!string.IsNullOrWhiteSpace(gitDiffText) && gitDiffText.IndexOf("\n+++ ", StringComparison.OrdinalIgnoreCase) >= 0).ToString().ToLowerInvariant());
            _log.Info("[GIT-DIFF] GitDiff efetivo contÃ©m @@: " + (!string.IsNullOrWhiteSpace(gitDiffText) && gitDiffText.IndexOf("@@", StringComparison.OrdinalIgnoreCase) >= 0).ToString().ToLowerInvariant());
            _log.Info("[GIT-DIFF] Adapter parse iniciado.");
            if (gitDiff.HadEmptyHunks)
                _log.Info("[GIT-DIFF] Hunk vazio detectado.");
            if (gitDiff.IgnoredTrailingEmptyHunks)
                _log.Info("[GIT-DIFF] Hunk vazio final ignorado com seguranÃ§a.");
            if (gitDiff.ContainsOnlyEmptyHunks)
                _log.Info("[GIT-DIFF] Diff contÃ©m apenas hunks vazios.");
            if (gitDiff.HasEmptyHunkInMiddle)
                _log.Error("[GIT-DIFF] ConversÃ£o bloqueada: hunk vazio no meio do diff.");
            _log.Info("[GIT-DIFF] Arquivos parseados: " + gitDiff.Files.Count);
            _log.Info("[GIT-DIFF] Hunks parseados: " + gitDiff.TotalHunks);
            _log.Info("[GIT-DIFF] ValidaÃ§Ã£o de seguranÃ§a iniciada.");
            _log.Info("[GIT-DIFF] SeguranÃ§a: safe=" + safety.IsSafe.ToString().ToLowerInvariant());
            _log.Info("[GIT-DIFF] Arquivos validados: " + safety.FileCount);
            _log.Info("[GIT-DIFF] Hunks validados: " + safety.HunkCount);
            _log.Info("[GIT-DIFF] Erros de seguranÃ§a: " + safety.Errors.Count);
            _log.Info("[GIT-DIFF] Warnings de seguranÃ§a: " + safety.Warnings.Count);

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
                    _log.Info("[GIT-DIFF] Warning de seguranÃ§a: " + warning);
            }

            if (safety.Errors != null)
            {
                foreach (string error in safety.Errors.Where(e => !string.IsNullOrWhiteSpace(e)))
                    _log.Error("[GIT-DIFF] Bloqueado: " + error);
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

            _log.Info("[DIAG] Resposta IA bruta parcial inÃ­cio:\n" + respostaMascarada.Substring(0, 4000));
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
                "funÃƒÂ§ÃƒÂ£o",
                "funcao",
                "versÃƒÂ£o",
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
            await _requestExecutionEngine.PrepararEExecutarFachadaDispatchAsync(
                result,
                historyEntry,
                structuredResponse,
                projectRoot,
                _toolDispatcher != null,
                UseGitDiff,
                RegistrarErroBrutoDaIAAtual,
                RegistrarDiagnosticoGitDiff,
                (parseResult, respostaIA) => TentarRecuperarKimiPreDispatch(parseResult, respostaIA, projectRoot),
                ToAgentAction,
                RespostaSemAcoesContemProtocoloExecutavelForte,
                (responseParaExecucao, decision) => ExecutarAcoesComDispatcherAsync(
                    result, historyEntry, responseParaExecucao, projectRoot, systemPrompt, promptBase, entradaUsuario, decision),
                () => RegistrarHistorico(result, historyEntry, projectRoot)).ConfigureAwait(false);
        }
        private RequestExecutionEngine.PreDispatchExecutionDecision TentarRecuperarKimiPreDispatch(
            ActionParseResult parseResult,
            string respostaIA,
            string projectRoot)
        {
            if (KimiActionDialectRecovery.TryRecoverDeleteOnly(
                    respostaIA,
                    projectRoot,
                    message => _log.Info(message),
                    out KimiDialectRecoveryResult kimiRecovery))
                {
                    _log.Info("[KIMI-RECOVERY] Encaminhando aÃ§Ã£o recuperada pelo pipeline seguro.");
                    return new RequestExecutionEngine.PreDispatchExecutionDecision
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
                        ? "Resposta Kimi sem GitDiff nÃ£o pÃ´de ser recuperada."
                        : kimiRecovery.ErrorMessage;
                    RegistrarErroBrutoDaIAAtual(parseResult, respostaIA);
                    _log.Error("[KIMI-RECOVERY] RecuperaÃ§Ã£o bloqueada: " + motivo);
                    throw new InvalidOperationException(motivo);
                }

            return null;
        }

        private async Task ExecutarAcoesComDispatcherAsync(
            AgentResult result,
            ProjectHistoryEntry historyEntry,
            AgentResponse structuredResponse,
            string projectRoot,
            string systemPrompt,
            string promptBase,
            string entradaUsuario,
            RequestExecutionEngine.PreDispatchExecutionDecision decision)
        {
            ReportStatus("Executando acoes propostas");

            ToolDispatchResult dispatchResult = null;
            bool recovered = false;
            AgentResponse responseParaExecucao = decision?.ResponseParaExecucao ?? structuredResponse;

            try
            {
                if (decision != null && decision.UsouCanonico)
                    _log.Info("[PARSER-PIPELINE] Enviando aÃ§Ã£o canÃ´nica ao ToolDispatcher.");

                if (decision != null && decision.UsouCanonico)
                    _log.Info("[PARSER-PIPELINE] ExecuÃ§Ã£o canÃ´nica confirmada para dispatcher.");

                int acoesParaDispatcher = responseParaExecucao?.Acoes == null ? 0 : responseParaExecucao.Acoes.Count;
                _log.Info("[PARSER-PIPELINE] AÃ§Ãµes enviadas ao ToolDispatcher: " + acoesParaDispatcher);

                if (acoesParaDispatcher == 0)
                {
                    if (decision?.ParseResult != null && decision.ParseResult.Outcome == ActionParseOutcome.LegitimateNoAction)
                    {
                        _log.Info("[PARSER-PIPELINE] ExecuÃ§Ã£o encerrada sem dispatcher: nenhuma alteraÃ§Ã£o necessÃ¡ria.");
                        return;
                    }

                    string mensagemBloqueio = decision?.ParseResult?.GetUserFriendlyErrorMessage() ?? "AÃ§Ã£o invÃ¡lida recebida pelo dispatcher. O ParserAct deveria ter bloqueado este formato.";
                    throw new InvalidOperationException(mensagemBloqueio);
                }

                _requestExecutionEngine.ValidarAcoesArquivoLocalAntesDoDispatcher(responseParaExecucao);
                dispatchResult = _toolDispatcher.Dispatch(responseParaExecucao, projectRoot);
                result.ToolDispatchQueued = true;

                RequestExecutionEngine.DispatchResultClassification dispatchClassification =
                    _requestExecutionEngine.ClassificarResultadoDispatch(dispatchResult);
                RequestExecutionEngine.DispatchHandlingDecision dispatchHandling =
                    _requestExecutionEngine.DecidirTratamentoClassificacaoDispatch(dispatchClassification);
                if (dispatchHandling != RequestExecutionEngine.DispatchHandlingDecision.ContinuarConclusaoParcial)
                {
                    if (dispatchHandling == RequestExecutionEngine.DispatchHandlingDecision.EncerrarFalhaVerificacao)
                    {
                        _log.Error(dispatchResult.Error);
                        _log.Info("[INFO] Reprocessamento automÃƒÂ¡tico ignorado por falha de verificaÃƒÂ§ÃƒÂ£o.");
                        result.ToolDispatchError = dispatchResult.Error;
                        historyEntry.Error = dispatchResult.Error;
                        return;
                    }

                    recovered = await _requestExecutionEngine.TentarCorrigirFalhaExecucaoAsync(
                        result,
                        historyEntry,
                        dispatchResult,
                        projectRoot,
                        systemPrompt,
                        promptBase,
                        entradaUsuario,
                        _contextBuilder,
                        ReportStatus,
                        UseGitDiff,
                        RespostaSemAcoesContemProtocoloExecutavelForte,
                        RespostaSemAcoesContemTermosTecnicosGenericos,
                        ExtrairSinaisTecnicosFortes,
                        LogRespostaBrutaDiagnostico,
                        RespostaSemAcoesEhConclusaoSemAlteracao,
                        TruncateForPrompt,
                        RedactSensitiveText,
                        ObterLimitePrompt,
                        (response, root) => _toolDispatcher.Dispatch(response, root),
                        SummarizeDispatchError).ConfigureAwait(false);

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
                if (string.Equals(ex.GetType().Name, "ProviderRateLimitException", StringComparison.Ordinal))
                    _log.Error("[AGENTCORE] ProviderRateLimitException saindo do AgentCore");
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
                Descricao = string.IsNullOrWhiteSpace(parsed.Description) ? "AÃ§Ã£o canÃ´nica recuperada" : parsed.Description,
                Dados = (JObject)dados.DeepClone(),
                RequerConfirmacao = false
            };
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

            return RequestExecutionEngine.TruncateForLog(firstLine, 500);
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
            if (!_requestExecutionEngine.RespostaIndicaConclusaoParcial(ultimaResposta))
                return false;

            bool executouContinuacao = false;

            for (int tentativa = 1; tentativa <= 3; tentativa++)
            {
                ReportStatus("Continuando etapas pendentes");
                _log.Info("Resposta indica conclusao parcial. Solicitando continuacao automatica. Tentativa " + tentativa + "/3.");

                RequestExecutionEngine.RetrySemAcoesResult technicalContinuation = await _requestExecutionEngine.ExecutarContinuacaoParcialTecnicaAsync(
                    systemPrompt,
                    promptBase,
                    entradaUsuario,
                    projectRoot,
                    _contextBuilder,
                    ReportStatus,
                    UseGitDiff,
                    RespostaSemAcoesContemProtocoloExecutavelForte,
                    RespostaSemAcoesContemTermosTecnicosGenericos,
                    ExtrairSinaisTecnicosFortes,
                    LogRespostaBrutaDiagnostico,
                    RespostaSemAcoesEhConclusaoSemAlteracao).ConfigureAwait(false);

                string contextoAtual = technicalContinuation.ContextoProjeto;
                string promptContinuacao = technicalContinuation.FinalPrompt;
                string respostaContinuacao = technicalContinuation.RespostaIA;
                string entradaContinuacao = technicalContinuation.EntradaUsuario;
                AgentResponse responseContinuacao = technicalContinuation.StructuredResponse;
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

                    bool recovered = await _requestExecutionEngine.TentarCorrigirFalhaExecucaoAsync(
                        result,
                        historyEntry,
                        dispatchContinuacao,
                        projectRoot,
                        systemPrompt,
                        promptBase,
                        entradaContinuacao,
                        _contextBuilder,
                        ReportStatus,
                        UseGitDiff,
                        RespostaSemAcoesContemProtocoloExecutavelForte,
                        RespostaSemAcoesContemTermosTecnicosGenericos,
                        ExtrairSinaisTecnicosFortes,
                        LogRespostaBrutaDiagnostico,
                        RespostaSemAcoesEhConclusaoSemAlteracao,
                        TruncateForPrompt,
                        RedactSensitiveText,
                        ObterLimitePrompt,
                        (response, root) => _toolDispatcher.Dispatch(response, root),
                        SummarizeDispatchError).ConfigureAwait(false);

                    if (!recovered)
                    {
                        result.ToolDispatchError = dispatchContinuacao.Error;
                        historyEntry.Error = dispatchContinuacao.Error;
                        throw new InvalidOperationException("Falha ao executar continuacao automatica: " + SummarizeDispatchError(dispatchContinuacao.Error));
                    }

                    executouContinuacao = true;
                    return true;
                }

                executouContinuacao = _requestExecutionEngine.AplicarEfeitosColateraisContinuacaoParcial(
                    result,
                    historyEntry,
                    dispatchContinuacao,
                    responseContinuacao,
                    respostaContinuacao,
                    contextoAtual,
                    promptContinuacao);


                if (!_requestExecutionEngine.RespostaIndicaConclusaoParcial(responseContinuacao))
                    return true;

                ultimaResposta = responseContinuacao;
            }

            _log.Error("Continuacao automatica atingiu o limite de 3 ciclos ainda indicando etapa pendente.");
            return executouContinuacao;
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
    }
}
