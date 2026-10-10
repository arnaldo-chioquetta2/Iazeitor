using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Planning
{
    public sealed class InvestigationStep
    {
        public string StepId { get; set; }
        public string Objective { get; set; }
        public string PrimaryDomain { get; set; }
        public IReadOnlyList<string> KnowledgePackIds { get; set; }
        public int Priority { get; set; }
        public IReadOnlyList<string> DependsOnStepIds { get; set; }
        public KnowledgeStatus Status { get; set; }
        public bool RequiresProjectConsultation { get; set; }
        public bool RequiresReplanning { get; set; }
        public string Limitation { get; set; }
    }
}
