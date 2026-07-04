namespace atcIA
{
    partial class FormProjetoCadastro
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.label1 = new System.Windows.Forms.Label();
            this.txtNome = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.txtCaminho = new System.Windows.Forms.TextBox();
            this.btNavegar = new System.Windows.Forms.Button();
            this.labelKnowledgeIndexPath = new System.Windows.Forms.Label();
            this.textBoxKnowledgeIndexPath = new System.Windows.Forms.TextBox();
            this.buttonKnowledgeIndexPath = new System.Windows.Forms.Button();
            this.labelLanguageProfile = new System.Windows.Forms.Label();
            this.cboLanguageProfile = new System.Windows.Forms.ComboBox();
            this.labelAiProvider = new System.Windows.Forms.Label();
            this.cboAiProvider = new System.Windows.Forms.ComboBox();
            this.labelFtpHost = new System.Windows.Forms.Label();
            this.txtFtpHost = new System.Windows.Forms.TextBox();
            this.labelFtpPorta = new System.Windows.Forms.Label();
            this.numericFtpPorta = new System.Windows.Forms.NumericUpDown();
            this.labelFtpUsuario = new System.Windows.Forms.Label();
            this.txtFtpUsuario = new System.Windows.Forms.TextBox();
            this.labelFtpSenha = new System.Windows.Forms.Label();
            this.txtFtpSenha = new System.Windows.Forms.TextBox();
            this.labelFtpRaiz = new System.Windows.Forms.Label();
            this.txtFtpRaiz = new System.Windows.Forms.TextBox();
            this.chkFtpSsl = new System.Windows.Forms.CheckBox();
            this.chkFtpPassivo = new System.Windows.Forms.CheckBox();
            this.btTestarFtp = new System.Windows.Forms.Button();
            this.labelDbTitulo = new System.Windows.Forms.Label();
            this.labelDbTipo = new System.Windows.Forms.Label();
            this.cboDbTipo = new System.Windows.Forms.ComboBox();
            this.labelDbHost = new System.Windows.Forms.Label();
            this.txtDbHost = new System.Windows.Forms.TextBox();
            this.labelDbPorta = new System.Windows.Forms.Label();
            this.numericDbPorta = new System.Windows.Forms.NumericUpDown();
            this.labelDbNome = new System.Windows.Forms.Label();
            this.txtDbNome = new System.Windows.Forms.TextBox();
            this.labelDbUsuario = new System.Windows.Forms.Label();
            this.txtDbUsuario = new System.Windows.Forms.TextBox();
            this.labelDbSenha = new System.Windows.Forms.Label();
            this.txtDbSenha = new System.Windows.Forms.TextBox();
            this.labelDbCharset = new System.Windows.Forms.Label();
            this.txtDbCharset = new System.Windows.Forms.TextBox();
            this.labelDbTimeout = new System.Windows.Forms.Label();
            this.numericDbTimeout = new System.Windows.Forms.NumericUpDown();
            this.chkDbSsl = new System.Windows.Forms.CheckBox();
            this.btTestarDb = new System.Windows.Forms.Button();
            this.btSalvar = new System.Windows.Forms.Button();
            this.btCancelar = new System.Windows.Forms.Button();
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.label3 = new System.Windows.Forms.Label();
            this.cboTipoCredencial = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.txtNomeCredencial = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.txtValorCredencial = new System.Windows.Forms.TextBox();
            this.btAdicionarCredencial = new System.Windows.Forms.Button();
            this.lstCredenciais = new System.Windows.Forms.ListBox();
            this.ckIncVer = new System.Windows.Forms.CheckBox();
            this.btVerificacao = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.numericFtpPorta)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericDbPorta)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericDbTimeout)).BeginInit();
            this.SuspendLayout();
            //
            // label1
            //
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(22, 20);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(38, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Nome:";
            //
            // txtNome
            //
            this.txtNome.Location = new System.Drawing.Point(78, 17);
            this.txtNome.Name = "txtNome";
            this.txtNome.Size = new System.Drawing.Size(227, 20);
            this.txtNome.TabIndex = 1;
            //
            // label2
            //
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(22, 55);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(51, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "Caminho:";
            //
            // txtCaminho
            //
            this.txtCaminho.Location = new System.Drawing.Point(78, 52);
            this.txtCaminho.Name = "txtCaminho";
            this.txtCaminho.Size = new System.Drawing.Size(318, 20);
            this.txtCaminho.TabIndex = 3;
            //
            // btNavegar
            //
            this.btNavegar.Location = new System.Drawing.Point(402, 50);
            this.btNavegar.Name = "btNavegar";
            this.btNavegar.Size = new System.Drawing.Size(75, 23);
            this.btNavegar.TabIndex = 4;
            this.btNavegar.Text = "Navegar";
            this.btNavegar.UseVisualStyleBackColor = true;
            this.btNavegar.Click += new System.EventHandler(this.btNavegar_Click);
            //
            // labelKnowledgeIndexPath
            //
            this.labelKnowledgeIndexPath.AutoSize = true;
            this.labelKnowledgeIndexPath.Location = new System.Drawing.Point(22, 88);
            this.labelKnowledgeIndexPath.Name = "labelKnowledgeIndexPath";
            this.labelKnowledgeIndexPath.Size = new System.Drawing.Size(124, 13);
            this.labelKnowledgeIndexPath.TabIndex = 5;
            this.labelKnowledgeIndexPath.Text = "Índice de conhecimento:";
            //
            // textBoxKnowledgeIndexPath
            //
            this.textBoxKnowledgeIndexPath.Location = new System.Drawing.Point(143, 85);
            this.textBoxKnowledgeIndexPath.Name = "textBoxKnowledgeIndexPath";
            this.textBoxKnowledgeIndexPath.Size = new System.Drawing.Size(257, 20);
            this.textBoxKnowledgeIndexPath.TabIndex = 6;
            //
            // buttonKnowledgeIndexPath
            //
            this.buttonKnowledgeIndexPath.Location = new System.Drawing.Point(406, 83);
            this.buttonKnowledgeIndexPath.Name = "buttonKnowledgeIndexPath";
            this.buttonKnowledgeIndexPath.Size = new System.Drawing.Size(71, 23);
            this.buttonKnowledgeIndexPath.TabIndex = 7;
            this.buttonKnowledgeIndexPath.Text = "Procurar...";
            this.buttonKnowledgeIndexPath.UseVisualStyleBackColor = true;
            this.buttonKnowledgeIndexPath.Click += new System.EventHandler(this.buttonKnowledgeIndexPath_Click);
            //
            // labelLanguageProfile
            //
            this.labelLanguageProfile.AutoSize = true;
            this.labelLanguageProfile.Location = new System.Drawing.Point(22, 122);
            this.labelLanguageProfile.Name = "labelLanguageProfile";
            this.labelLanguageProfile.Size = new System.Drawing.Size(62, 13);
            this.labelLanguageProfile.TabIndex = 8;
            this.labelLanguageProfile.Text = "Linguagem:";
            //
            // cboLanguageProfile
            //
            this.cboLanguageProfile.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboLanguageProfile.FormattingEnabled = true;
            this.cboLanguageProfile.Items.AddRange(new object[] {
            "Geral",
            "C#",
            "PHP"});
            this.cboLanguageProfile.Location = new System.Drawing.Point(87, 119);
            this.cboLanguageProfile.Name = "cboLanguageProfile";
            this.cboLanguageProfile.Size = new System.Drawing.Size(96, 21);
            this.cboLanguageProfile.TabIndex = 9;
            //
            // labelAiProvider
            //
            this.labelAiProvider.AutoSize = true;
            this.labelAiProvider.Location = new System.Drawing.Point(311, 20);
            this.labelAiProvider.Name = "labelAiProvider";
            this.labelAiProvider.Size = new System.Drawing.Size(70, 13);
            this.labelAiProvider.TabIndex = 10;
            this.labelAiProvider.Text = "IA do projeto:";
            //
            // cboAiProvider
            //
            this.cboAiProvider.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboAiProvider.FormattingEnabled = true;
            this.cboAiProvider.Location = new System.Drawing.Point(387, 17);
            this.cboAiProvider.Name = "cboAiProvider";
            this.cboAiProvider.Size = new System.Drawing.Size(92, 21);
            this.cboAiProvider.TabIndex = 11;
            this.cboAiProvider.SelectedIndexChanged += new System.EventHandler(this.cboAiProvider_SelectedIndexChanged);
            //
            // labelFtpHost
            //
            this.labelFtpHost.AutoSize = true;
            this.labelFtpHost.Location = new System.Drawing.Point(22, 157);
            this.labelFtpHost.Name = "labelFtpHost";
            this.labelFtpHost.Size = new System.Drawing.Size(53, 13);
            this.labelFtpHost.TabIndex = 12;
            this.labelFtpHost.Text = "FTP host:";
            //
            // txtFtpHost
            //
            this.txtFtpHost.Location = new System.Drawing.Point(87, 154);
            this.txtFtpHost.Name = "txtFtpHost";
            this.txtFtpHost.Size = new System.Drawing.Size(220, 20);
            this.txtFtpHost.TabIndex = 13;
            //
            // labelFtpPorta
            //
            this.labelFtpPorta.AutoSize = true;
            this.labelFtpPorta.Location = new System.Drawing.Point(319, 157);
            this.labelFtpPorta.Name = "labelFtpPorta";
            this.labelFtpPorta.Size = new System.Drawing.Size(35, 13);
            this.labelFtpPorta.TabIndex = 14;
            this.labelFtpPorta.Text = "Porta:";
            //
            // numericFtpPorta
            //
            this.numericFtpPorta.Location = new System.Drawing.Point(360, 155);
            this.numericFtpPorta.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.numericFtpPorta.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericFtpPorta.Name = "numericFtpPorta";
            this.numericFtpPorta.Size = new System.Drawing.Size(117, 20);
            this.numericFtpPorta.TabIndex = 15;
            this.numericFtpPorta.Value = new decimal(new int[] {
            21,
            0,
            0,
            0});
            //
            // labelFtpUsuario
            //
            this.labelFtpUsuario.AutoSize = true;
            this.labelFtpUsuario.Location = new System.Drawing.Point(22, 190);
            this.labelFtpUsuario.Name = "labelFtpUsuario";
            this.labelFtpUsuario.Size = new System.Drawing.Size(46, 13);
            this.labelFtpUsuario.TabIndex = 16;
            this.labelFtpUsuario.Text = "Usuario:";
            //
            // txtFtpUsuario
            //
            this.txtFtpUsuario.Location = new System.Drawing.Point(87, 187);
            this.txtFtpUsuario.Name = "txtFtpUsuario";
            this.txtFtpUsuario.Size = new System.Drawing.Size(143, 20);
            this.txtFtpUsuario.TabIndex = 17;
            //
            // labelFtpSenha
            //
            this.labelFtpSenha.AutoSize = true;
            this.labelFtpSenha.Location = new System.Drawing.Point(246, 190);
            this.labelFtpSenha.Name = "labelFtpSenha";
            this.labelFtpSenha.Size = new System.Drawing.Size(41, 13);
            this.labelFtpSenha.TabIndex = 18;
            this.labelFtpSenha.Text = "Senha:";
            //
            // txtFtpSenha
            //
            this.txtFtpSenha.Location = new System.Drawing.Point(293, 187);
            this.txtFtpSenha.Name = "txtFtpSenha";
            this.txtFtpSenha.PasswordChar = '*';
            this.txtFtpSenha.Size = new System.Drawing.Size(184, 20);
            this.txtFtpSenha.TabIndex = 19;
            //
            // labelFtpRaiz
            //
            this.labelFtpRaiz.AutoSize = true;
            this.labelFtpRaiz.Location = new System.Drawing.Point(22, 223);
            this.labelFtpRaiz.Name = "labelFtpRaiz";
            this.labelFtpRaiz.Size = new System.Drawing.Size(54, 13);
            this.labelFtpRaiz.TabIndex = 20;
            this.labelFtpRaiz.Text = "Raiz FTP:";
            //
            // txtFtpRaiz
            //
            this.txtFtpRaiz.Location = new System.Drawing.Point(87, 220);
            this.txtFtpRaiz.Name = "txtFtpRaiz";
            this.txtFtpRaiz.Size = new System.Drawing.Size(220, 20);
            this.txtFtpRaiz.TabIndex = 21;
            this.txtFtpRaiz.Text = "/";
            //
            // chkFtpSsl
            //
            this.chkFtpSsl.AutoSize = true;
            this.chkFtpSsl.Location = new System.Drawing.Point(322, 222);
            this.chkFtpSsl.Name = "chkFtpSsl";
            this.chkFtpSsl.Size = new System.Drawing.Size(46, 17);
            this.chkFtpSsl.TabIndex = 22;
            this.chkFtpSsl.Text = "SSL";
            this.chkFtpSsl.UseVisualStyleBackColor = true;
            //
            // chkFtpPassivo
            //
            this.chkFtpPassivo.AutoSize = true;
            this.chkFtpPassivo.Checked = true;
            this.chkFtpPassivo.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkFtpPassivo.Location = new System.Drawing.Point(385, 222);
            this.chkFtpPassivo.Name = "chkFtpPassivo";
            this.chkFtpPassivo.Size = new System.Drawing.Size(63, 17);
            this.chkFtpPassivo.TabIndex = 23;
            this.chkFtpPassivo.Text = "Passivo";
            this.chkFtpPassivo.UseVisualStyleBackColor = true;
            //
            // btTestarFtp
            //
            this.btTestarFtp.Location = new System.Drawing.Point(402, 248);
            this.btTestarFtp.Name = "btTestarFtp";
            this.btTestarFtp.Size = new System.Drawing.Size(75, 23);
            this.btTestarFtp.TabIndex = 24;
            this.btTestarFtp.Text = "Testar FTP";
            this.btTestarFtp.UseVisualStyleBackColor = true;
            this.btTestarFtp.Click += new System.EventHandler(this.btTestarFtp_Click);
            //
            // labelDbTitulo
            //
            this.labelDbTitulo.AutoSize = true;
            this.labelDbTitulo.Location = new System.Drawing.Point(22, 253);
            this.labelDbTitulo.Name = "labelDbTitulo";
            this.labelDbTitulo.Size = new System.Drawing.Size(121, 13);
            this.labelDbTitulo.TabIndex = 25;
            this.labelDbTitulo.Text = "Configuracao do banco:";
            //
            // labelDbTipo
            //
            this.labelDbTipo.AutoSize = true;
            this.labelDbTipo.Location = new System.Drawing.Point(22, 286);
            this.labelDbTipo.Name = "labelDbTipo";
            this.labelDbTipo.Size = new System.Drawing.Size(31, 13);
            this.labelDbTipo.TabIndex = 26;
            this.labelDbTipo.Text = "Tipo:";
            //
            // cboDbTipo
            //
            this.cboDbTipo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboDbTipo.FormattingEnabled = true;
            this.cboDbTipo.Items.AddRange(new object[] {
            "MySQL",
            "MariaDB",
            "PostgreSQL",
            "SQL Server"});
            this.cboDbTipo.Location = new System.Drawing.Point(78, 283);
            this.cboDbTipo.Name = "cboDbTipo";
            this.cboDbTipo.Size = new System.Drawing.Size(152, 21);
            this.cboDbTipo.TabIndex = 27;
            this.cboDbTipo.SelectedIndexChanged += new System.EventHandler(this.cboDbTipo_SelectedIndexChanged);
            //
            // labelDbHost
            //
            this.labelDbHost.AutoSize = true;
            this.labelDbHost.Location = new System.Drawing.Point(246, 286);
            this.labelDbHost.Name = "labelDbHost";
            this.labelDbHost.Size = new System.Drawing.Size(32, 13);
            this.labelDbHost.TabIndex = 28;
            this.labelDbHost.Text = "Host:";
            //
            // txtDbHost
            //
            this.txtDbHost.Location = new System.Drawing.Point(293, 283);
            this.txtDbHost.Name = "txtDbHost";
            this.txtDbHost.Size = new System.Drawing.Size(184, 20);
            this.txtDbHost.TabIndex = 29;
            //
            // labelDbPorta
            //
            this.labelDbPorta.AutoSize = true;
            this.labelDbPorta.Location = new System.Drawing.Point(22, 319);
            this.labelDbPorta.Name = "labelDbPorta";
            this.labelDbPorta.Size = new System.Drawing.Size(35, 13);
            this.labelDbPorta.TabIndex = 30;
            this.labelDbPorta.Text = "Porta:";
            //
            // numericDbPorta
            //
            this.numericDbPorta.Location = new System.Drawing.Point(78, 317);
            this.numericDbPorta.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.numericDbPorta.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericDbPorta.Name = "numericDbPorta";
            this.numericDbPorta.Size = new System.Drawing.Size(72, 20);
            this.numericDbPorta.TabIndex = 31;
            this.numericDbPorta.Value = new decimal(new int[] {
            3306,
            0,
            0,
            0});
            //
            // labelDbNome
            //
            this.labelDbNome.AutoSize = true;
            this.labelDbNome.Location = new System.Drawing.Point(164, 319);
            this.labelDbNome.Name = "labelDbNome";
            this.labelDbNome.Size = new System.Drawing.Size(41, 13);
            this.labelDbNome.TabIndex = 32;
            this.labelDbNome.Text = "Banco:";
            //
            // txtDbNome
            //
            this.txtDbNome.Location = new System.Drawing.Point(211, 316);
            this.txtDbNome.Name = "txtDbNome";
            this.txtDbNome.Size = new System.Drawing.Size(266, 20);
            this.txtDbNome.TabIndex = 33;
            //
            // labelDbUsuario
            //
            this.labelDbUsuario.AutoSize = true;
            this.labelDbUsuario.Location = new System.Drawing.Point(22, 352);
            this.labelDbUsuario.Name = "labelDbUsuario";
            this.labelDbUsuario.Size = new System.Drawing.Size(46, 13);
            this.labelDbUsuario.TabIndex = 34;
            this.labelDbUsuario.Text = "Usuario:";
            //
            // txtDbUsuario
            //
            this.txtDbUsuario.Location = new System.Drawing.Point(78, 349);
            this.txtDbUsuario.Name = "txtDbUsuario";
            this.txtDbUsuario.Size = new System.Drawing.Size(152, 20);
            this.txtDbUsuario.TabIndex = 35;
            //
            // labelDbSenha
            //
            this.labelDbSenha.AutoSize = true;
            this.labelDbSenha.Location = new System.Drawing.Point(246, 352);
            this.labelDbSenha.Name = "labelDbSenha";
            this.labelDbSenha.Size = new System.Drawing.Size(41, 13);
            this.labelDbSenha.TabIndex = 36;
            this.labelDbSenha.Text = "Senha:";
            //
            // txtDbSenha
            //
            this.txtDbSenha.Location = new System.Drawing.Point(293, 349);
            this.txtDbSenha.Name = "txtDbSenha";
            this.txtDbSenha.PasswordChar = '*';
            this.txtDbSenha.Size = new System.Drawing.Size(184, 20);
            this.txtDbSenha.TabIndex = 37;
            //
            // labelDbCharset
            //
            this.labelDbCharset.AutoSize = true;
            this.labelDbCharset.Location = new System.Drawing.Point(22, 385);
            this.labelDbCharset.Name = "labelDbCharset";
            this.labelDbCharset.Size = new System.Drawing.Size(46, 13);
            this.labelDbCharset.TabIndex = 38;
            this.labelDbCharset.Text = "Charset:";
            //
            // txtDbCharset
            //
            this.txtDbCharset.Location = new System.Drawing.Point(78, 382);
            this.txtDbCharset.Name = "txtDbCharset";
            this.txtDbCharset.Size = new System.Drawing.Size(105, 20);
            this.txtDbCharset.TabIndex = 39;
            this.txtDbCharset.Text = "utf8mb4";
            //
            // labelDbTimeout
            //
            this.labelDbTimeout.AutoSize = true;
            this.labelDbTimeout.Location = new System.Drawing.Point(205, 385);
            this.labelDbTimeout.Name = "labelDbTimeout";
            this.labelDbTimeout.Size = new System.Drawing.Size(59, 13);
            this.labelDbTimeout.TabIndex = 40;
            this.labelDbTimeout.Text = "Timeout(s):";
            //
            // numericDbTimeout
            //
            this.numericDbTimeout.Location = new System.Drawing.Point(272, 383);
            this.numericDbTimeout.Maximum = new decimal(new int[] {
            600,
            0,
            0,
            0});
            this.numericDbTimeout.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericDbTimeout.Name = "numericDbTimeout";
            this.numericDbTimeout.Size = new System.Drawing.Size(68, 20);
            this.numericDbTimeout.TabIndex = 41;
            this.numericDbTimeout.Value = new decimal(new int[] {
            30,
            0,
            0,
            0});
            //
            // chkDbSsl
            //
            this.chkDbSsl.AutoSize = true;
            this.chkDbSsl.Location = new System.Drawing.Point(402, 382);
            this.chkDbSsl.Name = "chkDbSsl";
            this.chkDbSsl.Size = new System.Drawing.Size(46, 17);
            this.chkDbSsl.TabIndex = 42;
            this.chkDbSsl.Text = "SSL";
            this.chkDbSsl.UseVisualStyleBackColor = true;
            //
            // btTestarDb
            //
            this.btTestarDb.Location = new System.Drawing.Point(402, 410);
            this.btTestarDb.Name = "btTestarDb";
            this.btTestarDb.Size = new System.Drawing.Size(75, 23);
            this.btTestarDb.TabIndex = 43;
            this.btTestarDb.Text = "Testar DB";
            this.btTestarDb.UseVisualStyleBackColor = true;
            this.btTestarDb.Click += new System.EventHandler(this.btTestarDb_Click);
            //
            // btSalvar
            //
            this.btSalvar.Location = new System.Drawing.Point(321, 580);
            this.btSalvar.Name = "btSalvar";
            this.btSalvar.Size = new System.Drawing.Size(75, 23);
            this.btSalvar.TabIndex = 18;
            this.btSalvar.Text = "Salvar";
            this.btSalvar.UseVisualStyleBackColor = true;
            this.btSalvar.Click += new System.EventHandler(this.btSalvar_Click);
            //
            // btCancelar
            //
            this.btCancelar.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btCancelar.Location = new System.Drawing.Point(402, 580);
            this.btCancelar.Name = "btCancelar";
            this.btCancelar.Size = new System.Drawing.Size(75, 23);
            this.btCancelar.TabIndex = 19;
            this.btCancelar.Text = "Cancelar";
            this.btCancelar.UseVisualStyleBackColor = true;
            this.btCancelar.Click += new System.EventHandler(this.btCancelar_Click);
            //
            // label3
            //
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(22, 417);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(31, 13);
            this.label3.TabIndex = 44;
            this.label3.Text = "Tipo:";
            //
            // cboTipoCredencial
            //
            this.cboTipoCredencial.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboTipoCredencial.FormattingEnabled = true;
            this.cboTipoCredencial.Items.AddRange(new object[] {
            "SSH",
            "Outro"});
            this.cboTipoCredencial.Location = new System.Drawing.Point(78, 414);
            this.cboTipoCredencial.Name = "cboTipoCredencial";
            this.cboTipoCredencial.Size = new System.Drawing.Size(152, 21);
            this.cboTipoCredencial.TabIndex = 45;
            //
            // label4
            //
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(22, 450);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(38, 13);
            this.label4.TabIndex = 46;
            this.label4.Text = "Nome:";
            //
            // txtNomeCredencial
            //
            this.txtNomeCredencial.Location = new System.Drawing.Point(78, 447);
            this.txtNomeCredencial.Name = "txtNomeCredencial";
            this.txtNomeCredencial.Size = new System.Drawing.Size(242, 20);
            this.txtNomeCredencial.TabIndex = 47;
            //
            // label5
            //
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(22, 483);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(34, 13);
            this.label5.TabIndex = 48;
            this.label5.Text = "Valor:";
            //
            // txtValorCredencial
            //
            this.txtValorCredencial.Location = new System.Drawing.Point(78, 480);
            this.txtValorCredencial.Name = "txtValorCredencial";
            this.txtValorCredencial.Size = new System.Drawing.Size(399, 20);
            this.txtValorCredencial.TabIndex = 49;
            //
            // btAdicionarCredencial
            //
            this.btAdicionarCredencial.Location = new System.Drawing.Point(326, 445);
            this.btAdicionarCredencial.Name = "btAdicionarCredencial";
            this.btAdicionarCredencial.Size = new System.Drawing.Size(151, 23);
            this.btAdicionarCredencial.TabIndex = 50;
            this.btAdicionarCredencial.Text = "Adicionar credencial";
            this.btAdicionarCredencial.UseVisualStyleBackColor = true;
            this.btAdicionarCredencial.Click += new System.EventHandler(this.btAdicionarCredencial_Click);
            //
            // lstCredenciais
            //
            this.lstCredenciais.FormattingEnabled = true;
            this.lstCredenciais.Location = new System.Drawing.Point(25, 515);
            this.lstCredenciais.Name = "lstCredenciais";
            this.lstCredenciais.Size = new System.Drawing.Size(454, 56);
            this.lstCredenciais.TabIndex = 51;
            //
            // ckIncVer
            //
            this.ckIncVer.AutoSize = true;
            this.ckIncVer.Location = new System.Drawing.Point(189, 123);
            this.ckIncVer.Name = "ckIncVer";
            this.ckIncVer.Size = new System.Drawing.Size(118, 17);
            this.ckIncVer.TabIndex = 52;
            this.ckIncVer.Text = "Incrementar Versão";
            this.ckIncVer.UseVisualStyleBackColor = true;
            //
            // btVerificacao
            //
            this.btVerificacao.AutoSize = true;
            this.btVerificacao.Location = new System.Drawing.Point(359, 123);
            this.btVerificacao.Name = "btVerificacao";
            this.btVerificacao.Size = new System.Drawing.Size(118, 17);
            this.btVerificacao.TabIndex = 53;
            this.btVerificacao.Text = "Incrementar Versão";
            this.btVerificacao.UseVisualStyleBackColor = true;
            //
            // FormProjetoCadastro
            //
            this.AcceptButton = this.btSalvar;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btCancelar;
            this.ClientSize = new System.Drawing.Size(500, 613);
            this.Controls.Add(this.btVerificacao);
            this.Controls.Add(this.ckIncVer);
            this.Controls.Add(this.lstCredenciais);
            this.Controls.Add(this.btAdicionarCredencial);
            this.Controls.Add(this.txtValorCredencial);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.txtNomeCredencial);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.cboTipoCredencial);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.btCancelar);
            this.Controls.Add(this.btSalvar);
            this.Controls.Add(this.btNavegar);
            this.Controls.Add(this.btTestarDb);
            this.Controls.Add(this.btTestarFtp);
            this.Controls.Add(this.chkDbSsl);
            this.Controls.Add(this.numericDbTimeout);
            this.Controls.Add(this.labelDbTimeout);
            this.Controls.Add(this.txtDbCharset);
            this.Controls.Add(this.labelDbCharset);
            this.Controls.Add(this.txtDbSenha);
            this.Controls.Add(this.labelDbSenha);
            this.Controls.Add(this.txtDbUsuario);
            this.Controls.Add(this.labelDbUsuario);
            this.Controls.Add(this.txtDbNome);
            this.Controls.Add(this.labelDbNome);
            this.Controls.Add(this.numericDbPorta);
            this.Controls.Add(this.labelDbPorta);
            this.Controls.Add(this.txtDbHost);
            this.Controls.Add(this.labelDbHost);
            this.Controls.Add(this.cboDbTipo);
            this.Controls.Add(this.labelDbTipo);
            this.Controls.Add(this.labelDbTitulo);
            this.Controls.Add(this.chkFtpPassivo);
            this.Controls.Add(this.chkFtpSsl);
            this.Controls.Add(this.txtFtpRaiz);
            this.Controls.Add(this.labelFtpRaiz);
            this.Controls.Add(this.txtFtpSenha);
            this.Controls.Add(this.labelFtpSenha);
            this.Controls.Add(this.txtFtpUsuario);
            this.Controls.Add(this.labelFtpUsuario);
            this.Controls.Add(this.numericFtpPorta);
            this.Controls.Add(this.labelFtpPorta);
            this.Controls.Add(this.txtFtpHost);
            this.Controls.Add(this.labelFtpHost);
            this.Controls.Add(this.cboAiProvider);
            this.Controls.Add(this.labelAiProvider);
            this.Controls.Add(this.cboLanguageProfile);
            this.Controls.Add(this.labelLanguageProfile);
            this.Controls.Add(this.txtCaminho);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.buttonKnowledgeIndexPath);
            this.Controls.Add(this.textBoxKnowledgeIndexPath);
            this.Controls.Add(this.labelKnowledgeIndexPath);
            this.Controls.Add(this.txtNome);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FormProjetoCadastro";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Cadastro de Projeto";
            ((System.ComponentModel.ISupportInitialize)(this.numericFtpPorta)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericDbPorta)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericDbTimeout)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtNome;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox txtCaminho;
        private System.Windows.Forms.Button btNavegar;
        private System.Windows.Forms.Label labelKnowledgeIndexPath;
        private System.Windows.Forms.TextBox textBoxKnowledgeIndexPath;
        private System.Windows.Forms.Button buttonKnowledgeIndexPath;
        private System.Windows.Forms.Label labelLanguageProfile;
        private System.Windows.Forms.ComboBox cboLanguageProfile;
        private System.Windows.Forms.Label labelAiProvider;
        private System.Windows.Forms.ComboBox cboAiProvider;
        private System.Windows.Forms.Label labelFtpHost;
        private System.Windows.Forms.TextBox txtFtpHost;
        private System.Windows.Forms.Label labelFtpPorta;
        private System.Windows.Forms.NumericUpDown numericFtpPorta;
        private System.Windows.Forms.Label labelFtpUsuario;
        private System.Windows.Forms.TextBox txtFtpUsuario;
        private System.Windows.Forms.Label labelFtpSenha;
        private System.Windows.Forms.TextBox txtFtpSenha;
        private System.Windows.Forms.Label labelFtpRaiz;
        private System.Windows.Forms.TextBox txtFtpRaiz;
        private System.Windows.Forms.CheckBox chkFtpSsl;
        private System.Windows.Forms.CheckBox chkFtpPassivo;
        private System.Windows.Forms.Button btTestarFtp;
        private System.Windows.Forms.Label labelDbTitulo;
        private System.Windows.Forms.Label labelDbTipo;
        private System.Windows.Forms.ComboBox cboDbTipo;
        private System.Windows.Forms.Label labelDbHost;
        private System.Windows.Forms.TextBox txtDbHost;
        private System.Windows.Forms.Label labelDbPorta;
        private System.Windows.Forms.NumericUpDown numericDbPorta;
        private System.Windows.Forms.Label labelDbNome;
        private System.Windows.Forms.TextBox txtDbNome;
        private System.Windows.Forms.Label labelDbUsuario;
        private System.Windows.Forms.TextBox txtDbUsuario;
        private System.Windows.Forms.Label labelDbSenha;
        private System.Windows.Forms.TextBox txtDbSenha;
        private System.Windows.Forms.Label labelDbCharset;
        private System.Windows.Forms.TextBox txtDbCharset;
        private System.Windows.Forms.Label labelDbTimeout;
        private System.Windows.Forms.NumericUpDown numericDbTimeout;
        private System.Windows.Forms.CheckBox chkDbSsl;
        private System.Windows.Forms.Button btTestarDb;
        private System.Windows.Forms.Button btSalvar;
        private System.Windows.Forms.Button btCancelar;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ComboBox cboTipoCredencial;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox txtNomeCredencial;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox txtValorCredencial;
        private System.Windows.Forms.Button btAdicionarCredencial;
        private System.Windows.Forms.ListBox lstCredenciais;
        private System.Windows.Forms.CheckBox ckIncVer;
        private System.Windows.Forms.CheckBox btVerificacao;
    }
}
