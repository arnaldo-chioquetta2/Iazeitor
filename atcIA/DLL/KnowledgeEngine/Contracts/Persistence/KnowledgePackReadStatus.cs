namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    /// <summary>
    /// Resultado de uma leitura coordenada de Knowledge Pack.
    /// Read safety transactional: distingue ausencia limpa de estado parcial/inconsistente.
    /// </summary>
    public enum KnowledgePackReadStatus
    {
        /// <summary>O estado persistido esta completo e coerente; o snapshot pode ser reidratado.</summary>
        Ready,

        /// <summary>O Pack nao existe fisicamente e nao ha transacao pendente ou evidencia conflitante.</summary>
        NotFound,

        /// <summary>
        /// O formato persistido foi reconhecido como legado (manifest sem Content), mas e
        /// insuficiente para produzir um PackContract completo sem inventar dados.
        /// Nao e corrupcao e nao deve ser tratado como NotFound nem como Unresolved.
        /// </summary>
        LegacyUnsupported,

        /// <summary>
        /// Nao foi possivel garantir leitura segura: timeout de PackLock, journal invalido,
        /// multiplas transacoes, recovery Unrecoverable ou estado fisico divergente.
        /// Evidencias sao preservadas.
        /// </summary>
        Unresolved
    }
}
