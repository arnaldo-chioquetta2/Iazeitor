using System;

namespace Iazeitor.KnowledgeEngine.Contracts.Shared
{
    public sealed class KnowledgeIdentifier
    {
        public KnowledgeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("O identificador do conhecimento não pode ser vazio.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }
    }
}