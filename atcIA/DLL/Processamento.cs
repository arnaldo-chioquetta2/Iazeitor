using System;
using System.IO;
using System.Xml;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;

namespace GptBolDll
{
    public class Processamento
    {
        private List<string> logMessages = new List<string>();
        private List<string> linhas = new List<string>();
        private readonly string[] TagsIgnoradas = { "#comment" };
        private bool ocorreuErro = false;
        private string arquivo = "";
        private int cursorLinha = 0;

        public void Processa(string respostaIA, ref TextBox textBox1, ref Timer clipboardTimer, ref Button button1)
        {
            Processa(respostaIA, projectRoot: null, ref textBox1, ref clipboardTimer, ref button1);
        }

        public void Processa(string respostaIA, string projectRoot, ref TextBox textBox1, ref Timer clipboardTimer, ref Button button1)
        {
            gen.logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");

            logMessages.Clear();
            ocorreuErro = false;

            if (File.Exists(gen.logFilePath))
                File.Delete(gen.logFilePath);

            try
            {
                gen.Log("=== RESPOSTA BRUTA DA IA ===");
                gen.Log(respostaIA);
                gen.Log("=== FIM RESPOSTA IA ===");

                int inicio = FindFirstMarker(respostaIA);

                if (inicio < 0)
                {
                    gen.Log("[ERRO] Nenhum marcador de protocolo encontrado (ex: ARQ=, DOS=, FTP_GET=...).");
                    return;
                }

                string conteudoProtocolo = respostaIA.Substring(inicio);

                // Remove markdown se vier
                conteudoProtocolo = conteudoProtocolo
                    .Replace("```csharp", "")
                    .Replace("```", "");

                gen.Log("Iniciando processamento em modo ATCIA PROTOCOL.");

                var engine = new AtcIaProtocolEngine(gen.Log);
                engine.Apply(conteudoProtocolo, projectRoot);

                gen.Log("Processamento concluído.");
            }
            catch (Exception ex)
            {
                gen.Log("ERRO FATAL durante o processamento:");
                gen.Log(ex.ToString());

                clipboardTimer?.Stop();
                button1.Enabled = true;

                ocorreuErro = true;
            }

            File.WriteAllLines(gen.logFilePath, logMessages);
            ExibirLog(ref textBox1);
        }

        private static int FindFirstMarker(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return -1;

            var markers = new[] { "ARQ=", "DOS=", "DOS_BLOCK", "FTP_GET=", "FTP_PUT=", "FTP_LIST=" };
            int best = -1;

            foreach (var m in markers)
            {
                int idx = text.IndexOf(m, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                if (best < 0 || idx < best) best = idx;
            }

            return best;
        }

        private void ExibirLog(ref TextBox textBox1)
        {
            // Exibe as mensagens no TextBox e grava no log.txt
            textBox1.Clear();
            foreach (var mensagem in logMessages)
            {
                textBox1.AppendText(mensagem + Environment.NewLine);
            }
        }

        private void AplicarSearchProtocol(string conteudo)
        {
            gen.Log("=== [SEARCH PROTOCOL MODE] ===");

            var comandos = conteudo.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            string arquivoAlvo = null;
            int index = 0;

            // Função auxiliar: pula linhas vazias/ruído
            bool IsIgnoravel(string s)
            {
                if (s == null) return true;
                var t = s.Trim();
                if (t.Length == 0) return true;
                if (t == "---") return true;
                if (t.StartsWith("#")) return true;
                if (t.StartsWith("//")) return true;
                if (t.StartsWith(";")) return true;
                return false;
            }

            // ============================
            // 1) ENCONTRAR E CARREGAR ARQUIVO
            // ============================
            // Procura a primeira linha ARQ= (case-insensitive) ignorando ruído inicial
            while (index < comandos.Length && IsIgnoravel(comandos[index]))
                index++;

            if (index >= comandos.Length)
            {
                gen.Log("[ERRO] Nenhum comando encontrado. Esperado ARQ=...");
                return;
            }

            string primeira = comandos[index].Trim();

            // Aceita ARQ= ou Arq= ou arq= ...
            if (!primeira.StartsWith("ARQ=", StringComparison.OrdinalIgnoreCase))
            {
                gen.Log($"[ERRO] Primeira linha útil deve ser ARQ=... (linha {index + 1}): {primeira}");
                return;
            }

            arquivoAlvo = primeira.Substring(4).Trim();

            if (!File.Exists(arquivoAlvo))
            {
                gen.Log($"[ERRO] Arquivo não encontrado: {arquivoAlvo}");
                return;
            }

            // Backup
            try
            {
                string bak = arquivoAlvo + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(arquivoAlvo, bak, true);
                gen.Log($"[BAK] Backup criado: {bak}");
            }
            catch (Exception ex)
            {
                gen.Log($"[WARN] Falha ao criar backup: {ex.Message}");
            }

            linhas = File.ReadAllLines(arquivoAlvo).ToList();
            gen.Log($"[OK] Arquivo carregado: {arquivoAlvo}");
            gen.Log($"[INFO] Total linhas: {linhas.Count}");

            index++; // vai para a próxima linha após ARQ=

            // ============================
            // 2) PROCESSAR COMANDOS
            // ============================
            while (index < comandos.Length)
            {
                string bruto = comandos[index];
                string linha = bruto?.Trim() ?? "";

                if (IsIgnoravel(linha))
                {
                    index++;
                    continue;
                }

                // --------------------------------
                // SEARCH + REPLACE simples
                // --------------------------------
                if (linha.StartsWith("SEARCH=", StringComparison.OrdinalIgnoreCase))
                {
                    string search = linha.Substring(7);

                    // pula ruídos até achar a próxima instrução
                    index++;
                    while (index < comandos.Length && IsIgnoravel(comandos[index]))
                        index++;

                    if (index >= comandos.Length)
                    {
                        gen.Log($"[ERRO] Faltou REPLACE= ou DELETE após SEARCH= (linha {index})");
                        break;
                    }

                    string next = (comandos[index] ?? "").Trim();

                    if (next.StartsWith("REPLACE=", StringComparison.OrdinalIgnoreCase))
                    {
                        string replace = next.Substring(8);

                        bool encontrou = false;

                        for (int i = 0; i < linhas.Count; i++)
                        {
                            if (linhas[i].Contains(search))
                            {
                                gen.Log($"[REPLACE] Linha {i + 1}");
                                linhas[i] = linhas[i].Replace(search, replace);
                                encontrou = true;
                                break;
                            }
                        }

                        if (!encontrou)
                            gen.Log($"[ERRO] SEARCH não encontrado: {search}");

                        index++; // avança após REPLACE=
                        continue;
                    }
                    else if (next.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                    {
                        bool apagou = false;
                        for (int i = 0; i < linhas.Count; i++)
                        {
                            if (linhas[i].Contains(search))
                            {
                                gen.Log($"[DELETE] Linha {i + 1}");
                                linhas.RemoveAt(i);
                                apagou = true;
                                break;
                            }
                        }

                        if (!apagou)
                            gen.Log($"[ERRO] SEARCH não encontrado para DELETE: {search}");

                        index++; // avança após DELETE
                        continue;
                    }
                    else
                    {
                        gen.Log($"[ERRO] Esperado REPLACE=... ou DELETE após SEARCH=... (linha {index + 1}): {next}");
                        index++;
                        continue;
                    }
                }

                // --------------------------------
                // SEARCH_BLOCK
                // --------------------------------
                if (linha.Equals("SEARCH_BLOCK", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    List<string> blocoBusca = new List<string>();

                    while (index < comandos.Length && !comandos[index].Trim().Equals("END_SEARCH", StringComparison.OrdinalIgnoreCase))
                    {
                        blocoBusca.Add(comandos[index]);
                        index++;
                    }

                    if (index >= comandos.Length)
                    {
                        gen.Log("[ERRO] SEARCH_BLOCK sem END_SEARCH.");
                        return;
                    }

                    index++; // pula END_SEARCH

                    // pula ruídos até REPLACE_BLOCK
                    while (index < comandos.Length && IsIgnoravel(comandos[index]))
                        index++;

                    if (index >= comandos.Length || !comandos[index].Trim().Equals("REPLACE_BLOCK", StringComparison.OrdinalIgnoreCase))
                    {
                        gen.Log("[ERRO] Esperado REPLACE_BLOCK.");
                        return;
                    }

                    index++;
                    List<string> blocoReplace = new List<string>();

                    while (index < comandos.Length && !comandos[index].Trim().Equals("END_REPLACE", StringComparison.OrdinalIgnoreCase))
                    {
                        blocoReplace.Add(comandos[index]);
                        index++;
                    }

                    if (index >= comandos.Length)
                    {
                        gen.Log("[ERRO] REPLACE_BLOCK sem END_REPLACE.");
                        return;
                    }

                    // aplicar substituição de bloco
                    AplicarSubstituicaoDeBloco(blocoBusca, blocoReplace);

                    index++; // pula END_REPLACE
                    continue;
                }

                // --------------------------------
                // LINHA NÃO RECONHECIDA -> agora vira SKIP, não ERRO fatal
                // --------------------------------
                gen.Log($"[SKIP] Linha não é comando (linha {index + 1}): {linha}");
                index++;
            }

            // ============================
            // 3) SALVAR
            // ============================
            File.WriteAllLines(arquivoAlvo, linhas);
            gen.Log("[OK] Arquivo salvo com sucesso.");
            gen.Log("=== [FIM SEARCH PROTOCOL] ===");
        }

        private void AplicarSubstituicaoDeBloco(List<string> blocoBusca, List<string> blocoReplace)
        {
            for (int i = 0; i <= linhas.Count - blocoBusca.Count; i++)
            {
                bool match = true;

                for (int j = 0; j < blocoBusca.Count; j++)
                {
                    if (linhas[i + j].Trim() != blocoBusca[j].Trim())
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    gen.Log($"[BLOCK REPLACE] Iniciando na linha {i + 1}");

                    linhas.RemoveRange(i, blocoBusca.Count);
                    linhas.InsertRange(i, blocoReplace);

                    return;
                }
            }

            gen.Log("[ERRO] Bloco SEARCH não encontrado.");
        }

    }
}
