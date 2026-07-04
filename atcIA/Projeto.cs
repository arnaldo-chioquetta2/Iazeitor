using GptBolDll;
using GptBolDll.Configuration;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace atcIA
{
    public class Projeto
    {
        public Guid Id { get; set; }
        public string Nome { get; set; }
        public string Caminho { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public LanguageProfile LanguageProfile { get; set; } = LanguageProfile.Geral;

        public string LanguageProfileId { get; set; } = "geral";

        public string KnowledgeIndexPath { get; set; } = string.Empty;

        public string AiProviderId { get; set; } = AiProviderConfig.DefaultProviderId;
        public FtpProfile Ftp { get; set; }
        public DatabaseProfile Database { get; set; }
        public List<ProjectCredential> Credenciais { get; set; }

        public bool IncrementarVersaoAoConcluir { get; set; }
        public bool AutoVerificationEnabled { get; set; }

        public Projeto()
        {
            Id = Guid.NewGuid();
            Nome = "";
            Caminho = "";
            LanguageProfile = LanguageProfile.Geral;
            LanguageProfileId = "geral";
            KnowledgeIndexPath = string.Empty;
            AiProviderId = AiProviderConfig.DefaultProviderId;
            Ftp = new FtpProfile();
            Database = new DatabaseProfile();
            Credenciais = new List<ProjectCredential>();
            AutoVerificationEnabled = false;
        }

        public bool EhValido(out string mensagemErro)
        {
            if (string.IsNullOrWhiteSpace(Nome))
            {
                mensagemErro = "Informe o nome do projeto.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Caminho))
            {
                mensagemErro = "Informe o caminho do projeto.";
                return false;
            }

            mensagemErro = "";
            return true;
        }

        public override string ToString()
        {
            return Nome;
        }
    }
}
