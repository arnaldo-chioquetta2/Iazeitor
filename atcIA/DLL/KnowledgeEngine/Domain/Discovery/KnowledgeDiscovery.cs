using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Domain.Discovery
{
    public sealed class KnowledgeDiscovery
    {
        public KnowledgeIdentifier Identifier { get; set; }

        public string Content { get; set; }

        public string Classification { get; set; }

        public KnowledgeReference Source { get; set; }

        public KnowledgeScope Scope { get; set; }

        public KnowledgeConfidence Confidence { get; set; }

        public KnowledgeStatus Status { get; set; }

        public DateTime DiscoveredAtUtc { get; set; }

        public IReadOnlyList<KnowledgeReference> RelatedReferences { get; set; }

        public KnowledgeMetadata Metadata { get; set; }
    }
}