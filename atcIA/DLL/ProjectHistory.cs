using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GptBolDll
{
    public sealed class ProjectHistory
    {
        [JsonProperty("project_id")]
        public Guid ProjectId { get; set; }

        [JsonProperty("project_name")]
        public string ProjectName { get; set; }

        [JsonProperty("entries")]
        public List<ProjectHistoryEntry> Entries { get; set; } = new List<ProjectHistoryEntry>();
    }

    public sealed class ProjectHistoryEntry
    {
        [JsonProperty("timestamp_utc")]
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        [JsonProperty("user_question")]
        public string UserQuestion { get; set; }

        [JsonProperty("visible_answer")]
        public string VisibleAnswer { get; set; }

        [JsonProperty("proposed_actions")]
        public List<AgentAction> ProposedActions { get; set; } = new List<AgentAction>();

        [JsonProperty("executed_actions")]
        public List<AgentAction> ExecutedActions { get; set; } = new List<AgentAction>();

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("raw_response")]
        public string RawResponse { get; set; }

        [JsonProperty("language_profile")]
        public string LanguageProfile { get; internal set; }

        [JsonProperty("ai_provider_id")]
        public string AiProviderId { get; set; }

        [JsonProperty("ai_provider_name")]
        public string AiProviderName { get; set; }

        [JsonProperty("nivel_maximo_dificuldade")]
        public int NivelMaximoDificuldade { get; set; }

        [JsonProperty("nivel_maximo_ia")]
        public int NivelMaximoIa { get; set; }

        [JsonProperty("nivel_efetivo_usado")]
        public int NivelEfetivoUsado { get; set; }

        [JsonProperty("nivel_atribuido")]
        public int NivelAtribuido { get; set; }

        [JsonProperty("operation_mode")]
        public string OperationMode { get; set; }

        [JsonProperty("generated_task_request")]
        public string GeneratedTaskRequest { get; set; }
    }
}
