using System;

namespace Iazeitor.KnowledgeEngine.Contracts.Shared
{
    public sealed class KnowledgeScope
    {
        public KnowledgeScope(string domain, string boundary)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                throw new ArgumentException("O domínio do conhecimento não pode ser vazio.", nameof(domain));
            }

            if (string.IsNullOrWhiteSpace(boundary))
            {
                throw new ArgumentException("O limite do conhecimento não pode ser vazio.", nameof(boundary));
            }

            Domain = domain;
            Boundary = boundary;
        }

        public string Domain { get; }

        public string Boundary { get; }
    }
}