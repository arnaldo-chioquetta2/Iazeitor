namespace Iazeitor.KnowledgeEngine.Contracts.Shared
{
    public sealed class KnowledgeError
    {
        public string Code { get; set; }

        public string Message { get; set; }

        public string Details { get; set; }

        public KnowledgeStatus Status { get; set; }

        public KnowledgeSource Source { get; set; }
    }
}