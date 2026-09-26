using System.Collections.Generic;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    public sealed class KnowledgePackMetadataContract
    {
        public string Summary { get; set; }
        public IReadOnlyList<string> Tags { get; set; }
    }
}
