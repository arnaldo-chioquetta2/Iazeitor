using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Routing;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Planning
{
    public sealed class InvestigationPlan
    {
        public string PlanId { get; set; }
        public KnowledgeDemand Demand { get; set; }
        public RouterDecision RouterDecision { get; set; }
        public string PrimaryDomain { get; set; }
        public KnowledgeIdentifier SessionId { get; set; }
        public IReadOnlyList<string> KnowledgePackIds { get; set; }
        public IReadOnlyList<InvestigationStep> Steps { get; set; }
        public KnowledgeStatus Status { get; set; }
        public bool RequiresProjectConsultation { get; set; }
        public bool RequiresReplanning { get; set; }
        public IReadOnlyList<string> Limitations { get; set; }
    }
}
