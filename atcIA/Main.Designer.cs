using System;

namespace atcIA
{
    partial class Main
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Main));
            this.button1 = new System.Windows.Forms.Button();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.textBoxContinuacao = new System.Windows.Forms.TextBox();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.listBoxAcoes = new System.Windows.Forms.ListBox();
            this.listBoxEtapas = new System.Windows.Forms.ListBox();
            this.textBoxLog = new System.Windows.Forms.TextBox();
            this.button2 = new System.Windows.Forms.Button();
            this.buttonStatus = new System.Windows.Forms.Button();
            this.textBoxTokens = new System.Windows.Forms.TextBox();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.toolStripStatusLabel1 = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel2 = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel3 = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabelPerfil = new System.Windows.Forms.ToolStripStatusLabel();
            this.cbProjetos = new System.Windows.Forms.ComboBox();
            this.btAdicionarTarefa = new System.Windows.Forms.Button();
            this.btEditarProjeto = new System.Windows.Forms.Button();
            this.btLinguagens = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.labelContinuacao = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.labelEtapas = new System.Windows.Forms.Label();
            this.statusStrip1.SuspendLayout();
            this.SuspendLayout();
            //
            // button1
            //
            this.button1.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button1.Location = new System.Drawing.Point(445, 52);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(115, 32);
            this.button1.TabIndex = 2;
            this.button1.Text = "Processar";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            //
            // textBox1
            //
            this.textBox1.Location = new System.Drawing.Point(16, 125);
            this.textBox1.Multiline = true;
            this.textBox1.Name = "textBox1";
            this.textBox1.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox1.Size = new System.Drawing.Size(716, 95);
            this.textBox1.TabIndex = 5;
            //
            // textBoxContinuacao
            //
            this.textBoxContinuacao.Location = new System.Drawing.Point(16, 255);
            this.textBoxContinuacao.Multiline = true;
            this.textBoxContinuacao.Name = "textBoxContinuacao";
            this.textBoxContinuacao.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBoxContinuacao.Size = new System.Drawing.Size(716, 70);
            this.textBoxContinuacao.TabIndex = 6;
            //
            // textBox2
            //
            this.textBox2.Location = new System.Drawing.Point(16, 360);
            this.textBox2.Multiline = true;
            this.textBox2.Name = "textBox2";
            this.textBox2.ReadOnly = true;
            this.textBox2.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox2.Size = new System.Drawing.Size(716, 70);
            this.textBox2.TabIndex = 7;
            this.textBox2.TabStop = false;
            this.textBox2.WordWrap = false;
            this.textBox2.Click += new System.EventHandler(this.CopiarConteudoTextBox_Click);
            //
            // listBoxAcoes
            //
            this.listBoxAcoes.FormattingEnabled = true;
            this.listBoxAcoes.ItemHeight = 17;
            this.listBoxAcoes.Location = new System.Drawing.Point(16, 575);
            this.listBoxAcoes.Name = "listBoxAcoes";
            this.listBoxAcoes.Size = new System.Drawing.Size(716, 55);
            this.listBoxAcoes.TabIndex = 9;
            //
            // listBoxEtapas
            //
            this.listBoxEtapas.FormattingEnabled = true;
            this.listBoxEtapas.ItemHeight = 17;
            this.listBoxEtapas.Location = new System.Drawing.Point(16, 465);
            this.listBoxEtapas.Name = "listBoxEtapas";
            this.listBoxEtapas.Size = new System.Drawing.Size(716, 72);
            this.listBoxEtapas.TabIndex = 18;
            //
            // textBoxLog
            //
            this.textBoxLog.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)));
            this.textBoxLog.Location = new System.Drawing.Point(16, 670);
            this.textBoxLog.Multiline = true;
            this.textBoxLog.Name = "textBoxLog";
            this.textBoxLog.ReadOnly = true;
            this.textBoxLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBoxLog.Size = new System.Drawing.Size(716, 65);
            this.textBoxLog.TabIndex = 10;
            this.textBoxLog.TabStop = false;
            this.textBoxLog.WordWrap = false;
            this.textBoxLog.Click += new System.EventHandler(this.CopiarConteudoTextBox_Click);
            //
            // button2
            //
            this.button2.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.button2.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button2.Location = new System.Drawing.Point(654, 52);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(80, 32);
            this.button2.TabIndex = 3;
            this.button2.Text = "Configurar";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            //
            // buttonStatus
            //
            this.buttonStatus.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.buttonStatus.BackColor = System.Drawing.Color.Gray;
            this.buttonStatus.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.buttonStatus.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.buttonStatus.ForeColor = System.Drawing.Color.White;
            this.buttonStatus.Location = new System.Drawing.Point(628, 14);
            this.buttonStatus.Name = "buttonStatus";
            this.buttonStatus.Size = new System.Drawing.Size(106, 32);
            this.buttonStatus.TabIndex = 16;
            this.buttonStatus.TabStop = false;
            this.buttonStatus.Text = "Interno";
            this.buttonStatus.UseVisualStyleBackColor = false;
            this.buttonStatus.Visible = false;
            //
            // textBoxTokens
            //
            this.textBoxTokens.Location = new System.Drawing.Point(80, 54);
            this.textBoxTokens.Name = "textBoxTokens";
            this.textBoxTokens.ReadOnly = true;
            this.textBoxTokens.Size = new System.Drawing.Size(355, 25);
            this.textBoxTokens.TabIndex = 14;
            this.textBoxTokens.TabStop = false;
            this.textBoxTokens.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            //
            // statusStrip1
            //
            this.statusStrip1.AutoSize = false;
            this.statusStrip1.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripStatusLabel1,
            this.toolStripStatusLabel2,
            this.toolStripStatusLabel3,
            this.toolStripStatusLabelPerfil});
            this.statusStrip1.Location = new System.Drawing.Point(0, 733);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(746, 28);
            this.statusStrip1.TabIndex = 15;
            this.statusStrip1.Text = "statusStrip1";
            //
            // toolStripStatusLabel1
            //
            this.toolStripStatusLabel1.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            this.toolStripStatusLabel1.Size = new System.Drawing.Size(122, 23);
            this.toolStripStatusLabel1.Text = "Status: aguardando";
            //
            // toolStripStatusLabel2
            //
            this.toolStripStatusLabel2.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.toolStripStatusLabel2.Name = "toolStripStatusLabel2";
            this.toolStripStatusLabel2.Size = new System.Drawing.Size(46, 23);
            this.toolStripStatusLabel2.Text = "IA: n/d";
            //
            // toolStripStatusLabel3
            //
            this.toolStripStatusLabel3.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.toolStripStatusLabel3.Name = "toolStripStatusLabel3";
            this.toolStripStatusLabel3.Size = new System.Drawing.Size(86, 23);
            this.toolStripStatusLabel3.Text = "Tempo: 00:00";
            //
            // toolStripStatusLabelPerfil
            //
            this.toolStripStatusLabelPerfil.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.toolStripStatusLabelPerfil.Name = "toolStripStatusLabelPerfil";
            this.toolStripStatusLabelPerfil.Size = new System.Drawing.Size(75, 23);
            this.toolStripStatusLabelPerfil.Text = "Perfil: Geral";
            //
            // cbProjetos
            //
            this.cbProjetos.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbProjetos.FormattingEnabled = true;
            this.cbProjetos.Location = new System.Drawing.Point(80, 16);
            this.cbProjetos.Name = "cbProjetos";
            this.cbProjetos.Size = new System.Drawing.Size(260, 25);
            this.cbProjetos.TabIndex = 0;
            this.cbProjetos.SelectedIndexChanged += new System.EventHandler(this.cbProjetos_SelectedIndexChanged);
            //
            // btAdicionarTarefa
            //
            this.btAdicionarTarefa.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btAdicionarTarefa.Location = new System.Drawing.Point(350, 14);
            this.btAdicionarTarefa.Name = "btAdicionarTarefa";
            this.btAdicionarTarefa.Size = new System.Drawing.Size(135, 32);
            this.btAdicionarTarefa.TabIndex = 1;
            this.btAdicionarTarefa.Text = "Adicionar Projeto";
            this.btAdicionarTarefa.UseVisualStyleBackColor = true;
            this.btAdicionarTarefa.Click += new System.EventHandler(this.btAdicionarTarefa_Click);
            //
            // btEditarProjeto
            //
            this.btEditarProjeto.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btEditarProjeto.Location = new System.Drawing.Point(495, 14);
            this.btEditarProjeto.Name = "btEditarProjeto";
            this.btEditarProjeto.Size = new System.Drawing.Size(125, 32);
            this.btEditarProjeto.TabIndex = 17;
            this.btEditarProjeto.Text = "Editar Projeto";
            this.btEditarProjeto.UseVisualStyleBackColor = true;
            this.btEditarProjeto.Click += new System.EventHandler(this.btEditarProjeto_Click);
            //
            // btLinguagens
            //
            this.btLinguagens.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.btLinguagens.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btLinguagens.Location = new System.Drawing.Point(566, 52);
            this.btLinguagens.Name = "btLinguagens";
            this.btLinguagens.Size = new System.Drawing.Size(80, 32);
            this.btLinguagens.TabIndex = 21;
            this.btLinguagens.Text = "Linguagens";
            this.btLinguagens.UseVisualStyleBackColor = true;
            this.btLinguagens.Click += new System.EventHandler(this.btLinguagens_Click);
            //
            // label1
            //
            this.label1.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(16, 20);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(60, 24);
            this.label1.TabIndex = 4;
            this.label1.Text = "Projeto:";
            //
            // label2
            //
            this.label2.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(16, 100);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(180, 24);
            this.label2.TabIndex = 7;
            this.label2.Text = "Entrada do usuário:";
            //
            // labelContinuacao
            //
            this.labelContinuacao.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labelContinuacao.Location = new System.Drawing.Point(16, 230);
            this.labelContinuacao.Name = "labelContinuacao";
            this.labelContinuacao.Size = new System.Drawing.Size(260, 24);
            this.labelContinuacao.TabIndex = 20;
            this.labelContinuacao.Text = "Continuação da tarefa anterior:";
            //
            // label3
            //
            this.label3.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.Location = new System.Drawing.Point(16, 335);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(180, 24);
            this.label3.TabIndex = 8;
            this.label3.Text = "Resposta da IA:";
            //
            // label4
            //
            this.label4.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.Location = new System.Drawing.Point(16, 550);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(180, 24);
            this.label4.TabIndex = 11;
            this.label4.Text = "Arquivos alterados:";
            //
            // label5
            //
            this.label5.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label5.Location = new System.Drawing.Point(16, 645);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(180, 24);
            this.label5.TabIndex = 12;
            this.label5.Text = "Log técnico:";
            //
            // label7
            //
            this.label7.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label7.Location = new System.Drawing.Point(16, 58);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(60, 24);
            this.label7.TabIndex = 14;
            this.label7.Text = "Tokens:";
            //
            // labelEtapas
            //
            this.labelEtapas.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labelEtapas.Location = new System.Drawing.Point(16, 440);
            this.labelEtapas.Name = "labelEtapas";
            this.labelEtapas.Size = new System.Drawing.Size(180, 24);
            this.labelEtapas.TabIndex = 19;
            this.labelEtapas.Text = "Etapas definidas:";
            //
            // Main
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(746, 761);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.labelEtapas);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.labelContinuacao);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.textBoxTokens);
            this.Controls.Add(this.textBoxLog);
            this.Controls.Add(this.listBoxAcoes);
            this.Controls.Add(this.listBoxEtapas);
            this.Controls.Add(this.textBox2);
            this.Controls.Add(this.btAdicionarTarefa);
            this.Controls.Add(this.btEditarProjeto);
            this.Controls.Add(this.btLinguagens);
            this.Controls.Add(this.cbProjetos);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.buttonStatus);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.textBoxContinuacao);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.statusStrip1);
            this.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Main";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "atcIa";
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.TextBox textBoxContinuacao;
        private System.Windows.Forms.TextBox textBox2;
        private System.Windows.Forms.ListBox listBoxAcoes;
        private System.Windows.Forms.ListBox listBoxEtapas;
        private System.Windows.Forms.TextBox textBoxLog;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button buttonStatus;
        private System.Windows.Forms.TextBox textBoxTokens;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel2;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel3;
        private System.Windows.Forms.ComboBox cbProjetos;
        private System.Windows.Forms.Button btAdicionarTarefa;
        private System.Windows.Forms.Button btEditarProjeto;
        private System.Windows.Forms.Button btLinguagens;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label labelContinuacao;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label labelEtapas;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabelPerfil;
    }
}
