using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Domain.Updates
{
    public sealed class KnowledgeUpdate
    {
        public KnowledgeIdentifier Identifier { get; set; }
        public KnowledgeIdentifier KnowledgePackId { get; set; }
        public KnowledgeVersion PreviousVersion { get; set; }
        public KnowledgeVersion CurrentVersion { get; set; }
        public KnowledgeStatus Status { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public IReadOnlyList<KnowledgeIdentifier> PromotedDiscoveryIds { get; set; }
    }
}
