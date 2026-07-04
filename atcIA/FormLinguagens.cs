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

        public FormLinguagens()
        {
            Text = "Linguagens";
            StartPosition = FormStartPosition.CenterParent;
            Width = 780;
            Height = 420;
            MinimizeBox = false;
            MaximizeBox = false;

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
            btFechar.Click += (s, e) => Close();

            Controls.Add(grid);
            Controls.Add(btAdicionar);
            Controls.Add(btSelecionarPrompt);
            Controls.Add(btSalvar);
            Controls.Add(btFechar);

            Carregar();
        }

        private void Carregar()
        {
            perfis = LanguageProfileConfigRepository.Listar();
            AtualizarGrid();
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
            if (perfil == null)
                return;

            string atual = LanguageProfileConfigRepository.ResolverPromptPath(perfil);
            if (!string.IsNullOrWhiteSpace(atual) && File.Exists(atual))
                openFileDialog.FileName = atual;

            if (openFileDialog.ShowDialog(this) != DialogResult.OK)
                return;

            perfil.PromptPath = TornarCaminhoRelativoSePossivel(openFileDialog.FileName);
            perfil.PromptAtivo = true;
            AtualizarGrid();
            SelecionarPerfil(perfil);
        }

        private void btSalvar_Click(object sender, EventArgs e)
        {
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
