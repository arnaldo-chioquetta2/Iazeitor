using System;
using System.IO;
using System.Text;

namespace GptBolDll
{
    public static class ProjectKnowledgeIndexLoader
    {
        private const int MaxKnowledgeIndexChars = 50000;

        public static string Load(string knowledgeIndexPath, ExecutionLogger log)
        {
            if (string.IsNullOrWhiteSpace(knowledgeIndexPath))
            {
                log?.Info("[WARN] Índice de conhecimento não configurado.");
                return string.Empty;
            }

            if (!File.Exists(knowledgeIndexPath))
            {
                log?.Info("[WARN] Arquivo de índice de conhecimento não encontrado: " + knowledgeIndexPath);
                return string.Empty;
            }

            string content;
            try
            {
                content = File.ReadAllText(knowledgeIndexPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                log?.Info("[WARN] Falha ao ler índice de conhecimento: " + ex.Message);
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                log?.Info("[WARN] Índice de conhecimento vazio.");
                return string.Empty;
            }

            if (content.Length > MaxKnowledgeIndexChars)
            {
                content = content.Substring(0, MaxKnowledgeIndexChars);
                log?.Info("[WARN] Índice de conhecimento truncado para 50000 caracteres.");
            }

            log?.Info("[INFO] Índice de conhecimento carregado. Caracteres: " + content.Length);
            return content;
        }
    }
}
