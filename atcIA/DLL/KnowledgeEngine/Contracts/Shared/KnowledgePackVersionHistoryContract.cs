using System.Collections.Generic;

namespace Iazeitor.KnowledgeEngine.Contracts.Shared
{
    public sealed class KnowledgePackVersionHistoryContract
    {
        public KnowledgePackVersionContract CurrentVersion { get; set; }
        public KnowledgePackVersionContract ActiveVersion { get; set; }
        public KnowledgePackVersionContract ArchivedVersion { get; set; }
        public IReadOnlyList<KnowledgePackVersionContract> PreviousVersions { get; set; }
    }
}
