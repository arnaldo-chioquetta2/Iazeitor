namespace Iazeitor.PromptEngine.Contracts
{
    /// <summary>
    /// Unidade suportada pelo PromptBudget.
    ///
    /// O sistema atual NAO possui tokenizador real de provider. A unica unidade
    /// confiavel e o caractere (mesma contagem deterministica usada pelo
    /// PromptBuilder.CountEquivalentChars). Este enum existe para que a unidade
    /// seja explicita no contrato e para evitar chamar heuristica de caracteres
    /// de "tokens".
    /// </summary>
    public enum PromptBudgetUnit
    {
        Characters = 0
    }
}
