using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Domain.Conflicts
{
    public sealed class KnowledgeRelation
    {
        public KnowledgeIdentifier Identifier { get; set; }
        public KnowledgeIdentifier FromIdentifier { get; set; }
        public KnowledgeIdentifier ToIdentifier { get; set; }
        public string Type { get; set; }
        public KnowledgeStatus Status { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public IReadOnlyList<KnowledgeReference> References { get; set; }
    }
}
