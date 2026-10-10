using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Persistence;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Compatibility.Mappers
{
    internal static class PackPersistenceMapper
    {
        internal static KnowledgePackPersistenceContract ToPersistence(
            PackContract pack,
            KnowledgeIdentifier versionId,
            DateTimeOffset persistedAt)
        {
            if (pack == null || pack.Identifier == null || versionId == null)
            {
                return null;
            }

            return new KnowledgePackPersistenceContract
            {
                Identity = new KnowledgePackIdentityContract
                {
                    PackId = pack.Identifier,
                    Domain = pack.Scope == null ? null : pack.Scope.Domain,
                    Scope = pack.Scope,
                    CompatibilityVersion = null,
                    CreatedAt = persistedAt,
                    LastUpdatedAt = persistedAt
                },
                Metadata = new KnowledgePackMetadataContract
                {
                    Summary = null,
                    Tags = new List<string>().AsReadOnly()
                },
                Content = new KnowledgePackContentContract
                {
                    Name = pack.Name,
                    VersionId = versionId,
                    Status = pack.Status,
                    References = pack.References == null
                        ? new List<KnowledgeReference>().AsReadOnly()
                        : pack.References.ToList().AsReadOnly(),
                    Metadata = pack.Metadata
                }
            };
        }
    }
}
