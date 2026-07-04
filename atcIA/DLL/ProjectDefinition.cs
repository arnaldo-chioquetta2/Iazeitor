using System.Collections.Generic;

namespace GptBolDll
{
    public sealed class ProjectDefinition
    {
        public int SchemaVersion { get; set; } = 1;
        public string Name { get; set; }
        public string Description { get; set; }
        public string Root { get; set; }
        public List<ProjectCredential> Credentials { get; set; } = new List<ProjectCredential>();

        public List<string> Include { get; set; } = new List<string>();
        public List<string> Exclude { get; set; } = new List<string>();

        public List<string> ContextFiles { get; set; } = new List<string>();
        public List<string> PrimaryFiles { get; set; } = new List<string>();
        public List<string> Instructions { get; set; } = new List<string>();
        public List<string> AllowedDosCommands { get; set; } = new List<string>();

        public int MaxFileBytes { get; set; } = 120_000;
        public int MaxTotalBytes { get; set; } = 600_000;
        public int MaxFiles { get; set; } = 60;

        public FtpProfile Ftp { get; set; }
        public DatabaseProfile Database { get; set; }
    }

    public sealed class ProjectCredential
    {
        public string Tipo { get; set; }
        public string Nome { get; set; }
        public string Valor { get; set; }
    }

    public sealed class FtpProfile
    {
        public string Host { get; set; }
        public int Port { get; set; } = 21;
        public string User { get; set; }

        public string Password { get; set; }
        public string PasswordEnv { get; set; }

        public bool UsePassive { get; set; } = true;
        public bool UseBinary { get; set; } = true;
        public bool EnableSsl { get; set; } = false;

        public string RemoteRoot { get; set; } = "/";
    }

    public sealed class DatabaseProfile
    {
        public string Type { get; set; } = "MySQL";
        public string Host { get; set; }
        public int Port { get; set; } = 3306;
        public string DatabaseName { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string PasswordEnv { get; set; }
        public bool UseSsl { get; set; } = false;
        public string Charset { get; set; } = "utf8mb4";
        public int TimeoutSeconds { get; set; } = 30;
    }
}
