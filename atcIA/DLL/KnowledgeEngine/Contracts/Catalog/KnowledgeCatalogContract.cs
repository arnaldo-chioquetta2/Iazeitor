using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Catalog
{
    public sealed class KnowledgeCatalogContract
    {
        public KnowledgeIdentifier Identifier { get; set; }

        public KnowledgeScope Scope { get; set; }

        public KnowledgeVersion Version { get; set; }

        public KnowledgeStatus Status { get; set; }

        public IReadOnlyList<KnowledgeIdentifier> KnowledgePackIds { get; set; }

        public KnowledgeMetadata Metadata { get; set; }
    }
}