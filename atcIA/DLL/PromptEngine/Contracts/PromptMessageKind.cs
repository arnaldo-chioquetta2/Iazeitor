namespace Iazeitor.PromptEngine.Contracts
{
    /// <summary>
    /// Funcao semantica do bloco de prompt dentro do fluxo do Programador.
    ///
    /// Existe porque Role (System/User/Assistant) e TrustLevel
    /// (TrustedInstruction/UntrustedData) nao bastam para decidir prioridade de
    /// reducao sob orcamento: promptBase, prompt de linguagem, contexto de
    /// projeto e Knowledge sao todos User/UntrustedData, mas tem pesos
    /// operacionais diferentes.
    ///
    /// Kind e provider-neutral e nao deve ser usado por renderers de provider.
    /// Serve para politica de orcamento (PromptBudgetPolicy) e observabilidade.
    /// </summary>
    public enum PromptMessageKind
    {
        /// <summary>Bloco sem classificacao semantica. Contratos antigos (P02) caem aqui.</summary>
        Unspecified = 0,

        /// <summary>Instrucoes internas do Iazeitor (schema/protocolo) vindas de PromptLoader.</summary>
        InternalSystem = 1,

        /// <summary>Instrucao interna de perfil de linguagem (constante do produto).</summary>
        LanguageProfileInstruction = 2,

        /// <summary>Prompt especifico de linguagem: arquivo externo editavel em dados/prompts-linguagens.</summary>
        LanguageSpecificPrompt = 3,

        /// <summary>Prompt base do projeto (prompt.txt / prompt de complemento): arquivo externo editavel.</summary>
        PromptBase = 4,

        /// <summary>Contexto do projeto (codigo, README, arquivos, diretrizes).</summary>
        ProjectContext = 5,

        /// <summary>AdditionalContext do Knowledge Engine (conteudo derivado de dados).</summary>
        Knowledge = 6,

        /// <summary>Entrada atual do usuario (demanda da execucao corrente).</summary>
        UserInput = 7
    }
}
