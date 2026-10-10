using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Catalog
{
    public interface IKnowledgeCatalog
    {
        KnowledgeCatalogContract Catalog { get; }

        KnowledgeIdentifier FindPackIdByIdentifier(KnowledgeIdentifier identifier);

        bool ContainsPack(KnowledgeIdentifier identifier);

        IReadOnlyList<KnowledgeIdentifier> FindPackIdsByDomain(string domain);

        IReadOnlyList<KnowledgeIdentifier> FindPackIdsByScope(KnowledgeScope scope);

        IReadOnlyList<KnowledgeIdentifier> FilterPackIdsByStatus(KnowledgeStatus status);

        IReadOnlyList<KnowledgeIdentifier> FilterPackIdsByVersion(KnowledgeVersion version);

        bool HasCompatiblePacks(KnowledgeScope scope, KnowledgeVersion version);

        KnowledgeCatalogContract CreateContract();
    }
}