using GptBolDll;
using GptBolDll.Configuration;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace atcIA
{
    public partial class Main : Form
    {
        private INI cINI;
        private AgentCore agentCore;
        private ToolDispatcher toolDispatcherPrincipal;
        private Timer clipboardTimer;
        private string previousClipboardText = "";
        private bool carregando = false;
        private bool MemInicializada = false;
        private bool carregandoProjetos = false;
        private AgentResult ultimoResultado;
        private TokenCounter tokenCounter;
        private readonly string executionLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dados", "execucao.log");
        private readonly string dllExecutionLogPath = Path.Combine(Path.GetDirectoryName(typeof(AgentCore).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory, "dll-execucao.log");
        private ExecutionLogger dllExecutionLogger;
        private string aiAtualDescricao = string.Empty;
        private string aiAtualModelo = string.Empty;
        private AiProviderConfig aiProviderAtual;
        private Timer statusTimer;
        private DateTime processamentoInicio;
        private string statusProcessamento = "aguardando";
        private string ultimoStatusRegistrado = string.Empty;
        private TimeSpan duracaoUltimoProcessamento = TimeSpan.Zero;
        private bool processamentoAtivo;
        private bool logTecnicoVisivel;
        private LanguageProfile languageProfileAtual = LanguageProfile.Geral;
        private string languageProfileIdAtual = "geral";
        private string papelProcessamentoAtual = string.Empty;
        private bool processamentoComplementoAtual;

        private void button1_Click(object sender, EventArgs e)
        {
            buttonStatus.Visible = true;
            buttonStatus.Refresh();
            string projectRoot = ObterPastaProjetoAtiva();
            var projetoAtivo = ObterProjetoAtivo();
            var provider = AiProviderRepository.BuscarAtivoOuPadrao(projetoAtivo == null ? AiProviderConfig.DefaultProviderId : projetoAtivo.AiProviderId);
            string apiKey = ObterApiKeyParaProvider(provider);

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show(
                    "A chave da API Gemini nÃ£o estÃ¡ configurada.\n\nConfigure em: Menu â†’ ConfiguraÃ§Ãµes.",
                    "Chave nÃ£o encontrada",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            {
                MessageBox.Show(
                    "A pasta do projeto nÃ£o estÃ¡ configurada ou nÃ£o existe.\n\nConfigure em: Menu â†’ ConfiguraÃ§Ãµes.",
                    "Projeto nÃ£o encontrado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            AtualizarLanguageProfileDoProjetoAtivo();
            PrepararAgentCoreParaProjetoAtivo();

            AcionaProcessamento();
        }

        public Main()
        {
            InitializeComponent();
            carregando = true;
            cINI = new INI();
            tokenCounter = new TokenCounter();
            statusTimer = new Timer();
            statusTimer.Interval = 1000;
            statusTimer.Tick += StatusTimer_Tick;

            PrepararLogTecnico();
            aiProviderAtual = AiProviderRepository.BuscarAtivoOuPadrao(AiProviderConfig.DefaultProviderId);
            aiAtualDescricao = aiProviderAtual.Nome;
            aiAtualModelo = aiProviderAtual.ModelName;
            dllExecutionLogger = new ExecutionLogger(message =>
            {
                if (textBoxLog == null)
                    return;

                if (textBoxLog.InvokeRequired)
                {
                    textBoxLog.BeginInvoke(new Action<string>(m =>
                    {
                        if (textBoxLog != null && !textBoxLog.IsDisposed)
                            textBoxLog.AppendText(m + Environment.NewLine);
                    }), message);
                    return;
                }

                textBoxLog.AppendText(message + Environment.NewLine);
            }, dllExecutionLogPath);
            toolDispatcherPrincipal = new ToolDispatcher(dllExecutionLogger, ConfirmarAcao, ObterFtpProjetoAtivo);
            toolDispatcherPrincipal.AutoVerificationEnabled = false;
            agentCore = CriarAgentCoreParaProjetoAtual(toolDispatcherPrincipal);
            logTecnicoVisivel = ConfigManager.GetLogVisible();
            AtualizarTokensNaTela();
            AtualizarStatusStrip();
            if (buttonStatus != null)
                buttonStatus.Visible = false;
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            this.Text = $"atcIa - VersÃ£o {version}";
            CarregarProjetos();
            AtualizarStatusPerfilLinguagem();
            carregando = false;
        }

        private void CarregarProjetos()
        {
            carregandoProjetos = true;

            var projetos = ProjetoRepository.Listar();

            cbProjetos.DataSource = null;
            cbProjetos.DisplayMember = "Nome";
            cbProjetos.ValueMember = "Id";
            cbProjetos.DataSource = projetos;

            carregandoProjetos = false;

            if (projetos.Count == 0)
            {
                ConfigManager.SaveLastProjectId("");
                AtualizarLanguageProfileDoProjetoAtivo();
                AtualizarAiAtualDoProjetoAtivo();
                AtualizarStatusPerfilLinguagem();
                return;
            }

            string lastId = ConfigManager.GetLastProjectId();

            if (!string.IsNullOrWhiteSpace(lastId) && Guid.TryParse(lastId, out Guid id))
            {
                SelecionarProjeto(id);
            }
            else
            {
                SelecionarProjeto(projetos[0].Id);
            }
        }

        private void SelecionarProjeto(Guid id)
        {
            if (cbProjetos.DataSource == null)
                return;

            var projetos = cbProjetos.DataSource as List<Projeto>;

            if (projetos == null)
                return;

            var projeto = projetos.FirstOrDefault(p => p.Id == id);

            if (projeto == null)
                return;

            cbProjetos.SelectedItem = projeto;
            ConfigManager.SaveLastProjectId(projeto.Id.ToString());
            AtualizarLanguageProfileDoProjetoAtivo();
            AtualizarAiAtualDoProjetoAtivo();
            AtualizarStatusPerfilLinguagem();
            RecarregarInstrucaoPendente(projeto, true);
        }

        private void ClipboardTimer_Tick(object sender, EventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                string currentClipboardText = Clipboard.GetText();
                if (currentClipboardText != previousClipboardText && currentClipboardText.StartsWith("<Gptbol>"))
                {
                    textBox1.Text = currentClipboardText;
                    previousClipboardText = currentClipboardText;
                    AcionaProcessamento();
                    Clipboard.Clear();
                }
            }
        }

        private async void AcionaProcessamento()
        {
            await AcionaProcessamentoAsync(textBox1.Text, true, false, false);
        }

        private async void buttonContinuar_Click(object sender, EventArgs e)
        {
            string ajuste = textBoxContinuacao == null ? string.Empty : textBoxContinuacao.Text;
            if (string.IsNullOrWhiteSpace(ajuste))
            {
                MessageBox.Show("Informe o ajuste de continuação.", "Continuação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var projetoAtivo = ObterProjetoAtivo();
            string entradaContinuacao = MontarEntradaContinuacao(ajuste, projetoAtivo);
            await AcionaProcessamentoAsync(entradaContinuacao, true, true, true);
        }

        private async System.Threading.Tasks.Task AcionaProcessamentoAsync(string entradaUsuario, bool salvarPendente, bool limparContinuacaoAoConcluir, bool isComplementoOperacao)
        {
            processamentoComplementoAtual = isComplementoOperacao;
            AtualizarLanguageProfileDoProjetoAtivo();
            PrepararAgentCoreParaProjetoAtivo();

            var projetoAtivo = ObterProjetoAtivo();
            if (projetoAtivo != null && salvarPendente && !string.IsNullOrWhiteSpace(entradaUsuario))
                PendingInstructionRepository.Save(projetoAtivo.Id, entradaUsuario);

            bool processamentoConcluido = false;
            textBoxLog.Clear();
            PrepararLogTecnico();
            dllExecutionLogger?.Clear();
            IniciarIndicadorProcessamento();

            try
            {
                papelProcessamentoAtual = "Analista";
                AtualizarStatusProcessamento("Iniciando analise da requisicao");

                var requisicao = await agentCore.ExecutarGeracaoRequisicaoAsync(entradaUsuario);
                ultimoResultado = requisicao;

                ExibirRespostaIA(requisicao);
                ExibirEtapasAnalista(requisicao);
                ExibirAcoesPropostas(requisicao);
                RegistrarTokens(requisicao);

                AtualizarStatusProcessamento("Requisicao de tarefa gerada");

                string entradaProgramador = CriarEntradaProgramador(entradaUsuario, requisicao, projetoAtivo);

                papelProcessamentoAtual = "Programador";
                AtualizarStatusProcessamento("Executando etapas como programador");

                var resultado = await agentCore.ExecutarAsync(entradaProgramador);
                ultimoResultado = resultado;

                ExibirRespostaIA(resultado);
                ExibirAcoesPropostas(resultado);
                RegistrarTokens(resultado);

                int totalAcoesPropostas = resultado?.StructuredResponse?.Acoes?.Count ?? 0;
                int totalAcoesExecutadas = resultado?.ExecutedActions?.Count ?? 0;
                int totalAcoes = Math.Max(totalAcoesPropostas, totalAcoesExecutadas);

                if (!string.IsNullOrWhiteSpace(resultado?.ToolDispatchError))
                {
                    AtualizarStatusProcessamento("Erro ao executar acoes");
                }
                else if (totalAcoes == 0)
                {
                    bool conclusaoSemAlteracao =
                        resultado?.StructuredResponse != null &&
                        !resultado.StructuredResponse.RequerConfirmacao &&
                        (!string.IsNullOrWhiteSpace(resultado.StructuredResponse.MensagemUsuario) ||
                         !string.IsNullOrWhiteSpace(resultado.StructuredResponse.Explicacao));

                    bool resultadoGitDiff = string.Equals(resultado?.ParserOutcome, "GitDiffDetected", StringComparison.OrdinalIgnoreCase);

                    if (resultadoGitDiff)
                    {
                        AtualizarStatusProcessamento("GitDiff detectado");
                        processamentoConcluido = true;
                    }
                    else if (conclusaoSemAlteracao)
                    {
                        AtualizarStatusProcessamento("IA concluiu que nenhuma alteracao e necessaria");
                        processamentoConcluido = true;
                    }
                    else
                    {
                        AtualizarStatusProcessamento("Erro: programador nao retornou acoes");
                    }
                }
                else
                {
                    processamentoConcluido = true;

                    if (projetoAtivo != null)
                    {
                        PendingInstructionRepository.Clear(projetoAtivo.Id);
                        IncrementarVersaoProjetoDestinoSeNecessario(projetoAtivo);
                    }

                    if (limparContinuacaoAoConcluir && textBoxContinuacao != null)
                        textBoxContinuacao.Clear();

                    papelProcessamentoAtual = string.Empty;
                    AtualizarStatusProcessamento("Processamento concluido");
                }
            }
            catch (Exception ex)
            {
                string mensagemUsuario = FormatarMensagemErroUsuario(ex, papelProcessamentoAtual);
                mensagemUsuario = AjustarMensagemQuotaOpenAIComResultado(mensagemUsuario);

                papelProcessamentoAtual = string.Empty;
                AtualizarStatusProcessamento("Erro durante processamento");

                textBoxLog.AppendText("[ERRO] " + ex.Message + Environment.NewLine);
                RegistrarExcecaoDetalhada(ex);

                if (!string.Equals(mensagemUsuario, ex.Message, StringComparison.Ordinal))
                    textBoxLog.AppendText("[ERRO-USUARIO] " + mensagemUsuario + Environment.NewLine);

                MessageBox.Show(mensagemUsuario, "Erro no processamento", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                FinalizarIndicadorProcessamento(
                    processamentoConcluido
                        ? "Processamento concluido"
                        : statusProcessamento);

                if (!processamentoConcluido && projetoAtivo != null)
                    RecarregarInstrucaoPendente(projetoAtivo, false);
            }
        }

        private void IncrementarVersaoProjetoDestinoSeNecessario(Projeto projeto)
        {
            if (projeto == null || !projeto.IncrementarVersaoAoConcluir)
                return;

            try
            {
                var result = ProjectVersionIncrementer.Incrementar(projeto.Caminho);

                if (result.Success)
                {
                    string arquivo = string.IsNullOrWhiteSpace(result.FilePath)
                        ? ""
                        : " Arquivo: " + result.FilePath;

                    string mensagem =
                        "[INFO] Versao do projeto destino incrementada: " +
                        result.OldVersion + " -> " + result.NewVersion +
                        "." + arquivo;

                    textBoxLog.AppendText(mensagem + Environment.NewLine);
                    File.AppendAllText(executionLogPath, mensagem + Environment.NewLine);
                }
                else
                {
                    string mensagem =
                        "[WARN] Opcao de incremento de versao estava ativa, mas a versao nao foi alterada: " +
                        result.Message;

                    textBoxLog.AppendText(mensagem + Environment.NewLine);
                    File.AppendAllText(executionLogPath, mensagem + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                string mensagem =
                    "[WARN] Falha ao incrementar versao do projeto destino: " + ex.Message;

                textBoxLog.AppendText(mensagem + Environment.NewLine);
                File.AppendAllText(executionLogPath, mensagem + Environment.NewLine);
            }
        }

        private static string FormatarMensagemErroUsuario(Exception ex, string etapaProcessamento = null)
        {
            string mensagem = ex == null ? string.Empty : ex.Message ?? string.Empty;
            var kimiUnavailable = EncontrarProviderUnavailableException(ex);

            if (kimiUnavailable != null)
                return FormatarErroKimiIndisponivel(kimiUnavailable, etapaProcessamento);

            if (MensagemIndicaContextoGrande(mensagem))
            {
                return "O contexto ficou grande demais para o modelo selecionado. " +
                    "Tente reduzir o escopo do pedido ou informe os arquivos especificos que devem ser alterados. " +
                    "Esse erro aconteceu antes da IA conseguir processar a solicitacao.";
            }

            if (MensagemIndicaCreditoGeminiEsgotado(mensagem))
                return FormatarErroCreditoGeminiEsgotado();

            if (MensagemIndicaQuotaOpenAI(mensagem))
                return FormatarErroQuotaOpenAI();

            if (MensagemIndicaLimiteDiarioGroq(mensagem))
                return FormatarErroLimiteGroq(mensagem);

            return mensagem;
        }

        private static ProviderUnavailableException EncontrarProviderUnavailableException(Exception ex)
        {
            while (ex != null)
            {
                if (ex is ProviderUnavailableException kimi)
                    return kimi;

                ex = ex.InnerException;
            }

            return null;
        }

        private static string FormatarErroKimiIndisponivel(ProviderUnavailableException ex, string etapaProcessamento)
        {
            if (ex == null)
            {
                return ConstruirMensagemKimiIndisponivel(null, etapaProcessamento);
            }

            var sb = new StringBuilder(ConstruirMensagemKimiIndisponivel(ex, etapaProcessamento));
            sb.Append(" Verifique se o endpoint configurado está ativo: ");
            sb.Append(string.IsNullOrWhiteSpace(ex.BaseUrl) ? "indisponível" : ex.BaseUrl);
            sb.Append(".");
            sb.Append(" Também confira se o modelo configurado está disponível: ");
            sb.Append(string.IsNullOrWhiteSpace(ex.ModelName) ? "indisponível" : ex.ModelName);
            sb.Append(".");
            sb.Append(" Como este endpoint é externo/não oficial, ele pode estar temporariamente desligado ou instável.");
            sb.Append(" Tente novamente mais tarde ou selecione outra IA.");

            if (ex.EnableSearch || ex.EnableThinking)
            {
                sb.Append(" Se necessário, tente desativar Search/Thinking.");
            }

            if (ex.StatusCode.HasValue)
                sb.Append(" Status HTTP: ").Append(ex.StatusCode.Value).Append('.');

            return sb.ToString();
        }

        private static string ConstruirMensagemKimiIndisponivel(ProviderUnavailableException ex, string etapaProcessamento)
        {
            string etapa = string.IsNullOrWhiteSpace(etapaProcessamento)
                ? (ex == null ? string.Empty : ex.Stage ?? string.Empty)
                : etapaProcessamento;
            if (string.Equals(etapa, "http", StringComparison.OrdinalIgnoreCase))
                etapa = string.Empty;

            string alvo;
            if (string.Equals(etapa, "Analista", StringComparison.OrdinalIgnoreCase))
                alvo = "a IA na etapa Analista";
            else if (string.Equals(etapa, "Programador", StringComparison.OrdinalIgnoreCase))
                alvo = "a IA para gerar alteracoes";
            else
                alvo = "a IA";

            return "Servidor Kimi indisponível ou não respondeu dentro do tempo esperado ao consultar " + alvo + ".";
        }

        private static bool MensagemIndicaContextoGrande(string mensagem)
        {
            if (string.IsNullOrWhiteSpace(mensagem))
                return false;

            return mensagem.IndexOf("too large for model", StringComparison.OrdinalIgnoreCase) >= 0
                || mensagem.IndexOf("maximum context length", StringComparison.OrdinalIgnoreCase) >= 0
                || mensagem.IndexOf("Prompt contains", StringComparison.OrdinalIgnoreCase) >= 0
                || mensagem.IndexOf("invalid_request_invalid_args", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatarErroCreditoGeminiEsgotado()
        {
            var sb = new StringBuilder();

            sb.Append("O Gemini não pode ser usado agora porque os créditos pré-pagos da chave acabaram.");
            sb.AppendLine();
            sb.Append("Você pode recarregar créditos no Google AI Studio ou trocar a IA deste projeto.");

            var alternativas = ListarIasAlternativasComChave("Gemini");

            if (!string.IsNullOrWhiteSpace(alternativas))
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append("IAs disponíveis com chave cadastrada: ");
                sb.Append(alternativas);
                sb.Append(".");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append("Não encontrei outra IA ativa com chave cadastrada. Cadastre uma chave em Configurações antes de trocar.");
            }

            return sb.ToString();
        }

        private static string ListarIasAlternativasComChave(params string[] providerTypesIgnorados)
        {
            try
            {
                var ignorados = new HashSet<string>(
                    providerTypesIgnorados ?? new string[0],
                    StringComparer.OrdinalIgnoreCase);

                var alternativas = AiProviderRepository.ListarAtivos()
                    .Where(p => p != null)
                    .Where(p => !ignorados.Contains(p.ProviderType ?? string.Empty))
                    .Where(ProviderPossuiChaveCadastrada)
                    .Select(p =>
                    {
                        string nome = string.IsNullOrWhiteSpace(p.Nome)
                            ? p.ProviderType
                            : p.Nome;

                        string tipo = string.IsNullOrWhiteSpace(p.ProviderType)
                            ? string.Empty
                            : p.ProviderType;

                        return string.IsNullOrWhiteSpace(tipo)
                            ? nome
                            : nome + " (" + tipo + ")";
                    })
                    .Where(nome => !string.IsNullOrWhiteSpace(nome))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return alternativas.Count == 0 ? string.Empty : string.Join(", ", alternativas);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ProviderPossuiChaveCadastrada(AiProviderConfig provider)
        {
            if (provider == null)
                return false;

            if (!string.IsNullOrWhiteSpace(provider.ApiKey))
                return true;

            if (string.Equals(provider.ApiKeyConfigName, "GeminiApiKey", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(ConfigManager.GetApiKey());
            if (string.Equals(provider.ApiKeyConfigName, "KimiOpenAIApiKey", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(ConfigManager.GetKimiOpenAIApiKey());

            return false;
        }


        private static bool MensagemIndicaCreditoGeminiEsgotado(string mensagem)
        {
            if (string.IsNullOrWhiteSpace(mensagem))
                return false;

            return mensagem.IndexOf("Gemini", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   mensagem.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (
                       mensagem.IndexOf("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       mensagem.IndexOf("prepayment credits are depleted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       mensagem.IndexOf("credits are depleted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       mensagem.IndexOf("billing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       mensagem.IndexOf("prepay", StringComparison.OrdinalIgnoreCase) >= 0
                   );
        }

        private static string FormatarErroLimiteGroq(string mensagem)
        {
            string hora = ExtrairHorarioRenovacaoGroq(mensagem);
            var sb = new StringBuilder();
            sb.Append("O limite diario da Groq foi atingido.");
            if (!string.IsNullOrWhiteSpace(hora))
                sb.Append(" O limite da Groq sera renovado por volta de ").Append(hora).Append(".");
            else if (MensagemIndicaLiberacaoCurtaGroq(mensagem))
            {
                sb.Clear();
                sb.Append("A Groq recusou a requisicao por limite de tokens mesmo apos 4 tentativas automaticas. Aguarde cerca de 1 minuto e tente novamente.");
            }

            var alternativas = ListarIasAlternativasAtivas("Groq", "Grog");
            if (!string.IsNullOrWhiteSpace(alternativas))
                sb.AppendLine().AppendLine().Append("Voce pode continuar agora usando outra IA ativa: ").Append(alternativas).Append(".");

            return sb.ToString();
        }

        private string AjustarMensagemQuotaOpenAIComResultado(string mensagemUsuario)
        {
            if (string.IsNullOrWhiteSpace(mensagemUsuario) ||
                !MensagemIndicaQuotaOpenAI(mensagemUsuario))
            {
                return mensagemUsuario;
            }

            bool houveAlteracao = ultimoResultado != null &&
                                  ultimoResultado.ExecutedActions != null &&
                                  ultimoResultado.ExecutedActions.Count > 0;

            if (houveAlteracao)
            {
                return mensagemUsuario +
                       Environment.NewLine + Environment.NewLine +
                       "A execucao parou por cota insuficiente. Verifique o Git diff para confirmar alteracoes parciais.";
            }

            return mensagemUsuario + Environment.NewLine + Environment.NewLine + "Detalhes tecnicos foram registrados no log.";
        }

        private static string FormatarErroQuotaOpenAI()
        {
            var sb = new StringBuilder();
            sb.Append("A cota da OpenAI foi atingida ou nao ha saldo/plano disponivel para esta chave.");
            sb.AppendLine().Append("Verifique o billing, o limite de uso ou troque a IA do projeto.");

            var alternativas = ListarIasAlternativasAtivas("OpenAI");
            if (!string.IsNullOrWhiteSpace(alternativas))
                sb.AppendLine().AppendLine().Append("Voce pode continuar agora usando outra IA ativa: ").Append(alternativas).Append(".");

            return sb.ToString();
        }

        private static bool MensagemIndicaQuotaOpenAI(string mensagem)
        {
            if (string.IsNullOrWhiteSpace(mensagem))
                return false;

            string lower = mensagem.ToLowerInvariant();
            bool mencionaOpenAI = lower.Contains("openai") || lower.Contains("gpt") || lower.Contains("responses");
            bool mencionaQuota =
                lower.Contains("insufficient_quota") ||
                lower.Contains("exceeded your current quota") ||
                lower.Contains("429") ||
                lower.Contains("billing") ||
                lower.Contains("quota");

            return mencionaOpenAI && mencionaQuota;
        }

        private static bool MensagemIndicaLimiteDiarioGroq(string mensagem)
        {
            if (string.IsNullOrWhiteSpace(mensagem))
                return false;

            return mensagem.IndexOf("Groq", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (mensagem.IndexOf("limite diario", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mensagem.IndexOf("tokens per day", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mensagem.IndexOf("(TPD)", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string ExtrairHorarioRenovacaoGroq(string mensagem)
        {
            var match = Regex.Match(mensagem ?? string.Empty, @"por volta de\s+(?<hora>\d{2}:\d{2}(?::\d{2})?)(?:\s+de\s+(?<data>\d{2}/\d{2}/\d{4}))?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string hora = match.Groups["hora"].Value;
                string data = match.Groups["data"].Success ? match.Groups["data"].Value : string.Empty;
                return string.IsNullOrWhiteSpace(data) ? hora : hora + " de " + data;
            }

            var delay = ExtrairDelayGroq(mensagem);
            if (delay.HasValue)
            {
                if (delay.Value < TimeSpan.FromMinutes(1))
                    return string.Empty;

                var localTime = DateTime.Now.Add(delay.Value);
                string horario = delay.Value < TimeSpan.FromHours(1)
                    ? localTime.ToString("HH:mm:ss")
                    : localTime.ToString("HH:mm");

                return horario + " de " + localTime.ToString("dd/MM/yyyy");
            }

            return string.Empty;
        }

        private static TimeSpan? ExtrairDelayGroq(string mensagem)
        {
            var millisecondsMatch = Regex.Match(mensagem ?? string.Empty, @"try again in\s+(?<milliseconds>[0-9]+(?:\.[0-9]+)?)ms", RegexOptions.IgnoreCase);
            if (millisecondsMatch.Success &&
                double.TryParse(millisecondsMatch.Groups["milliseconds"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double milliseconds))
            {
                return TimeSpan.FromMilliseconds(Math.Max(1000, milliseconds + 500));
            }

            var minuteSecondMatch = Regex.Match(mensagem ?? string.Empty, @"try again in\s+(?<minutes>[0-9]+)m(?<seconds>[0-9]+(?:\.[0-9]+)?)s", RegexOptions.IgnoreCase);
            if (minuteSecondMatch.Success &&
                int.TryParse(minuteSecondMatch.Groups["minutes"].Value, out int minutes) &&
                double.TryParse(minuteSecondMatch.Groups["seconds"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds))
            {
                return TimeSpan.FromSeconds(minutes * 60 + seconds + 1);
            }

            var secondsMatch = Regex.Match(mensagem ?? string.Empty, @"try again in\s+(?<seconds>[0-9]+(?:\.[0-9]+)?)s", RegexOptions.IgnoreCase);
            if (secondsMatch.Success &&
                double.TryParse(secondsMatch.Groups["seconds"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds))
            {
                return TimeSpan.FromSeconds(seconds + 1);
            }

            return null;
        }

        private static bool MensagemIndicaLiberacaoCurtaGroq(string mensagem)
        {
            if (string.IsNullOrWhiteSpace(mensagem))
                return false;

            return mensagem.IndexOf("poucos segundos", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   mensagem.IndexOf("em poucos segundos", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (mensagem.IndexOf("try again in", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    ExtrairDelayGroq(mensagem).GetValueOrDefault(TimeSpan.FromMinutes(2)) < TimeSpan.FromMinutes(1));
        }

        private static string ListarIasAlternativasAtivas(params string[] providerTypesIgnorados)
        {
            try
            {
                var ignorados = new HashSet<string>(
                    providerTypesIgnorados ?? new string[0],
                    StringComparer.OrdinalIgnoreCase);

                var alternativas = AiProviderRepository.ListarAtivos()
                    .Where(p => p != null && !ignorados.Contains(p.ProviderType ?? string.Empty))
                    .Select(p => p.Nome)
                    .Where(nome => !string.IsNullOrWhiteSpace(nome))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return alternativas.Count == 0 ? string.Empty : string.Join(", ", alternativas);
            }
            catch
            {
                return string.Empty;
            }
        }


        private static string CriarEntradaProgramador(string demandaOriginal, AgentResult requisicao, Projeto projetoAtivo)
        {
            var textoRequisicao = requisicao == null
                ? string.Empty
                : requisicao.GeneratedTaskRequest ?? requisicao.ModelResponse ?? string.Empty;

            if (string.IsNullOrWhiteSpace(textoRequisicao))
                throw new InvalidOperationException("A requisicao de tarefa gerada pelo analista esta vazia.");

            var partes = new List<string>
            {
                "PAPEL: programador.",
                "Execute a requisicao de tarefa abaixo em sequencia, respeitando o NIVEL_ATRIBUIDO e as restricoes.",
                "Nao execute apenas a primeira etapa. Antes de finalizar, confira cada item em ETAPAS e garanta que cada objetivo foi implementado ou que ja estava implementado no codigo atual.",
                "Se uma etapa depender de outra, gere as acoes na ordem necessaria na mesma resposta. Nao pule integracao de eventos, chamadas de botoes, handlers, designer ou metodos de servico quando eles estiverem no escopo.",
                "Na explicacao, inclua uma linha curta CHECK_ETAPAS informando quais etapas ficaram cobertas pelas acoes retornadas.",
                "Retorne somente JSON valido no schema de acoes do sistema. Nao retorne nova requisicao, markdown ou explicacoes fora do JSON."
            };

            // Adicionar contexto do projeto
            partes.Add("CONTEXTO_DO_PROJETO_ATIVO:");
            partes.Add("Nome: " + (projetoAtivo != null ? projetoAtivo.Nome : "Nao especificado"));
            partes.Add("Raiz: " + (projetoAtivo != null ? projetoAtivo.Caminho : "Nao especificado"));

            partes.Add("DEMANDA_ORIGINAL:");
            partes.Add(demandaOriginal ?? string.Empty);
            partes.Add("REQUISICAO_DE_TAREFA:");
            partes.Add(textoRequisicao);

            string projectRootForFiles = projetoAtivo != null ? projetoAtivo.Caminho : string.Empty;
            string arquivosReferenciados = ColetarArquivosReferenciados(projectRootForFiles, textoRequisicao);
            if (!string.IsNullOrWhiteSpace(arquivosReferenciados))
            {
                partes.Add(arquivosReferenciados);
            }

            return string.Join(Environment.NewLine + Environment.NewLine, partes);
        }

        private static string ColetarArquivosReferenciados(string projectRoot, string texto)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot) || string.IsNullOrWhiteSpace(texto))
                return string.Empty;

            var arquivos = new List<string>();
            foreach (Match match in Regex.Matches(texto, @"[A-Za-z]:\\[^\r\n`""]+\.[A-Za-z0-9]+"))
                AdicionarArquivoSeExistir(arquivos, projectRoot, match.Value.Trim());

            foreach (Match match in Regex.Matches(texto, @"[\w\-.]+\.(Designer\.cs|cs|csproj|resx|blade\.php|php|phtml|css|js|json)", RegexOptions.IgnoreCase))
            {
                var nome = match.Value.Trim();
                foreach (var path in Directory.GetFiles(projectRoot, nome, SearchOption.AllDirectories).Take(5))
                    AdicionarArquivoSeExistir(arquivos, projectRoot, path);
            }

            if (arquivos.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("ARQUIVOS_REFERENCIADOS_PARA_EXECUCAO:");
            foreach (var arquivo in arquivos.Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
            {
                sb.AppendLine();
                sb.AppendLine("--- FILE: " + MakeRelative(projectRoot, arquivo) + " ---");
                sb.AppendLine(ReadTextLimited(arquivo, 60000));
                sb.AppendLine("--- END FILE: " + MakeRelative(projectRoot, arquivo) + " ---");
            }

            return sb.ToString();
        }

        private static void AdicionarArquivoSeExistir(List<string> arquivos, string projectRoot, string path)
        {
            if (arquivos == null || string.IsNullOrWhiteSpace(path))
                return;

            string fullPath;
            try
            {
                fullPath = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(projectRoot, path));
            }
            catch
            {
                return;
            }

            if (File.Exists(fullPath) && IsUnder(fullPath, projectRoot))
                arquivos.Add(fullPath);
        }

        private static string ReadTextLimited(string path, int maxChars)
        {
            try
            {
                var text = File.ReadAllText(path);
                return text.Length <= maxChars ? text : text.Substring(0, maxChars) + Environment.NewLine + "<... truncado ...>";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string MakeRelative(string root, string fullPath)
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
                var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return full.StartsWith(basePath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            FormConfiguracoes Fc = new FormConfiguracoes();
            Fc.ShowDialog();
        }

        private void btLinguagens_Click(object sender, EventArgs e)
        {
            using (var form = new FormLinguagens())
            {
                form.ShowDialog(this);
            }

            AtualizarLanguageProfileDoProjetoAtivo();
            AtualizarStatusPerfilLinguagem();
        }

        private void buttonLog_Click(object sender, EventArgs e)
        {
            logTecnicoVisivel = !logTecnicoVisivel;
            ConfigManager.SaveLogVisible(logTecnicoVisivel);
            //AplicarVisibilidadeLog(logTecnicoVisivel, true);
        }

        private void cbProjetos_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (carregandoProjetos)
                return;

            var projeto = cbProjetos.SelectedItem as Projeto;

            if (projeto == null)
                return;

            ConfigManager.SaveLastProjectId(projeto.Id.ToString());
            AtualizarLanguageProfileDoProjetoAtivo();
            AtualizarAiAtualDoProjetoAtivo();
            AtualizarStatusPerfilLinguagem();
            RecarregarInstrucaoPendente(projeto, true);
        }

        private void RecarregarInstrucaoPendente(Projeto projeto, bool somenteSeCampoVazio)
        {
            if (projeto == null || projeto.Id == Guid.Empty || textBox1 == null)
                return;

            string pendente = PendingInstructionRepository.Load(projeto.Id);
            if (string.IsNullOrWhiteSpace(pendente))
                return;

            if (somenteSeCampoVazio && !string.IsNullOrWhiteSpace(textBox1.Text))
                return;

            textBox1.Text = pendente;
            textBox1.SelectionStart = textBox1.TextLength;
            textBox1.SelectionLength = 0;

            if (!StatusAtualEhErro())
                AtualizarStatusProcessamento("Instrucao pendente restaurada");
        }

        private bool StatusAtualEhErro()
        {
            return !string.IsNullOrWhiteSpace(statusProcessamento) &&
                   statusProcessamento.IndexOf("erro", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void btAdicionarTarefa_Click(object sender, EventArgs e)
        {
            using (var form = new FormProjetoCadastro())
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    CarregarProjetos();

                    if (form.ProjetoSalvo != null)
                        SelecionarProjeto(form.ProjetoSalvo.Id);
                }
            }
        }

        private void btEditarProjeto_Click(object sender, EventArgs e)
        {
            var projeto = cbProjetos.SelectedItem as Projeto;

            if (projeto == null)
            {
                MessageBox.Show("Selecione um projeto para editar.", "Editar projeto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var form = new FormProjetoCadastro(projeto))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    Guid idEditado = form.ProjetoSalvo != null ? form.ProjetoSalvo.Id : projeto.Id;
                    CarregarProjetos();
                    SelecionarProjeto(idEditado);
                }
            }
        }

        private string ObterPastaProjetoAtiva()
        {
            var projeto = ObterProjetoAtivo();
            if (projeto != null && Directory.Exists(projeto.Caminho))
                return projeto.Caminho;

            return ConfigManager.GetProjectRoot();
        }

        private string ObterNomeProjetoAtivo(string projectRoot)
        {
            var projeto = ObterProjetoAtivo();
            if (projeto != null && string.Equals(projeto.Caminho, projectRoot, StringComparison.OrdinalIgnoreCase))
                return projeto.Nome;

            if (string.IsNullOrWhiteSpace(projectRoot))
                return string.Empty;

            return Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private Projeto ObterProjetoAtivo()
        {
            var selecionado = cbProjetos.SelectedItem as Projeto;
            if (selecionado != null)
                return selecionado;

            string lastId = ConfigManager.GetLastProjectId();
            if (!string.IsNullOrWhiteSpace(lastId) && Guid.TryParse(lastId, out Guid id))
                return ProjetoRepository.BuscarPorId(id);

            return null;
        }

        private void ExibirRespostaIA(AgentResult resultado)
        {
            if (resultado != null && resultado.OperationMode == AgentOperationMode.AnalistaDeSistemas)
            {
                textBox2.Text = resultado.GeneratedTaskRequest ?? resultado.ModelResponse ?? string.Empty;
                return;
            }

            if (resultado == null || resultado.StructuredResponse == null)
            {
                textBox2.Clear();
                return;
            }

            var resposta = resultado.StructuredResponse;
            var partes = new List<string>();

            if (!string.IsNullOrWhiteSpace(resposta.MensagemUsuario))
                partes.Add(resposta.MensagemUsuario.Trim());

            if (!string.IsNullOrWhiteSpace(resposta.Explicacao))
                partes.Add(resposta.Explicacao.Trim());

            textBox2.Text = string.Join(Environment.NewLine + Environment.NewLine, partes);
        }

        private void CopiarConteudoTextBox_Click(object sender, EventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null || string.IsNullOrEmpty(textBox.Text))
                return;

            Clipboard.SetText(textBox.Text);
            if (StatusAtualEhErro())
            {
                RegistrarStatusAuxiliar("Conteudo copiado para memoria");
                return;
            }

            AtualizarStatusProcessamento("Conteudo copiado para memoria");
        }

        private void ExibirAcoesPropostas(AgentResult resultado)
        {
            listBoxAcoes.Items.Clear();

            if (resultado == null)
                return;

            var arquivos = ExtrairArquivosAlterados(resultado).ToList();
            RegistrarStatusAuxiliar("[DIAG] Arquivos alterados recebidos: " + arquivos.Count);
            foreach (var arquivo in arquivos)
            {
                listBoxAcoes.Items.Add(arquivo);
            }

            RegistrarStatusAuxiliar("[DIAG] listBoxAcoes itens apos preencher: " + listBoxAcoes.Items.Count);
        }

        private void ExibirEtapasAnalista(AgentResult resultado)
        {
            if (listBoxEtapas == null)
                return;

            listBoxEtapas.Items.Clear();
            string texto = resultado == null
                ? string.Empty
                : resultado.GeneratedTaskRequest ?? resultado.ModelResponse ?? string.Empty;

            foreach (string etapa in ExtrairEtapasAnalista(texto))
                listBoxEtapas.Items.Add(etapa);
        }

        private static IEnumerable<string> ExtrairEtapasAnalista(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                yield break;

            var matches = Regex.Matches(texto, @"^\s*(?<n>\d+)\.\s+OBJETIVO:\s*(?<objetivo>.+)$", RegexOptions.Multiline);
            foreach (Match match in matches)
            {
                string numero = match.Groups["n"].Value.Trim();
                string objetivo = match.Groups["objetivo"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(objetivo))
                    yield return numero + ". " + objetivo;
            }

            if (matches.Count > 0)
                yield break;

            var objetivoUnico = Regex.Match(texto, @"^\s*OBJETIVO:\s*(?<objetivo>.+)$", RegexOptions.Multiline);
            if (objetivoUnico.Success)
            {
                yield return "1. " + objetivoUnico.Groups["objetivo"].Value.Trim();
                yield break;
            }

            var primeiraLinhaUtil = texto
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("TAREFA_ID:", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(primeiraLinhaUtil))
                yield return "1. " + primeiraLinhaUtil.Trim();
        }

        private static IEnumerable<string> ExtrairArquivosAlterados(AgentResult resultado)
        {
            var actions = resultado.ExecutedActions != null && resultado.ExecutedActions.Count > 0
                ? resultado.ExecutedActions
                : resultado.StructuredResponse?.Acoes;

            if (actions == null)
                yield break;

            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var action in actions)
            {
                foreach (string arquivo in ExtrairArquivosDaAcao(action))
                {
                    if (!string.IsNullOrWhiteSpace(arquivo) && vistos.Add(arquivo))
                        yield return arquivo;
                }
            }
        }

        private static IEnumerable<string> ExtrairArquivosDaAcao(AgentAction action)
        {
            if (action == null || action.Dados == null)
                yield break;

            string arq = action.Dados.Value<string>("ARQ");
            if (!string.IsNullOrWhiteSpace(arq))
                yield return arq.Trim();

            string protocolo = action.Dados.Value<string>("protocolo");
            if (string.IsNullOrWhiteSpace(protocolo))
                yield break;

            foreach (Match match in Regex.Matches(protocolo, @"(?im)^\s*ARQ\s*=\s*(?<arquivo>.+?)\s*$"))
            {
                string arquivo = match.Groups["arquivo"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(arquivo))
                    yield return arquivo;
            }

        }

        private string MontarEntradaContinuacao(string ajusteUsuario, Projeto projetoAtivo)
        {
            string ajuste = (ajusteUsuario ?? string.Empty).Trim();
            var sb = new StringBuilder();

            sb.AppendLine("CONTINUACAO_DA_TAREFA_ANTERIOR");
            sb.AppendLine();
            sb.AppendLine("PEDIDO_ATUAL_DO_USUARIO:");
            sb.AppendLine(ajuste);
            sb.AppendLine();
            sb.AppendLine("INSTRUCOES_DE_EXECUCAO:");
            sb.AppendLine("- Trate este pedido como ajuste da tarefa anterior deste projeto.");
            sb.AppendLine("- Use o contexto resumido abaixo para resolver referencias como \"o botao que criamos\", \"a tela nova\", \"isso\", \"ficou\" ou \"faltou\".");
            sb.AppendLine("- Faça a menor alteração possível.");
            sb.AppendLine("- Não recrie a solução inteira e não altere partes não relacionadas.");
            sb.AppendLine("- Se for ajuste visual, priorize os arquivos Designer/Form relacionados.");
            sb.AppendLine("- Se o contexto anterior não for suficiente, solicite os arquivos necessários ou use apenas arquivos claramente relacionados.");

            string contexto = MontarResumoUltimaTarefa(projetoAtivo);
            if (!string.IsNullOrWhiteSpace(contexto))
            {
                sb.AppendLine();
                sb.AppendLine("CONTEXTO_RESUMIDO_DA_TAREFA_ANTERIOR:");
                sb.AppendLine(contexto);
            }

            return sb.ToString();
        }

        private string MontarResumoUltimaTarefa(Projeto projetoAtivo)
        {
            if (projetoAtivo == null || projetoAtivo.Id == Guid.Empty)
                return string.Empty;

            ProjectHistory history = ProjectHistoryRepository.Load(projetoAtivo.Id.ToString(), projetoAtivo.Nome);
            if (history == null || history.Entries == null || history.Entries.Count == 0)
                return string.Empty;

            var recentes = history.Entries
                .Where(e => e != null)
                .OrderByDescending(e => e.TimestampUtc)
                .Take(8)
                .ToList();

            var ultimaRequisicao = recentes.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.GeneratedTaskRequest));
            var ultimaExecucao = recentes.FirstOrDefault(e =>
                (e.ExecutedActions != null && e.ExecutedActions.Count > 0) ||
                (e.ProposedActions != null && e.ProposedActions.Count > 0));

            var sb = new StringBuilder();

            ProjectHistoryEntry referencia = ultimaExecucao ?? ultimaRequisicao ?? recentes.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(referencia?.UserQuestion))
            {
                sb.AppendLine("Solicitacao anterior:");
                sb.AppendLine(ResumirTexto(referencia.UserQuestion, 1800));
            }

            if (!string.IsNullOrWhiteSpace(ultimaRequisicao?.GeneratedTaskRequest))
            {
                sb.AppendLine();
                sb.AppendLine("Etapas decididas pelo analista:");
                foreach (string etapa in ExtrairEtapasAnalista(ultimaRequisicao.GeneratedTaskRequest).Take(8))
                    sb.AppendLine("- " + etapa);
            }

            var arquivos = ExtrairArquivosHistorico(recentes).Take(20).ToList();
            if (arquivos.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Arquivos alterados ou planejados na ultima tarefa:");
                foreach (string arquivo in arquivos)
                    sb.AppendLine("- " + arquivo);
            }

            var acoes = ExtrairDescricoesAcoesHistorico(recentes).Take(10).ToList();
            if (acoes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Acoes recentes:");
                foreach (string acao in acoes)
                    sb.AppendLine("- " + acao);
            }

            if (!string.IsNullOrWhiteSpace(referencia?.Error))
            {
                sb.AppendLine();
                sb.AppendLine("Observacao: a ultima execucao registrou erro. O ajuste deve considerar o estado atual dos arquivos, sem repetir cegamente a acao que falhou.");
            }

            return sb.ToString().Trim();
        }

        private static IEnumerable<string> ExtrairArquivosHistorico(IEnumerable<ProjectHistoryEntry> entries)
        {
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries ?? Enumerable.Empty<ProjectHistoryEntry>())
            {
                foreach (var action in (entry.ExecutedActions ?? new List<AgentAction>()).Concat(entry.ProposedActions ?? new List<AgentAction>()))
                {
                    foreach (string arquivo in ExtrairArquivosDaAcao(action))
                    {
                        if (!string.IsNullOrWhiteSpace(arquivo) && vistos.Add(arquivo))
                            yield return arquivo;
                    }
                }
            }
        }

        private static IEnumerable<string> ExtrairDescricoesAcoesHistorico(IEnumerable<ProjectHistoryEntry> entries)
        {
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries ?? Enumerable.Empty<ProjectHistoryEntry>())
            {
                foreach (var action in (entry.ExecutedActions ?? new List<AgentAction>()).Concat(entry.ProposedActions ?? new List<AgentAction>()))
                {
                    string descricao = action == null ? string.Empty : action.Descricao;
                    if (!string.IsNullOrWhiteSpace(descricao) && vistos.Add(descricao))
                        yield return ResumirTexto(descricao, 220);
                }
            }
        }

        private static string ResumirTexto(string texto, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return string.Empty;

            string limpo = Regex.Replace(texto.Trim(), @"\s+", " ");
            if (limpo.Length <= maxChars)
                return limpo;

            return limpo.Substring(0, Math.Max(0, maxChars - 3)) + "...";
        }

        private void RegistrarTokens(AgentResult resultado)
        {
            if (resultado == null || tokenCounter == null)
                return;

            int promptTokens = tokenCounter.RegisterInput(resultado.FinalPrompt);
            int responseTokens = tokenCounter.RegisterOutput(resultado.ModelResponse);
            AtualizarTokensNaTela();

            if (textBoxLog != null)
            {
                var tokenLine = $"[TOKENS] +{promptTokens} entrada, +{responseTokens} saida, total {tokenCounter.TotalTokens}";
                textBoxLog.AppendText(tokenLine + Environment.NewLine);
                File.AppendAllText(executionLogPath, tokenLine + Environment.NewLine);
            }
        }

        private void IniciarIndicadorProcessamento()
        {
            processamentoAtivo = true;
            papelProcessamentoAtual = string.Empty;
            processamentoInicio = DateTime.Now;
            duracaoUltimoProcessamento = TimeSpan.Zero;
            ultimoStatusRegistrado = string.Empty;
            if (buttonStatus != null)
                buttonStatus.Visible = true;
            AtualizarBotaoStatus("Interno", Color.Gray);
            if (statusTimer != null)
                statusTimer.Start();

            AtualizarStatusStrip();
        }

        private void FinalizarIndicadorProcessamento(string statusFinal = null)
        {
            processamentoAtivo = false;
            papelProcessamentoAtual = string.Empty;
            duracaoUltimoProcessamento = DateTime.Now - processamentoInicio;

            if (statusTimer != null)
                statusTimer.Stop();

            string statusVisualFinal = NormalizarStatusFinalProcessamento(statusFinal ?? statusProcessamento);

            statusProcessamento = statusVisualFinal;

            AtualizarBotaoStatusPorStatus(statusVisualFinal);
            AtualizarStatusStrip();
        }

        private string NormalizarStatusFinalProcessamento(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "Processamento concluido";

            string texto = status.Trim();

            if (string.Equals(texto, "Interno", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(texto, "Analista", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(texto, "Programador", StringComparison.OrdinalIgnoreCase))
            {
                return "Processamento concluido";
            }

            return texto;
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            AtualizarStatusStrip();
        }


        private void AtualizarStatusProcessamento(string status)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AtualizarStatusProcessamento), status);
                return;
            }

            if (string.IsNullOrWhiteSpace(status))
                return;

            statusProcessamento = status;
            SelecionarEtapaPeloStatus(status);
            AtualizarBotaoStatusPorStatus(status);
            if (!string.Equals(ultimoStatusRegistrado, status, StringComparison.OrdinalIgnoreCase))
            {
                var statusLine = "[STATUS] " + status;
                if (textBoxLog != null)
                    textBoxLog.AppendText(statusLine + Environment.NewLine);

                File.AppendAllText(executionLogPath, statusLine + Environment.NewLine);
                ultimoStatusRegistrado = status;
            }

            AtualizarStatusStrip();
        }

        private void RegistrarStatusAuxiliar(string status)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(RegistrarStatusAuxiliar), status);
                return;
            }

            if (string.IsNullOrWhiteSpace(status))
                return;

            var statusLine = "[STATUS] " + status;
            if (textBoxLog != null)
                textBoxLog.AppendText(statusLine + Environment.NewLine);

            File.AppendAllText(executionLogPath, statusLine + Environment.NewLine);
        }

        private void SelecionarEtapaPeloStatus(string status)
        {
            if (listBoxEtapas == null || listBoxEtapas.Items.Count == 0 || string.IsNullOrWhiteSpace(status))
                return;

            var match = Regex.Match(status, @"\bEtapa\s+(?<n>\d+)\b", RegexOptions.IgnoreCase);
            if (!match.Success || !int.TryParse(match.Groups["n"].Value, out int etapa))
                return;

            int index = etapa - 1;
            if (index < 0 || index >= listBoxEtapas.Items.Count)
                return;

            if (listBoxEtapas.SelectedIndex != index)
                listBoxEtapas.SelectedIndex = index;
        }

        private void AtualizarBotaoStatusPorStatus(string status)
        {
            var texto = status ?? string.Empty;

            if (texto.IndexOf("erro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("Acoes: 0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("acoes: 0", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AtualizarBotaoStatus("Erro", Color.Firebrick);
                return;
            }

            if (texto.IndexOf("concluido", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("conclu", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AtualizarBotaoStatus("OK", Color.ForestGreen);
                return;
            }

            if (processamentoAtivo && string.Equals(papelProcessamentoAtual, "Analista", StringComparison.OrdinalIgnoreCase))
            {
                AtualizarBotaoStatus("Analista", Color.FromArgb(230, 105, 0));
                return;
            }

            if (processamentoAtivo && string.Equals(papelProcessamentoAtual, "Programador", StringComparison.OrdinalIgnoreCase))
            {
                AtualizarBotaoStatus("Programador", Color.FromArgb(245, 155, 55));
                return;
            }

            if (texto.IndexOf("Consultando", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("IA bloqueada", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("Aguardando", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AtualizarBotaoStatus("IA", Color.DarkOrange);
                return;
            }

            if (texto.IndexOf("Executando", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("acoes propostas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("alterando", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AtualizarBotaoStatus("Arquivos", Color.RoyalBlue);
                return;
            }

            if (texto.IndexOf("concluido", StringComparison.OrdinalIgnoreCase) >= 0 ||
                texto.IndexOf("concluído", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AtualizarBotaoStatus("OK", Color.ForestGreen);
                return;
            }

            AtualizarBotaoStatus("Interno", Color.Gray);
        }

        private void AtualizarBotaoStatus(string texto, Color cor)
        {
            if (buttonStatus == null)
                return;

            if (buttonStatus.InvokeRequired)
            {
                buttonStatus.Invoke(new Action(() => AtualizarBotaoStatus(texto, cor)));
                return;
            }

            buttonStatus.Text = texto;
            buttonStatus.BackColor = cor;
            buttonStatus.ForeColor = Color.White;
            buttonStatus.UseVisualStyleBackColor = false;
        }

        private void AtualizarStatusStrip()
        {
            if (toolStripStatusLabel1 == null || toolStripStatusLabel2 == null || toolStripStatusLabel3 == null)
                return;

            var tempo = processamentoAtivo
                ? DateTime.Now - processamentoInicio
                : duracaoUltimoProcessamento;

            toolStripStatusLabel1.Text = "Status: " + statusProcessamento;
            toolStripStatusLabel2.Text = "IA: " + (string.IsNullOrWhiteSpace(aiAtualModelo) ? "n/d" : aiAtualModelo);
            toolStripStatusLabel3.Text = "Tempo: " + tempo.ToString(@"hh\:mm\:ss");
        }

        private void PrepararLogTecnico()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(executionLogPath) ?? AppDomain.CurrentDomain.BaseDirectory);
            File.WriteAllText(executionLogPath, string.Empty);
        }

        private void RegistrarExcecaoDetalhada(Exception ex)
        {
            if (ex == null)
                return;

            var linhas = new List<string>
            {
                "[ERRO-DETALHE] Tipo: " + ex.GetType().FullName,
                "[ERRO-DETALHE] Mensagem: " + ex.Message
            };

            var kimiUnavailable = EncontrarProviderUnavailableException(ex);
            if (kimiUnavailable != null)
            {
                linhas.Add("[KIMI] Endpoint indisponível ou timeout.");
                linhas.Add("[KIMI] Base URL: " + kimiUnavailable.BaseUrl);
                linhas.Add("[KIMI] Modelo: " + kimiUnavailable.ModelName);
                linhas.Add("[KIMI] EnableSearch: " + (kimiUnavailable.EnableSearch ? "true" : "false"));
                linhas.Add("[KIMI] EnableThinking: " + (kimiUnavailable.EnableThinking ? "true" : "false"));
                linhas.Add("[KIMI] Status HTTP: " + (kimiUnavailable.StatusCode.HasValue ? kimiUnavailable.StatusCode.Value.ToString() : "indisponivel"));
                linhas.Add("[KIMI] Etapa: " + kimiUnavailable.Stage);
            }

            if (ex.InnerException != null)
            {
                linhas.Add("[ERRO-DETALHE] Inner tipo: " + ex.InnerException.GetType().FullName);
                linhas.Add("[ERRO-DETALHE] Inner mensagem: " + ex.InnerException.Message);
            }

            linhas.Add("[ERRO-DETALHE] StackTrace parcial: " + TruncarTexto(ex.ToString(), 2000));

            foreach (var linha in linhas)
            {
                if (textBoxLog != null)
                    textBoxLog.AppendText(linha + Environment.NewLine);

                File.AppendAllText(executionLogPath, linha + Environment.NewLine);
            }
        }

        private static string TruncarTexto(string texto, int maximo)
        {
            if (string.IsNullOrEmpty(texto) || maximo <= 0 || texto.Length <= maximo)
                return texto ?? string.Empty;

            return texto.Substring(0, maximo) + "...";
        }

        private void CarregarLogTecnico()
        {
            if (textBoxLog == null)
                return;

            if (!File.Exists(executionLogPath))
            {
                textBoxLog.Clear();
                return;
            }

            textBoxLog.Text = File.ReadAllText(executionLogPath);
        }

        //private void AplicarVisibilidadeLog(bool visivel, bool carregarLog)
        //{
        //    if (textBoxLog != null)
        //        textBoxLog.Visible = visivel;

        //    if (label5 != null)
        //        label5.Visible = visivel;

        //    //if (buttonLog != null)
        //    //    buttonLog.Text = visivel ? "Ocultar log" : "Log";

        //    if (visivel && carregarLog)
        //        CarregarLogTecnico();
        //}

        private void AtualizarTokensNaTela()
        {
            if (textBoxTokens == null || tokenCounter == null)
                return;

            textBoxTokens.Text = tokenCounter.FormatStatus();
        }

        private bool ConfirmarAcao(AgentAction action, string projectRoot)
        {
            if (InvokeRequired)
            {
                return (bool)Invoke(new Func<AgentAction, string, bool>(ConfirmarAcao), action, projectRoot);
            }

            if (action == null)
                return false;

            string descricao = action.Descricao;
            if (string.IsNullOrWhiteSpace(descricao))
                descricao = action.Tipo.ToString();

            string mensagem = "A IA pediu para executar esta acao:\n\n" + descricao + "\n\nDeseja continuar?";

            var resposta = MessageBox.Show(
                mensagem,
                "Confirmar acao",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            return resposta == DialogResult.Yes;
        }

        private void buttonConfirmar_Click(object sender, EventArgs e)
        {
            if (ultimoResultado == null || ultimoResultado.StructuredResponse == null)
                return;

            listBoxAcoes.Items.Clear();
            AcionaProcessamento();
        }

        private async void btGerarRequisicao_Click(object sender, EventArgs e)
        {
            textBoxLog.Clear();
            PrepararLogTecnico();
            dllExecutionLogger?.Clear();
            IniciarIndicadorProcessamento();

            try
            {
                PrepararAgentCoreParaProjetoAtivo();
                papelProcessamentoAtual = "Analista";
                var resultado = await agentCore.ExecutarGeracaoRequisicaoAsync(textBox1.Text);
                ultimoResultado = resultado;
                textBox2.Text = resultado.GeneratedTaskRequest ?? resultado.ModelResponse ?? string.Empty;
                ExibirEtapasAnalista(resultado);
                listBoxAcoes.Items.Clear();
                AtualizarStatusProcessamento("Requisicao de tarefa gerada");
            }
            catch (Exception ex)
            {
                string mensagemUsuario = FormatarMensagemErroUsuario(ex, papelProcessamentoAtual);
                AtualizarStatusProcessamento("Erro ao gerar requisicao");
                textBoxLog.AppendText("[ERRO] " + ex.Message + Environment.NewLine);
                RegistrarExcecaoDetalhada(ex);
                if (!string.Equals(mensagemUsuario, ex.Message, StringComparison.Ordinal))
                    textBoxLog.AppendText("[ERRO-USUARIO] " + mensagemUsuario + Environment.NewLine);

                MessageBox.Show(mensagemUsuario, "Gerar requisicao", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                FinalizarIndicadorProcessamento();
            }
        }

        private sealed class MainProjectProvider : IProjectProvider
        {
            private readonly Main _form;

            public MainProjectProvider(Main form)
            {
                _form = form;
            }

            public string GetActiveProjectId()
            {
                var projeto = _form.ObterProjetoAtivo();
                return projeto != null ? projeto.Id.ToString() : string.Empty;
            }

            public string GetActiveProjectRoot()
            {
                return _form.ObterPastaProjetoAtiva();
            }

            public string GetActiveProjectName(string projectRoot)
            {
                return _form.ObterNomeProjetoAtivo(projectRoot);
            }
        }

        private void AtualizarStatusPerfilLinguagem()
        {
            if (toolStripStatusLabelPerfil == null)
                return;

            var languageConfig = LanguageProfileConfigRepository.BuscarPorId(languageProfileIdAtual);
            string perfil = languageConfig == null
                ? languageProfileAtual.ToString()
                : languageConfig.Nome;

            var projeto = ObterProjetoAtivo();
            var provider = aiProviderAtual ?? AiProviderRepository.BuscarAtivoOuPadrao(projeto == null ? AiProviderConfig.DefaultProviderId : projeto.AiProviderId);
            int nivel = provider == null ? 6 : provider.NivelMaximoSuportado;
            toolStripStatusLabelPerfil.Text = "Perfil: " + perfil + " | Nivel: " + nivel;
        }

        private void configuracoesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var form = new FormConfiguracoes())
            {
                form.ShowDialog(this);
            }

            PrepararAgentCoreParaProjetoAtivo();
        }

        private void AtualizarLanguageProfileDoProjetoAtivo()
        {
            var projeto = cbProjetos.SelectedItem as Projeto;
            languageProfileIdAtual = projeto != null
                ? LanguageProfileConfigRepository.ResolveId(projeto.LanguageProfileId, projeto.LanguageProfile)
                : "geral";
            languageProfileAtual = projeto != null
                ? LanguageProfileConfigRepository.LegacyProfileFromId(languageProfileIdAtual)
                : LanguageProfile.Geral;

            if (agentCore != null)
            {
                agentCore.LanguageProfile = languageProfileAtual;
                agentCore.LanguageProfileId = languageProfileIdAtual;
            }
        }

        private void AtualizarAiAtualDoProjetoAtivo()
        {
            var projeto = ObterProjetoAtivo();
            aiProviderAtual = AiProviderRepository.BuscarAtivoOuPadrao(projeto == null ? AiProviderConfig.DefaultProviderId : projeto.AiProviderId);
            aiAtualDescricao = aiProviderAtual == null ? "n/d" : aiProviderAtual.Nome;
            aiAtualModelo = aiProviderAtual == null
                ? "n/d"
                : ProviderEhGemini(aiProviderAtual)
                    ? "Analista: gemini-2.5-pro | Programador: gemini-2.5-flash"
                    : aiProviderAtual.ModelName;
            AtualizarStatusStrip();
        }

        private void PrepararAgentCoreParaProjetoAtivo()
        {
            if (agentCore != null)
            {
                agentCore.LanguageProfile = languageProfileAtual;
                agentCore.LanguageProfileId = languageProfileIdAtual;
                agentCore.AnalystPromptPath = ConfigManager.GetAnalystPromptPath();
                var projeto = ObterProjetoAtivo();
                AtualizarProvidersRuntime();
                var provider = ObterProviderRuntimeAtual(projeto);
                agentCore.ComplementPromptPath = ConfigManager.GetComplementPromptPath();
                agentCore.KnowledgeIndexPath = projeto == null ? string.Empty : projeto.KnowledgeIndexPath ?? string.Empty;
                agentCore.AutoVerificationEnabled = projeto != null && projeto.AutoVerificationEnabled;
                if (toolDispatcherPrincipal != null)
                    toolDispatcherPrincipal.AutoVerificationEnabled = agentCore.AutoVerificationEnabled;

                if (string.IsNullOrWhiteSpace(agentCore.KnowledgeIndexPath))
                    dllExecutionLogger?.Info("[INFO] KnowledgeIndexPath nao configurado.");
                else
                    dllExecutionLogger?.Info("[INFO] KnowledgeIndexPath configurado: " + agentCore.KnowledgeIndexPath);

                if (agentCore.AutoVerificationEnabled)
                    dllExecutionLogger?.Info("[INFO] Verificação automática habilitada para o projeto.");
                else
                    dllExecutionLogger?.Info("[INFO] Verificação automática desabilitada para o projeto.");

                LogProviderRuntime(provider);
                if (provider != null && string.Equals(provider.ProviderType, AiProviderConfig.KimiProviderType, StringComparison.OrdinalIgnoreCase))
                    dllExecutionLogger?.Info("[GIT-DIFF] Prompt GitDiff não aplicado ao Analista.");
            }
        }

        private FtpProfile ObterFtpProjetoAtivo(string projectRoot)
        {
            var projeto = ObterProjetoAtivo();
            if (projeto == null || projeto.Ftp == null || string.IsNullOrWhiteSpace(projeto.Ftp.Host))
                return null;

            return projeto.Ftp;
        }

        private AgentCore CriarAgentCoreParaProjetoAtual(IToolDispatcher toolDispatcher)
        {
            var projeto = ObterProjetoAtivo();
            AtualizarProvidersRuntime();
            var provider = ObterProviderRuntimeAtual(projeto);
            var client = CriarClientParaPapel(provider, "Programador");
            var analystClient = CriarClientParaPapel(provider, "Analista");
            int nivelIa = provider.NivelMaximoSuportado;

            if (toolDispatcher is ToolDispatcher dispatcher)
                dispatcher.AutoVerificationEnabled = projeto != null && projeto.AutoVerificationEnabled;

            return new AgentCore(
                client,
                new MainProjectProvider(this),
                new ProjectContextBuilder(),
                projectRoot => CarregarPromptProgramadorRuntime(projectRoot, provider),
                toolDispatcher,
                AtualizarStatusProcessamento,
                dllExecutionLogger,
                ObterNomeProviderParaPapel(provider, "Programador"),
                analystClient,
                ObterNomeProviderParaPapel(provider, "Analista"))
            {
                LanguageProfile = languageProfileAtual,
                LanguageProfileId = languageProfileIdAtual,
                UseComplementPrompt = processamentoComplementoAtual,
                ComplementPromptPath = ConfigManager.GetComplementPromptPath(),
                KnowledgeIndexPath = projeto == null ? string.Empty : projeto.KnowledgeIndexPath ?? string.Empty,
                AutoVerificationEnabled = projeto != null && projeto.AutoVerificationEnabled,
                UseGitDiff = provider != null && provider.UseGitDiff,
                AiProviderId = provider.Id,
                AiProviderName = provider.Nome,
                NivelMaximoDificuldade = nivelIa,
                NivelMaximoIa = provider.NivelMaximoSuportado,
                MaxPromptChars = ConfigManager.GetMaxPromptChars()
            };
        }

        private string CarregarPromptProgramadorRuntime(string projectRoot, AiProviderConfig provider)
        {
            var projetoAtual = ObterProjetoAtivo();
            if (projetoAtual != null)
            {
                AtualizarProvidersRuntime();
                var providerRecuperado = ObterProviderRuntimeAtual(projetoAtual);
                if (providerRecuperado != null &&
                    (provider == null || string.Equals(providerRecuperado.Id, provider.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    provider = providerRecuperado;
                }
            }

            LogProviderRuntime(provider, projectRoot);
            if (provider != null && provider.UseGitDiff)
            {
                dllExecutionLogger?.Info("[GIT-DIFF] Modo GitDiff habilitado para IA " + provider.Nome + ".");

                string gitDiffPromptPath = (provider.GitDiffPromptPath ?? string.Empty).Trim();
                string gitDiffPromptResolvedPath = ResolverCaminhoPrompt(projectRoot, gitDiffPromptPath);
                if (string.IsNullOrWhiteSpace(gitDiffPromptResolvedPath) || !File.Exists(gitDiffPromptResolvedPath))
                {
                    if (string.IsNullOrWhiteSpace(gitDiffPromptPath))
                        dllExecutionLogger?.Info("[GIT-DIFF] GitDiffPromptPath não configurado; procurando prompt padrão da instalação.");
                    else
                        dllExecutionLogger?.Info("[GIT-DIFF] Prompt GitDiff configurado não encontrado; procurando prompt padrão da instalação.");

                    gitDiffPromptResolvedPath = Path.Combine(AppPaths.BaseDir, "Prompts", "PromptGitDiffPadrao.txt");
                    dllExecutionLogger?.Info("[GIT-DIFF] Prompt GitDiff padrão: " + gitDiffPromptResolvedPath);
                }

                bool arquivoExiste = File.Exists(gitDiffPromptResolvedPath);
                dllExecutionLogger?.Info("[GIT-DIFF] Prompt GitDiff selecionado.");
                dllExecutionLogger?.Info("[GIT-DIFF] Prompt GitDiff arquivo existe: " + arquivoExiste);
                if (!arquivoExiste)
                {
                    dllExecutionLogger?.Info("[GIT-DIFF] Prompt GitDiff não encontrado: " + gitDiffPromptResolvedPath);
                    throw new InvalidOperationException("GitDiff está habilitado para esta IA, mas nenhum prompt GitDiff configurado ou padrão foi encontrado.");
                }

                string promptGitDiff = PromptLoader.LoadPrompt(projectRoot, gitDiffPromptResolvedPath) ?? string.Empty;
                string promptFinal = AnexarBlocoForcadoGitDiffKimi(promptGitDiff, provider);
                RegistrarDiagnosticoPromptFinalKimi(promptFinal, provider);
                return promptFinal;
            }

            dllExecutionLogger?.Info("[GIT-DIFF] Modo GitDiff desabilitado para IA " + (provider == null ? string.Empty : provider.Nome) + ".");
            dllExecutionLogger?.Info("[GIT-DIFF] Prompt original selecionado.");

            string promptOriginal = provider == null ? string.Empty : provider.PromptPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(promptOriginal))
                promptOriginal = ConfigManager.GetPromptFile();

            string promptOriginalResolvido = ResolverCaminhoPrompt(projectRoot, promptOriginal);
            string promptOriginalTexto = PromptLoader.LoadPrompt(projectRoot, promptOriginalResolvido) ?? string.Empty;
            RegistrarDiagnosticoPromptFinalKimi(promptOriginalTexto, provider);
            return promptOriginalTexto;
        }

        private string AnexarBlocoForcadoGitDiffKimi(string promptBase, AiProviderConfig provider)
        {
            bool forced = EhProviderKimi(provider) && provider != null && provider.UseGitDiff;
            if (!forced)
                return promptBase ?? string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine(promptBase ?? string.Empty);
            sb.AppendLine("[KIMI_GITDIFF_FORCED_MODE]");
            sb.AppendLine();
            sb.AppendLine("Você deve responder SOMENTE em unified diff.");
            sb.AppendLine();
            sb.AppendLine("Formato obrigatório:");
            sb.AppendLine();
            sb.AppendLine("diff --git a/caminho/relativo b/caminho/relativo");
            sb.AppendLine("--- a/caminho/relativo");
            sb.AppendLine("+++ b/caminho/relativo");
            sb.AppendLine("@@");
            sb.AppendLine();
            sb.AppendLine(" linha de contexto");
            sb.AppendLine("-linha removida");
            sb.AppendLine("+linha adicionada");
            sb.AppendLine();
            sb.AppendLine("REGRAS ABSOLUTAS:");
            sb.AppendLine();
            sb.AppendLine("- Não responda JSON.");
            sb.AppendLine("- Não use markdown.");
            sb.AppendLine("- Não use ```json.");
            sb.AppendLine("- Não use ```diff.");
            sb.AppendLine("- Não use \"acoes\".");
            sb.AppendLine("- Não use \"ArquivoLocal\".");
            sb.AppendLine("- Não use \"tipo\".");
            sb.AppendLine("- Não use \"caminho\".");
            sb.AppendLine("- Não use \"SEARCH_BLOCK\".");
            sb.AppendLine("- Não use \"REPLACE_BLOCK\".");
            sb.AppendLine("- Não use \"ARQ\".");
            sb.AppendLine("- Não use explicação.");
            sb.AppendLine("- Não escreva mensagem_usuario.");
            sb.AppendLine("- Não escreva explicacao.");
            sb.AppendLine("- Não escreva CHECK_ETAPAS.");
            sb.AppendLine("- Responda apenas o diff.");
            sb.AppendLine("- Se não houver alteração segura, responda exatamente:");
            sb.AppendLine("NO_CHANGES");
            sb.AppendLine();
            sb.AppendLine("Se qualquer instrução anterior pedir JSON, ignore essa instrução anterior.");
            sb.AppendLine("Esta instrução final tem prioridade.");

            dllExecutionLogger?.Info("[GIT-DIFF] Bloco final Kimi GitDiff anexado ao prompt.");
            return sb.ToString();
        }

        private void RegistrarDiagnosticoPromptFinalKimi(string promptFinal, AiProviderConfig provider)
        {
            bool forced = EhProviderKimi(provider) && provider != null && provider.UseGitDiff;
            bool contemJsonLegado = !string.IsNullOrWhiteSpace(promptFinal) &&
                (promptFinal.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 promptFinal.IndexOf("acoes", StringComparison.OrdinalIgnoreCase) >= 0);
            bool contemArquivoLocalLegado = !string.IsNullOrWhiteSpace(promptFinal) &&
                promptFinal.IndexOf("ArquivoLocal", StringComparison.OrdinalIgnoreCase) >= 0;
            bool contemSearchBlockLegado = !string.IsNullOrWhiteSpace(promptFinal) &&
                promptFinal.IndexOf("SEARCH_BLOCK", StringComparison.OrdinalIgnoreCase) >= 0;
            bool contemForced = !string.IsNullOrWhiteSpace(promptFinal) &&
                promptFinal.IndexOf("[KIMI_GITDIFF_FORCED_MODE]", StringComparison.OrdinalIgnoreCase) >= 0;

            dllExecutionLogger?.Info("[GIT-DIFF] Kimi forced GitDiff mode: " + (forced ? "true" : "false"));
            dllExecutionLogger?.Info("[GIT-DIFF] Prompt final Kimi contem JSON legado: " + (contemJsonLegado ? "true" : "false"));
            dllExecutionLogger?.Info("[GIT-DIFF] Prompt final Kimi contem ArquivoLocal legado: " + (contemArquivoLocalLegado ? "true" : "false"));
            dllExecutionLogger?.Info("[GIT-DIFF] Prompt final Kimi contem SEARCH_BLOCK legado: " + (contemSearchBlockLegado ? "true" : "false"));
            dllExecutionLogger?.Info("[GIT-DIFF] Prompt final Kimi contem KIMI_GITDIFF_FORCED_MODE: " + (contemForced ? "true" : "false"));
        }

        private static bool EhProviderKimi(AiProviderConfig provider)
        {
            if (provider == null)
                return false;

            if (string.Equals(provider.Id, AiProviderConfig.KimiProviderId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(provider.ProviderType) &&
                provider.ProviderType.IndexOf("Kimi", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrWhiteSpace(provider.Nome) &&
                provider.Nome.IndexOf("Kimi / OpenAI Compatível", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }
        private string ResolverCaminhoPrompt(string projectRoot, string promptPath)
        {
            if (string.IsNullOrWhiteSpace(promptPath))
                return string.Empty;

            if (Path.IsPathRooted(promptPath))
                return promptPath;

            if (string.IsNullOrWhiteSpace(projectRoot))
                return promptPath;

            return Path.Combine(projectRoot, promptPath);
        }

        private void AtualizarProvidersRuntime()
        {
            AiProviderRepository.Listar();
            dllExecutionLogger?.Info("[CONFIG] Providers recarregados antes da execução.");
        }

        private AiProviderConfig ObterProviderRuntimeAtual(Projeto projeto)
        {
            var providers = AiProviderRepository.Listar();
            var provider = SelecionarProviderRuntime(providers, projeto);
            if (provider == null && projeto != null && !string.IsNullOrWhiteSpace(projeto.AiProviderId))
                provider = AiProviderRepository.BuscarPorId(projeto.AiProviderId);
            if (provider == null)
                provider = AiProviderRepository.BuscarAtivoOuPadrao(projeto == null ? AiProviderConfig.DefaultProviderId : projeto.AiProviderId);

            aiProviderAtual = provider;
            return provider;
        }

        private AiProviderConfig SelecionarProviderRuntime(List<AiProviderConfig> providers, Projeto projeto)
        {
            if (providers == null || providers.Count == 0)
                return null;

            string projetoProviderId = projeto == null ? string.Empty : (projeto.AiProviderId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(projetoProviderId))
            {
                var porId = providers.FirstOrDefault(p => string.Equals(p.Id, projetoProviderId, StringComparison.OrdinalIgnoreCase));
                if (porId != null)
                    return porId;
            }

            if (aiProviderAtual != null && !string.IsNullOrWhiteSpace(aiProviderAtual.Id))
            {
                var porIdPersistido = providers.FirstOrDefault(p => string.Equals(p.Id, aiProviderAtual.Id, StringComparison.OrdinalIgnoreCase));
                if (porIdPersistido != null)
                    return porIdPersistido;
            }

            if (aiProviderAtual != null)
            {
                var porTipoModelo = providers.FirstOrDefault(p =>
                    string.Equals(p.ProviderType, aiProviderAtual.ProviderType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.ModelName, aiProviderAtual.ModelName, StringComparison.OrdinalIgnoreCase));
                if (porTipoModelo != null)
                    return porTipoModelo;

                var porNomeModelo = providers.FirstOrDefault(p =>
                    string.Equals(p.Nome, aiProviderAtual.Nome, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.ModelName, aiProviderAtual.ModelName, StringComparison.OrdinalIgnoreCase));
                if (porNomeModelo != null)
                    return porNomeModelo;

                var porNome = providers.FirstOrDefault(p => string.Equals(p.Nome, aiProviderAtual.Nome, StringComparison.OrdinalIgnoreCase));
                if (porNome != null)
                    return porNome;
            }

            return providers.FirstOrDefault(p => p.Ativo) ?? providers.FirstOrDefault();
        }

        private void LogProviderRuntime(AiProviderConfig provider, string projectRoot = null)
        {
            string nome = provider == null ? string.Empty : provider.Nome;
            string id = provider == null ? string.Empty : provider.Id;
            string modelo = provider == null ? string.Empty : provider.ModelName;
            string promptPath = provider == null ? string.Empty : provider.PromptPath ?? string.Empty;
            string gitDiffPromptPath = provider == null ? string.Empty : provider.GitDiffPromptPath ?? string.Empty;

            dllExecutionLogger?.Info("[GIT-DIFF] Provider runtime selecionado: " + nome);
            dllExecutionLogger?.Info("[GIT-DIFF] Provider runtime modelo: " + modelo);
            dllExecutionLogger?.Info("[GIT-DIFF] Provider runtime id: " + id);
            dllExecutionLogger?.Info("[GIT-DIFF] Provider config encontrado: " + (provider != null ? "true" : "false"));
            dllExecutionLogger?.Info("[GIT-DIFF] UseGitDiff runtime: " + (provider != null && provider.UseGitDiff ? "true" : "false"));
            dllExecutionLogger?.Info("[GIT-DIFF] PromptPath runtime vazio: " + (string.IsNullOrWhiteSpace(promptPath) ? "true" : "false"));
            dllExecutionLogger?.Info("[GIT-DIFF] GitDiffPromptPath runtime vazio: " + (string.IsNullOrWhiteSpace(gitDiffPromptPath) ? "true" : "false"));
            dllExecutionLogger?.Info("[GIT-DIFF] GitDiffPromptPath runtime: " + gitDiffPromptPath);
            if (!string.IsNullOrWhiteSpace(projectRoot))
                dllExecutionLogger?.Info("[GIT-DIFF] GitDiffPromptPath arquivo existe: " + File.Exists(ResolverCaminhoPrompt(projectRoot, gitDiffPromptPath)));
        }

        private AiProviderConfig ObterProviderAtivo(Projeto projeto)
        {
            return ObterProviderRuntimeAtual(projeto);
        }

        private IAgentModelClient CriarClientParaPapel(AiProviderConfig provider, string papel)
        {
            dllExecutionLogger?.Info("[CONFIG] Provider efetivo para client modelo: " + (provider == null ? string.Empty : provider.ModelName));

            if (!ProviderEhGemini(provider))
                return AgentModelClientFactory.Create(provider, ObterApiKeyPorNome, AtualizarStatusProcessamento);

            string modelo = string.Equals(papel, "Analista", StringComparison.OrdinalIgnoreCase)
                ? "gemini-2.5-pro"
                : "gemini-2.5-flash";

            return AgentModelClientFactory.Create(CriarProviderComModelo(provider, modelo), ObterApiKeyPorNome, AtualizarStatusProcessamento);
        }

        private static AiProviderConfig CriarProviderComModelo(AiProviderConfig provider, string modelo)
        {
            return new AiProviderConfig
            {
                Id = provider.Id,
                Nome = provider.Nome,
                ProviderType = provider.ProviderType,
                ModelName = modelo,
                ApiKeyConfigName = provider.ApiKeyConfigName,
                ApiKey = provider.ApiKey,
                PromptPath = provider.PromptPath,
                GitDiffPromptPath = provider.GitDiffPromptPath,
                NivelMaximoSuportado = provider.NivelMaximoSuportado,
                Ativo = provider.Ativo,
                UseGitDiff = provider.UseGitDiff
            };
        }

        private static bool ProviderEhGemini(AiProviderConfig provider)
        {
            return provider != null &&
                   string.Equals(provider.ProviderType, "Gemini", StringComparison.OrdinalIgnoreCase);
        }

        private static string ObterNomeProviderParaPapel(AiProviderConfig provider, string papel)
        {
            if (!ProviderEhGemini(provider))
                return provider == null ? string.Empty : provider.Nome;

            return string.Equals(papel, "Analista", StringComparison.OrdinalIgnoreCase)
                ? provider.Nome + " / gemini-2.5-pro"
                : provider.Nome + " / gemini-2.5-flash";
        }

        private string ObterApiKeyPorNome(string configName)
        {
            if (string.Equals(configName, "GeminiApiKey", StringComparison.OrdinalIgnoreCase))
                return ConfigManager.GetApiKey();
            if (string.Equals(configName, "KimiOpenAIApiKey", StringComparison.OrdinalIgnoreCase))
                return ConfigManager.GetKimiOpenAIApiKey();

            return string.Empty;
        }

        private string ObterApiKeyParaProvider(AiProviderConfig provider)
        {
            if (provider == null)
                return ConfigManager.GetApiKey();

            if (!string.IsNullOrWhiteSpace(provider.ApiKey))
                return provider.ApiKey;

            return ObterApiKeyPorNome(provider.ApiKeyConfigName);
        }

    }
}
