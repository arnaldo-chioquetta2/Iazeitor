using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Query
{
    public sealed class QueryContract
    {
        public KnowledgeIdentifier QueryId { get; set; }

        public KnowledgeDemand Demand { get; set; }

        public KnowledgeIdentifier SessionId { get; set; }

        public KnowledgeScope Scope { get; set; }

        public KnowledgeConfidence MinimumConfidence { get; set; }
    }
}