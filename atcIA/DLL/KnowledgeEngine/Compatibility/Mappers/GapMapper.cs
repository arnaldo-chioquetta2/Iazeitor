using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Domain.Gaps;

namespace Iazeitor.KnowledgeEngine.Compatibility.Mappers
{
    internal static class GapMapper
    {
        internal static GapContract ToContract(KnowledgeGap source)
        {
            if (source == null) return null;
            return new GapContract
            {
                Identifier = source.Identifier,
                Description = source.Description,
                Criticality = source.Criticality,
                Status = source.Status,
                RelatedDomain = source.RelatedDomain,
                Scope = source.Scope,
                Source = source.Source,
                IdentifiedAt = new DateTimeOffset(source.IdentifiedAtUtc),
                RelatedReferences = source.RelatedReferences == null ? new List<KnowledgeReference>().AsReadOnly() : source.RelatedReferences.ToList().AsReadOnly(),
                Metadata = source.Metadata
            };
        }

        internal static KnowledgeGap ToDomain(GapContract source)
        {
            if (source == null) return null;
            return new KnowledgeGap
            {
                Identifier = source.Identifier,
                Description = source.Description,
                Criticality = source.Criticality,
                Status = source.Status,
                RelatedDomain = source.RelatedDomain,
                Scope = source.Scope,
                Source = source.Source,
                IdentifiedAtUtc = source.IdentifiedAt.UtcDateTime,
                RelatedReferences = source.RelatedReferences == null ? new List<KnowledgeReference>().AsReadOnly() : source.RelatedReferences.ToList().AsReadOnly(),
                Metadata = source.Metadata
            };
        }
    }
}
