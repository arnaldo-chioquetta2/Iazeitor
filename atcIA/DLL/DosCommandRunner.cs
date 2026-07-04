using System;
using System.Diagnostics;
using System.Text;

namespace GptBolDll
{
    public sealed class DosCommandResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; }
        public string StdErr { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public static class DosCommandRunner
    {
        public static DosCommandResult Run(string command, string workingDirectory, int timeoutMs = 120_000)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("Comando vazio.");

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

            using (var p = new Process { StartInfo = psi })
            {
                p.Start();

                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();

                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException($"Timeout ao executar comando ({timeoutMs}ms): {command}");
                }

                sw.Stop();

                return new DosCommandResult
                {
                    ExitCode = p.ExitCode,
                    StdOut = stdout ?? "",
                    StdErr = stderr ?? "",
                    Duration = sw.Elapsed
                };
            }
        }
    }
}
