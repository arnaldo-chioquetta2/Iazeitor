using Iazeitor.KnowledgeEngine.Contracts.Planning;
using Iazeitor.KnowledgeEngine.Contracts.Query;
using Iazeitor.KnowledgeEngine.Contracts.Quality;
using Iazeitor.KnowledgeEngine.Contracts.Routing;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Contracts.Updating;
using Iazeitor.KnowledgeEngine.Contracts.Public;

namespace Iazeitor.KnowledgeEngine.Contracts.Gateway
{
    public sealed class KnowledgeGatewayResult
    {
        public string OperationId { get; set; }
        public SessionContract Session { get; set; }
        public string Status { get; set; }
        public KnowledgeConfidence Confidence { get; set; }
        public string PipelineStage { get; set; }
        public string Limitations { get; set; }
        public bool RequiresAdditionalQuery { get; set; }
        public bool RequiresReplanning { get; set; }
        public bool HasConflicts { get; set; }
        public bool HasKnowledgeGaps { get; set; }
        public QueryResult QueryResult { get; set; }
        public RouterDecision RouterDecision { get; set; }
        public InvestigationPlan InvestigationPlan { get; set; }
        public InvestigationResult InvestigationResult { get; set; }
        public QualityAssessment QualityAssessment { get; set; }
        public KnowledgeUpdateResult KnowledgeUpdateResult { get; set; }
        public KnowledgeError Error { get; set; }
        public string Summary { get; set; }
    }
}
