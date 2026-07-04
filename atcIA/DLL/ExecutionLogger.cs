using System;
using System.IO;

namespace GptBolDll
{
    public sealed class ExecutionLogger
    {
        private readonly Action<string> _sink;
        private readonly string _logFilePath;

        public ExecutionLogger(Action<string> sink = null, string logFilePath = null)
        {
            _sink = sink ?? (_ => { });
            _logFilePath = string.IsNullOrWhiteSpace(logFilePath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dados", "execucao.log")
                : logFilePath;
        }

        public string LogFilePath
        {
            get { return _logFilePath; }
        }

        public void Clear()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath) ?? AppDomain.CurrentDomain.BaseDirectory);
                File.WriteAllText(_logFilePath, string.Empty);
            }
            catch
            {
            }
        }

        public void Command(string command)
        {
            Write("[CMD] " + command);
        }

        public void StdOut(string stdout)
        {
            if (string.IsNullOrWhiteSpace(stdout))
                return;

            Write("[STDOUT]\n" + stdout.TrimEnd());
        }

        public void StdErr(string stderr)
        {
            if (string.IsNullOrWhiteSpace(stderr))
                return;

            Write("[STDERR]\n" + stderr.TrimEnd());
        }

        public void FileChanged(string description)
        {
            Write("[ARQUIVO] " + description);
        }

        public void FtpSent(string description)
        {
            Write("[FTP] " + description);
        }

        public void PermissionDenied(string description)
        {
            Write("[PERMISSAO] " + description);
        }

        public void Info(string message)
        {
            Write("[INFO] " + message);
        }

        public void Error(string message)
        {
            Write("[ERRO] " + message);
        }

        private void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath) ?? AppDomain.CurrentDomain.BaseDirectory);
                File.AppendAllLines(_logFilePath, new[] { message });
            }
            catch
            {
            }

            _sink(message);
        }
    }
}
