using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Routing
{
    public sealed class RouterContract
    {
        public KnowledgeDemand Demand { get; set; }

        public KnowledgeScope Scope { get; set; }

        public IReadOnlyList<KnowledgeIdentifier> PreferredKnowledgePackIds { get; set; }

        public KnowledgeConfidence MinimumConfidence { get; set; }

        public KnowledgeStatus Status { get; set; }
    }
}