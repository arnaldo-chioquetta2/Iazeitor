using System.Threading.Tasks;

namespace GptBolDll
{
    public interface IAgentModelClient
    {
        string ProviderName { get; }
        string ModelName { get; }
        string DisplayName { get; }
        Task<string> AskAsync(string prompt);
    }

    public interface IAgentModelClientWithTimeout : IAgentModelClient
    {
        Task<string> AskAsync(string prompt, int timeoutMs);
    }
}
