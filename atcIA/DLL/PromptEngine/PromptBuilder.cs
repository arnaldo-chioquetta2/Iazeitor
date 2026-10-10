using System;
using System.Collections.Generic;
using System.Text;
using Iazeitor.PromptEngine.Contracts;

namespace Iazeitor.PromptEngine
{
    /// <summary>
    /// Builder minimo do Prompt Engine.
    ///
    /// Responsabilidade unica: receber os blocos semanticos que ja existem no fluxo atual
    /// e produzir um PromptRequest tipado. Nao carrega arquivos, nao chama provider, nao
    /// conhece HTTP, nao conta tokens, nao trunca, nao aplica politica de trust e nao
    /// conhece KnowledgeEngine concretamente. Depende apenas de BCL e dos contratos do
    /// proprio Prompt Engine.
    ///
    /// Classificacao de confianca carregada neste momento (metadata apenas; a politica
    /// efetiva de seguranca fica para uma etapa posterior):
    ///
    ///   System + TrustedInstruction
    ///     - instrucoes internas do Iazeitor (protocolo/schema) vindas de PromptLoader;
    ///     - instrucao de perfil de linguagem, que tambem e constante interna do produto.
    ///
    ///   User + UntrustedData
    ///     - prompt especifico de linguagem (arquivo externo editavel em dados/prompts-linguagens);
    ///     - prompt base do projeto (prompt.txt / prompt de complemento: arquivo externo);
    ///     - contexto do projeto (codigo, README, arquivos);
    ///     - AdditionalContext do Knowledge (conteudo derivado de dados);
    ///     - entrada do usuario.
    ///
    /// O separador usado aqui e identico ao de RequestExecutionEngine.BuildFinalPrompt, para
    /// que a concatenacao deterministica dos conteudos reproduza o texto legado.
    /// </summary>
    public static class PromptBuilder
    {
        /// <summary>Mesmo separador usado historicamente na montagem do prompt final.</summary>
        public static readonly string Separator = Environment.NewLine + Environment.NewLine;

        /// <summary>
        /// Monta o PromptRequest do caminho principal do Programador preservando a ordem
        /// historica: systemPrompt, promptBase, contexto do projeto, AdditionalContext, entrada.
        /// Blocos nulos ou somente com espacos sao omitidos, como no BuildFinalPrompt legado.
        /// </summary>
        public static PromptRequest BuildProgrammerRequest(
            string provider,
            string model,
            string internalSystemInstructions,
            string languageProfileInstruction,
            string languageSpecificPrompt,
            string promptBase,
            string projectContext,
            string additionalContext,
            string userInput)
        {
            var messages = new List<PromptMessage>(7);

            AddMessage(messages, PromptRole.System, PromptTrustLevel.TrustedInstruction, PromptMessageKind.InternalSystem, internalSystemInstructions);
            AddMessage(messages, PromptRole.System, PromptTrustLevel.TrustedInstruction, PromptMessageKind.LanguageProfileInstruction, languageProfileInstruction);
            AddMessage(messages, PromptRole.User, PromptTrustLevel.UntrustedData, PromptMessageKind.LanguageSpecificPrompt, languageSpecificPrompt);
            AddMessage(messages, PromptRole.User, PromptTrustLevel.UntrustedData, PromptMessageKind.PromptBase, promptBase);
            AddMessage(messages, PromptRole.User, PromptTrustLevel.UntrustedData, PromptMessageKind.ProjectContext, projectContext);
            AddMessage(messages, PromptRole.User, PromptTrustLevel.UntrustedData, PromptMessageKind.Knowledge, additionalContext);
            AddMessage(messages, PromptRole.User, PromptTrustLevel.UntrustedData, PromptMessageKind.UserInput, userInput);

            return new PromptRequest(messages, provider, model);
        }

        private static void AddMessage(
            List<PromptMessage> messages,
            PromptRole role,
            PromptTrustLevel trustLevel,
            PromptMessageKind kind,
            string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return;

            messages.Add(new PromptMessage(role, content, trustLevel, kind));
        }

        /// <summary>
        /// Concatenacao deterministica dos conteudos no mesmo formato do prompt legado.
        /// Serve APENAS para observabilidade/comparacao em harness. Nao e caminho de envio:
        /// os providers recebem as mensagens tipadas pelo caminho AskAsync(PromptRequest).
        /// </summary>
        public static string ToDeterministicText(PromptRequest request)
        {
            if (request == null || request.Messages == null || request.Messages.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            bool primeiro = true;
            for (int i = 0; i < request.Messages.Count; i++)
            {
                PromptMessage message = request.Messages[i];
                if (message == null || string.IsNullOrWhiteSpace(message.Content))
                    continue;

                if (!primeiro)
                    builder.Append(Separator);

                builder.Append(message.Content);
                primeiro = false;
            }

            return builder.ToString();
        }

        /// <summary>
        /// Contagem de caracteres equivalente a ToDeterministicText, sem alocar o texto.
        /// Usada para preservar os logs de tamanho sem registrar conteudo sensivel.
        /// </summary>
        public static int CountEquivalentChars(PromptRequest request)
        {
            if (request == null || request.Messages == null || request.Messages.Count == 0)
                return 0;

            int total = 0;
            int blocos = 0;
            for (int i = 0; i < request.Messages.Count; i++)
            {
                PromptMessage message = request.Messages[i];
                if (message == null || string.IsNullOrWhiteSpace(message.Content))
                    continue;

                total += message.Content.Length;
                blocos++;
            }

            if (blocos > 1)
                total += (blocos - 1) * Separator.Length;

            return total;
        }
    }
}
