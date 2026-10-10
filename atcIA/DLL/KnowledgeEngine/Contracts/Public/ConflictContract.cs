using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Public
{
    public sealed class ConflictContract
    {
        public KnowledgeIdentifier Identifier { get; set; }
        public string Description { get; set; }
        public KnowledgeStatus Status { get; set; }
        public KnowledgeConfidence Confidence { get; set; }
        public DateTimeOffset IdentifiedAt { get; set; }
        public IReadOnlyList<KnowledgeReference> RelatedReferences { get; set; }
    }
}
