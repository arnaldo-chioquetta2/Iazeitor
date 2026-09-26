namespace Iazeitor.KnowledgeEngine.Contracts.Shared
{
    public sealed class KnowledgeDemand
    {
        public string RequestId { get; set; }

        public string Objective { get; set; }

        public string Domain { get; set; }

        public string Scope { get; set; }

        public string Context { get; set; }

        public string SessionId { get; set; }
    }
}