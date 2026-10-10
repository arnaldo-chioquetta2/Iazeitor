using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Domain.Conflicts
{
    public sealed class KnowledgeConflict
    {
        public KnowledgeIdentifier Identifier { get; set; }
        public string Description { get; set; }
        public KnowledgeStatus Status { get; set; }
        public KnowledgeConfidence Confidence { get; set; }
        public DateTime IdentifiedAtUtc { get; set; }
        public IReadOnlyList<KnowledgeReference> RelatedReferences { get; set; }
    }
}
