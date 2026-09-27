using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Iazeitor.PromptEngine.Contracts
{
    /// <summary>
    /// Resultado da politica de orcamento.
    ///
    /// Request nunca e null: em CannotFit ele e o request original, preservado
    /// para que o chamador possa diagnosticar/decidir o fallback explicitamente.
    /// FailureReason nunca contem conteudo de prompt nem credenciais: apenas
    /// motivo categorizado e contagens.
    /// </summary>
    public sealed class PromptBudgetResult
    {
        public PromptBudgetResult(
            PromptRequest request,
            PromptBudgetStatus status,
            int originalCharacters,
            int finalCharacters,
            IReadOnlyList<PromptMessageKind> adjustedKinds,
            IReadOnlyList<PromptMessageKind> omittedKinds,
            string failureReason)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var kinds = new List<PromptMessageKind>();
            if (adjustedKinds != null)
            {
                for (int i = 0; i < adjustedKinds.Count; i++)
                {
                    if (!kinds.Contains(adjustedKinds[i]))
                        kinds.Add(adjustedKinds[i]);
                }
            }

            Request = request;
            Status = status;
            OriginalCharacters = originalCharacters;
            FinalCharacters = finalCharacters;
            AdjustedKinds = new ReadOnlyCollection<PromptMessageKind>(kinds);
            OmittedKinds = new ReadOnlyCollection<PromptMessageKind>(BuildDistinct(omittedKinds));
            FailureReason = failureReason ?? string.Empty;
        }

        public PromptRequest Request { get; }

        public PromptBudgetStatus Status { get; }

        public bool WasReduced
        {
            get { return Status == PromptBudgetStatus.Reduced; }
        }

        public int OriginalCharacters { get; }

        public int FinalCharacters { get; }

        /// <summary>Kinds de blocos efetivamente reduzidos ou omitidos (sem repeticao).</summary>
        public IReadOnlyList<PromptMessageKind> AdjustedKinds { get; }

        /// <summary>Kinds de blocos removidos por completo (subconjunto de AdjustedKinds).</summary>
        public IReadOnlyList<PromptMessageKind> OmittedKinds { get; }

        /// <summary>Motivo categorizado quando Status == CannotFit. Nunca contem conteudo.</summary>
        public string FailureReason { get; }

        private static List<PromptMessageKind> BuildDistinct(IReadOnlyList<PromptMessageKind> kinds)
        {
            var distinct = new List<PromptMessageKind>();
            if (kinds == null)
                return distinct;

            for (int i = 0; i < kinds.Count; i++)
            {
                if (!distinct.Contains(kinds[i]))
                    distinct.Add(kinds[i]);
            }

            return distinct;
        }
    }
}
