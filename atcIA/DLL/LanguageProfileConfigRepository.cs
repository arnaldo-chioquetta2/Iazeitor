using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GptBolDll
{
    public static class LanguageProfileConfigRepository
    {
        private const string FileName = "language-profiles.json";
        private const string PromptDirName = "prompts-linguagens";

        public static string DadosDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dados"); }
        }

        public static string FilePath
        {
            get { return Path.Combine(DadosDir, FileName); }
        }

        public static string PromptDir
        {
            get { return Path.Combine(DadosDir, PromptDirName); }
        }

        public static List<LanguageProfileConfig> Listar()
        {
            GarantirInicializado();

            try
            {
                var json = File.ReadAllText(FilePath);
                var perfis = JsonConvert.DeserializeObject<List<LanguageProfileConfig>>(json) ?? new List<LanguageProfileConfig>();
                return NormalizarLista(perfis);
            }
            catch
            {
                return CriarPadroes();
            }
        }

        public static LanguageProfileConfig BuscarPorId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                id = "geral";

            return Listar().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static void Salvar(List<LanguageProfileConfig> perfis)
        {
            if (perfis == null)
                perfis = new List<LanguageProfileConfig>();

            Directory.CreateDirectory(DadosDir);
            Directory.CreateDirectory(PromptDir);

            var normalizados = NormalizarLista(perfis);
            var json = JsonConvert.SerializeObject(normalizados, Formatting.Indented);
            File.WriteAllText(FilePath, json);
        }

        public static string ResolverPromptPath(LanguageProfileConfig perfil)
        {
            if (perfil == null || string.IsNullOrWhiteSpace(perfil.PromptPath))
                return string.Empty;

            if (Path.IsPathRooted(perfil.PromptPath))
                return perfil.PromptPath;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, perfil.PromptPath);
        }

        public static string LoadPromptForLanguage(string languageProfileId)
        {
            var perfil = BuscarPorId(languageProfileId);
            if (perfil == null || !perfil.PromptAtivo)
                return string.Empty;

            string path = ResolverPromptPath(perfil);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return string.Empty;

            return File.ReadAllText(path);
        }

        public static string NormalizeId(string id)
        {
            id = (id ?? string.Empty).Trim().ToLowerInvariant();
            if (id == "c#" || id == "csharp")
                return "csharp";
            if (id == "php")
                return "php";
            if (string.IsNullOrWhiteSpace(id))
                return "geral";

            return id;
        }

        public static string IdFromLegacyProfile(Configuration.LanguageProfile profile)
        {
            switch (profile)
            {
                case Configuration.LanguageProfile.CSharp:
                    return "csharp";
                case Configuration.LanguageProfile.PHP:
                    return "php";
                case Configuration.LanguageProfile.Geral:
                default:
                    return "geral";
            }
        }

        public static string ResolveId(string languageProfileId, Configuration.LanguageProfile legacyProfile)
        {
            string normalizedId = NormalizeId(languageProfileId);
            string legacyId = IdFromLegacyProfile(legacyProfile);

            if (string.IsNullOrWhiteSpace(languageProfileId))
                return legacyId;

            if (normalizedId == "geral" && legacyProfile != Configuration.LanguageProfile.Geral)
                return legacyId;

            return normalizedId;
        }

        public static Configuration.LanguageProfile LegacyProfileFromId(string id)
        {
            id = NormalizeId(id);
            if (id == "csharp")
                return Configuration.LanguageProfile.CSharp;
            if (id == "php")
                return Configuration.LanguageProfile.PHP;

            return Configuration.LanguageProfile.Geral;
        }

        private static void GarantirInicializado()
        {
            Directory.CreateDirectory(DadosDir);
            Directory.CreateDirectory(PromptDir);
            GarantirPromptCSharp();

            if (!File.Exists(FilePath))
                Salvar(CriarPadroes());
        }

        private static List<LanguageProfileConfig> NormalizarLista(List<LanguageProfileConfig> perfis)
        {
            var resultado = new List<LanguageProfileConfig>();
            foreach (var perfil in perfis)
            {
                if (perfil == null)
                    continue;

                perfil.Id = NormalizeId(perfil.Id);
                perfil.Nome = string.IsNullOrWhiteSpace(perfil.Nome) ? perfil.Id : perfil.Nome.Trim();
                perfil.PromptPath = perfil.PromptPath ?? string.Empty;

                if (!resultado.Any(p => string.Equals(p.Id, perfil.Id, StringComparison.OrdinalIgnoreCase)))
                    resultado.Add(perfil);
            }

            foreach (var padrao in CriarPadroes())
            {
                if (!resultado.Any(p => string.Equals(p.Id, padrao.Id, StringComparison.OrdinalIgnoreCase)))
                    resultado.Add(padrao);
            }

            return resultado.OrderByDescending(p => p.Sistema).ThenBy(p => p.Nome).ToList();
        }

        private static List<LanguageProfileConfig> CriarPadroes()
        {
            return new List<LanguageProfileConfig>
            {
                new LanguageProfileConfig { Id = "geral", Nome = "Geral", PromptPath = string.Empty, PromptAtivo = false, Sistema = true },
                new LanguageProfileConfig { Id = "csharp", Nome = "C#", PromptPath = @"dados\prompts-linguagens\csharp.txt", PromptAtivo = true, Sistema = true },
                new LanguageProfileConfig { Id = "php", Nome = "PHP", PromptPath = string.Empty, PromptAtivo = false, Sistema = true }
            };
        }

        private static void GarantirPromptCSharp()
        {
            string path = Path.Combine(PromptDir, "csharp.txt");
            if (File.Exists(path))
                return;

            File.WriteAllText(path,
@"# Prompt especifico para C#

- Em WinForms, nao edite arquivos *.Designer.cs salvo se for inevitavel.
- Se for inevitavel editar Designer.cs, nao misture regioes diferentes do InitializeComponent na mesma acao.
- Nao busque InitializeComponent no arquivo .cs principal para alterar controles do Designer.
- Nao use ancoras de Designer no code-behind.
- Nao inserir metodo entre a assinatura e a chave de outro metodo.
- Use somente ancoras reais existentes no contexto recebido.
- Nao invente arquivos como MainForm.cs ou Form1.cs; use apenas arquivos reais do projeto atual.
- Para WinForms, prefira criar e configurar controles dinamicamente no .cs principal, apos InitializeComponent ou em metodo proprio chamado pelo construtor.
- Se precisar inserir codigo novo, ancore em uma linha de codigo real pequena e repita a linha original no REPLACE_BLOCK antes do novo codigo.
");
        }
    }
}
