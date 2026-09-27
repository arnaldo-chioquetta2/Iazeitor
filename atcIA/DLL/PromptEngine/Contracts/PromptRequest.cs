using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Iazeitor.PromptEngine.Contracts
{
    /// <summary>
    /// Requisicao semantica de prompt: descreve a intencao do prompt, nao o transporte HTTP.
    ///
    /// Provider, Model, TemplateId e TemplateVersion sao apenas metadados para futura
    /// selecao de renderer/orcamento. Nao alteram a semantica das mensagens.
    /// </summary>
    public sealed class PromptRequest
    {
        public PromptRequest(IReadOnlyList<PromptMessage> messages, string provider, string model)
            : this(messages, provider, model, null, null)
        {
        }

        public PromptRequest(
            IReadOnlyList<PromptMessage> messages,
            string provider,
            string model,
            string templateId,
            string templateVersion)
            : this(messages, provider, model, templateId, templateVersion, false)
        {
        }

        internal PromptRequest(
            IReadOnlyList<PromptMessage> messages,
            string provider,
            string model,
            string templateId,
            string templateVersion,
            bool trustPolicyApplied)
        {
            if (messages == null)
                throw new ArgumentNullException(nameof(messages));

            var copy = new List<PromptMessage>(messages.Count);
            for (int i = 0; i < messages.Count; i++)
            {
                PromptMessage message = messages[i];
                if (message == null)
                    throw new ArgumentException("A colecao de mensagens nao pode conter itens null.", nameof(messages));

                copy.Add(message);
            }

            Messages = new ReadOnlyCollection<PromptMessage>(copy);
            Provider = provider ?? string.Empty;
            Model = model ?? string.Empty;
            TemplateId = templateId ?? string.Empty;
            TemplateVersion = templateVersion ?? string.Empty;
            TrustPolicyApplied = trustPolicyApplied;
        }

        /// <summary>Copia imutavel das mensagens, na ordem informada pelo chamador.</summary>
        public IReadOnlyList<PromptMessage> Messages { get; }

        /// <summary>Nome do provider como string configuravel. Null vira string vazia.</summary>
        public string Provider { get; }

        /// <summary>Nome do modelo como string configuravel. Null vira string vazia.</summary>
        public string Model { get; }

        /// <summary>Identificador opcional de template. Null vira string vazia.</summary>
        public string TemplateId { get; }

        /// <summary>Versao opcional de template. Null vira string vazia.</summary>
        public string TemplateVersion { get; }

        internal bool TrustPolicyApplied { get; }
    }
}
