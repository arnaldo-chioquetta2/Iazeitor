using System;

namespace GptBolDll
{
    public sealed class FtpAction
    {
        public string Operacao { get; set; }
        public string Remoto { get; set; }
        public string Local { get; set; }

        public static FtpAction From(AgentAction action)
        {
            if (action == null)
                return null;

            return new FtpAction
            {
                Operacao = GetString(action, "operacao"),
                Remoto = GetString(action, "remoto"),
                Local = GetString(action, "local")
            };
        }

        private static string GetString(AgentAction action, string propertyName)
        {
            if (action?.Dados == null)
                return null;

            return action.Dados[propertyName]?.ToString();
        }
    }
}
