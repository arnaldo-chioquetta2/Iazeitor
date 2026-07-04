using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GptBolDll
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AgentActionType
    {
        Nenhuma,
        ArquivoLocal,
        ComandoDos,
        Ftp
    }
}
