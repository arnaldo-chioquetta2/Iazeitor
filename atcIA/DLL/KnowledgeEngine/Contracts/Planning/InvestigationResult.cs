using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Planning
{
    public sealed class InvestigationResult
    {
        public string InvestigationId { get; set; }
        public KnowledgeDemand Demand { get; set; }
        public KnowledgeStatus Status { get; set; }
        public string Summary { get; set; }
        public KnowledgeConfidence Confidence { get; set; }
        public InvestigationPlan Plan { get; set; }
        public IReadOnlyList<string> StepIds { get; set; }
        public IReadOnlyList<string> DiscoveryIds { get; set; }
        public IReadOnlyList<string> KnowledgeGapIds { get; set; }
        public IReadOnlyList<string> EvidenceIds { get; set; }
        public IReadOnlyList<string> RelationIds { get; set; }
        public IReadOnlyList<string> ConflictIds { get; set; }
        public IReadOnlyList<string> Limitations { get; set; }
        public bool Interrupted { get; set; }
        public bool RequiresAdditionalQuery { get; set; }
        public bool RequiresReplanning { get; set; }
        public IReadOnlyList<KnowledgeSource> Sources { get; set; }
        public KnowledgeError Error { get; set; }
    }
}
