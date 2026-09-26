using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    public sealed class KnowledgePackJournalContract
    {
        public int SchemaVersion { get; set; }

        public KnowledgeIdentifier TransactionId { get; set; }

        public KnowledgeIdentifier PackId { get; set; }

        public KnowledgePackJournalState State { get; set; }

        public KnowledgeIdentifier VersionId { get; set; }

        public KnowledgeIdentifier PreviousVersionIdToArchive { get; set; }

        public string StagingDirectoryName { get; set; }
    }
}
