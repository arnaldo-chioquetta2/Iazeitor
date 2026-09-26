using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Domain.Discovery;

namespace Iazeitor.KnowledgeEngine.Compatibility.Mappers
{
    internal static class DiscoveryMapper
    {
        internal static DiscoveryContract ToContract(KnowledgeDiscovery source)
        {
            if (source == null) return null;
            return new DiscoveryContract
            {
                Identifier = source.Identifier,
                Content = source.Content,
                Classification = source.Classification,
                Source = source.Source,
                Scope = source.Scope,
                Confidence = source.Confidence,
                Status = source.Status,
                DiscoveredAt = new DateTimeOffset(source.DiscoveredAtUtc),
                RelatedReferences = source.RelatedReferences == null ? new List<KnowledgeReference>().AsReadOnly() : source.RelatedReferences.ToList().AsReadOnly(),
                Metadata = source.Metadata
            };
        }

        internal static KnowledgeDiscovery ToDomain(DiscoveryContract source)
        {
            if (source == null) return null;
            return new KnowledgeDiscovery
            {
                Identifier = source.Identifier,
                Content = source.Content,
                Classification = source.Classification,
                Source = source.Source,
                Scope = source.Scope,
                Confidence = source.Confidence,
                Status = source.Status,
                DiscoveredAtUtc = source.DiscoveredAt.UtcDateTime,
                RelatedReferences = source.RelatedReferences == null ? new List<KnowledgeReference>().AsReadOnly() : source.RelatedReferences.ToList().AsReadOnly(),
                Metadata = source.Metadata
            };
        }
    }
}
