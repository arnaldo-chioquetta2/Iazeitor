using System.Collections.Generic;

namespace GptBolDll
{
    public sealed class ToolDispatchResult
    {
        public List<AgentAction> ExecutedActions { get; set; } = new List<AgentAction>();
        public string Error { get; set; }
        public int FailedActionIndex { get; set; } = -1;
        public AgentAction FailedAction { get; set; }
    }
}
