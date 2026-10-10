using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Iazeitor.PromptEngine.Contracts;

namespace Iazeitor.PromptEngine
{
    /// <summary>
    /// Politica central e deterministica de reducao de prompt para o caminho tipado
    /// do Programador.
    ///
    /// Regras:
    ///   - System/TrustedInstruction NUNCA e cortado silenciosamente;
    ///   - entrada atual do usuario (Kind=UserInput) NUNCA e cortada;
    ///   - se apenas os blocos nao redutiveis excederem o orcamento, o resultado e
    ///     CannotFit (o chamador decide o fallback explicitamente);
    ///   - reducao ocorre nesta ordem de candidatos: Knowledge, ProjectContext,
    ///     prompt especifico de linguagem, prompt base e, por ultimo, blocos sem
    ///     Kind declarado;
    ///   - a contagem usa PromptBuilder.CountEquivalentChars, a mesma concatenacao
    ///     deterministica do texto legado (caracteres, nao tokens).
    ///
    /// A politica NAO altera provider, parser, protocolo JSON, Knowledge Engine nem
    /// ProjectContextBuilder. Ela apenas substitui o conteudo de mensagens
    /// redutiveis preservando Role, TrustLevel e Kind.
    /// </summary>
    public static class PromptBudgetPolicy
    {
        private const int MinimumKnowledgeChars = 160;
        private const int MinimumProjectContextChars = 400;
        private const int MinimumLanguagePromptChars = 200;
        private const int MinimumPromptBaseChars = 200;
        private const int MinimumGenericChars = 120;
        private const int MinimumProjectBlockChars = 200;

        private static readonly string MiddleMarker =
            Environment.NewLine + "<... conteudo reduzido pelo PromptBudget ...>" + Environment.NewLine;

        private const string ProjectContextNote = "[CONTEXTO REDUZIDO PELO PROMPTBUDGET]";

        /// <summary>Ordem de reducao dos blocos redutiveis (do primeiro ao ultimo candidato).</summary>
        private static readonly PromptMessageKind[] ReductionOrder = new[]
        {
            PromptMessageKind.Knowledge,
            PromptMessageKind.ProjectContext,
            PromptMessageKind.LanguageSpecificPrompt,
            PromptMessageKind.PromptBase
        };

        private static readonly Regex ProjectBlockRegex = new Regex(
            "--- (?<kind>FILE|CSPROJ): (?<label>[^\\r\\n]*) ---\\r?\\n(?<body>[\\s\\S]*?)\\r?\\n--- END \\k<kind>: (?<label2>[^\\r\\n]*) ---[ \\t]*(?:\\r?\\n|$)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static PromptBudgetResult Apply(PromptRequest request, PromptBudget budget)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (budget == null)
                throw new ArgumentNullException(nameof(budget));

            int originalCharacters = PromptBuilder.CountEquivalentChars(request);
            if (originalCharacters <= budget.MaxCharacters)
            {
                return new PromptBudgetResult(
                    request,
                    PromptBudgetStatus.WithinBudget,
                    originalCharacters,
                    originalCharacters,
                    null,
                    null,
                    string.Empty);
            }

            var working = new List<PromptMessage>(request.Messages.Count);
            for (int i = 0; i < request.Messages.Count; i++)
                working.Add(request.Messages[i]);

            var adjusted = new List<PromptMessageKind>();
            var omitted = new List<PromptMessageKind>();
            int current = originalCharacters;

            for (int k = 0; k < ReductionOrder.Length && current > budget.MaxCharacters; k++)
                ReduceKind(working, ReductionOrder[k], budget.MaxCharacters, adjusted, omitted, ref current);

            if (current > budget.MaxCharacters)
                ReduceKind(working, PromptMessageKind.Unspecified, budget.MaxCharacters, adjusted, omitted, ref current);

            if (current > budget.MaxCharacters)
            {
                string reason =
                    "Nao foi possivel caber no orcamento sem cortar conteudo nao redutivel. " +
                    "BlocosNaoRedutiveis=" + CountNonReducible(working) +
                    " CaracteresNaoRedutiveis=" + CountNonReducibleCharacters(working) +
                    " Orcamento=" + budget.MaxCharacters +
                    " CaracteresOriginais=" + originalCharacters + ".";

                return new PromptBudgetResult(
                    request,
                    PromptBudgetStatus.CannotFit,
                    originalCharacters,
                    originalCharacters,
                    adjusted,
                    omitted,
                    reason);
            }

            if (working.Count == 0)
            {
                return new PromptBudgetResult(
                    request,
                    PromptBudgetStatus.CannotFit,
                    originalCharacters,
                    originalCharacters,
                    adjusted,
                    omitted,
                    "Nenhum bloco restou apos a reducao; request vazio nao e enviavel.");
            }

            var reducedRequest = new PromptRequest(
                working,
                request.Provider,
                request.Model,
                request.TemplateId,
                request.TemplateVersion,
                request.TrustPolicyApplied);

            return new PromptBudgetResult(
                reducedRequest,
                PromptBudgetStatus.Reduced,
                originalCharacters,
                current,
                adjusted,
                omitted,
                string.Empty);
        }

        private static void ReduceKind(
            List<PromptMessage> working,
            PromptMessageKind kind,
            int maxCharacters,
            List<PromptMessageKind> adjusted,
            List<PromptMessageKind> omitted,
            ref int current)
        {
            for (int i = 0; i < working.Count && current > maxCharacters; i++)
            {
                PromptMessage message = working[i];
                if (message == null || message.Kind != kind || !IsReducible(message))
                    continue;

                while (current > maxCharacters && i < working.Count)
                {
                    message = working[i];
                    string semanticContent = GetReducibleContent(message);
                    int needed = current - maxCharacters;
                    int newLength = semanticContent.Length - needed;
                    int minimum = MinimumKeepChars(kind);

                    if (newLength < minimum)
                    {
                        working.RemoveAt(i);
                        i--;
                        AddDistinct(adjusted, kind);
                        AddDistinct(omitted, kind);
                        current = CountMessages(working);
                        break;
                    }

                    string reducedContent = ReduceContent(kind, semanticContent, newLength);
                    working[i] = message.TrustPolicyApplied
                        ? PromptPolicy.RebuildReduced(message, reducedContent, i)
                        : new PromptMessage(message.Role, reducedContent, message.TrustLevel, message.Kind);
                    AddDistinct(adjusted, kind);
                    int previous = current;
                    current = CountMessages(working);
                    if (current >= previous)
                        break;
                }
            }
        }

        private static bool IsReducible(PromptMessage message)
        {
            if (message.TrustLevel == PromptTrustLevel.TrustedInstruction)
                return false;
            if (message.Kind == PromptMessageKind.UserInput)
                return false;
            if (string.IsNullOrWhiteSpace(GetReducibleContent(message)))
                return false;

            return true;
        }

        private static string GetReducibleContent(PromptMessage message)
        {
            return message.TrustPolicyApplied ? message.UntrustedOriginalContent : message.Content;
        }

        private static int MinimumKeepChars(PromptMessageKind kind)
        {
            switch (kind)
            {
                case PromptMessageKind.Knowledge:
                    return MinimumKnowledgeChars;
                case PromptMessageKind.ProjectContext:
                    return MinimumProjectContextChars;
                case PromptMessageKind.LanguageSpecificPrompt:
                    return MinimumLanguagePromptChars;
                case PromptMessageKind.PromptBase:
                    return MinimumPromptBaseChars;
                default:
                    return MinimumGenericChars;
            }
        }

        private static string ReduceContent(PromptMessageKind kind, string content, int targetCharacters)
        {
            if (string.IsNullOrEmpty(content) || targetCharacters <= 0)
                return string.Empty;

            if (kind == PromptMessageKind.ProjectContext)
                return ReduceProjectContext(content, targetCharacters);

            return ReduceByMiddle(content, targetCharacters);
        }

        /// <summary>Reducao generica preservando inicio e fim uteis, com marcador explicito.</summary>
        internal static string ReduceByMiddle(string content, int targetCharacters)
        {
            if (string.IsNullOrEmpty(content) || targetCharacters <= 0)
                return string.Empty;
            if (content.Length <= targetCharacters)
                return content;

            if (targetCharacters <= MiddleMarker.Length + 2)
                return SafePrefix(content, targetCharacters);

            int keep = targetCharacters - MiddleMarker.Length;
            int headLength = keep / 2;
            int tailLength = keep - headLength;

            string head = SafePrefix(content, headLength);
            string tail = SafeSuffix(content, tailLength);

            int overflow = head.Length + MiddleMarker.Length + tail.Length - targetCharacters;
            if (overflow > 0)
                tail = SafePrefix(tail, Math.Max(0, tail.Length - overflow));

            return head + MiddleMarker + tail;
        }

        /// <summary>
        /// Reducao de contexto de projeto preservando cabecalho e marcadores
        /// --- FILE/CSPROJ: x --- ... --- END FILE/CSPROJ: x --- dos blocos mantidos.
        /// Blocos sao preservados na ordem original (o ProjectContextBuilder ja coloca
        /// arquivos prioritarios primeiro).
        /// </summary>
        internal static string ReduceProjectContext(string content, int targetCharacters)
        {
            if (string.IsNullOrWhiteSpace(content) || targetCharacters <= 0)
                return string.Empty;
            if (content.Length <= targetCharacters)
                return content;

            MatchCollection matches = ProjectBlockRegex.Matches(content);
            if (matches.Count == 0)
                return ReduceByMiddle(content, targetCharacters);

            string header = content.Substring(0, matches[0].Index);
            var builder = new StringBuilder(targetCharacters);

            int reserveForBlocks = Math.Max(0, targetCharacters - ProjectContextNote.Length - 4);
            int headerBudget = Math.Min(header.Length, Math.Max(300, reserveForBlocks / 3));
            builder.Append(ReduceByMiddle(header, headerBudget).TrimEnd());
            builder.AppendLine();
            builder.AppendLine(ProjectContextNote);

            for (int i = 0; i < matches.Count; i++)
            {
                int remaining = targetCharacters - builder.Length - 2;
                if (remaining < 120)
                    break;

                string block = matches[i].Value.TrimEnd('\r', '\n');
                builder.AppendLine();

                if (block.Length <= remaining)
                {
                    builder.AppendLine(block);
                    continue;
                }

                if (remaining < MinimumProjectBlockChars)
                    break;

                builder.AppendLine(ReduceProjectBlock(block, remaining));
            }

            string result = builder.ToString();
            if (result.Length > targetCharacters)
                result = SafePrefix(result, targetCharacters);

            return result;
        }

        private static string ReduceProjectBlock(string block, int maxCharacters)
        {
            if (string.IsNullOrEmpty(block) || maxCharacters <= 0)
                return string.Empty;
            if (block.Length <= maxCharacters)
                return block;

            int firstNewline = block.IndexOf('\n');
            if (firstNewline < 0)
                return ReduceByMiddle(block, maxCharacters);

            string startMarker = block.Substring(0, firstNewline).TrimEnd('\r');
            string rest = block.Substring(firstNewline + 1);

            int endMarkerIndex = rest.LastIndexOf("--- END ", StringComparison.OrdinalIgnoreCase);
            if (endMarkerIndex < 0)
                return ReduceByMiddle(block, maxCharacters);

            string body = rest.Substring(0, endMarkerIndex).TrimEnd('\r', '\n');
            string endMarker = rest.Substring(endMarkerIndex).TrimEnd('\r', '\n');

            int fixedLength = startMarker.Length + endMarker.Length + Environment.NewLine.Length * 2;
            int bodyBudget = maxCharacters - fixedLength;
            if (bodyBudget <= 0)
                return startMarker.Length <= maxCharacters ? startMarker : SafePrefix(startMarker, maxCharacters);

            string reducedBody = ReduceByMiddle(body, bodyBudget);
            string result = startMarker + Environment.NewLine + reducedBody + Environment.NewLine + endMarker;

            return result.Length <= maxCharacters ? result : SafePrefix(result, maxCharacters);
        }

        private static int CountMessages(List<PromptMessage> messages)
        {
            int total = 0;
            int blocks = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                PromptMessage message = messages[i];
                if (message == null || string.IsNullOrWhiteSpace(message.Content))
                    continue;

                total += message.Content.Length;
                blocks++;
            }

            if (blocks > 1)
                total += (blocks - 1) * PromptBuilder.Separator.Length;

            return total;
        }

        private static int CountNonReducible(List<PromptMessage> messages)
        {
            int count = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                PromptMessage message = messages[i];
                if (message == null || string.IsNullOrWhiteSpace(message.Content))
                    continue;
                if (!IsReducible(message))
                    count++;
            }

            return count;
        }

        private static int CountNonReducibleCharacters(List<PromptMessage> messages)
        {
            int total = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                PromptMessage message = messages[i];
                if (message == null || string.IsNullOrWhiteSpace(message.Content))
                    continue;
                if (!IsReducible(message))
                    total += message.Content.Length;
            }

            return total;
        }

        private static void AddDistinct(List<PromptMessageKind> kinds, PromptMessageKind kind)
        {
            if (!kinds.Contains(kind))
                kinds.Add(kind);
        }

        private static string SafePrefix(string text, int length)
        {
            if (string.IsNullOrEmpty(text) || length <= 0)
                return string.Empty;
            if (length >= text.Length)
                return text;

            int end = length;
            if (char.IsHighSurrogate(text[end - 1]) && end < text.Length && char.IsLowSurrogate(text[end]))
                end--;

            return text.Substring(0, end);
        }

        private static string SafeSuffix(string text, int length)
        {
            if (string.IsNullOrEmpty(text) || length <= 0)
                return string.Empty;
            if (length >= text.Length)
                return text;

            int start = text.Length - length;
            if (start > 0 && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1]))
                start--;

            return text.Substring(start);
        }
    }
}
