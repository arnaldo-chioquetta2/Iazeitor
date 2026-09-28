using System;

namespace GptBolDll
{
    /// <summary>
    /// Estrutura de dados que representará o contexto completo de uma requisição.
    /// Apenas modelo de dados; ainda não utilizado por nenhuma classe do sistema.
    /// </summary>
    public sealed class RequestContext
    {
        public ProjectDefinition Project { get; set; }
        public AiProviderConfig Provider { get; set; }
        public string UserInput { get; set; }
        public string PromptBase { get; set; }
        public string PromptFinal { get; set; }
        public string SystemPrompt { get; set; }
        public string ProjectContext { get; set; }
        public AgentResponse StructuredResponse { get; set; }
        public AgentResult Result { get; set; }
        public ActionParseResult ParseResult { get; set; }
        public AgentOperationMode OperationMode { get; set; }
        public string WorkingDirectory { get; set; }
        public bool UseGitDiff { get; set; }
        public bool AutoContinue { get; set; }
        public bool AutoVerification { get; set; }
        public DateTime StartedAt { get; set; }
    }
}
