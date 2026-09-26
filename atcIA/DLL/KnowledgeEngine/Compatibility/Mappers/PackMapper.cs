using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Domain.Knowledge;

namespace Iazeitor.KnowledgeEngine.Compatibility.Mappers
{
    internal static class PackMapper
    {
        internal static PackContract ToContract(KnowledgePack source)
        {
            if (source == null) return null;
            return new PackContract
            {
                Identifier = source.Identifier,
                Name = source.Name,
                Version = source.Version,
                Scope = source.Scope,
                Metadata = source.Metadata,
                Status = source.Status,
                References = source.References == null ? new List<Contracts.Shared.KnowledgeReference>().AsReadOnly() : source.References.ToList().AsReadOnly()
            };
        }

        internal static KnowledgePack ToDomain(PackContract source)
        {
            if (source == null) return null;
            return new KnowledgePack
            {
                Identifier = source.Identifier,
                Name = source.Name,
                Version = source.Version,
                Scope = source.Scope,
                Metadata = source.Metadata,
                Status = source.Status,
                References = source.References == null ? new List<Contracts.Shared.KnowledgeReference>().AsReadOnly() : source.References.ToList().AsReadOnly()
            };
        }
    }
}
