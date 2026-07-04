using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GptBolDll
{
    public sealed class AgentAction
    {
        [JsonProperty("tipo")]
        public AgentActionType Tipo { get; set; }

        [JsonProperty("descricao")]
        public string Descricao { get; set; }

        [JsonProperty("dados")]
        public JObject Dados { get; set; }

        [JsonProperty("requer_confirmacao")]
        public bool RequerConfirmacao { get; set; }
    }
}
