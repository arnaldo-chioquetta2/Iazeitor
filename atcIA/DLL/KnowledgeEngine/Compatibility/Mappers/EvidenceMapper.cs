using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Domain.Discovery;

namespace Iazeitor.KnowledgeEngine.Compatibility.Mappers
{
    internal static class EvidenceMapper
    {
        internal static EvidenceContract ToContract(KnowledgeEvidence source)
        {
            if (source == null) return null;
            return new EvidenceContract
            {
                Identifier = source.Identifier,
                Source = source.Source,
                Type = source.Type,
                Description = source.Description,
                Classification = source.Classification,
                Confidence = source.Confidence,
                CollectedAt = new DateTimeOffset(source.CollectedAtUtc),
                RelatedReferences = source.RelatedReferences == null ? new List<KnowledgeReference>().AsReadOnly() : source.RelatedReferences.ToList().AsReadOnly(),
                Metadata = source.Metadata
            };
        }

        internal static KnowledgeEvidence ToDomain(EvidenceContract source)
        {
            if (source == null) return null;
            return new KnowledgeEvidence
            {
                Identifier = source.Identifier,
                Source = source.Source,
                Type = source.Type,
                Description = source.Description,
                Classification = source.Classification,
                Confidence = source.Confidence,
                CollectedAtUtc = source.CollectedAt.UtcDateTime,
                RelatedReferences = source.RelatedReferences == null ? new List<KnowledgeReference>().AsReadOnly() : source.RelatedReferences.ToList().AsReadOnly(),
                Metadata = source.Metadata
            };
        }
    }
}
