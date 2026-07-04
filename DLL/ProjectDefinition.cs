using System.Collections.Generic;

namespace GptBolDll
{
    public sealed class ProjectDefinition
    {
        public string Name { get; set; }
        public string Root { get; set; }

        public List<string> Include { get; set; } = new List<string>();
        public List<string> Exclude { get; set; } = new List<string>();

        public List<string> ContextFiles { get; set; } = new List<string>();

        public int MaxFileBytes { get; set; } = 120_000;
        public int MaxTotalBytes { get; set; } = 600_000;
        public int MaxFiles { get; set; } = 60;

        public FtpProfile Ftp { get; set; }
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
}
