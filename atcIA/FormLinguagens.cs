using GptBolDll;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace atcIA
{
    public class FormLinguagens : Form
    {
        private readonly DataGridView grid;
        private readonly Button btAdicionar;
        private readonly Button btSelecionarPrompt;
        private readonly Button btSalvar;
        private readonly Button btFechar;
        private readonly OpenFileDialog openFileDialog;
        private List<LanguageProfileConfig> perfis;
        private const string FeatureMarker = "ProviderLanguageFallback-v1;LanguageProviderListFix-v1;LanguageProviderDiag-v1;LanguageProviderUiFix-v1";
        private readonly bool modoSelecaoProvider;
        private readonly ComboBox cboProviderFallback;
        private static int contadorCarregarListaProvider;
        public string SelectedProviderId { get; private set; }

        public FormLinguagens() : this(false, null) { }

        public FormLinguagens(string providerIdAtual) : this(true, providerIdAtual) { }

        private FormLinguagens(bool modoSelecaoProvider, string providerIdAtual)
        {
            this.modoSelecaoProvider = modoSelecaoProvider;
            Text = modoSelecaoProvider ? "Selecionar IA" : "Linguagens";
            StartPosition = FormStartPosition.CenterParent;
            Width = 780;
            Height = 420;
            MinimizeBox = false;
            MaximizeBox = false;

            if (modoSelecaoProvider)
            {
                cboProviderFallback = new ComboBox { Left = 12, Top = 12, Width = 520, DropDownStyle = ComboBoxStyle.DropDownList };
                contadorCarregarListaProvider++;
                RegistrarLogProvider("[FORM-LINGUAGENS] CarregarLista chamado.");
                RegistrarLogProvider("[FORM-LINGUAGENS] Contador: " + contadorCarregarListaProvider);
                var todosProviders = AiProviderRepository.Listar() ?? new List<AiProviderConfig>();
                RegistrarDiagnosticoProviders(todosProviders);
                RegistrarLogProvider("[FORM-LINGUAGENS] Providers recebidos: " + todosProviders.Count);
                var providers = todosProviders
                    .Where(ProviderDisponivelParaFallback)
                    .OrderBy(p => p.Nome ?? p.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                RegistrarLogProvider("[FORM-LINGUAGENS] Providers apos filtro: " + providers.Count);
                foreach (var providerFiltrado in providers)
                    RegistrarLogProvider("[FORM-LINGUAGENS] Filtro Provider Id=" + (providerFiltrado.Id ?? string.Empty) + " Nome=" + (providerFiltrado.Nome ?? string.Empty));
                RegistrarLogProvider("[FORM-LINGUAGENS] Quantidade de itens a adicionar: " + providers.Count);
                cboProviderFallback.DataSource = null;
                cboProviderFallback.DisplayMember = "Nome";
                cboProviderFallback.ValueMember = "Id";
                cboProviderFallback.DataSource = providers;
                cboProviderFallback.SelectedIndex = -1;
                RegistrarLogProvider("[FORM-LINGUAGENS] DataSource configurado: true");
                RegistrarLogProvider("[FORM-LINGUAGENS] Items.Count: " + cboProviderFallback.Items.Count);
                RegistrarLogProvider("[FORM-LINGUAGENS] ValueMember: " + cboProviderFallback.ValueMember + " DisplayMember: " + cboProviderFallback.DisplayMember);
                for (int indiceItem = 0; indiceItem < cboProviderFallback.Items.Count; indiceItem++)
                    RegistrarLogProvider("[FORM-LINGUAGENS] Item[" + indiceItem + "]=\"" + cboProviderFallback.GetItemText(cboProviderFallback.Items[indiceItem]) + "\"");
                if (!string.IsNullOrWhiteSpace(providerIdAtual))
                {
                    var providerAtual = providers.FirstOrDefault(p => string.Equals(p.Id, providerIdAtual, StringComparison.OrdinalIgnoreCase));
                    if (providerAtual != null)
                        cboProviderFallback.SelectedItem = providerAtual;
                }
                Controls.Add(cboProviderFallback);
            }

            grid = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 320,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Id", HeaderText = "Identificador", Width = 120 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Nome", HeaderText = "Nome", Width = 160 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "PromptPath", HeaderText = "Arquivo de prompt", Width = 330 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "PromptAtivo", HeaderText = "Ativo", Width = 60 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Sistema", HeaderText = "Sistema", Width = 70, ReadOnly = true });
            if (modoSelecaoProvider) grid.Visible = false;

            btAdicionar = new Button { Text = "Adicionar", Left = 12, Top = 332, Width = 95, Height = 28 };
            btSelecionarPrompt = new Button { Text = "Selecionar prompt", Left = 113, Top = 332, Width = 125, Height = 28 };
            btSalvar = new Button { Text = "Salvar", Left = 560, Top = 332, Width = 90, Height = 28 };
            btFechar = new Button { Text = "Fechar", Left = 656, Top = 332, Width = 90, Height = 28 };
            openFileDialog = new OpenFileDialog
            {
                Title = "Selecionar arquivo de prompt",
                Filter = "Arquivos de texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*"
            };

            btAdicionar.Click += btAdicionar_Click;
            btSelecionarPrompt.Click += btSelecionarPrompt_Click;
            btSalvar.Click += btSalvar_Click;
            if (modoSelecaoProvider)
            {
                btAdicionar.Visible = false;
                btSelecionarPrompt.Visible = false;
                btSalvar.Text = "Selecionar";
            }
            btFechar.Click += (s, e) => Close();

            Controls.Add(grid);
            Controls.Add(btAdicionar);
            Controls.Add(btSelecionarPrompt);
            Controls.Add(btSalvar);
            Controls.Add(btFechar);

            Carregar();
            if (modoSelecaoProvider && cboProviderFallback != null)
            {
                RegistrarLogProvider("[FORM-LINGUAGENS] Apos Carregar DataSource=" + (cboProviderFallback.DataSource == null ? "null" : "configurado") +
                    " Items.Count=" + cboProviderFallback.Items.Count +
                    " SelectedIndex=" + cboProviderFallback.SelectedIndex +
                    " SelectedItem=" + (cboProviderFallback.SelectedItem == null ? "" : cboProviderFallback.GetItemText(cboProviderFallback.SelectedItem)) +
                    " SelectedValue=" + (cboProviderFallback.SelectedValue == null ? "" : cboProviderFallback.SelectedValue.ToString()));
            }
        }

        private void Carregar()
        {
            perfis = LanguageProfileConfigRepository.Listar();
            AtualizarGrid();
        }

        private static bool ProviderDisponivelParaFallback(AiProviderConfig provider)
        {
            return provider != null && provider.Ativo &&
                   !string.IsNullOrWhiteSpace(provider.ApiKey) &&
                   !string.IsNullOrWhiteSpace(provider.ModelName);
        }

        private static void RegistrarDiagnosticoProviders(IEnumerable<AiProviderConfig> providers)
        {
            foreach (var provider in providers ?? Enumerable.Empty<AiProviderConfig>())
            {
                RegistrarLogProvider("[FORM-LINGUAGENS] Nome: " + (provider == null ? string.Empty : provider.Nome ?? provider.Id) +
                    " Ativo=" + (provider != null && provider.Ativo ? "true" : "false") +
                    " API key=" + (provider != null && !string.IsNullOrWhiteSpace(provider.ApiKey) ? "true" : "false") +
                    " Modelo=" + (provider != null && !string.IsNullOrWhiteSpace(provider.ModelName) ? "true" : "false") +
                    " Id=" + (provider == null ? string.Empty : provider.Id ?? string.Empty));
            }
        }

        private static void RegistrarLogProvider(string mensagem)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(mensagem);
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config-diagnostics.log"), mensagem + Environment.NewLine);
            }
            catch { }
        }
        private void AtualizarGrid()
        {
            grid.DataSource = null;
            grid.DataSource = perfis;
        }

        private void btAdicionar_Click(object sender, EventArgs e)
        {
            string idBase = "nova-linguagem";
            string id = idBase;
            int sequencia = 2;
            while (perfis.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                id = idBase + "-" + sequencia;
                sequencia++;
            }
            var novo = new LanguageProfileConfig
            {
                Id = id,
                Nome = "Nova linguagem",
                PromptPath = string.Empty,
                PromptAtivo = false,
                Sistema = false
            };
            perfis.Add(novo);
            AtualizarGrid();
            SelecionarPerfil(novo);
        }

        private void btSelecionarPrompt_Click(object sender, EventArgs e)
        {
            var perfil = ObterPerfilSelecionado();
            if (perfil == null) return;
            string atual = LanguageProfileConfigRepository.ResolverPromptPath(perfil);
            if (!string.IsNullOrWhiteSpace(atual) && File.Exists(atual)) openFileDialog.FileName = atual;
            if (openFileDialog.ShowDialog(this) != DialogResult.OK) return;
            perfil.PromptPath = TornarCaminhoRelativoSePossivel(openFileDialog.FileName);
            perfil.PromptAtivo = true;
            AtualizarGrid();
            SelecionarPerfil(perfil);
        }

        private void btSalvar_Click(object sender, EventArgs e)
        {
            if (modoSelecaoProvider)
            {
                var providerSelecionado = cboProviderFallback == null
                    ? null
                    : cboProviderFallback.SelectedItem as AiProviderConfig;

                if (!ProviderDisponivelParaFallback(providerSelecionado) ||
                    string.IsNullOrWhiteSpace(providerSelecionado.Id))
                {
                    MessageBox.Show(
                        "Selecione uma IA ativa, configurada e com modelo válido.",
                        "Selecionar IA",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                SelectedProviderId = providerSelecionado.Id;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            grid.EndEdit();
            foreach (var perfil in perfis)
            {
                perfil.Id = LanguageProfileConfigRepository.NormalizeId(perfil.Id);
                perfil.Nome = string.IsNullOrWhiteSpace(perfil.Nome) ? perfil.Id : perfil.Nome.Trim();
                perfil.PromptPath = perfil.PromptPath ?? string.Empty;
            }
            if (perfis.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            {
                MessageBox.Show("Existem linguagens com identificador duplicado.", "Linguagens", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            LanguageProfileConfigRepository.Salvar(perfis);
            MessageBox.Show("Linguagens salvas.", "Linguagens", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Carregar();
        }

        private LanguageProfileConfig ObterPerfilSelecionado()
        {
            if (grid.CurrentRow == null)
                return null;

            return grid.CurrentRow.DataBoundItem as LanguageProfileConfig;
        }

        private void SelecionarPerfil(LanguageProfileConfig perfil)
        {
            if (perfil == null)
                return;

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (ReferenceEquals(row.DataBoundItem, perfil))
                {
                    row.Selected = true;
                    grid.CurrentCell = row.Cells[0];
                    break;
                }
            }
        }

        private static string TornarCaminhoRelativoSePossivel(string path)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (path.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return path.Substring(baseDir.Length);

            return path;
        }
    }
}
