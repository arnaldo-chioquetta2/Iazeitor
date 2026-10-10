using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Domain.Discovery;
using Iazeitor.KnowledgeEngine.Domain.Conflicts;
using Iazeitor.KnowledgeEngine.Domain.Gaps;
using Iazeitor.KnowledgeEngine.Domain.Updates;

namespace Iazeitor.KnowledgeEngine.Domain.Session
{
    public sealed class KnowledgeSession
    {
        public KnowledgeIdentifier Identifier { get; set; }

        public KnowledgeStatus Status { get; set; }

        public KnowledgeDemand Demand { get; set; }

        public string Context { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public DateTime? EndedAtUtc { get; set; }

        public IReadOnlyList<KnowledgeIdentifier> KnowledgePackIds { get; set; }

        public KnowledgeMetadata Metadata { get; set; }

        public KnowledgeConfidence AccumulatedConfidence { get; set; }

        public IReadOnlyList<KnowledgeDiscovery> Discoveries { get; set; }

        public IReadOnlyList<KnowledgeEvidence> Evidences { get; set; }

        public IReadOnlyList<KnowledgeRelation> Relations { get; set; }

        public IReadOnlyList<KnowledgeConflict> Conflicts { get; set; }

        public IReadOnlyList<KnowledgeGap> KnowledgeGaps { get; set; }

        public IReadOnlyList<KnowledgeUpdate> Updates { get; set; }
    }
}
