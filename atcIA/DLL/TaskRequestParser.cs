using System;
using System.Text.RegularExpressions;

namespace GptBolDll
{
    public sealed class GeneratedTaskRequestInfo
    {
        public string TaskId { get; set; }
        public int NivelAtribuido { get; set; }
    }

    public static class TaskRequestParser
    {
        public static bool TryValidate(string response, int nivelEfetivo, out GeneratedTaskRequestInfo info, out string erro)
        {
            info = null;
            erro = string.Empty;

            if (string.IsNullOrWhiteSpace(response))
                return Fail("Resposta vazia.", out erro);

            string text = response.Trim();
            if (!text.StartsWith("TAREFA_ID:", StringComparison.Ordinal))
                return Fail("A resposta deve iniciar com TAREFA_ID.", out erro);
            if (text.IndexOf("```", StringComparison.Ordinal) >= 0)
                return Fail("A resposta nao pode conter markdown.", out erro);
            if (text.IndexOf("TRIGGER_PROXIMO:", StringComparison.Ordinal) < 0)
                return Fail("TRIGGER_PROXIMO ausente.", out erro);

            var idMatch = Regex.Match(text, @"^TAREFA_ID:\s*(?<id>\S+)", RegexOptions.Multiline);
            var nivelMatch = Regex.Match(text, @"^NIVEL_ATRIBUIDO:\s*(?<nivel>\d+)", RegexOptions.Multiline);
            if (!idMatch.Success || string.IsNullOrWhiteSpace(idMatch.Groups["id"].Value))
                return Fail("TAREFA_ID invalido.", out erro);
            if (!nivelMatch.Success || !int.TryParse(nivelMatch.Groups["nivel"].Value, out int nivel))
                return Fail("NIVEL_ATRIBUIDO invalido.", out erro);
            if (nivel < 1 || nivel > 10)
                return Fail("NIVEL_ATRIBUIDO deve estar entre 1 e 10.", out erro);
            if (nivel > nivelEfetivo)
                return Fail("NIVEL_ATRIBUIDO excede o nivel efetivo.", out erro);

            Require(text, "ETAPAS:", out erro);
            if (!string.IsNullOrWhiteSpace(erro)) return false;
            Require(text, "OBJETIVO:", out erro);
            if (!string.IsNullOrWhiteSpace(erro)) return false;
            Require(text, "ESCOPO:", out erro);
            if (!string.IsNullOrWhiteSpace(erro)) return false;
            Require(text, "DEPENDENCIAS:", out erro);
            if (!string.IsNullOrWhiteSpace(erro)) return false;
            Require(text, "VERIFICACAO:", out erro);
            if (!string.IsNullOrWhiteSpace(erro)) return false;
            Require(text, "RESTRICOES_GERAIS:", out erro);
            if (!string.IsNullOrWhiteSpace(erro)) return false;
            Require(text, "FORMATO_EXECUCAO:", out erro);
            if (!string.IsNullOrWhiteSpace(erro)) return false;

            info = new GeneratedTaskRequestInfo
            {
                TaskId = idMatch.Groups["id"].Value.Trim(),
                NivelAtribuido = nivel
            };
            return true;
        }

        private static void Require(string text, string marker, out string erro)
        {
            erro = text.IndexOf(marker, StringComparison.Ordinal) >= 0 ? string.Empty : marker + " ausente.";
        }

        private static bool Fail(string message, out string erro)
        {
            erro = message;
            return false;
        }
    }
}
