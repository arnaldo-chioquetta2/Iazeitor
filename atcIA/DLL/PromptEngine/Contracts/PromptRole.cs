namespace Iazeitor.PromptEngine.Contracts
{
    /// <summary>
    /// Papel semantico de uma mensagem de prompt.
    /// Neutro em relacao ao provider: nao descreve formato de payload HTTP.
    /// </summary>
    public enum PromptRole
    {
        System = 0,
        User = 1,
        Assistant = 2
    }
}
