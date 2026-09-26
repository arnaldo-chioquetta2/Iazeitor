using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Public
{
    public sealed class SessionContract
    {
        public KnowledgeIdentifier Identifier { get; set; }
        public KnowledgeStatus Status { get; set; }
        public KnowledgeDemand Demand { get; set; }
        public string Context { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public IReadOnlyList<KnowledgeIdentifier> KnowledgePackIds { get; set; }
        public KnowledgeMetadata Metadata { get; set; }
        public KnowledgeConfidence AccumulatedConfidence { get; set; }
        public IReadOnlyList<DiscoveryContract> Discoveries { get; set; }
        public IReadOnlyList<EvidenceContract> Evidences { get; set; }
        public IReadOnlyList<RelationContract> Relations { get; set; }
        public IReadOnlyList<ConflictContract> Conflicts { get; set; }
        public IReadOnlyList<GapContract> KnowledgeGaps { get; set; }
        public IReadOnlyList<UpdateContract> Updates { get; set; }
    }
}
