using GptBolDll;
using GptBolDll.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace atcIA
{
    public partial class FormProjetoCadastro : Form
    {
        public Projeto ProjetoSalvo { get; private set; }
        private readonly List<ProjectCredential> credenciais = new List<ProjectCredential>();
        private List<LanguageProfileConfig> languageProfiles = new List<LanguageProfileConfig>();
        private Projeto projetoEdicao;

        public FormProjetoCadastro()
        {
            InitializeComponent();
            InicializarFormulario(null);
        }

        public FormProjetoCadastro(Projeto projeto)
        {
            InitializeComponent();
            InicializarFormulario(projeto);
        }

        private void InicializarFormulario(Projeto projeto)
        {
            projetoEdicao = projeto;
            cboTipoCredencial.SelectedIndex = 0;
            cboDbTipo.SelectedIndex = 0;
            CarregarLanguageProfiles();
            CarregarAiProviders();

            if (projeto == null)
            {
                Text = "Novo projeto";
                return;
            }

            Text = "Editar projeto";
            txtNome.Text = projeto.Nome;
            txtCaminho.Text = projeto.Caminho;
            textBoxKnowledgeIndexPath.Text = projeto.KnowledgeIndexPath ?? "";
            SelecionarLanguageProfile(projeto.LanguageProfileId, projeto.LanguageProfile);
            SelecionarAiProvider(projeto.AiProviderId);
            CarregarFtp(projeto.Ftp);
            CarregarDatabase(projeto.Database);

            credenciais.Clear();
            if (projeto.Credenciais != null)
                credenciais.AddRange(projeto.Credenciais);

            AtualizarListaCredenciais();
            ckIncVer.Checked = projeto.IncrementarVersaoAoConcluir;
            btVerificacao.Checked = projeto.AutoVerificationEnabled;
        }

        private void CarregarLanguageProfiles()
        {
            languageProfiles = LanguageProfileConfigRepository.Listar();
            cboLanguageProfile.Items.Clear();
            cboLanguageProfile.DropDownWidth = 260;
            foreach (var profile in languageProfiles)
                cboLanguageProfile.Items.Add(profile);

            if (cboLanguageProfile.Items.Count > 0)
                cboLanguageProfile.SelectedIndex = 0;
        }

        private void SelecionarLanguageProfile(string profileId, LanguageProfile legacyProfile)
        {
            profileId = LanguageProfileConfigRepository.ResolveId(profileId, legacyProfile);
            var profile = languageProfiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
            if (profile != null)
                cboLanguageProfile.SelectedItem = profile;

            if (cboLanguageProfile.SelectedIndex < 0)
                cboLanguageProfile.SelectedIndex = 0;
        }

        private LanguageProfile ObterLanguageProfileSelecionado()
        {
            return LanguageProfileConfigRepository.LegacyProfileFromId(ObterLanguageProfileIdSelecionado());
        }

        private string ObterLanguageProfileIdSelecionado()
        {
            var profile = cboLanguageProfile.SelectedItem as LanguageProfileConfig;
            if (profile == null)
                return "geral";

            return LanguageProfileConfigRepository.NormalizeId(profile.Id);
        }

        private void CarregarFtp(FtpProfile ftp)
        {
            if (ftp == null)
                return;

            txtFtpHost.Text = ftp.Host ?? "";
            numericFtpPorta.Value = ftp.Port > 0 && ftp.Port <= 65535 ? ftp.Port : 21;
            txtFtpUsuario.Text = ftp.User ?? "";
            txtFtpSenha.Text = ftp.Password ?? "";
            txtFtpRaiz.Text = ftp.RemoteRoot ?? "/";
            chkFtpSsl.Checked = ftp.EnableSsl;
            chkFtpPassivo.Checked = ftp.UsePassive;
        }

        private void CarregarDatabase(DatabaseProfile database)
        {
            if (database == null)
                return;

            cboDbTipo.Text = string.IsNullOrWhiteSpace(database.Type) ? "MySQL" : database.Type;
            txtDbHost.Text = database.Host ?? "";
            numericDbPorta.Value = database.Port > 0 && database.Port <= 65535 ? database.Port : 3306;
            txtDbNome.Text = database.DatabaseName ?? "";
            txtDbUsuario.Text = database.User ?? "";
            txtDbSenha.Text = database.Password ?? "";
            txtDbCharset.Text = string.IsNullOrWhiteSpace(database.Charset) ? "utf8mb4" : database.Charset;
            numericDbTimeout.Value = database.TimeoutSeconds > 0 && database.TimeoutSeconds <= 600 ? database.TimeoutSeconds : 30;
            chkDbSsl.Checked = database.UseSsl;
        }

        private void CarregarAiProviders()
        {
            var providers = AiProviderRepository.ListarAtivos();
            cboAiProvider.DataSource = null;
            cboAiProvider.DisplayMember = "Nome";
            cboAiProvider.ValueMember = "Id";
            cboAiProvider.DataSource = providers;

            if (providers.Count > 0)
                cboAiProvider.SelectedIndex = 0;
        }

        private void SelecionarAiProvider(string providerId)
        {
            var providers = cboAiProvider.DataSource as List<AiProviderConfig>;
            if (providers == null || providers.Count == 0)
                return;

            var provider = providers.FirstOrDefault(p => p.Id == providerId) ?? providers.First();
            cboAiProvider.SelectedItem = provider;
        }

        private AiProviderConfig ObterAiProviderSelecionado()
        {
            return cboAiProvider.SelectedItem as AiProviderConfig
                ?? AiProviderRepository.BuscarAtivoOuPadrao(AiProviderConfig.DefaultProviderId);
        }

        private void btNavegar_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.Description = "Selecione a pasta raiz do projeto";
            folderBrowserDialog1.ShowNewFolderButton = true;

            if (!string.IsNullOrWhiteSpace(txtCaminho.Text) && Directory.Exists(txtCaminho.Text))
            {
                folderBrowserDialog1.SelectedPath = txtCaminho.Text;
            }

            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                string caminhoSelecionado = folderBrowserDialog1.SelectedPath;

                if (string.IsNullOrWhiteSpace(caminhoSelecionado) || !Directory.Exists(caminhoSelecionado))
                {
                    MessageBox.Show(
                        "O caminho selecionado não é válido.",
                        "Caminho inválido",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }

                txtCaminho.Text = caminhoSelecionado;
            }
        }

        private void buttonKnowledgeIndexPath_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Selecione o indice de conhecimento";
                openFileDialog.Filter = "Arquivos de indice (*.json;*.md;*.txt)|*.json;*.md;*.txt|Todos os arquivos (*.*)|*.*";
                openFileDialog.CheckFileExists = true;
                openFileDialog.CheckPathExists = true;

                var current = textBoxKnowledgeIndexPath.Text.Trim();
                if (!string.IsNullOrWhiteSpace(current))
                {
                    if (File.Exists(current))
                        openFileDialog.FileName = current;
                    else if (Directory.Exists(Path.GetDirectoryName(current)))
                        openFileDialog.InitialDirectory = Path.GetDirectoryName(current);
                }

                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                    textBoxKnowledgeIndexPath.Text = openFileDialog.FileName;
            }
        }

        private void btAdicionarCredencial_Click(object sender, EventArgs e)
        {
            string tipo = cboTipoCredencial.Text.Trim();
            string nome = txtNomeCredencial.Text.Trim();
            string valor = txtValorCredencial.Text.Trim();

            if (string.IsNullOrWhiteSpace(tipo))
            {
                MessageBox.Show("Informe o tipo da credencial.", "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(nome))
            {
                MessageBox.Show("Informe o nome da credencial.", "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(valor))
            {
                MessageBox.Show("Informe o valor da credencial.", "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var credencial = new ProjectCredential
            {
                Tipo = tipo,
                Nome = nome,
                Valor = valor
            };

            credenciais.Add(credencial);
            AtualizarListaCredenciais();

            txtNomeCredencial.Clear();
            txtValorCredencial.Clear();
            txtNomeCredencial.Focus();
        }

        private void btTestarFtp_Click(object sender, EventArgs e)
        {
            var ftp = CriarFtpDoFormulario();
            if (string.IsNullOrWhiteSpace(ftp.Host))
            {
                MessageBox.Show("Informe o host do FTP.", "Teste FTP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btTestarFtp.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                new FtpClient(ftp).TestConnection();
                MessageBox.Show("Conexao FTP realizada com sucesso.", "Teste FTP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (WebException ex)
            {
                MessageBox.Show("Falha ao conectar no FTP:\n\n" + ObterMensagemWebException(ex), "Teste FTP", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Falha ao conectar no FTP:\n\n" + ex.Message, "Teste FTP", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                btTestarFtp.Enabled = true;
            }
        }

        private void btTestarDb_Click(object sender, EventArgs e)
        {
            var database = CriarDatabaseDoFormulario();
            btTestarDb.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (TestarDbViaLaravelSshSeConfigurado())
                {
                    MessageBox.Show("Conexao com o banco realizada com sucesso via Laravel/SSH.", "Teste banco", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                GarantirTunelSshSeConfigurado(database);
                DatabaseConnectionTester.Test(database);
                MessageBox.Show("Conexao com o banco realizada com sucesso.", "Teste banco", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Falha ao conectar no banco:\n\n" + ObterMensagemErroBanco(database, ex), "Teste banco", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                btTestarDb.Enabled = true;
            }
        }

        private string ObterMensagemWebException(WebException ex)
        {
            var response = ex.Response as FtpWebResponse;
            if (response == null)
                return ex.Message;

            return response.StatusDescription ?? ex.Message;
        }

        private string ObterMensagemErroBanco(DatabaseProfile database, Exception ex)
        {
            var mensagem = ex.Message;
            var tipo = database == null ? "" : database.Type ?? "";
            var host = database == null ? "" : database.Host ?? "";

            if ((tipo.IndexOf("mysql", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 tipo.IndexOf("mariadb", StringComparison.OrdinalIgnoreCase) >= 0) &&
                mensagem.IndexOf("Unable to connect to any of the specified MySQL hosts", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return mensagem +
                       "\n\nO driver MySQL carregou, mas o aplicativo nao conseguiu abrir conexao com o host informado." +
                       "\nSe os dados originais usam host 'localhost', eles funcionam apenas dentro do servidor onde o MySQL roda." +
                       "\nDevido as configuracoes do servidor, o acesso ao banco de dados provavelmente so sera possivel via SSH/tunel." +
                       "\nInforme o host/IP externo do MySQL apenas se a hospedagem liberar acesso remoto na porta 3306 para o seu IP." +
                       "\nHost testado: " + host + ":" + database.Port;
            }

            return mensagem;
        }

        private bool TestarDbViaLaravelSshSeConfigurado()
        {
            var credencial = credenciais.FirstOrDefault(c =>
                string.Equals(c.Tipo, "SSH", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.Valor) &&
                c.Valor.IndexOf("LaravelRoot=", StringComparison.OrdinalIgnoreCase) >= 0);

            if (credencial == null)
                return false;

            var cfg = ParseCredencialChaveValor(credencial.Valor);
            var sshHost = ObterValor(cfg, "Host");
            var sshUser = ObterValor(cfg, "User");
            var sshPort = ObterInteiro(cfg, "Port", 22);
            var password = ObterValor(cfg, "Password");
            var laravelRoot = ObterValor(cfg, "LaravelRoot");
            var phpBinary = ObterValor(cfg, "Php");

            if (string.IsNullOrWhiteSpace(phpBinary))
                phpBinary = "php";

            if (string.IsNullOrWhiteSpace(sshHost) ||
                string.IsNullOrWhiteSpace(sshUser) ||
                string.IsNullOrWhiteSpace(laravelRoot))
                throw new InvalidOperationException("Credencial SSH/Laravel incompleta. Informe Host, Port, User e LaravelRoot.");

            var plinkPath = LocalizarPlink();
            if (string.IsNullOrWhiteSpace(plinkPath))
                throw new InvalidOperationException("plink.exe nao encontrado para executar o teste Laravel via SSH.");

            var phpCode =
                "$root = " + PhpString(laravelRoot) + ";" +
                "chdir($root);" +
                "require $root . '/vendor/autoload.php';" +
                "$app = require $root . '/bootstrap/app.php';" +
                "$kernel = $app->make(Illuminate\\Contracts\\Console\\Kernel::class);" +
                "$kernel->bootstrap();" +
                "Illuminate\\Support\\Facades\\DB::select('select 1 as ok');" +
                "echo json_encode(['ok' => true]);";

            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(phpCode));
            var remoteCommand = phpBinary + " -r " + ShellSingleQuote("eval(base64_decode('" + encoded + "'));");
            var args = "-batch -ssh " + Quote(sshUser + "@" + sshHost) +
                       " -P " + sshPort + " " +
                       ShellDoubleQuote(remoteCommand);

            if (!string.IsNullOrWhiteSpace(password))
                args = "-pw " + Quote(password) + " " + args;

            var startInfo = new ProcessStartInfo
            {
                FileName = plinkPath,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = Process.Start(startInfo))
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("Tempo esgotado ao testar banco via Laravel/SSH.");
                }

                var stdout = stdoutTask.Result;
                var stderr = stderrTask.Result;

                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Falha ao executar Laravel via SSH:\n" + (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr));

                if (stdout.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidOperationException("Laravel respondeu, mas o teste de banco nao confirmou sucesso:\n" + stdout);
            }

            return true;
        }

        private void GarantirTunelSshSeConfigurado(DatabaseProfile database)
        {
            if (database == null || !EhLoopback(database.Host))
                return;

            if (PortaLocalAberta(database.Port))
                return;

            var credencial = credenciais.FirstOrDefault(c =>
                string.Equals(c.Tipo, "SSH", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.Valor) &&
                c.Valor.IndexOf("LocalPort=" + database.Port, StringComparison.OrdinalIgnoreCase) >= 0);

            if (credencial == null)
                return;

            var cfg = ParseCredencialChaveValor(credencial.Valor);
            var sshHost = ObterValor(cfg, "Host");
            var sshUser = ObterValor(cfg, "User");
            var sshPort = ObterInteiro(cfg, "Port", 22);
            var localPort = ObterInteiro(cfg, "LocalPort", database.Port);
            var remoteHost = ObterValor(cfg, "RemoteHost");
            var remotePort = ObterInteiro(cfg, "RemotePort", 3306);
            var password = ObterValor(cfg, "Password");

            if (string.IsNullOrWhiteSpace(sshHost) ||
                string.IsNullOrWhiteSpace(sshUser) ||
                string.IsNullOrWhiteSpace(remoteHost))
                throw new InvalidOperationException("Credencial SSH do tunel incompleta. Informe Host, Port, User, LocalPort, RemoteHost e RemotePort.");

            var plinkPath = LocalizarPlink();
            if (string.IsNullOrWhiteSpace(plinkPath))
                throw new InvalidOperationException("plink.exe nao encontrado para abrir o tunel SSH.");

            var args = "-batch -ssh " + Quote(sshUser + "@" + sshHost) +
                       " -P " + sshPort +
                       " -L " + localPort + ":" + remoteHost + ":" + remotePort +
                       " -N";

            if (!string.IsNullOrWhiteSpace(password))
                args = "-pw " + Quote(password) + " " + args;

            var startInfo = new ProcessStartInfo
            {
                FileName = plinkPath,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(startInfo);

            var limite = DateTime.Now.AddSeconds(8);
            while (DateTime.Now < limite)
            {
                if (PortaLocalAberta(localPort))
                    return;

                Thread.Sleep(250);
            }

            throw new InvalidOperationException("Nao foi possivel abrir o tunel SSH na porta local " + localPort + ". Verifique usuario/senha SSH e liberacao do servidor.");
        }

        private bool EhLoopback(string host)
        {
            return string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
        }

        private bool PortaLocalAberta(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect("127.0.0.1", port, null, null);
                    var connected = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
                    if (!connected)
                        return false;

                    client.EndConnect(result);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private string LocalizarPlink()
        {
            var candidatos = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plink.exe"),
                @"C:\laragon\bin\heidisql\plink.exe",
                @"C:\laragon\bin\heidi sql\plink.exe"
            };

            return candidatos.FirstOrDefault(File.Exists);
        }

        private Dictionary<string, string> ParseCredencialChaveValor(string valor)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parte in (valor ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = parte.IndexOf('=');
                if (idx <= 0)
                    continue;

                dict[parte.Substring(0, idx).Trim()] = parte.Substring(idx + 1).Trim();
            }

            return dict;
        }

        private string ObterValor(Dictionary<string, string> cfg, string chave)
        {
            string valor;
            return cfg.TryGetValue(chave, out valor) ? valor : "";
        }

        private int ObterInteiro(Dictionary<string, string> cfg, string chave, int padrao)
        {
            int valor;
            return int.TryParse(ObterValor(cfg, chave), out valor) ? valor : padrao;
        }

        private string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private string ShellDoubleQuote(string value)
        {
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private string ShellSingleQuote(string value)
        {
            return "'" + (value ?? "").Replace("'", "'\"'\"'") + "'";
        }

        private string PhpString(string value)
        {
            return "'" + (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'") + "'";
        }

        private void AtualizarListaCredenciais()
        {
            lstCredenciais.DataSource = null;
            lstCredenciais.DataSource = credenciais.Select(c => $"{c.Tipo} | {c.Nome}").ToList();
        }

        private void btSalvar_Click(object sender, EventArgs e)
        {
            var projeto = CriarProjetoDoFormulario();

            string erro;
            if (!projeto.EhValido(out erro))
            {
                MessageBox.Show(erro, "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ProjetoSalvo = projetoEdicao == null
                ? ProjetoRepository.Salvar(projeto)
                : ProjetoRepository.Atualizar(projeto);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private Projeto CriarProjetoDoFormulario()
        {
            var projeto = projetoEdicao ?? new Projeto();

            projeto.Nome = txtNome.Text.Trim();
            projeto.Caminho = txtCaminho.Text.Trim();
            projeto.LanguageProfile = ObterLanguageProfileSelecionado();
            projeto.LanguageProfileId = ObterLanguageProfileIdSelecionado();
            projeto.KnowledgeIndexPath = textBoxKnowledgeIndexPath.Text.Trim();
            var provider = ObterAiProviderSelecionado();
            projeto.AiProviderId = provider == null ? AiProviderConfig.DefaultProviderId : provider.Id;
            projeto.IncrementarVersaoAoConcluir = ckIncVer.Checked;
            projeto.AutoVerificationEnabled = btVerificacao.Checked;
            projeto.Ftp = CriarFtpDoFormulario();
            projeto.Database = CriarDatabaseDoFormulario();
            projeto.Credenciais = credenciais.ToList();

            return projeto;
        }

        private FtpProfile CriarFtpDoFormulario()
        {
            return new FtpProfile
            {
                Host = txtFtpHost.Text.Trim(),
                Port = (int)numericFtpPorta.Value,
                User = txtFtpUsuario.Text.Trim(),
                Password = txtFtpSenha.Text,
                RemoteRoot = string.IsNullOrWhiteSpace(txtFtpRaiz.Text) ? "/" : txtFtpRaiz.Text.Trim(),
                EnableSsl = chkFtpSsl.Checked,
                UsePassive = chkFtpPassivo.Checked,
                UseBinary = true
            };
        }

        private DatabaseProfile CriarDatabaseDoFormulario()
        {
            return new DatabaseProfile
            {
                Type = string.IsNullOrWhiteSpace(cboDbTipo.Text) ? "MySQL" : cboDbTipo.Text.Trim(),
                Host = txtDbHost.Text.Trim(),
                Port = (int)numericDbPorta.Value,
                DatabaseName = txtDbNome.Text.Trim(),
                User = txtDbUsuario.Text.Trim(),
                Password = txtDbSenha.Text,
                UseSsl = chkDbSsl.Checked,
                Charset = string.IsNullOrWhiteSpace(txtDbCharset.Text) ? "utf8mb4" : txtDbCharset.Text.Trim(),
                TimeoutSeconds = (int)numericDbTimeout.Value
            };
        }

        private void cboDbTipo_SelectedIndexChanged(object sender, EventArgs e)
        {
            var tipo = cboDbTipo.SelectedItem as string;
            if (string.Equals(tipo, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                numericDbPorta.Value = 5432;
                txtDbCharset.Text = "UTF8";
                return;
            }

            if (string.Equals(tipo, "SQL Server", StringComparison.OrdinalIgnoreCase))
            {
                numericDbPorta.Value = 1433;
                txtDbCharset.Text = "";
                return;
            }

            numericDbPorta.Value = 3306;
            txtDbCharset.Text = "utf8mb4";
        }

        private void cboAiProvider_SelectedIndexChanged(object sender, EventArgs e)
        {
        }

        private void btCancelar_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
