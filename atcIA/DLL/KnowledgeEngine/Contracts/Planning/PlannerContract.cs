using Iazeitor.KnowledgeEngine.Contracts.Routing;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Planning
{
    public sealed class PlannerContract
    {
        public KnowledgeDemand Demand { get; set; }

        public RouterDecision RouterDecision { get; set; }

        public KnowledgeIdentifier SessionId { get; set; }

        public KnowledgeConfidence Confidence { get; set; }

        public KnowledgeStatus Status { get; set; }
    }
}