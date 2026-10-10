using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    public sealed class KnowledgePackPersistenceOperationContract
    {
        public KnowledgePackPersistenceContract Persistence { get; set; }

        public KnowledgePackVersionContract Version { get; set; }

        public KnowledgeIdentifier PreviousVersionIdToArchive { get; set; }
    }
}
