using System;

namespace Iazeitor.PromptEngine.Contracts
{
    /// <summary>
    /// Orcamento de prompt do Prompt Engine.
    ///
    /// Hoje a unica fonte de verdade e o limite configurado (MaxPromptChars do
    /// RequestExecutionEngine/AgentCore, ja normalizado pelo ObterLimitePrompt).
    /// Nao existe tabela de context window por provider/model, portanto P05 nao
    /// inventa esses numeros.
    /// </summary>
    public sealed class PromptBudget
    {
        public PromptBudget(int maxCharacters)
        {
            if (maxCharacters < 0)
                throw new ArgumentOutOfRangeException(nameof(maxCharacters), "MaxCharacters nao pode ser negativo.");

            MaxCharacters = maxCharacters;
        }

        /// <summary>Limite total em caracteres para todo o conteudo semantic do request.</summary>
        public int MaxCharacters { get; }

        /// <summary>Unidade do limite. Sempre Characters nesta etapa.</summary>
        public PromptBudgetUnit Unit
        {
            get { return PromptBudgetUnit.Characters; }
        }
    }
}
