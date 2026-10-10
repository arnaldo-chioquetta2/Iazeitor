using System.Collections.Generic;
using System;
using Iazeitor.KnowledgeEngine.Contracts.Catalog;
using Iazeitor.KnowledgeEngine.Contracts.Persistence;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Infrastructure.Catalog
{
    internal sealed class KnowledgePackCatalog : IKnowledgePackCatalog
    {
        private readonly IKnowledgePackRepository repository;

        public KnowledgePackCatalog(IKnowledgePackRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            this.repository = repository;
        }

        public KnowledgePackIdentityContract Find(KnowledgePackIdentityContract identity)
        {
            if (identity == null || identity.PackId == null)
            {
                return null;
            }

            KnowledgePackPersistenceContract pack = repository.Get(identity.PackId);
            return pack == null ? null : pack.Identity;
        }

        public KnowledgePackVersionHistoryContract GetVersionHistory(KnowledgeIdentifier packId)
        {
            return packId == null ? null : repository.GetVersionHistory(packId);
        }

        public IReadOnlyList<KnowledgePackIdentityContract> ListActive()
        {
            var result = new List<KnowledgePackIdentityContract>();
            foreach (KnowledgePackIdentityContract identity in repository.ListIdentities())
            {
                try
                {
                    KnowledgePackVersionContract activeVersion = repository.GetActiveVersion(identity.PackId);
                    if (activeVersion != null)
                    {
                        result.Add(identity);
                    }
                }
                catch (Exception)
                {
                    // Um Pack inválido não impede a consulta dos demais.
                }
            }

            return result.AsReadOnly();
        }

        public IReadOnlyList<KnowledgePackIdentityContract> FindByDomain(string domain)
        {
            if (String.IsNullOrWhiteSpace(domain))
            {
                return new List<KnowledgePackIdentityContract>().AsReadOnly();
            }

            return FindCandidates(pack => String.Equals(pack.Domain, domain, StringComparison.Ordinal));
        }

        public IReadOnlyList<KnowledgePackIdentityContract> FindByScope(KnowledgeScope scope)
        {
            if (scope == null)
            {
                return new List<KnowledgePackIdentityContract>().AsReadOnly();
            }

            return FindCandidates(pack =>
                pack.Scope != null &&
                String.Equals(pack.Scope.Domain, scope.Domain, StringComparison.Ordinal) &&
                String.Equals(pack.Scope.Boundary, scope.Boundary, StringComparison.Ordinal));
        }

        private IReadOnlyList<KnowledgePackIdentityContract> FindCandidates(
            Func<KnowledgePackIdentityContract, bool> predicate)
        {
            var result = new List<KnowledgePackIdentityContract>();
            foreach (KnowledgePackIdentityContract identity in repository.ListIdentities())
            {
                if (identity != null && predicate(identity))
                {
                    result.Add(identity);
                }
            }

            return result.AsReadOnly();
        }
    }
}
