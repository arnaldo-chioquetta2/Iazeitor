using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Domain.Updates;

namespace Iazeitor.KnowledgeEngine.Compatibility.Mappers
{
    internal static class UpdateMapper
    {
        internal static UpdateContract ToContract(KnowledgeUpdate source)
        {
            if (source == null) return null;
            return new UpdateContract
            {
                Identifier = source.Identifier,
                KnowledgePackId = source.KnowledgePackId,
                PreviousVersion = source.PreviousVersion,
                CurrentVersion = source.CurrentVersion,
                Status = source.Status,
                Reason = source.Reason,
                CreatedAt = new DateTimeOffset(source.CreatedAtUtc),
                PromotedDiscoveryIds = source.PromotedDiscoveryIds == null ? new List<Contracts.Shared.KnowledgeIdentifier>().AsReadOnly() : source.PromotedDiscoveryIds.ToList().AsReadOnly()
            };
        }

        internal static KnowledgeUpdate ToDomain(UpdateContract source)
        {
            if (source == null) return null;
            return new KnowledgeUpdate
            {
                Identifier = source.Identifier,
                KnowledgePackId = source.KnowledgePackId,
                PreviousVersion = source.PreviousVersion,
                CurrentVersion = source.CurrentVersion,
                Status = source.Status,
                Reason = source.Reason,
                CreatedAtUtc = source.CreatedAt.UtcDateTime,
                PromotedDiscoveryIds = source.PromotedDiscoveryIds == null ? new List<Contracts.Shared.KnowledgeIdentifier>().AsReadOnly() : source.PromotedDiscoveryIds.ToList().AsReadOnly()
            };
        }
    }
}
