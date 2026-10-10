using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Domain.Knowledge
{
    public sealed class KnowledgePack
    {
        public KnowledgeIdentifier Identifier { get; set; }

        public string Name { get; set; }

        public KnowledgeVersion Version { get; set; }

        public KnowledgeScope Scope { get; set; }

        public KnowledgeMetadata Metadata { get; set; }

        public KnowledgeStatus Status { get; set; }

        public IReadOnlyList<KnowledgeReference> References { get; set; }
    }
}