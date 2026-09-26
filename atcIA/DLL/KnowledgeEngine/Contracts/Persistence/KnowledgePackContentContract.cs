using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    public sealed class KnowledgePackContentContract
    {
        public string Name { get; set; }

        public KnowledgeIdentifier VersionId { get; set; }

        public KnowledgeStatus Status { get; set; }

        public IReadOnlyList<KnowledgeReference> References { get; set; }

        public KnowledgeMetadata Metadata { get; set; }
    }
}
