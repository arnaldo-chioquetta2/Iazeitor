using System;
using System.Collections.Generic;

namespace GptBolDll
{
    public sealed class AgentResult
    {
        public Guid ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string ProjectRoot { get; set; }
        public string PromptBase { get; set; }
        public string ProjectContext { get; set; }
        public string FinalPrompt { get; set; }
        public string ModelResponse { get; set; }
        public AgentResponse StructuredResponse { get; set; }
        public string ParserOutcome { get; set; }
        public List<AgentAction> ExecutedActions { get; set; } = new List<AgentAction>();
        public bool ToolDispatchQueued { get; set; }
        public string ToolDispatchError { get; set; }
        public AgentOperationMode OperationMode { get; set; }
        public int NivelEfetivoUsado { get; set; }
        public int NivelAtribuido { get; set; }
        public string GeneratedTaskRequest { get; set; }
    }
}
