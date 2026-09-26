namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    /// <summary>
    /// Resultado de uma validacao profunda (DEEP) de integridade semantica de versoes.
    /// Nao e um veredito de leitura: leitura normal usa apenas a validacao FAST.
    /// </summary>
    public enum KnowledgePackIntegrityStatus
    {
        /// <summary>Estado fisico e historico coerentes com os invariantes de versao.</summary>
        Consistent,

        /// <summary>Existe ao menos uma violacao de invariante; evidencias sao preservadas.</summary>
        Inconsistent,

        /// <summary>
        /// O Pack esta no formato legado reconhecido (sem Content). Nao e um estado moderno
        /// "Consistent" e tambem nao e uma divergencia: a validacao DEEP moderna nao se aplica.
        /// </summary>
        LegacyUnsupported
    }
}
