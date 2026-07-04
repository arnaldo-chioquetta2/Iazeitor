using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace GptBolDll
{
    public sealed class DosCommandTool
    {
        private readonly AllowedCommandPolicy _policy;
        private readonly ExecutionLogger _logger;

        public DosCommandTool(AllowedCommandPolicy policy, ExecutionLogger logger = null)
        {
            _policy = policy ?? new AllowedCommandPolicy();
            _logger = logger ?? new ExecutionLogger();
        }

        public DosCommandResult Execute(string command, string workingDirectory, int timeoutMs = 120_000)
        {
            return Execute(command, workingDirectory, false, timeoutMs);
        }

        public DosCommandResult Execute(string command, string workingDirectory, bool allowSafeVerification, int timeoutMs = 120_000)
        {
            if (allowSafeVerification && AllowedCommandPolicy.IsSafeVerificationCommand(command))
            {
                _logger.Info("[INFO] Comando de verificacao seguro permitido automaticamente: " + command);
            }
            else
            {
                _policy.EnsureAllowed(command);
            }

            var sw = Stopwatch.StartNew();
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + command,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _logger.Command(command);

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                if (!process.Start())
                    throw new InvalidOperationException("Nao foi possivel iniciar o cmd.exe.");

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException($"Timeout ao executar comando ({timeoutMs}ms): {command}");
                }

                Task.WaitAll(stdoutTask, stderrTask);

                sw.Stop();

                var stdout = stdoutTask.Result ?? string.Empty;
                var stderr = stderrTask.Result ?? string.Empty;

                _logger.Info("ExitCode=" + process.ExitCode + " DurationMs=" + (int)sw.Elapsed.TotalMilliseconds);
                _logger.StdOut(stdout);
                _logger.StdErr(stderr);

                return new DosCommandResult
                {
                    ExitCode = process.ExitCode,
                    StdOut = stdout,
                    StdErr = stderr,
                    Duration = sw.Elapsed
                };
            }
        }
    }
}
