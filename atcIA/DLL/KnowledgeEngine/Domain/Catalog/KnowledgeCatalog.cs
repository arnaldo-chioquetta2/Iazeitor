using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Catalog;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Domain.Knowledge;

namespace Iazeitor.KnowledgeEngine.Domain.Catalog
{
    public sealed class KnowledgeCatalog : IKnowledgeCatalog
    {
        private readonly IReadOnlyList<KnowledgePack> packs;
        private readonly KnowledgeCatalogContract catalog;

        public KnowledgeCatalog()
            : this(new List<KnowledgePack>(), new KnowledgeCatalogContract())
        {
        }

        public KnowledgeCatalog(IEnumerable<KnowledgePack> packs)
            : this(packs, new KnowledgeCatalogContract())
        {
        }

        public KnowledgeCatalog(
            IEnumerable<KnowledgePack> packs,
            KnowledgeCatalogContract catalog)
        {
            if (packs == null)
            {
                throw new ArgumentNullException(nameof(packs));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            this.packs = new List<KnowledgePack>(packs).AsReadOnly();
            this.catalog = catalog;
        }

        public KnowledgeCatalogContract Catalog
        {
            get { return catalog; }
        }

        public IReadOnlyList<KnowledgePack> Packs
        {
            get { return packs; }
        }

        public KnowledgeIdentifier FindPackIdByIdentifier(KnowledgeIdentifier identifier)
        {
            if (identifier == null)
            {
                return null;
            }

            return packs
                .Where(pack =>
                    pack != null &&
                    pack.Identifier != null &&
                    string.Equals(pack.Identifier.Value, identifier.Value, StringComparison.Ordinal))
                .Select(pack => pack.Identifier)
                .FirstOrDefault();
        }

        public bool ContainsPack(KnowledgeIdentifier identifier)
        {
            return FindPackIdByIdentifier(identifier) != null;
        }

        public IReadOnlyList<KnowledgeIdentifier> FindPackIdsByDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                return new List<KnowledgeIdentifier>().AsReadOnly();
            }

            return packs
                .Where(pack =>
                    pack != null &&
                    pack.Identifier != null &&
                    pack.Scope != null &&
                    string.Equals(pack.Scope.Domain, domain, StringComparison.Ordinal))
                .Select(pack => pack.Identifier)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<KnowledgeIdentifier> FindPackIdsByScope(KnowledgeScope scope)
        {
            if (scope == null)
            {
                return new List<KnowledgeIdentifier>().AsReadOnly();
            }

            return packs
                .Where(pack =>
                    pack != null &&
                    pack.Identifier != null &&
                    pack.Scope != null &&
                    string.Equals(pack.Scope.Domain, scope.Domain, StringComparison.Ordinal) &&
                    string.Equals(pack.Scope.Boundary, scope.Boundary, StringComparison.Ordinal))
                .Select(pack => pack.Identifier)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<KnowledgeIdentifier> FilterPackIdsByStatus(KnowledgeStatus status)
        {
            return packs
                .Where(pack => pack != null && pack.Identifier != null && pack.Status == status)
                .Select(pack => pack.Identifier)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<KnowledgeIdentifier> FilterPackIdsByVersion(KnowledgeVersion version)
        {
            if (version == null)
            {
                return new List<KnowledgeIdentifier>().AsReadOnly();
            }

            return packs
                .Where(pack =>
                    pack != null &&
                    pack.Identifier != null &&
                    pack.Version != null &&
                    pack.Version.Major == version.Major &&
                    pack.Version.Minor == version.Minor &&
                    pack.Version.Patch == version.Patch)
                .Select(pack => pack.Identifier)
                .ToList()
                .AsReadOnly();
        }

        public bool HasCompatiblePacks(KnowledgeScope scope, KnowledgeVersion version)
        {
            if (scope == null || version == null)
            {
                return false;
            }

            return packs.Any(pack =>
                pack != null &&
                pack.Identifier != null &&
                pack.Scope != null &&
                pack.Version != null &&
                (pack.Status == KnowledgeStatus.Active || pack.Status == KnowledgeStatus.Partial) &&
                string.Equals(pack.Scope.Domain, scope.Domain, StringComparison.Ordinal) &&
                string.Equals(pack.Scope.Boundary, scope.Boundary, StringComparison.Ordinal) &&
                pack.Version.Major == version.Major &&
                pack.Version.Minor == version.Minor &&
                pack.Version.Patch == version.Patch);
        }

        public KnowledgeCatalogContract CreateContract()
        {
            return new KnowledgeCatalogContract
            {
                Identifier = catalog.Identifier,
                Scope = catalog.Scope,
                Version = catalog.Version,
                Status = catalog.Status,
                KnowledgePackIds = packs
                    .Where(pack => pack != null && pack.Identifier != null)
                    .Select(pack => pack.Identifier)
                    .ToList()
                    .AsReadOnly(),
                Metadata = catalog.Metadata
            };
        }
    }
}