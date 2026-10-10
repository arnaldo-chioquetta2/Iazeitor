using System;

namespace Iazeitor.PromptEngine.Contracts
{
    /// <summary>
    /// Mensagem semantica de prompt, neutra em relacao ao provider.
    /// Nao contem transporte HTTP, payload de provider, chave de API nem contagem de tokens.
    /// </summary>
    public sealed class PromptMessage
    {
        public PromptMessage(PromptRole role, string content, PromptTrustLevel trustLevel)
            : this(role, content, trustLevel, PromptMessageKind.Unspecified)
        {
        }

        public PromptMessage(PromptRole role, string content, PromptTrustLevel trustLevel, PromptMessageKind kind)
            : this(role, content, trustLevel, kind, null, false)
        {
        }

        internal PromptMessage(
            PromptRole role,
            string content,
            PromptTrustLevel trustLevel,
            PromptMessageKind kind,
            string untrustedOriginalContent,
            bool trustPolicyApplied)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            Role = role;
            Content = content;
            TrustLevel = trustLevel;
            Kind = kind;
            UntrustedOriginalContent = untrustedOriginalContent;
            TrustPolicyApplied = trustPolicyApplied;
        }

        public PromptRole Role { get; }

        /// <summary>
        /// Conteudo textual da mensagem. Nunca null.
        /// Texto vazio e permitido: o filtro de secoes vazias continua sendo responsabilidade
        /// do chamador, como acontece hoje na montagem de prompt existente.
        /// </summary>
        public string Content { get; }

        public PromptTrustLevel TrustLevel { get; }

        /// <summary>
        /// Funcao semantica do bloco no fluxo de prompt. Nao altera payload de provider
        /// e nao substitui Role/TrustLevel: e metadata usada pela politica de orcamento.
        /// </summary>
        public PromptMessageKind Kind { get; }

        internal string UntrustedOriginalContent { get; }

        internal bool TrustPolicyApplied { get; }
    }
}
