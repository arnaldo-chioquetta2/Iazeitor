using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Domain.Discovery
{
    public sealed class KnowledgeEvidence
    {
        public KnowledgeIdentifier Identifier { get; set; }

        public KnowledgeReference Source { get; set; }

        public string Type { get; set; }

        public string Description { get; set; }

        public string Classification { get; set; }

        public KnowledgeConfidence Confidence { get; set; }

        public DateTime CollectedAtUtc { get; set; }

        public IReadOnlyList<KnowledgeReference> RelatedReferences { get; set; }

        public KnowledgeMetadata Metadata { get; set; }
    }
}