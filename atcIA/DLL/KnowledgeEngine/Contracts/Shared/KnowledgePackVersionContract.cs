using System;

namespace Iazeitor.KnowledgeEngine.Contracts.Shared
{
    public sealed class KnowledgePackVersionContract
    {
        public KnowledgeIdentifier VersionId { get; set; }
        public KnowledgeIdentifier PackId { get; set; }
        public string VersionNumber { get; set; }
        public int Revision { get; set; }
        public KnowledgePackLifecycleStatus LifecycleStatus { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? PromotedAt { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }
        public bool IsCurrent { get; set; }
    }
}
