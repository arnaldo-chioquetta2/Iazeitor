namespace Iazeitor.PromptEngine.Contracts
{
    /// <summary>
    /// Resultado explicito da aplicacao do PromptBudget. O chamador nunca deve
    /// inferir falha por request null ou conteudo vazio.
    /// </summary>
    public enum PromptBudgetStatus
    {
        /// <summary>Request ja cabia no orcamento; nada foi alterado.</summary>
        WithinBudget = 0,

        /// <summary>Request foi reduzido/omitiu blocos e agora cabe no orcamento.</summary>
        Reduced = 1,

        /// <summary>Nao foi possivel caber no orcamento sem cortar System/UserInput.</summary>
        CannotFit = 2
    }
}
