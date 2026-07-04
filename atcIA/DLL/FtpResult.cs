using System.Collections.Generic;

namespace GptBolDll
{
    public sealed class FtpResult
    {
        public string Operation { get; set; }
        public string RemotePath { get; set; }
        public string LocalPath { get; set; }
        public int BytesTransferred { get; set; }
        public List<string> Items { get; set; } = new List<string>();
        public string Error { get; set; }
    }
}
