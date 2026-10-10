using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    public interface IKnowledgePackRepository
    {
        IReadOnlyList<KnowledgePackIdentityContract> ListIdentities();

        KnowledgePackPersistenceContract Get(KnowledgeIdentifier packId);

        KnowledgePackVersionContract GetActiveVersion(KnowledgeIdentifier packId);

        IReadOnlyList<KnowledgePackVersionContract> ListVersions(KnowledgeIdentifier packId);

        KnowledgePackVersionHistoryContract GetVersionHistory(KnowledgeIdentifier packId);

        void Save(KnowledgePackPersistenceContract pack);

        void Update(KnowledgePackPersistenceContract pack);

        void Archive(KnowledgeIdentifier packId, KnowledgeIdentifier versionId);

        void RegisterVersion(KnowledgePackVersionContract version);

        void MarkVersionActive(KnowledgeIdentifier packId, KnowledgeIdentifier versionId);

        void CreateComposed(KnowledgePackPersistenceOperationContract operation);

        void UpdateComposed(KnowledgePackPersistenceOperationContract operation);

        KnowledgePackComposedPersistenceResult PersistComposed(KnowledgePackPersistenceOperationContract operation);

        KnowledgePackReadSnapshot ReadComposed(KnowledgeIdentifier packId);

        KnowledgePackIntegrityReport ValidateComposed(KnowledgeIdentifier packId);

        KnowledgePackRecoveryResult RecoverCreateTransactions(KnowledgeIdentifier packId);

        KnowledgePackRecoveryResult RecoverUpdateTransactions(KnowledgeIdentifier packId);
    }
}
