using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GptBolDll
{
    public sealed class AtcIaProtocolEngine
    {
        private readonly Action<string> _log;

        public AtcIaProtocolEngine(Action<string> logger)
        {
            _log = logger ?? (_ => { });
        }

        public void Apply(string protocolText, string projectRoot = null)
        {
            if (string.IsNullOrWhiteSpace(protocolText))
                throw new ArgumentException("Protocolo vazio.");

            var lines = SplitLines(protocolText);

            var fileSegment = new List<string>();
            void FlushFileSegment()
            {
                if (fileSegment.Count == 0)
                    return;

                var text = string.Join(Environment.NewLine, fileSegment);
                var engine = new SearchProtocolEngine(_log);
                engine.Apply(text, makeBackup: true);
                fileSegment.Clear();
            }

            ProjectDefinition def = null;
            FtpClient ftp = null;
            FtpClient GetFtp()
            {
                if (ftp != null) return ftp;
                def = ProjectDefinitionLoader.LoadOrDefault(projectRoot ?? Environment.CurrentDirectory);
                if (def.Ftp == null || string.IsNullOrWhiteSpace(def.Ftp.Host))
                    throw new Exception("FTP não configurado em atcia.project.json (campo `Ftp`).");
                ftp = new FtpClient(def.Ftp);
                return ftp;
            }

            string workDir = !string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot)
                ? projectRoot
                : Environment.CurrentDirectory;

            for (int i = 0; i < lines.Count; i++)
            {
                var raw = lines[i] ?? "";
                var line = raw.TrimEnd();
                var trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (trimmed.StartsWith("#") || trimmed.StartsWith("//") || trimmed.StartsWith(";"))
                    continue;

                // Segmento de edição de arquivo (ARQ= + SEARCH/REPLACE/DELETE)
                if (StartsWithCmd(trimmed, "ARQ=") || IsSearchCmd(trimmed))
                {
                    fileSegment.Add(line);
                    continue;
                }

                // Chegou aqui: não é comando de arquivo => aplica o que acumulou
                FlushFileSegment();

                // DOS= (um comando)
                if (StartsWithCmd(trimmed, "DOS="))
                {
                    var cmd = trimmed.Substring("DOS=".Length);
                    _log($"[DOS] {cmd}");
                    var r = DosCommandRunner.Run(cmd, workDir);
                    _log($"[DOS] ExitCode={r.ExitCode} DurationMs={(int)r.Duration.TotalMilliseconds}");
                    if (!string.IsNullOrWhiteSpace(r.StdOut))
                        _log("[DOS:STDOUT]\n" + r.StdOut.TrimEnd());
                    if (!string.IsNullOrWhiteSpace(r.StdErr))
                        _log("[DOS:STDERR]\n" + r.StdErr.TrimEnd());
                    continue;
                }

                // DOS_BLOCK ... END_DOS (várias linhas viram um único /c)
                if (EqualsCmd(trimmed, "DOS_BLOCK"))
                {
                    var block = ReadBlock(lines, ref i, "END_DOS");
                    var cmd = string.Join(" & ", block.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
                    _log("[DOS_BLOCK] " + cmd);
                    var r = DosCommandRunner.Run(cmd, workDir);
                    _log($"[DOS] ExitCode={r.ExitCode} DurationMs={(int)r.Duration.TotalMilliseconds}");
                    if (!string.IsNullOrWhiteSpace(r.StdOut))
                        _log("[DOS:STDOUT]\n" + r.StdOut.TrimEnd());
                    if (!string.IsNullOrWhiteSpace(r.StdErr))
                        _log("[DOS:STDERR]\n" + r.StdErr.TrimEnd());
                    continue;
                }

                // FTP_GET=remote=>local
                if (StartsWithCmd(trimmed, "FTP_GET="))
                {
                    var spec = trimmed.Substring("FTP_GET=".Length).Trim();
                    var parts = spec.Split(new[] { "=>" }, StringSplitOptions.None);
                    if (parts.Length != 2)
                        throw new Exception("FTP_GET inválido. Use: FTP_GET=/remote/arquivo.txt=>relativo\\local.txt");

                    var remote = parts[0].Trim();
                    var localRel = parts[1].Trim();
                    var localPath = ResolveLocalPath(workDir, localRel);

                    _log($"[FTP_GET] {remote} => {localPath}");
                    var bytes = GetFtp().DownloadBytes(remote);
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? workDir);
                    File.WriteAllBytes(localPath, bytes);
                    _log($"[FTP_GET] OK ({bytes.Length} bytes)");
                    continue;
                }

                // FTP_PUT=local=>remote
                if (StartsWithCmd(trimmed, "FTP_PUT="))
                {
                    var spec = trimmed.Substring("FTP_PUT=".Length).Trim();
                    var parts = spec.Split(new[] { "=>" }, StringSplitOptions.None);
                    if (parts.Length != 2)
                        throw new Exception("FTP_PUT inválido. Use: FTP_PUT=relativo\\local.txt=>/remote/local.txt");

                    var localRel = parts[0].Trim();
                    var remote = parts[1].Trim();
                    var localPath = ResolveLocalPath(workDir, localRel);

                    if (!File.Exists(localPath))
                        throw new FileNotFoundException("Arquivo local não encontrado para FTP_PUT: " + localPath, localPath);

                    var bytes = File.ReadAllBytes(localPath);
                    _log($"[FTP_PUT] {localPath} => {remote}");
                    GetFtp().UploadBytes(remote, bytes);
                    _log($"[FTP_PUT] OK ({bytes.Length} bytes)");
                    continue;
                }

                // FTP_LIST=/dir
                if (StartsWithCmd(trimmed, "FTP_LIST="))
                {
                    var remoteDir = trimmed.Substring("FTP_LIST=".Length).Trim();
                    _log("[FTP_LIST] " + remoteDir);
                    var items = GetFtp().ListDirectory(remoteDir);
                    _log("[FTP_LIST] Itens:\n" + string.Join("\n", items));
                    continue;
                }

                throw new Exception("Comando de protocolo desconhecido: " + trimmed);
            }

            FlushFileSegment();
        }

        private static string ResolveLocalPath(string workDir, string localRelOrAbs)
        {
            if (string.IsNullOrWhiteSpace(localRelOrAbs))
                throw new ArgumentException("Caminho local vazio.");

            if (Path.IsPathRooted(localRelOrAbs))
                return localRelOrAbs;

            return Path.GetFullPath(Path.Combine(workDir, localRelOrAbs));
        }

        private static List<string> SplitLines(string text)
        {
            return (text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        }

        private static bool StartsWithCmd(string line, string cmd)
        {
            return line != null && line.StartsWith(cmd, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EqualsCmd(string line, string cmd)
        {
            return string.Equals(line?.Trim(), cmd, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSearchCmd(string line)
        {
            if (line == null) return false;
            var t = line.Trim();
            return StartsWithCmd(t, "SEARCH=") ||
                   EqualsCmd(t, "DELETE") ||
                   EqualsCmd(t, "SEARCH_BLOCK") ||
                   EqualsCmd(t, "END_SEARCH") ||
                   EqualsCmd(t, "REPLACE_BLOCK") ||
                   EqualsCmd(t, "END_REPLACE") ||
                   EqualsCmd(t, "DELETE_BLOCK") ||
                   StartsWithCmd(t, "REPLACE=");
        }

        private static List<string> ReadBlock(List<string> lines, ref int index, string endMarker)
        {
            var block = new List<string>();

            while (index + 1 < lines.Count)
            {
                index++;
                var cur = (lines[index] ?? "").TrimEnd();
                if (string.Equals(cur.Trim(), endMarker, StringComparison.OrdinalIgnoreCase))
                    return block;

                block.Add(lines[index]);
            }

            throw new Exception($"Bloco não terminou. Faltou '{endMarker}'.");
        }
    }
}
