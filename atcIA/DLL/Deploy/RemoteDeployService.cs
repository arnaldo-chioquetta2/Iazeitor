using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace GptBolDll.Deploy
{
    public sealed class RemoteDeployService
    {
        private readonly Action<string> _log;

        public RemoteDeployService(Action<string> log = null)
        {
            _log = log ?? delegate { };
        }

        public RemoteDeployResult UploadChangedFiles(
            string projectRoot,
            IEnumerable<string> relativeFiles,
            FtpProfile profile)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
                throw new InvalidOperationException("O projeto ativo não existe.");

            if (profile == null || string.IsNullOrWhiteSpace(profile.Host) ||
                string.IsNullOrWhiteSpace(profile.User) || !HasPassword(profile) ||
                string.IsNullOrWhiteSpace(profile.RemoteRoot))
                throw new InvalidOperationException("Credenciais de servidor não configuradas para este projeto.");

            var files = (relativeFiles ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(NormalizeRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
                throw new InvalidOperationException("Nenhum arquivo alterado disponível para envio.");

            _log("[DEPLOY] Envio manual iniciado.");
            _log("[DEPLOY] Arquivos para envio: " + files.Count);
            _log("[DEPLOY] Host configurado: true");
            _log("[DEPLOY] Usuario configurado: true");
            _log("[DEPLOY] Senha configurada: true");
            _log("[DEPLOY] Pasta remota configurada: true");

            var result = new RemoteDeployResult();
            var client = new FtpClient(profile);
            foreach (string relativePath in files)
            {
                var fileResult = new RemoteDeployFileResult { RelativePath = relativePath };
                result.Files.Add(fileResult);

                try
                {
                    string localPath = ResolveLocalPath(projectRoot, relativePath);
                    if (!File.Exists(localPath))
                        throw new FileNotFoundException("Arquivo alterado não encontrado localmente.");
                    if ((File.GetAttributes(localPath) & FileAttributes.Directory) != 0)
                        throw new InvalidOperationException("O caminho alterado não é um arquivo.");

                    string remotePath = relativePath.Replace('\\', '/');
                    _log("[DEPLOY] Enviando arquivo: " + relativePath);
                    client.UploadBytes(remotePath, File.ReadAllBytes(localPath));
                    fileResult.Success = true;
                    _log("[DEPLOY] Arquivo enviado com sucesso: " + relativePath);
                }
                catch (Exception ex)
                {
                    fileResult.ErrorMessage = SanitizeMessage(ex.Message, profile);
                    _log("[DEPLOY] Falha ao enviar arquivo: " + relativePath + " Motivo: " + fileResult.ErrorMessage);
                }
            }

            _log("[DEPLOY] Envio concluído. Sucesso: " + result.SuccessCount + " Falhas: " + result.FailureCount);
            return result;
        }

        private static bool HasPassword(FtpProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(profile.PasswordEnv))
                return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(profile.PasswordEnv));

            return !string.IsNullOrWhiteSpace(profile.Password);
        }

        private static string ResolveLocalPath(string projectRoot, string relativePath)
        {
            string root = Path.GetFullPath(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Caminho local fora do projeto.");
            return full;
        }

        private static string NormalizeRelativePath(string path)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('/', '\\');
            if (Path.IsPathRooted(normalized) || normalized.StartsWith("\\", StringComparison.Ordinal) ||
                normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Any(p => p == ".."))
                throw new InvalidOperationException("Caminho de arquivo alterado inválido.");
            return normalized.TrimStart('\\');
        }

        private static string SanitizeMessage(string message, FtpProfile profile)
        {
            string safe = message ?? "Falha de comunicação com o servidor.";
            if (!string.IsNullOrEmpty(profile.Password))
                safe = safe.Replace(profile.Password, "[REDACTED]");
            safe = Regex.Replace(safe, @"(?i)(password|passwd|pwd|token|api[_-]?key|authorization|bearer)\s*[:=]\s*[^\s,;]+", "$1=[REDACTED]");
            return safe;
        }
    }
}
