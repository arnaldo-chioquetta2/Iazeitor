using System;
using System.IO;

namespace atcIA
{
    public static class AppPaths
    {
        public static string BaseDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string DadosDir
        {
            get { return Path.Combine(BaseDir, "dados"); }
        }

        public static string LegacyDadosDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "atcIA"); }
        }

        public static string ConfigFile
        {
            get { return Path.Combine(DadosDir, "config.json"); }
        }

        public static string ProjetosFile
        {
            get { return Path.Combine(DadosDir, "projetos.json"); }
        }

        public static string MigracaoLogFile
        {
            get { return Path.Combine(DadosDir, "migracao.log"); }
        }

        public static void GarantirPastaDados()
        {
            if (!Directory.Exists(DadosDir))
                Directory.CreateDirectory(DadosDir);
        }

        public static bool MigrarArquivoLegadoSeNecessario(string fileName)
        {
            GarantirPastaDados();

            string legacyFile = Path.Combine(LegacyDadosDir, fileName);
            string localFile = Path.Combine(DadosDir, fileName);

            if (File.Exists(localFile) || !File.Exists(legacyFile))
                return false;

            File.Copy(legacyFile, localFile, false);
            RegistrarMigracao("Migrado " + fileName + " de '" + LegacyDadosDir + "' para '" + DadosDir + "'.");
            return true;
        }

        public static void RegistrarMigracao(string mensagem)
        {
            GarantirPastaDados();
            File.AppendAllText(MigracaoLogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + mensagem + Environment.NewLine);
        }
    }
}
