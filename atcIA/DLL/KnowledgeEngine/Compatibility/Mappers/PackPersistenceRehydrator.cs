using System;
using System.Globalization;
using Iazeitor.KnowledgeEngine.Contracts.Persistence;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Compatibility.Mappers
{
    internal static class PackPersistenceRehydrator
    {
        internal static PackContract ToContract(
            KnowledgePackPersistenceContract persistence,
            KnowledgePackVersionContract version)
        {
            if (persistence == null || persistence.Identity == null ||
                persistence.Content == null || version == null)
            {
                return null;
            }

            KnowledgeIdentifier packId = persistence.Identity.PackId;
            KnowledgeIdentifier contentVersionId = persistence.Content.VersionId;
            if (packId == null || persistence.Identity.Scope == null ||
                contentVersionId == null || version.VersionId == null ||
                version.PackId == null ||
                !SameIdentifier(contentVersionId, version.VersionId) ||
                !SameIdentifier(packId, version.PackId))
            {
                return null;
            }

            KnowledgeVersion parsedVersion = ParseVersion(version.VersionNumber);
            if (parsedVersion == null)
            {
                return null;
            }

            return new PackContract
            {
                Identifier = packId,
                Name = persistence.Content.Name,
                Version = parsedVersion,
                Scope = persistence.Identity.Scope,
                Status = persistence.Content.Status,
                References = persistence.Content.References,
                Metadata = persistence.Content.Metadata
            };
        }

        private static KnowledgeVersion ParseVersion(string versionNumber)
        {
            if (String.IsNullOrWhiteSpace(versionNumber))
            {
                return null;
            }

            string[] parts = versionNumber.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }

            int major;
            int minor;
            int patch;
            if (!Int32.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major) ||
                !Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor) ||
                !Int32.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch) ||
                major < 0 || minor < 0 || patch < 0)
            {
                return null;
            }

            return new KnowledgeVersion(major, minor, patch);
        }

        private static bool SameIdentifier(
            KnowledgeIdentifier first,
            KnowledgeIdentifier second)
        {
            return first != null && second != null &&
                String.Equals(first.Value, second.Value, StringComparison.Ordinal);
        }
    }
}
