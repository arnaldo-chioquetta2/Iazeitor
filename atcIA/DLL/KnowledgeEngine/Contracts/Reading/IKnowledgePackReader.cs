using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Reading
{
    public interface IKnowledgePackReader
    {
        PackContract Read(KnowledgeIdentifier packId);

        IReadOnlyList<PackContract> Read(IReadOnlyList<KnowledgeIdentifier> packIds);

        KnowledgePackReadResult ReadWithStatus(IReadOnlyList<KnowledgeIdentifier> packIds);
    }
}
