using System.Threading.Tasks;
using Iazeitor.PromptEngine.Contracts;

namespace GptBolDll
{
    public interface IAgentModelClient
    {
        string ProviderName { get; }
        string ModelName { get; }
        string DisplayName { get; }
        Task<string> AskAsync(string prompt);

        /// <summary>
        /// Caminho tipado (Prompt Engine). Preserva papel/conteudo ate o payload do provider.
        /// O caminho legado AskAsync(string) permanece intacto e com o mesmo comportamento.
        /// </summary>
        Task<string> AskAsync(PromptRequest request);
    }

    public interface IAgentModelClientWithTimeout : IAgentModelClient
    {
        Task<string> AskAsync(string prompt, int timeoutMs);
    }
}
