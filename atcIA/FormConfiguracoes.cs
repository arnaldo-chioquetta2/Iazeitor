using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GptBolDll;
using System.Windows.Forms;

namespace atcIA
{
    public partial class FormConfiguracoes : Form
    {
        private const string FeatureMarker = "PromptProviderLoadFix-v1;GitDiffConfigTraceFix-v1";
        private List<AiProviderConfig> providers = new List<AiProviderConfig>();

        public FormConfiguracoes()
        {
            InitializeComponent();
            textBox1.Text = ConfigManager.GetApiKey();
            textBoxAnalystPrompt.Text = ConfigManager.GetAnalystPromptPath();
            textBoxComplementPrompt.Text = ConfigManager.GetComplementPromptPath();
            textBoxKimiBaseUrl.Text = ConfigManager.GetKimiOpenAIBaseUrl();
            textBoxKimiApiKey.Text = ConfigManager.GetKimiOpenAIApiKey();
            cboKimiModel.Text = ConfigManager.GetKimiOpenAIModel();
            chkKimiEnableSearch.Checked = ConfigManager.GetKimiEnableSearch();
            chkKimiEnableThinking.Checked = ConfigManager.GetKimiEnableThinking();
            CarregarProviders();
            LogToConfigTrace("[CONFIG] FeatureMarker: " + FeatureMarker);
            LogToConfigTrace("[CONFIG-UI] GitDiffConfigTraceFix-v1 ativo.");
        }

        private void CarregarProviders()
        {
            providers = AiProviderRepository.Listar();
            listBoxAiProviders.Items.Clear();
            foreach (var provider in providers)
            {
                listBoxAiProviders.Items.Add(
                    provider.Nome + " | " +
                    provider.ProviderType + " | " +
                    provider.ModelName + " | Nivel " +
                    provider.NivelMaximoSuportado + " | " +
                    (provider.Ativo ? "Ativo" : "Inativo"));
            }

            if (listBoxAiProviders.Items.Count > 0)
            {
                listBoxAiProviders.SelectedIndex = 0;
                var providerSelecionado = listBoxAiProviders.SelectedItem as AiProviderConfig;
                if (providerSelecionado != null)
                    CarregarProviderNaTela(providerSelecionado);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (SalvarConfiguracoesTelaPrincipal(fecharAposSalvar: true))
            {
                MessageBox.Show("Configuracoes salvas com sucesso.");
                Close();
            }
        }

        private void buttonComplementPrompt_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Arquivos de prompt|*.txt;*.md;*.json|Todos os arquivos|*.*";
                openFileDialog.Title = "Selecione o prompt de complemento de operacao";

                var current = textBoxComplementPrompt.Text;
                if (!string.IsNullOrWhiteSpace(current))
                    openFileDialog.FileName = current;

                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                    textBoxComplementPrompt.Text = openFileDialog.FileName;
            }
        }

        private void buttonAnalystPrompt_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Arquivos de prompt|*.txt;*.md;*.json|Todos os arquivos|*.*";
                openFileDialog.Title = "Selecione o prompt do analista";

                var current = textBoxAnalystPrompt.Text;
                if (!string.IsNullOrWhiteSpace(current))
                    openFileDialog.FileName = current;

                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                    textBoxAnalystPrompt.Text = openFileDialog.FileName;
            }
        }

        private void listBoxAiProviders_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBoxAiProviders.SelectedIndex < 0 || listBoxAiProviders.SelectedIndex >= providers.Count)
                return;

            CarregarProviderNaTela(providers[listBoxAiProviders.SelectedIndex]);
        }

        private void CarregarProviderNaTela(AiProviderConfig provider)
        {
            if (provider == null)
                return;

            LogToConfigTrace("[CONFIG-UI] Provider selecionado tela: " + provider.Nome);
            LogToConfigTrace("[CONFIG-UI] ProviderId tela: " + provider.Id);
            LogToConfigTrace("[CONFIG-UI] txGitDiff.Text antes carregar: " + (txGitDiff == null ? string.Empty : (txGitDiff.Text ?? string.Empty)));
            LogToConfigTrace("[CONFIG-UI] provider.GitDiffPromptPath: " + (provider.GitDiffPromptPath ?? string.Empty));

            txtAiId.Text = provider.Id;
            txtAiNome.Text = provider.Nome;
            cboProviderType.Text = provider.ProviderType;
            txtAiModel.Text = provider.ModelName;
            txtAiKey.Text = provider.ApiKey;
            numericAiNivel.Value = AiProviderConfig.ClampLevel(provider.NivelMaximoSuportado);
            chkAiAtivo.Checked = provider.Ativo;
            ckGitDiff.Checked = provider.UseGitDiff;
            LogProviderSelection(provider);
            textBox2.Text = ObterPromptOriginalParaTela(provider, out bool usouFallbackGlobal);
            txGitDiff.Text = provider.GitDiffPromptPath ?? string.Empty;
            LogToConfigTrace("[CONFIG] txGitDiff carregado do provider: " + (provider != null ? "true" : "false"));
            LogToConfigTrace("[CONFIG] txGitDiff carregado vazio: " + (string.IsNullOrWhiteSpace(txGitDiff.Text) ? "true" : "false"));
            LogToConfigTrace("[CONFIG-UI] txGitDiff carregado do provider vazio: " + (string.IsNullOrWhiteSpace(txGitDiff.Text) ? "true" : "false"));
            LogPromptPath(provider, usouFallbackGlobal);
            LogGitDiff(provider);
            LogGitDiffPromptPath(provider);
            txtAiId.ReadOnly = true;
        }

        private void btNovaIa_Click(object sender, EventArgs e)
        {
            txtAiId.ReadOnly = false;
            txtAiId.Text = "";
            txtAiNome.Text = "";
            cboProviderType.Text = "Gemini";
            txtAiModel.Text = ObterModeloPadrao(cboProviderType.Text);
            txtAiKey.Text = "";
            numericAiNivel.Value = 6;
            chkAiAtivo.Checked = true;
            txtAiId.Focus();
        }

        private void btSalvarIa_Click(object sender, EventArgs e)
        {
            try
            {
                if (SalvarConfiguracoesTelaPrincipal(fecharAposSalvar: false))
                    CarregarProviders();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Configurar IA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool SalvarConfiguracoesTelaPrincipal(bool fecharAposSalvar)
        {
            LogConfig("[CONFIG-UI] EVENTO REAL DE SALVAR CONFIGURACOES ACIONADO.");

            ConfigManager.SaveApiKey(textBox1.Text);
            ConfigManager.SavePromptFile(textBox2.Text);
            ConfigManager.SaveAnalystPromptPath(textBoxAnalystPrompt.Text);
            ConfigManager.SaveComplementPromptPath(textBoxComplementPrompt.Text);
            ConfigManager.SaveKimiOpenAIBaseUrl(textBoxKimiBaseUrl.Text);
            ConfigManager.SaveKimiOpenAIApiKey(textBoxKimiApiKey.Text);
            ConfigManager.SaveKimiOpenAIModel(cboKimiModel.Text);
            ConfigManager.SaveKimiEnableSearch(chkKimiEnableSearch.Checked);
            ConfigManager.SaveKimiEnableThinking(chkKimiEnableThinking.Checked);

            bool providerSalvo = SalvarProviderSelecionado();

            return providerSalvo;
        }

        private bool SalvarProviderSelecionado()
        {
            if (providers == null || providers.Count == 0)
                CarregarProviders();

            var provider = ObterProviderVisualSelecionado();
            if (provider == null)
                throw new InvalidOperationException("Nenhum provider selecionado para salvar.");

            LogConfig("[CONFIG-UI] Provider salvo visual nome: " + provider.Nome);
            LogConfig("[CONFIG-UI] Provider salvo visual id: " + provider.Id);
            LogConfig("[CONFIG-UI] textBox2.Text salvar: " + (textBox2 == null ? string.Empty : (textBox2.Text ?? string.Empty)));
            LogConfig("[CONFIG-UI] txGitDiff.Text salvar: " + (txGitDiff == null ? string.Empty : (txGitDiff.Text ?? string.Empty)));
            LogConfig("[CONFIG-UI] ckGitDiff.Checked salvar: " + (ckGitDiff != null && ckGitDiff.Checked ? "true" : "false"));

            string providerId = provider.Id;
            string providerNome = provider.Nome;
            string promptPath = (textBox2.Text ?? string.Empty).Trim();
            string gitDiffPromptPath = (txGitDiff.Text ?? string.Empty).Trim();
            bool useGitDiff = ckGitDiff.Checked;

            LogConfig("[CONFIG] Salvando provider: " + providerNome);
            LogConfig("[CONFIG] ProviderId: " + providerId);
            LogConfig("[CONFIG] UseGitDiff salvo: " + (useGitDiff ? "true" : "false"));
            LogConfig("[CONFIG] PromptPath salvo vazio: " + (string.IsNullOrWhiteSpace(promptPath) ? "true" : "false"));
            LogConfig("[CONFIG] GitDiffPromptPath salvo vazio: " + (string.IsNullOrWhiteSpace(gitDiffPromptPath) ? "true" : "false"));
            LogConfig("[CONFIG] GitDiffPromptPath salvo: " + gitDiffPromptPath);

            provider.ProviderType = cboProviderType.Text.Trim();
            provider.ModelName = string.IsNullOrWhiteSpace(txtAiModel.Text) ? ObterModeloPadrao(cboProviderType.Text) : txtAiModel.Text.Trim();
            provider.ApiKeyConfigName = ObterApiKeyConfigName(cboProviderType.Text, providerId);
            provider.ApiKey = txtAiKey.Text;
            provider.PromptPath = promptPath;
            provider.GitDiffPromptPath = gitDiffPromptPath;
            provider.NivelMaximoSuportado = (int)numericAiNivel.Value;
            provider.Ativo = chkAiAtivo.Checked;
            provider.UseGitDiff = useGitDiff;

            if (useGitDiff && string.IsNullOrWhiteSpace(gitDiffPromptPath))
                LogWarningGitDiffSemPrompt(provider);

            LogConfig("[CONFIG-UI] provider.PromptPath antes repository vazio: " + string.IsNullOrWhiteSpace(provider.PromptPath));
            LogConfig("[CONFIG-UI] provider.GitDiffPromptPath antes repository vazio: " + string.IsNullOrWhiteSpace(provider.GitDiffPromptPath));
            LogConfig("[CONFIG-UI] provider.GitDiffPromptPath antes repository: " + (provider.GitDiffPromptPath ?? string.Empty));

            AiProviderRepository.Salvar(provider);
            LogPromptPath(provider, !string.IsNullOrWhiteSpace(provider.PromptPath));
            LogGitDiff(provider);
            LogGitDiffPromptPath(provider);

            var recarregado = AiProviderRepository.BuscarPorId(provider.Id);
            LogConfig("[CONFIG-STORE] Provider recarregado apos salvar: " + (recarregado == null ? string.Empty : recarregado.Nome));
            LogConfig("[CONFIG-STORE] ProviderId recarregado: " + (recarregado == null ? string.Empty : recarregado.Id));
            LogConfig("[CONFIG-STORE] UseGitDiff recarregado: " + (recarregado != null && recarregado.UseGitDiff ? "true" : "false"));
            LogConfig("[CONFIG-STORE] GitDiffPromptPath recarregado vazio: " + (recarregado == null || string.IsNullOrWhiteSpace(recarregado.GitDiffPromptPath) ? "true" : "false"));
            LogConfig("[CONFIG-STORE] GitDiffPromptPath recarregado: " + (recarregado == null ? string.Empty : recarregado.GitDiffPromptPath ?? string.Empty));

            if (useGitDiff && !string.IsNullOrWhiteSpace(gitDiffPromptPath) &&
                (recarregado == null || string.IsNullOrWhiteSpace(recarregado.GitDiffPromptPath)))
            {
                LogConfig("[CONFIG-UI] ERRO: txGitDiff tinha valor, mas reload voltou vazio.");
                MessageBox.Show("Erro: o caminho do prompt GitDiff não foi persistido no provider.", "Salvar configuracoes", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            try
            {
                string json = File.ReadAllText(AiProviderRepository.ProvidersFile);
                LogConfig("[CONFIG-STORE] Arquivo fisico contem GitDiffPromptPath: " + json.Contains("GitDiffPromptPath"));
                LogConfig("[CONFIG-STORE] Arquivo fisico contem kimi-openai-compatible: " + (json.IndexOf(AiProviderConfig.KimiProviderId, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            catch
            {
            }

            return true;
        }

        private AiProviderConfig ObterProviderVisualSelecionado()
        {
            if (listBoxAiProviders != null && listBoxAiProviders.SelectedIndex >= 0 && listBoxAiProviders.SelectedIndex < providers.Count)
                return providers[listBoxAiProviders.SelectedIndex];

            string idTela = txtAiId == null ? string.Empty : (txtAiId.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(idTela))
            {
                var provider = providers.FirstOrDefault(p => string.Equals(p.Id, idTela, StringComparison.OrdinalIgnoreCase));
                if (provider != null)
                    return provider;

                return AiProviderRepository.BuscarPorId(idTela);
            }

            return null;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            SelecionarArquivoPrompt(textBox2);
        }

        private void btGitDiff_Click(object sender, EventArgs e)
        {
            SelecionarArquivoPrompt(txGitDiff);
            LogConfig("[CONFIG-UI] btGitDiff selecionou: " + (txGitDiff == null ? string.Empty : (txGitDiff.Text ?? string.Empty)));
            LogConfig("[CONFIG-UI] txGitDiff.Text apos btGitDiff: " + (txGitDiff == null ? string.Empty : (txGitDiff.Text ?? string.Empty)));
        }

        private void SelecionarArquivoPrompt(TextBox target)
        {
            if (target == null)
                return;

            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Prompt/Text files (*.txt)|*.txt|All files (*.*)|*.*";
                openFileDialog.Title = "Selecione um arquivo de prompt";

                var current = target.Text;
                if (!string.IsNullOrWhiteSpace(current))
                    openFileDialog.FileName = current;

                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                    target.Text = openFileDialog.FileName;
            }
        }

        private string ObterPromptOriginalParaTela(AiProviderConfig provider, out bool usouFallbackGlobal)
        {
            usouFallbackGlobal = false;
            string promptProvider = provider == null ? string.Empty : (provider.PromptPath ?? string.Empty).Trim();
            bool providerVazio = string.IsNullOrWhiteSpace(promptProvider);
            string fallbackGlobal = string.Empty;

            if (providerVazio)
                fallbackGlobal = (ConfigManager.GetPromptFile() ?? string.Empty).Trim();

            if (providerVazio && !string.IsNullOrWhiteSpace(fallbackGlobal))
            {
                usouFallbackGlobal = true;
                string nome = provider == null ? string.Empty : provider.Nome;
                LogConfig("[CONFIG] Prompt original carregado de fallback global para IA " + nome + ".");
                return fallbackGlobal;
            }

            return promptProvider;
        }

        private void LogProviderSelection(AiProviderConfig provider)
        {
            string nome = provider == null ? string.Empty : provider.Nome;
            LogConfig("[CONFIG-UI] Provider selecionado tela: " + nome);
            LogConfig("[CONFIG-UI] ProviderId tela: " + (provider == null ? string.Empty : provider.Id));
            LogConfig("[CONFIG-UI] provider.GitDiffPromptPath: " + (provider == null ? string.Empty : provider.GitDiffPromptPath ?? string.Empty));
            LogConfig("[CONFIG-UI] txGitDiff carregado do provider vazio: " + (provider == null || string.IsNullOrWhiteSpace(provider.GitDiffPromptPath) ? "true" : "false"));
            LogConfig("[CONFIG] Provider selecionado: " + nome);
            LogConfig("[CONFIG] PromptPath provider vazio: " + (provider == null || string.IsNullOrWhiteSpace(provider.PromptPath) ? "true" : "false"));
            LogConfig("[CONFIG] GitDiffPromptPath provider vazio: " + (provider == null || string.IsNullOrWhiteSpace(provider.GitDiffPromptPath) ? "true" : "false"));
        }

        private void LogPromptPath(AiProviderConfig provider, bool carregadoNaTela)
        {
            string nome = provider == null ? string.Empty : provider.Nome;
            string mensagem = "[CONFIG] Prompt original carregado para IA " + nome + ": " + (carregadoNaTela ? "true" : "false");
            LogConfig(mensagem);
        }

        private void LogGitDiff(AiProviderConfig provider)
        {
            string nome = provider == null ? string.Empty : provider.Nome;
            string mensagem = "[CONFIG] GitDiff carregado para IA " + nome + ": " + (provider != null && provider.UseGitDiff ? "true" : "false");
            LogConfig(mensagem);
        }

        private void LogGitDiffPromptPath(AiProviderConfig provider)
        {
            string nome = provider == null ? string.Empty : provider.Nome;
            string mensagem = "[CONFIG] Prompt GitDiff carregado para IA " + nome + ": " + (provider != null && !string.IsNullOrWhiteSpace(provider.GitDiffPromptPath) ? "true" : "false");
            LogConfig(mensagem);
        }

        private void LogWarningGitDiffSemPrompt(AiProviderConfig provider)
        {
            string nome = provider == null ? string.Empty : provider.Nome;
            LogConfig("[CONFIG] GitDiff habilitado sem prompt GitDiff configurado.");
            LogConfig("[CONFIG] GitDiff carregado para IA " + nome + ": true");
        }

        private static void LogConfig(string mensagem)
        {
            System.Diagnostics.Debug.WriteLine(mensagem);
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config-diagnostics.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? AppDomain.CurrentDomain.BaseDirectory);
                File.AppendAllText(logPath, mensagem + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static void LogToConfigTrace(string mensagem)
        {
            LogConfig(mensagem);
        }

        private static string ObterModeloPadrao(string providerType)
        {
            if (string.Equals(providerType, "OpenAI", StringComparison.OrdinalIgnoreCase))
                return "gpt-5.2";
            if (string.Equals(providerType, "Groq", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(providerType, "Grog", StringComparison.OrdinalIgnoreCase))
                return "llama-3.3-70b-versatile";
            if (string.Equals(providerType, "Mistral", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(providerType, "Mixtral", StringComparison.OrdinalIgnoreCase))
                return "codestral-latest";
            if (string.Equals(providerType, "DeepSeek", StringComparison.OrdinalIgnoreCase))
                return "deepseek-chat";
            if (string.Equals(providerType, "KimiOpenAICompatible", StringComparison.OrdinalIgnoreCase))
                return "kimi-k2";

            return "gemini-2.5-flash";
        }

        private static string ObterApiKeyConfigName(string providerType, string id)
        {
            if (string.Equals(providerType, "OpenAI", StringComparison.OrdinalIgnoreCase))
                return "OpenAiApiKey";
            if (string.Equals(providerType, "Groq", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(providerType, "Grog", StringComparison.OrdinalIgnoreCase))
                return "GroqApiKey";
            if (string.Equals(providerType, "Mistral", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(providerType, "Mixtral", StringComparison.OrdinalIgnoreCase))
                return "MistralApiKey";
            if (string.Equals(providerType, "DeepSeek", StringComparison.OrdinalIgnoreCase))
                return "DeepSeekApiKey";
            if (string.Equals(providerType, "KimiOpenAICompatible", StringComparison.OrdinalIgnoreCase))
                return "KimiOpenAIApiKey";
            if (string.Equals(providerType, "Gemini", StringComparison.OrdinalIgnoreCase))
                return "GeminiApiKey";

            return (id ?? string.Empty).Trim() + "ApiKey";
        }
    }
}
