namespace Iazeitor.PromptEngine.Contracts
{
    /// <summary>
    /// Nivel de confianca do conteudo de uma mensagem de prompt.
    /// Neutro em relacao ao provider.
    ///
    /// TrustedInstruction: regras internas, politica e protocolo controlados pelo Iazeitor.
    /// UntrustedData: entrada do usuario, codigo, documentacao, contexto de projeto,
    /// knowledge persistido e resposta anterior de IA reaproveitada como dado.
    /// </summary>
    public enum PromptTrustLevel
    {
        TrustedInstruction = 0,
        UntrustedData = 1
    }
}
