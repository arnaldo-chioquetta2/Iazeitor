using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Catalog
{
    public interface IKnowledgePackCatalog
    {
        KnowledgePackIdentityContract Find(KnowledgePackIdentityContract identity);

        KnowledgePackVersionHistoryContract GetVersionHistory(KnowledgeIdentifier packId);

        IReadOnlyList<KnowledgePackIdentityContract> ListActive();

        IReadOnlyList<KnowledgePackIdentityContract> FindByDomain(string domain);

        IReadOnlyList<KnowledgePackIdentityContract> FindByScope(KnowledgeScope scope);
    }
}
