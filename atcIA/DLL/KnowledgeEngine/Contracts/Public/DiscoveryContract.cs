using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Public
{
    public sealed class DiscoveryContract
    {
        public KnowledgeIdentifier Identifier { get; set; }
        public string Content { get; set; }
        public string Classification { get; set; }
        public KnowledgeReference Source { get; set; }
        public KnowledgeScope Scope { get; set; }
        public KnowledgeConfidence Confidence { get; set; }
        public KnowledgeStatus Status { get; set; }
        public DateTimeOffset DiscoveredAt { get; set; }
        public IReadOnlyList<KnowledgeReference> RelatedReferences { get; set; }
        public KnowledgeMetadata Metadata { get; set; }
    }
}
