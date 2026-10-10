using System;

namespace Iazeitor.KnowledgeEngine.Contracts.Shared
{
    public sealed class KnowledgeMetadata
    {
        public KnowledgeMetadata(
            KnowledgeIdentifier identifier,
            KnowledgeVersion version,
            KnowledgeStatus status,
            KnowledgeConfidence confidence,
            KnowledgeReference source,
            DateTime createdAtUtc,
            DateTime? updatedAtUtc)
        {
            if (identifier == null)
            {
                throw new ArgumentNullException(nameof(identifier));
            }

            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            Identifier = identifier;
            Version = version;
            Status = status;
            Confidence = confidence;
            Source = source;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
        }

        public KnowledgeIdentifier Identifier { get; }

        public KnowledgeVersion Version { get; }

        public KnowledgeStatus Status { get; }

        public KnowledgeConfidence Confidence { get; }

        public KnowledgeReference Source { get; }

        public DateTime CreatedAtUtc { get; }

        public DateTime? UpdatedAtUtc { get; }
    }
}