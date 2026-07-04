namespace GptBolDll
{
    public interface IProjectProvider
    {
        string GetActiveProjectId();
        string GetActiveProjectRoot();
        string GetActiveProjectName(string projectRoot);
    }
}
