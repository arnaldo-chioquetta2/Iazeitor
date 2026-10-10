using System;
using System.Collections.Generic;
using Iazeitor.PromptEngine.Contracts;
using Newtonsoft.Json.Linq;

namespace Iazeitor.PromptEngine.Providers
{
    /// <summary>
    /// Mapeamento compartilhado entre PromptRequest/PromptMessage e os formatos de payload
    /// suportados pelos providers.
    ///
    /// Fica na borda de provider (adapter) justamente para que o PromptEngine.Contracts
    /// permaneca puro, sem dependencia de Newtonsoft.
    ///
    /// Este helper NAO acha o prompt: ele apenas preserva papel (Role) e conteudo (Content)
    /// ate a construcao do payload especifico de cada API. PromptTrustLevel e ignorado aqui
    /// nesta etapa (metadata para a etapa de trust boundaries).
    /// </summary>
    public static class PromptProviderMapping
    {
        public const string NoMessagesMessage = "PromptRequest sem mensagens: nada a renderizar.";

        public static void EnsureHasMessages(PromptRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (request.Messages == null || request.Messages.Count == 0)
                throw new InvalidOperationException(NoMessagesMessage);
        }

        /// <summary>PromptRole -> role de APIs compativeis com OpenAI (Responses e chat/completions).</summary>
        public static string ToOpenAiRole(PromptRole role)
        {
            switch (role)
            {
                case PromptRole.System:
                    return "system";
                case PromptRole.User:
                    return "user";
                case PromptRole.Assistant:
                    return "assistant";
                default:
                    throw new InvalidOperationException("PromptRole invalido para payload OpenAI-compatible: " + (int)role + ".");
            }
        }

        /// <summary>
        /// Array [{role, content}] usado tanto por 'messages' (chat/completions) quanto por
        /// 'input' (Responses API), que aceitam os mesmos nomes de papel.
        /// </summary>
        public static JArray BuildOpenAiMessages(PromptRequest request)
        {
            EnsureHasMessages(request);

            var messages = new JArray();
            for (int i = 0; i < request.Messages.Count; i++)
            {
                PromptMessage message = request.Messages[i];
                if (message == null)
                    throw new ArgumentException("PromptRequest.Messages contem item null.", nameof(request));

                messages.Add(new JObject
                {
                    ["role"] = ToOpenAiRole(message.Role),
                    ["content"] = message.Content ?? string.Empty
                });
            }

            return messages;
        }

        /// <summary>
        /// Junta as mensagens System em um unico texto, de forma deterministica, para APIs que
        /// exigem system instruction unica (Gemini systemInstruction). Retorna null quando nao
        /// existe mensagem System.
        /// </summary>
        public static string JoinSystemMessages(PromptRequest request)
        {
            if (request == null || request.Messages == null)
                return null;

            var parts = new List<string>();
            for (int i = 0; i < request.Messages.Count; i++)
            {
                PromptMessage message = request.Messages[i];
                if (message != null && message.Role == PromptRole.System)
                    parts.Add(message.Content ?? string.Empty);
            }

            if (parts.Count == 0)
                return null;

            return string.Join("\n\n", parts.ToArray());
        }

        /// <summary>PromptRole -> role de content do Gemini. System nao entra em contents.</summary>
        public static string ToGeminiRole(PromptRole role)
        {
            switch (role)
            {
                case PromptRole.User:
                    return "user";
                case PromptRole.Assistant:
                    return "model";
                case PromptRole.System:
                    throw new InvalidOperationException("PromptRole.System deve ser enviado como systemInstruction no Gemini, nao como content.");
                default:
                    throw new InvalidOperationException("PromptRole invalido para payload Gemini: " + (int)role + ".");
            }
        }

        /// <summary>
        /// Model vazio/ausente mantem o modelo configurado no client.
        /// Model informado sobrescreve o configurado (a politica atual do projeto nao possui
        /// allow-list de modelos; a decisao fica explicita no chamador).
        /// </summary>
        public static string ResolveModel(PromptRequest request, string configuredModel)
        {
            string informado = request == null ? null : request.Model;
            if (string.IsNullOrWhiteSpace(informado))
                return configuredModel;

            return informado.Trim();
        }

        /// <summary>
        /// Falha fechado quando request.Provider aponta explicitamente para outro provider.
        /// Provider vazio e aceito (sem validacao possivel). Nao faz descoberta de rede.
        /// </summary>
        public static void EnsureProviderCompatible(PromptRequest request, string providerKey, params string[] acceptedNames)
        {
            if (request == null)
                return;

            string informado = (request.Provider ?? string.Empty).Trim();
            if (informado.Length == 0)
                return;

            if (Matches(informado, providerKey))
                return;

            if (acceptedNames != null)
            {
                for (int i = 0; i < acceptedNames.Length; i++)
                {
                    if (Matches(informado, acceptedNames[i]))
                        return;
                }
            }

            throw new InvalidOperationException(
                "PromptRequest.Provider '" + informado + "' nao corresponde ao provider deste client (" + providerKey + "). Envio recusado.");
        }

        private static bool Matches(string informado, string aceito)
        {
            if (string.IsNullOrWhiteSpace(aceito))
                return false;

            return string.Equals(informado, aceito.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
