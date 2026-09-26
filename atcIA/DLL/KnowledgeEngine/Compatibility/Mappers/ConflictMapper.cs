using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Domain.Conflicts;

namespace Iazeitor.KnowledgeEngine.Compatibility.Mappers
{
    internal static class ConflictMapper
    {
        internal static ConflictContract ToContract(KnowledgeConflict source)
        {
            if (source == null) return null;
            return new ConflictContract
            {
                Identifier = source.Identifier,
                Description = source.Description,
                Status = source.Status,
                Confidence = source.Confidence,
                IdentifiedAt = new DateTimeOffset(source.IdentifiedAtUtc),
                RelatedReferences = source.RelatedReferences == null ? new List<KnowledgeReference>().AsReadOnly() : source.RelatedReferences.ToList().AsReadOnly()
            };
        }

        internal static KnowledgeConflict ToDomain(ConflictContract source)
        {
            if (source == null) return null;
            return new KnowledgeConflict
            {
                Identifier = source.Identifier,
                Description = source.Description,
                Status = source.Status,
                Confidence = source.Confidence,
                IdentifiedAtUtc = source.IdentifiedAt.UtcDateTime,
                RelatedReferences = source.RelatedReferences == null ? new List<KnowledgeReference>().AsReadOnly() : source.RelatedReferences.ToList().AsReadOnly()
            };
        }
    }
}
