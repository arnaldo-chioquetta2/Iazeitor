using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GptBolDll
{
    public sealed class FtpTool
    {
        private readonly FtpProfile _profile;
        private readonly ExecutionLogger _logger;
        private readonly string _projectRoot;

        public FtpTool(FtpProfile profile, string projectRoot, ExecutionLogger logger = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _projectRoot = string.IsNullOrWhiteSpace(projectRoot) ? AppDomain.CurrentDomain.BaseDirectory : projectRoot;
            _logger = logger ?? new ExecutionLogger();
        }

        public FtpResult Execute(FtpAction action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var result = new FtpResult
            {
                Operation = (action.Operacao ?? "").Trim().ToUpperInvariant(),
                RemotePath = action.Remoto,
                LocalPath = action.Local
            };

            var client = new FtpClient(_profile);

            switch (result.Operation)
            {
                case "GET":
                    ExecuteGet(client, action, result);
                    break;
                case "PUT":
                    ExecutePut(client, action, result);
                    break;
                case "LIST":
                    ExecuteList(client, action, result);
                    break;
                default:
                    throw new InvalidOperationException("Operacao FTP desconhecida: " + action.Operacao);
            }

            return result;
        }

        private void ExecuteGet(FtpClient client, FtpAction action, FtpResult result)
        {
            if (string.IsNullOrWhiteSpace(action.Remoto) || string.IsNullOrWhiteSpace(action.Local))
                throw new InvalidOperationException("FTP GET requer 'remoto' e 'local'.");

            var localPath = ResolveLocalPath(action.Local);
            _logger.Info("FTP GET " + action.Remoto + " => " + localPath);

            var bytes = client.DownloadBytes(action.Remoto);
            Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? AppDomain.CurrentDomain.BaseDirectory);
            File.WriteAllBytes(localPath, bytes);

            _logger.FileChanged("Arquivo gravado: " + localPath);
            _logger.Info("FTP GET OK (" + bytes.Length + " bytes)");

            result.LocalPath = localPath;
            result.BytesTransferred = bytes.Length;
        }

        private void ExecutePut(FtpClient client, FtpAction action, FtpResult result)
        {
            if (string.IsNullOrWhiteSpace(action.Local) || string.IsNullOrWhiteSpace(action.Remoto))
                throw new InvalidOperationException("FTP PUT requer 'local' e 'remoto'.");

            var sourcePath = ResolveLocalPath(action.Local);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Arquivo local nao encontrado: " + sourcePath, sourcePath);

            var bytes = File.ReadAllBytes(sourcePath);
            _logger.Info("FTP PUT " + sourcePath + " => " + action.Remoto);
            client.UploadBytes(action.Remoto, bytes);
            _logger.FtpSent(sourcePath + " => " + action.Remoto + " (" + bytes.Length + " bytes)");

            result.LocalPath = sourcePath;
            result.BytesTransferred = bytes.Length;
        }

        private void ExecuteList(FtpClient client, FtpAction action, FtpResult result)
        {
            var remoteDir = string.IsNullOrWhiteSpace(action.Remoto) ? "/" : action.Remoto;
            _logger.Info("FTP LIST " + remoteDir);

            var items = client.ListDirectory(remoteDir) ?? Array.Empty<string>();
            result.Items = items.ToList();
            _logger.Info("Itens:\n" + string.Join("\n", result.Items));
        }

        private string ResolveLocalPath(string localPath)
        {
            if (string.IsNullOrWhiteSpace(localPath))
                throw new ArgumentException("Caminho local vazio.");

            if (Path.IsPathRooted(localPath))
                return localPath;

            return Path.GetFullPath(Path.Combine(_projectRoot, localPath));
        }
    }
}
