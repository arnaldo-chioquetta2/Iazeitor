using System.Collections.Generic;
using Newtonsoft.Json;

namespace GptBolDll
{
    public sealed class AgentResponse
    {
        [JsonProperty("mensagem_usuario")]
        public string MensagemUsuario { get; set; }

        [JsonProperty("explicacao")]
        public string Explicacao { get; set; }

        [JsonProperty("acoes")]
        public List<AgentAction> Acoes { get; set; } = new List<AgentAction>();

        [JsonProperty("requer_confirmacao")]
        public bool RequerConfirmacao { get; set; }
    }
}
