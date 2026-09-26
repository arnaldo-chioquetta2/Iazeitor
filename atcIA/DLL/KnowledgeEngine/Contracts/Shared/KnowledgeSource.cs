namespace Iazeitor.KnowledgeEngine.Contracts.Shared
{
    public sealed class KnowledgeSource
    {
        public string Kind { get; set; }

        public string Identifier { get; set; }

        public string Location { get; set; }

        public string Description { get; set; }
    }
}