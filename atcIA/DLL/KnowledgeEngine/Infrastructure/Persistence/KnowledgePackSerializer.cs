using Newtonsoft.Json;
using Iazeitor.KnowledgeEngine.Contracts.Persistence;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Infrastructure.Persistence
{
    internal sealed class KnowledgePackSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include
        };

        public string SerializePersistenceContract(KnowledgePackPersistenceContract contract)
        {
            return JsonConvert.SerializeObject(contract, Formatting.None, Settings);
        }

        public KnowledgePackPersistenceContract DeserializePersistenceContract(string content)
        {
            return JsonConvert.DeserializeObject<KnowledgePackPersistenceContract>(content, Settings);
        }

        public string SerializeVersionContract(KnowledgePackVersionContract contract)
        {
            return JsonConvert.SerializeObject(contract, Formatting.None, Settings);
        }

        public KnowledgePackVersionContract DeserializeVersionContract(string content)
        {
            return JsonConvert.DeserializeObject<KnowledgePackVersionContract>(content, Settings);
        }

        public string SerializeVersionHistoryContract(KnowledgePackVersionHistoryContract contract)
        {
            return JsonConvert.SerializeObject(contract, Formatting.None, Settings);
        }

        public KnowledgePackVersionHistoryContract DeserializeVersionHistoryContract(string content)
        {
            return JsonConvert.DeserializeObject<KnowledgePackVersionHistoryContract>(content, Settings);
        }

        public string SerializeJournalContract(KnowledgePackJournalContract contract)
        {
            return JsonConvert.SerializeObject(contract, Formatting.None, Settings);
        }

        public KnowledgePackJournalContract DeserializeJournalContract(string content)
        {
            return JsonConvert.DeserializeObject<KnowledgePackJournalContract>(content, Settings);
        }

        public string SerializeProjectKnowledgeBindingContract(ProjectKnowledgeBindingContract contract)
        {
            return JsonConvert.SerializeObject(contract, Formatting.None, Settings);
        }

        public ProjectKnowledgeBindingContract DeserializeProjectKnowledgeBindingContract(string content)
        {
            return JsonConvert.DeserializeObject<ProjectKnowledgeBindingContract>(content, Settings);
        }
    }
}
