namespace atcIA
{
    partial class FormConfiguracoes
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TextBox textBox1;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.button1 = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.button2 = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.listBoxAiProviders = new System.Windows.Forms.ListBox();
            this.label4 = new System.Windows.Forms.Label();
            this.txtAiId = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.txtAiNome = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.cboProviderType = new System.Windows.Forms.ComboBox();
            this.label7 = new System.Windows.Forms.Label();
            this.txtAiModel = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            this.txtAiKey = new System.Windows.Forms.TextBox();
            this.label9 = new System.Windows.Forms.Label();
            this.numericAiNivel = new System.Windows.Forms.NumericUpDown();
            this.chkAiAtivo = new System.Windows.Forms.CheckBox();
            this.btNovaIa = new System.Windows.Forms.Button();
            this.btSalvarIa = new System.Windows.Forms.Button();
            this.labelAnalystPrompt = new System.Windows.Forms.Label();
            this.textBoxAnalystPrompt = new System.Windows.Forms.TextBox();
            this.buttonAnalystPrompt = new System.Windows.Forms.Button();
            this.labelComplementPrompt = new System.Windows.Forms.Label();
            this.textBoxComplementPrompt = new System.Windows.Forms.TextBox();
            this.buttonComplementPrompt = new System.Windows.Forms.Button();
            this.labelKimiHeader = new System.Windows.Forms.Label();
            this.labelKimiBaseUrl = new System.Windows.Forms.Label();
            this.textBoxKimiBaseUrl = new System.Windows.Forms.TextBox();
            this.labelKimiApiKey = new System.Windows.Forms.Label();
            this.textBoxKimiApiKey = new System.Windows.Forms.TextBox();
            this.labelKimiModel = new System.Windows.Forms.Label();
            this.cboKimiModel = new System.Windows.Forms.ComboBox();
            this.chkKimiEnableSearch = new System.Windows.Forms.CheckBox();
            this.chkKimiEnableThinking = new System.Windows.Forms.CheckBox();
            this.ckGitDiff = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.txGitDiff = new System.Windows.Forms.TextBox();
            this.btGitDiff = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.numericAiNivel)).BeginInit();
            this.SuspendLayout();
            //
            // textBox1
            //
            this.textBox1.Location = new System.Drawing.Point(-1000, -1000);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(10, 20);
            this.textBox1.TabIndex = 0;
            this.textBox1.Visible = false;
            //
            // button1
            //
            this.button1.Location = new System.Drawing.Point(483, 434);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.TabIndex = 22;
            this.button1.Text = "OK";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            //
            // label2
            //
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.label2.Location = new System.Drawing.Point(13, 7);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(60, 20);
            this.label2.TabIndex = 3;
            this.label2.Text = "Prompt";
            //
            // textBox2
            //
            this.textBox2.Location = new System.Drawing.Point(211, 9);
            this.textBox2.Name = "textBox2";
            this.textBox2.Size = new System.Drawing.Size(98, 20);
            this.textBox2.TabIndex = 4;
            //
            // button2
            //
            this.button2.Location = new System.Drawing.Point(315, 7);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(47, 23);
            this.button2.TabIndex = 5;
            this.button2.Text = "Abrir";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            //
            // label3
            //
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.label3.Location = new System.Drawing.Point(13, 114);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(114, 20);
            this.label3.TabIndex = 6;
            this.label3.Text = "IAs disponiveis";
            //
            // listBoxAiProviders
            //
            this.listBoxAiProviders.FormattingEnabled = true;
            this.listBoxAiProviders.Location = new System.Drawing.Point(125, 114);
            this.listBoxAiProviders.Name = "listBoxAiProviders";
            this.listBoxAiProviders.Size = new System.Drawing.Size(435, 69);
            this.listBoxAiProviders.TabIndex = 7;
            this.listBoxAiProviders.SelectedIndexChanged += new System.EventHandler(this.listBoxAiProviders_SelectedIndexChanged);
            //
            // label4
            //
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(125, 198);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(19, 13);
            this.label4.TabIndex = 8;
            this.label4.Text = "Id:";
            //
            // txtAiId
            //
            this.txtAiId.Location = new System.Drawing.Point(191, 195);
            this.txtAiId.Name = "txtAiId";
            this.txtAiId.Size = new System.Drawing.Size(150, 20);
            this.txtAiId.TabIndex = 9;
            //
            // label5
            //
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(125, 224);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(38, 13);
            this.label5.TabIndex = 10;
            this.label5.Text = "Nome:";
            //
            // txtAiNome
            //
            this.txtAiNome.Location = new System.Drawing.Point(191, 221);
            this.txtAiNome.Name = "txtAiNome";
            this.txtAiNome.Size = new System.Drawing.Size(308, 20);
            this.txtAiNome.TabIndex = 11;
            //
            // label6
            //
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(125, 250);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(49, 13);
            this.label6.TabIndex = 12;
            this.label6.Text = "Provider:";
            //
            // cboProviderType
            //
            this.cboProviderType.FormattingEnabled = true;
            this.cboProviderType.Items.AddRange(new object[] {
            "Gemini",
            "OpenAI",
            "Groq",
            "Mistral",
            "KimiOpenAICompatible",
            "DeepSeek",
            "Mixtral"});
            this.cboProviderType.Location = new System.Drawing.Point(191, 247);
            this.cboProviderType.Name = "cboProviderType";
            this.cboProviderType.Size = new System.Drawing.Size(150, 21);
            this.cboProviderType.TabIndex = 13;
            //
            // label7
            //
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(125, 277);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(45, 13);
            this.label7.TabIndex = 14;
            this.label7.Text = "Modelo:";
            //
            // txtAiModel
            //
            this.txtAiModel.Location = new System.Drawing.Point(191, 274);
            this.txtAiModel.Name = "txtAiModel";
            this.txtAiModel.Size = new System.Drawing.Size(308, 20);
            this.txtAiModel.TabIndex = 15;
            //
            // label8
            //
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(125, 303);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(41, 13);
            this.label8.TabIndex = 16;
            this.label8.Text = "Chave:";
            //
            // txtAiKey
            //
            this.txtAiKey.Location = new System.Drawing.Point(191, 300);
            this.txtAiKey.Name = "txtAiKey";
            this.txtAiKey.Size = new System.Drawing.Size(308, 20);
            this.txtAiKey.TabIndex = 17;
            //
            // label9
            //
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(125, 329);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(34, 13);
            this.label9.TabIndex = 18;
            this.label9.Text = "Nivel:";
            //
            // numericAiNivel
            //
            this.numericAiNivel.Location = new System.Drawing.Point(191, 327);
            this.numericAiNivel.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.numericAiNivel.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericAiNivel.Name = "numericAiNivel";
            this.numericAiNivel.Size = new System.Drawing.Size(72, 20);
            this.numericAiNivel.TabIndex = 19;
            this.numericAiNivel.Value = new decimal(new int[] {
            6,
            0,
            0,
            0});
            //
            // chkAiAtivo
            //
            this.chkAiAtivo.AutoSize = true;
            this.chkAiAtivo.Location = new System.Drawing.Point(281, 328);
            this.chkAiAtivo.Name = "chkAiAtivo";
            this.chkAiAtivo.Size = new System.Drawing.Size(50, 17);
            this.chkAiAtivo.TabIndex = 20;
            this.chkAiAtivo.Text = "Ativo";
            this.chkAiAtivo.UseVisualStyleBackColor = true;
            //
            // btNovaIa
            //
            this.btNovaIa.Location = new System.Drawing.Point(343, 324);
            this.btNovaIa.Name = "btNovaIa";
            this.btNovaIa.Size = new System.Drawing.Size(75, 23);
            this.btNovaIa.TabIndex = 21;
            this.btNovaIa.Text = "Nova IA";
            this.btNovaIa.UseVisualStyleBackColor = true;
            this.btNovaIa.Click += new System.EventHandler(this.btNovaIa_Click);
            //
            // btSalvarIa
            //
            this.btSalvarIa.Location = new System.Drawing.Point(424, 324);
            this.btSalvarIa.Name = "btSalvarIa";
            this.btSalvarIa.Size = new System.Drawing.Size(75, 23);
            this.btSalvarIa.TabIndex = 23;
            this.btSalvarIa.Text = "Salvar IA";
            this.btSalvarIa.UseVisualStyleBackColor = true;
            this.btSalvarIa.Click += new System.EventHandler(this.btSalvarIa_Click);
            //
            // labelAnalystPrompt
            //
            this.labelAnalystPrompt.AutoSize = true;
            this.labelAnalystPrompt.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.labelAnalystPrompt.Location = new System.Drawing.Point(13, 37);
            this.labelAnalystPrompt.Name = "labelAnalystPrompt";
            this.labelAnalystPrompt.Size = new System.Drawing.Size(143, 20);
            this.labelAnalystPrompt.TabIndex = 24;
            this.labelAnalystPrompt.Text = "Prompt do Analista";
            //
            // textBoxAnalystPrompt
            //
            this.textBoxAnalystPrompt.Location = new System.Drawing.Point(300, 41);
            this.textBoxAnalystPrompt.Name = "textBoxAnalystPrompt";
            this.textBoxAnalystPrompt.Size = new System.Drawing.Size(179, 20);
            this.textBoxAnalystPrompt.TabIndex = 25;
            //
            // buttonAnalystPrompt
            //
            this.buttonAnalystPrompt.Location = new System.Drawing.Point(485, 39);
            this.buttonAnalystPrompt.Name = "buttonAnalystPrompt";
            this.buttonAnalystPrompt.Size = new System.Drawing.Size(75, 23);
            this.buttonAnalystPrompt.TabIndex = 26;
            this.buttonAnalystPrompt.Text = "...";
            this.buttonAnalystPrompt.UseVisualStyleBackColor = true;
            this.buttonAnalystPrompt.Click += new System.EventHandler(this.buttonAnalystPrompt_Click);
            //
            // labelComplementPrompt
            //
            this.labelComplementPrompt.AutoSize = true;
            this.labelComplementPrompt.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.labelComplementPrompt.Location = new System.Drawing.Point(13, 71);
            this.labelComplementPrompt.Name = "labelComplementPrompt";
            this.labelComplementPrompt.Size = new System.Drawing.Size(281, 20);
            this.labelComplementPrompt.TabIndex = 27;
            this.labelComplementPrompt.Text = "Prompt de Complemento de Operacao";
            //
            // textBoxComplementPrompt
            //
            this.textBoxComplementPrompt.Location = new System.Drawing.Point(300, 71);
            this.textBoxComplementPrompt.Name = "textBoxComplementPrompt";
            this.textBoxComplementPrompt.Size = new System.Drawing.Size(179, 20);
            this.textBoxComplementPrompt.TabIndex = 28;
            //
            // buttonComplementPrompt
            //
            this.buttonComplementPrompt.Location = new System.Drawing.Point(485, 69);
            this.buttonComplementPrompt.Name = "buttonComplementPrompt";
            this.buttonComplementPrompt.Size = new System.Drawing.Size(75, 23);
            this.buttonComplementPrompt.TabIndex = 29;
            this.buttonComplementPrompt.Text = "...";
            this.buttonComplementPrompt.UseVisualStyleBackColor = true;
            this.buttonComplementPrompt.Click += new System.EventHandler(this.buttonComplementPrompt_Click);
            //
            // labelKimiHeader
            //
            this.labelKimiHeader.AutoSize = true;
            this.labelKimiHeader.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.labelKimiHeader.Location = new System.Drawing.Point(13, 357);
            this.labelKimiHeader.Name = "labelKimiHeader";
            this.labelKimiHeader.Size = new System.Drawing.Size(187, 20);
            this.labelKimiHeader.TabIndex = 30;
            this.labelKimiHeader.Text = "Kimi / OpenAI Compatível";
            //
            // labelKimiBaseUrl
            //
            this.labelKimiBaseUrl.AutoSize = true;
            this.labelKimiBaseUrl.Location = new System.Drawing.Point(13, 387);
            this.labelKimiBaseUrl.Name = "labelKimiBaseUrl";
            this.labelKimiBaseUrl.Size = new System.Drawing.Size(59, 13);
            this.labelKimiBaseUrl.TabIndex = 31;
            this.labelKimiBaseUrl.Text = "Base URL:";
            //
            // textBoxKimiBaseUrl
            //
            this.textBoxKimiBaseUrl.Location = new System.Drawing.Point(115, 384);
            this.textBoxKimiBaseUrl.Name = "textBoxKimiBaseUrl";
            this.textBoxKimiBaseUrl.Size = new System.Drawing.Size(445, 20);
            this.textBoxKimiBaseUrl.TabIndex = 32;
            //
            // labelKimiApiKey
            //
            this.labelKimiApiKey.AutoSize = true;
            this.labelKimiApiKey.Location = new System.Drawing.Point(13, 413);
            this.labelKimiApiKey.Name = "labelKimiApiKey";
            this.labelKimiApiKey.Size = new System.Drawing.Size(48, 13);
            this.labelKimiApiKey.TabIndex = 33;
            this.labelKimiApiKey.Text = "API Key:";
            //
            // textBoxKimiApiKey
            //
            this.textBoxKimiApiKey.Location = new System.Drawing.Point(115, 410);
            this.textBoxKimiApiKey.Name = "textBoxKimiApiKey";
            this.textBoxKimiApiKey.Size = new System.Drawing.Size(445, 20);
            this.textBoxKimiApiKey.TabIndex = 34;
            this.textBoxKimiApiKey.UseSystemPasswordChar = true;
            //
            // labelKimiModel
            //
            this.labelKimiModel.AutoSize = true;
            this.labelKimiModel.Location = new System.Drawing.Point(12, 439);
            this.labelKimiModel.Name = "labelKimiModel";
            this.labelKimiModel.Size = new System.Drawing.Size(45, 13);
            this.labelKimiModel.TabIndex = 35;
            this.labelKimiModel.Text = "Modelo:";
            //
            // cboKimiModel
            //
            this.cboKimiModel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboKimiModel.FormattingEnabled = true;
            this.cboKimiModel.Items.AddRange(new object[] {
            "kimi-k2",
            "kimi-k2.6",
            "kimi-k2.5",
            "kimi-search",
            "kimi-research"});
            this.cboKimiModel.Location = new System.Drawing.Point(113, 436);
            this.cboKimiModel.Name = "cboKimiModel";
            this.cboKimiModel.Size = new System.Drawing.Size(150, 21);
            this.cboKimiModel.TabIndex = 36;
            //
            // chkKimiEnableSearch
            //
            this.chkKimiEnableSearch.AutoSize = true;
            this.chkKimiEnableSearch.Location = new System.Drawing.Point(279, 438);
            this.chkKimiEnableSearch.Name = "chkKimiEnableSearch";
            this.chkKimiEnableSearch.Size = new System.Drawing.Size(60, 17);
            this.chkKimiEnableSearch.TabIndex = 37;
            this.chkKimiEnableSearch.Text = "Search";
            this.chkKimiEnableSearch.UseVisualStyleBackColor = true;
            //
            // chkKimiEnableThinking
            //
            this.chkKimiEnableThinking.AutoSize = true;
            this.chkKimiEnableThinking.Location = new System.Drawing.Point(349, 438);
            this.chkKimiEnableThinking.Name = "chkKimiEnableThinking";
            this.chkKimiEnableThinking.Size = new System.Drawing.Size(67, 17);
            this.chkKimiEnableThinking.TabIndex = 38;
            this.chkKimiEnableThinking.Text = "Thinking";
            this.chkKimiEnableThinking.UseVisualStyleBackColor = true;
            //
            // ckGitDiff
            //
            this.ckGitDiff.AutoSize = true;
            this.ckGitDiff.Location = new System.Drawing.Point(422, 438);
            this.ckGitDiff.Name = "ckGitDiff";
            this.ckGitDiff.Size = new System.Drawing.Size(55, 17);
            this.ckGitDiff.TabIndex = 39;
            this.ckGitDiff.Text = "GitDiff";
            this.ckGitDiff.UseVisualStyleBackColor = true;
            //
            // label1
            //
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(131, 14);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(74, 13);
            this.label1.TabIndex = 40;
            this.label1.Text = "Search_Block";
            //
            // label10
            //
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(368, 14);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(36, 13);
            this.label10.TabIndex = 43;
            this.label10.Text = "GitDiff";
            //
            // txGitDiff
            //
            this.txGitDiff.Location = new System.Drawing.Point(409, 12);
            this.txGitDiff.Name = "txGitDiff";
            this.txGitDiff.Size = new System.Drawing.Size(98, 20);
            this.txGitDiff.TabIndex = 41;
            //
            // btGitDiff
            //
            this.btGitDiff.Location = new System.Drawing.Point(513, 10);
            this.btGitDiff.Name = "btGitDiff";
            this.btGitDiff.Size = new System.Drawing.Size(47, 23);
            this.btGitDiff.TabIndex = 42;
            this.btGitDiff.Text = "Abrir";
            this.btGitDiff.UseVisualStyleBackColor = true;
            this.btGitDiff.Click += new System.EventHandler(this.btGitDiff_Click);
            //
            // FormConfiguracoes
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(572, 468);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.txGitDiff);
            this.Controls.Add(this.btGitDiff);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.ckGitDiff);
            this.Controls.Add(this.chkKimiEnableThinking);
            this.Controls.Add(this.chkKimiEnableSearch);
            this.Controls.Add(this.cboKimiModel);
            this.Controls.Add(this.labelKimiModel);
            this.Controls.Add(this.textBoxKimiApiKey);
            this.Controls.Add(this.labelKimiApiKey);
            this.Controls.Add(this.textBoxKimiBaseUrl);
            this.Controls.Add(this.labelKimiBaseUrl);
            this.Controls.Add(this.labelKimiHeader);
            this.Controls.Add(this.buttonComplementPrompt);
            this.Controls.Add(this.textBoxComplementPrompt);
            this.Controls.Add(this.labelComplementPrompt);
            this.Controls.Add(this.buttonAnalystPrompt);
            this.Controls.Add(this.textBoxAnalystPrompt);
            this.Controls.Add(this.labelAnalystPrompt);
            this.Controls.Add(this.btSalvarIa);
            this.Controls.Add(this.btNovaIa);
            this.Controls.Add(this.chkAiAtivo);
            this.Controls.Add(this.numericAiNivel);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.txtAiKey);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.txtAiModel);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.cboProviderType);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.txtAiNome);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.txtAiId);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.listBoxAiProviders);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.textBox2);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Name = "FormConfiguracoes";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Configuracoes";
            ((System.ComponentModel.ISupportInitialize)(this.numericAiNivel)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox textBox2;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ListBox listBoxAiProviders;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox txtAiId;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox txtAiNome;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.ComboBox cboProviderType;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.TextBox txtAiModel;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.TextBox txtAiKey;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.NumericUpDown numericAiNivel;
        private System.Windows.Forms.CheckBox chkAiAtivo;
        private System.Windows.Forms.Button btNovaIa;
        private System.Windows.Forms.Button btSalvarIa;
        private System.Windows.Forms.Label labelAnalystPrompt;
        private System.Windows.Forms.TextBox textBoxAnalystPrompt;
        private System.Windows.Forms.Button buttonAnalystPrompt;
        private System.Windows.Forms.Label labelComplementPrompt;
        private System.Windows.Forms.TextBox textBoxComplementPrompt;
        private System.Windows.Forms.Button buttonComplementPrompt;
        private System.Windows.Forms.Label labelKimiHeader;
        private System.Windows.Forms.Label labelKimiBaseUrl;
        private System.Windows.Forms.TextBox textBoxKimiBaseUrl;
        private System.Windows.Forms.Label labelKimiApiKey;
        private System.Windows.Forms.TextBox textBoxKimiApiKey;
        private System.Windows.Forms.Label labelKimiModel;
        private System.Windows.Forms.ComboBox cboKimiModel;
        private System.Windows.Forms.CheckBox chkKimiEnableSearch;
        private System.Windows.Forms.CheckBox chkKimiEnableThinking;
        private System.Windows.Forms.CheckBox ckGitDiff;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.TextBox txGitDiff;
        private System.Windows.Forms.Button btGitDiff;
    }
}
