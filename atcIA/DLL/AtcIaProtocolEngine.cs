using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GptBolDll
{
    public sealed class AtcIaProtocolEngine
    {
        private readonly Action<string> _log;

        public List<string> LastChangedFiles { get; } = new List<string>();

        public AtcIaProtocolEngine(Action<string> logger)
        {
            _log = logger ?? (_ => { });
        }

        public void Apply(string protocolText, string projectRoot = null)
        {
            if (string.IsNullOrWhiteSpace(protocolText))
                throw new ArgumentException("Protocolo vazio.");

            LastChangedFiles.Clear();
            var projectBase = !string.IsNullOrWhiteSpace(projectRoot) ? projectRoot : Environment.CurrentDirectory;
            var definition = ProjectDefinitionLoader.LoadOrDefault(projectBase);
            var lines = SplitLines(protocolText);

            var fileSegment = new List<string>();

            Action flushFileSegment = () =>
            {
                if (fileSegment.Count == 0)
                    return;

                var text = string.Join(Environment.NewLine, fileSegment);
                var engine = new SearchProtocolEngine(_log, projectBase);

                engine.Apply(text, makeBackup: false);
                // engine.Apply(text, makeBackup: true);

                LastChangedFiles.AddRange(engine.LastChangedFiles);
                fileSegment.Clear();
            };

            FtpClient ftp = null;

            Func<FtpClient> getFtp = () =>
            {
                if (ftp != null)
                    return ftp;

                if (definition.Ftp == null || string.IsNullOrWhiteSpace(definition.Ftp.Host))
                    throw new Exception("FTP nao configurado em atcia.project.json (campo Ftp).");

                ftp = new FtpClient(definition.Ftp);
                return ftp;
            };

            string workDir = !string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot)
                ? projectRoot
                : Environment.CurrentDirectory;

            for (int i = 0; i < lines.Count; i++)
            {
                var raw = lines[i] ?? "";
                var line = raw.TrimEnd();
                var trimmed = line.Trim();

                if (fileSegment.Count > 0 && IsInsideFileProtocolBlock(fileSegment) && !IsTopLevelProtocolCommand(trimmed) && !EqualsCmd(trimmed, "END_SEARCH") && !EqualsCmd(trimmed, "END_REPLACE"))
                {
                    fileSegment.Add(line);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (trimmed.StartsWith("#") || trimmed.StartsWith("//") || trimmed.StartsWith(";"))
                    continue;

                if (StartsWithCmd(trimmed, "ARQ=") || IsSearchCmd(trimmed) || EqualsCmd(trimmed, "END_SEARCH") || EqualsCmd(trimmed, "END_REPLACE"))
                {
                    fileSegment.Add(line);
                    continue;
                }

                flushFileSegment();

                if (StartsWithCmd(trimmed, "DOS="))
                {
                    var cmd = trimmed.Substring("DOS=".Length);
                    ExecuteDosCommand(cmd, workDir, definition);
                    continue;
                }

                if (EqualsCmd(trimmed, "DOS_BLOCK"))
                {
                    var block = ReadBlock(lines, ref i, "END_DOS");
                    foreach (var cmdLine in block.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()))
                        ExecuteDosCommand(cmdLine, workDir, definition);
                    continue;
                }

                if (StartsWithCmd(trimmed, "FTP_GET="))
                {
                    var spec = trimmed.Substring("FTP_GET=".Length).Trim();
                    var parts = spec.Split(new[] { "=>" }, StringSplitOptions.None);
                    if (parts.Length != 2)
                        throw new Exception("FTP_GET invalido. Use FTP_GET=/remote/arquivo.txt=>relativo\\local.txt");

                    var remote = parts[0].Trim();
                    var localRel = parts[1].Trim();
                    var localPath = ResolveLocalPath(workDir, localRel);

                    _log("[FTP_GET] " + remote + " => " + localPath);
                    var bytes = getFtp().DownloadBytes(remote);
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? workDir);
                    File.WriteAllBytes(localPath, bytes);
                    LastChangedFiles.Add(localPath);
                    _log("[FTP_GET] OK (" + bytes.Length + " bytes)");
                    continue;
                }

                if (StartsWithCmd(trimmed, "FTP_PUT="))
                {
                    var spec = trimmed.Substring("FTP_PUT=".Length).Trim();
                    var parts = spec.Split(new[] { "=>" }, StringSplitOptions.None);
                    if (parts.Length != 2)
                        throw new Exception("FTP_PUT invalido. Use FTP_PUT=relativo\\local.txt=>/remote/local.txt");

                    var localRel = parts[0].Trim();
                    var remote = parts[1].Trim();
                    var localPath = ResolveLocalPath(workDir, localRel);

                    if (!File.Exists(localPath))
                        throw new FileNotFoundException("Arquivo local nao encontrado para FTP_PUT: " + localPath, localPath);

                    var bytes = File.ReadAllBytes(localPath);
                    _log("[FTP_PUT] " + localPath + " => " + remote);
                    getFtp().UploadBytes(remote, bytes);
                    _log("[FTP_PUT] OK (" + bytes.Length + " bytes)");
                    continue;
                }

                if (StartsWithCmd(trimmed, "FTP_LIST="))
                {
                    var remoteDir = trimmed.Substring("FTP_LIST=".Length).Trim();
                    _log("[FTP_LIST] " + remoteDir);
                    var items = getFtp().ListDirectory(remoteDir);
                    _log("[FTP_LIST] Itens:\n" + string.Join("\n", items));
                    continue;
                }

                throw new Exception("Comando de protocolo desconhecido: " + trimmed);
            }

            flushFileSegment();
        }

        private static string ResolveLocalPath(string workDir, string localRelOrAbs)
        {
            if (string.IsNullOrWhiteSpace(localRelOrAbs))
                throw new ArgumentException("Caminho local vazio.");

            if (Path.IsPathRooted(localRelOrAbs))
                return localRelOrAbs;

            string normalized = localRelOrAbs.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
            string rootName = Path.GetFileName(workDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(rootName))
            {
                string prefix = rootName + Path.DirectorySeparatorChar;
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.Substring(prefix.Length);
            }

            return Path.GetFullPath(Path.Combine(workDir, normalized));
        }

        private static List<string> SplitLines(string text)
        {
            return (text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        }

        private static void ValidateDosCommand(string command, ProjectDefinition definition)
        {
            if (definition == null || definition.AllowedDosCommands == null || definition.AllowedDosCommands.Count == 0)
                return;

            if (ContainsCommandChain(command))
                throw new Exception("Comando DOS nao pode usar encadeamento (&, &&, ||, |).");

            var firstToken = ExtractFirstToken(command);
            if (string.IsNullOrWhiteSpace(firstToken))
                throw new Exception("Comando DOS vazio.");

            if (!definition.AllowedDosCommands.Any(x => string.Equals((x ?? "").Trim(), firstToken, StringComparison.OrdinalIgnoreCase)))
                throw new Exception("Comando DOS nao permitido pelo projeto: " + firstToken);
        }

        private void ExecuteDosCommand(string command, string workDir, ProjectDefinition definition)
        {
            ValidateDosCommand(command, definition);
            _log("[DOS] " + command);
            var result = DosCommandRunner.Run(command, workDir);
            _log("[DOS] ExitCode=" + result.ExitCode + " DurationMs=" + (int)result.Duration.TotalMilliseconds);
            if (!string.IsNullOrWhiteSpace(result.StdOut))
                _log("[DOS:STDOUT]\n" + result.StdOut.TrimEnd());
            if (!string.IsNullOrWhiteSpace(result.StdErr))
                _log("[DOS:STDERR]\n" + result.StdErr.TrimEnd());
        }

        private static bool ContainsCommandChain(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var text = command;
            return text.Contains("&&") || text.Contains("||") || text.Contains("|") || text.Contains("&");
        }

        private static string ExtractFirstToken(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return "";

            var trimmed = command.TrimStart();
            int index = 0;

            while (index < trimmed.Length && !char.IsWhiteSpace(trimmed[index]) && trimmed[index] != '&' && trimmed[index] != '|' && trimmed[index] != '>')
                index++;

            if (index <= 0)
                return "";

            return trimmed.Substring(0, index);
        }

        private static bool StartsWithCmd(string line, string cmd)
        {
            return line != null && line.StartsWith(cmd, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EqualsCmd(string line, string cmd)
        {
            return string.Equals((line ?? "").Trim(), cmd, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSearchCmd(string line)
        {
            if (line == null) return false;
            var t = line.Trim();
            return StartsWithCmd(t, "SEARCH=") ||
                   EqualsCmd(t, "DELETE") ||
                   StartsWithCmd(t, "SEARCH_BLOCK=") ||
                   EqualsCmd(t, "SEARCH_BLOCK") ||
                   EqualsCmd(t, "END_SEARCH") ||
                   StartsWithCmd(t, "REPLACE_BLOCK=") ||
                   EqualsCmd(t, "REPLACE_BLOCK") ||
                   EqualsCmd(t, "END_REPLACE") ||
                   EqualsCmd(t, "DELETE_BLOCK") ||
                   StartsWithCmd(t, "REPLACE=");
        }

        private static bool IsInsideFileProtocolBlock(List<string> fileSegment)
        {
            if (fileSegment == null || fileSegment.Count == 0)
                return false;

            for (int i = fileSegment.Count - 1; i >= 0; i--)
            {
                var line = (fileSegment[i] ?? "").Trim();
                if (EqualsCmd(line, "END_SEARCH") || EqualsCmd(line, "END_REPLACE"))
                    return false;

                if (EqualsCmd(line, "SEARCH_BLOCK"))
                    return true;

                if (StartsWithCmd(line, "SEARCH_BLOCK="))
                    return true;

                if (EqualsCmd(line, "REPLACE_BLOCK"))
                    return true;

                if (StartsWithCmd(line, "REPLACE_BLOCK="))
                    return true;

                if (StartsWithCmd(line, "REPLACE="))
                    return true;

                if (StartsWithCmd(line, "ARQ=") ||
                    StartsWithCmd(line, "SEARCH=") ||
                    EqualsCmd(line, "DELETE_BLOCK"))
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IsTopLevelProtocolCommand(string trimmed)
        {
            return StartsWithCmd(trimmed, "ARQ=") ||
                   StartsWithCmd(trimmed, "DOS=") ||
                   EqualsCmd(trimmed, "DOS_BLOCK") ||
                   StartsWithCmd(trimmed, "FTP_GET=") ||
                   StartsWithCmd(trimmed, "FTP_PUT=") ||
                   StartsWithCmd(trimmed, "FTP_LIST=");
        }

        private static List<string> ReadBlock(List<string> lines, ref int index, string endMarker)
        {
            var block = new List<string>();

            while (index + 1 < lines.Count)
            {
                index++;
                var current = (lines[index] ?? "").TrimEnd();
                if (string.Equals(current.Trim(), endMarker, StringComparison.OrdinalIgnoreCase))
                    return block;

                block.Add(lines[index]);
            }

            throw new Exception("Bloco nao terminou. Faltou '" + endMarker + "'.");
        }
    }
}
