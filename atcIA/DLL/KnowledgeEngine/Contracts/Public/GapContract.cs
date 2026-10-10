using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Public
{
    public sealed class GapContract
    {
        public KnowledgeIdentifier Identifier { get; set; }
        public string Description { get; set; }
        public string Criticality { get; set; }
        public KnowledgeStatus Status { get; set; }
        public string RelatedDomain { get; set; }
        public KnowledgeScope Scope { get; set; }
        public KnowledgeReference Source { get; set; }
        public DateTimeOffset IdentifiedAt { get; set; }
        public IReadOnlyList<KnowledgeReference> RelatedReferences { get; set; }
        public KnowledgeMetadata Metadata { get; set; }
    }
}
