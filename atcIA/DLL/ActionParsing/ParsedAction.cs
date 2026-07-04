using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace GptBolDll
{
    public sealed class ParsedAction
    {
        public string Type { get; set; }
        public bool IsExecutable { get; set; }
        public JObject Data { get; set; }
        public string ProtocolText { get; set; }
        public string Description { get; set; }
        public string RawActionJson { get; set; }
        public string Source { get; set; }
        public bool WasNormalized { get; set; }
        public bool IsNoOp { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();

        public bool HasErrors => Errors.Count > 0;
        public bool HasWarnings => Warnings.Count > 0;
    }
}
