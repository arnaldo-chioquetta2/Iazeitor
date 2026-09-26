using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Domain.Conflicts;

namespace Iazeitor.KnowledgeEngine.Compatibility.Mappers
{
    internal static class RelationMapper
    {
        internal static RelationContract ToContract(KnowledgeRelation source)
        {
            if (source == null) return null;
            return new RelationContract
            {
                Identifier = source.Identifier,
                FromIdentifier = source.FromIdentifier,
                ToIdentifier = source.ToIdentifier,
                Type = source.Type,
                Status = source.Status,
                CreatedAt = new DateTimeOffset(source.CreatedAtUtc),
                References = source.References == null ? new List<KnowledgeReference>().AsReadOnly() : source.References.ToList().AsReadOnly()
            };
        }

        internal static KnowledgeRelation ToDomain(RelationContract source)
        {
            if (source == null) return null;
            return new KnowledgeRelation
            {
                Identifier = source.Identifier,
                FromIdentifier = source.FromIdentifier,
                ToIdentifier = source.ToIdentifier,
                Type = source.Type,
                Status = source.Status,
                CreatedAtUtc = source.CreatedAt.UtcDateTime,
                References = source.References == null ? new List<KnowledgeReference>().AsReadOnly() : source.References.ToList().AsReadOnly()
            };
        }
    }
}
