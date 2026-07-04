namespace GptBolDll
{
    public interface IToolDispatcher
    {
        ToolDispatchResult Dispatch(AgentResponse response, string projectRoot);
        void SetLogger(ExecutionLogger logger);
    }
}
