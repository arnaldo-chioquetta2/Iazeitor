using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace atcIA
{
    public static class ProjetoRepository
    {
        static ProjetoRepository()
        {
            AppPaths.GarantirPastaDados();
            AppPaths.MigrarArquivoLegadoSeNecessario("projetos.json");
        }

        public static List<Projeto> Listar()
        {
            if (!File.Exists(AppPaths.ProjetosFile))
                return new List<Projeto>();

            try
            {
                var json = File.ReadAllText(AppPaths.ProjetosFile);
                var projetos = JsonConvert.DeserializeObject<List<Projeto>>(json) ?? new List<Projeto>();
                foreach (var projeto in projetos)
                    NormalizarProjeto(projeto);

                return projetos;
            }
            catch
            {
                return new List<Projeto>();
            }
        }

        public static Projeto BuscarPorId(Guid id)
        {
            return Listar().FirstOrDefault(p => p.Id == id);
        }

        public static Projeto Salvar(Projeto projeto)
        {
            if (projeto == null)
                throw new ArgumentNullException(nameof(projeto));

            string erro;
            if (!projeto.EhValido(out erro))
                throw new Exception(erro);

            if (projeto.Id == Guid.Empty)
                projeto.Id = Guid.NewGuid();

            NormalizarEValidarProjeto(projeto);

            var projetos = Listar();
            var existente = projetos.FirstOrDefault(p => p.Id == projeto.Id);

            if (existente == null)
            {
                projetos.Add(projeto);
            }
            else
            {
                CopiarDadosProjeto(projeto, existente);
            }

            SalvarLista(projetos);
            return projeto;
        }

        public static Projeto Atualizar(Projeto projeto)
        {
            if (projeto == null)
                throw new ArgumentNullException(nameof(projeto));

            if (projeto.Id == Guid.Empty)
                throw new Exception("Projeto sem identificador nao pode ser atualizado.");

            string erro;
            if (!projeto.EhValido(out erro))
                throw new Exception(erro);

            NormalizarEValidarProjeto(projeto);

            var projetos = Listar();
            var existente = projetos.FirstOrDefault(p => p.Id == projeto.Id);

            if (existente == null)
                throw new Exception("Projeto nao encontrado para atualizacao.");

            CopiarDadosProjeto(projeto, existente);
            SalvarLista(projetos);
            return existente;
        }

        private static void CopiarDadosProjeto(Projeto origem, Projeto destino)
        {
            destino.Nome = origem.Nome;
            destino.Caminho = origem.Caminho;
            destino.LanguageProfile = origem.LanguageProfile;
            destino.LanguageProfileId = origem.LanguageProfileId;
            destino.KnowledgeIndexPath = origem.KnowledgeIndexPath;
            destino.AiProviderId = origem.AiProviderId;
            destino.IncrementarVersaoAoConcluir = origem.IncrementarVersaoAoConcluir;
            destino.AutoVerificationEnabled = origem.AutoVerificationEnabled;
            destino.Ftp = origem.Ftp;
            destino.Database = origem.Database;
            destino.Credenciais = origem.Credenciais != null
                ? origem.Credenciais.ToList()
                : new List<GptBolDll.ProjectCredential>();
        }

        private static void NormalizarProjeto(Projeto projeto)
        {
            if (projeto == null)
                return;

            if (string.IsNullOrWhiteSpace(projeto.AiProviderId))
                projeto.AiProviderId = GptBolDll.AiProviderConfig.DefaultProviderId;

            projeto.LanguageProfileId = GptBolDll.LanguageProfileConfigRepository.ResolveId(
                projeto.LanguageProfileId,
                projeto.LanguageProfile);
            projeto.LanguageProfile = GptBolDll.LanguageProfileConfigRepository.LegacyProfileFromId(projeto.LanguageProfileId);
            projeto.KnowledgeIndexPath = projeto.KnowledgeIndexPath ?? string.Empty;
            projeto.AutoVerificationEnabled = projeto.AutoVerificationEnabled;

            if (projeto.Credenciais == null)
                projeto.Credenciais = new List<GptBolDll.ProjectCredential>();

            if (projeto.Ftp == null)
                projeto.Ftp = new GptBolDll.FtpProfile();

            if (projeto.Database == null)
                projeto.Database = new GptBolDll.DatabaseProfile();
        }

        private static void NormalizarEValidarProjeto(Projeto projeto)
        {
            NormalizarProjeto(projeto);

            var provider = AiProviderRepository.BuscarAtivoOuPadrao(projeto.AiProviderId);
            if (provider == null || !provider.Ativo)
                throw new Exception("IA do projeto nao esta ativa ou nao existe.");

            provider.Normalize();
        }

        private static void SalvarLista(List<Projeto> projetos)
        {
            AppPaths.GarantirPastaDados();
            var json = JsonConvert.SerializeObject(projetos, Formatting.Indented);
            File.WriteAllText(AppPaths.ProjetosFile, json);
        }
    }
}
