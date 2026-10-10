using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    public sealed class KnowledgePackPersistenceContract
    {
        public KnowledgePackIdentityContract Identity { get; set; }
        public KnowledgePackMetadataContract Metadata { get; set; }
        public KnowledgePackContentContract Content { get; set; }
    }
}
