using System;

namespace Iazeitor.KnowledgeEngine.Contracts.Shared
{
    public sealed class KnowledgePackIdentityContract
    {
        public KnowledgeIdentifier PackId { get; set; }
        public string Domain { get; set; }
        public KnowledgeScope Scope { get; set; }
        public string CompatibilityVersion { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastUpdatedAt { get; set; }
    }
}
