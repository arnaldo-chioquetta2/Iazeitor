using System;

namespace Iazeitor.KnowledgeEngine.Contracts.Shared
{
    public sealed class KnowledgeReference
    {
        public KnowledgeReference(string kind, KnowledgeIdentifier identifier, string location)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("O tipo da referência não pode ser vazio.", nameof(kind));
            }

            if (identifier == null)
            {
                throw new ArgumentNullException(nameof(identifier));
            }

            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("A localização da referência não pode ser vazia.", nameof(location));
            }

            Kind = kind;
            Identifier = identifier;
            Location = location;
        }

        public string Kind { get; }

        public KnowledgeIdentifier Identifier { get; }

        public string Location { get; }
    }
}