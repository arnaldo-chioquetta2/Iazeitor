using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Public
{
    public sealed class UpdateContract
    {
        public KnowledgeIdentifier Identifier { get; set; }
        public KnowledgeIdentifier KnowledgePackId { get; set; }
        public KnowledgeVersion PreviousVersion { get; set; }
        public KnowledgeVersion CurrentVersion { get; set; }
        public KnowledgeStatus Status { get; set; }
        public string Reason { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public IReadOnlyList<KnowledgeIdentifier> PromotedDiscoveryIds { get; set; }
    }
}
