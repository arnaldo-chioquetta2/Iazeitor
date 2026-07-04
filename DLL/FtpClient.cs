using System;
using System.IO;
using System.Net;
using System.Text;

namespace GptBolDll
{
    public sealed class FtpClient
    {
        private readonly FtpProfile _profile;

        public FtpClient(FtpProfile profile)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        public byte[] DownloadBytes(string remotePath)
        {
            var request = CreateRequest(remotePath, WebRequestMethods.Ftp.DownloadFile);
            using (var response = (FtpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                if (stream != null)
                    stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public string DownloadText(string remotePath, Encoding encoding = null)
        {
            var bytes = DownloadBytes(remotePath);
            return (encoding ?? Encoding.UTF8).GetString(bytes);
        }

        public void UploadBytes(string remotePath, byte[] data)
        {
            if (data == null) data = Array.Empty<byte>();

            var request = CreateRequest(remotePath, WebRequestMethods.Ftp.UploadFile);
            request.ContentLength = data.Length;

            using (var reqStream = request.GetRequestStream())
                reqStream.Write(data, 0, data.Length);

            using (var response = (FtpWebResponse)request.GetResponse())
            {
                // apenas consome para garantir envio
            }
        }

        public void UploadText(string remotePath, string text, Encoding encoding = null)
        {
            var bytes = (encoding ?? Encoding.UTF8).GetBytes(text ?? "");
            UploadBytes(remotePath, bytes);
        }

        public string[] ListDirectory(string remotePath)
        {
            var request = CreateRequest(remotePath, WebRequestMethods.Ftp.ListDirectory);
            using (var response = (FtpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var sr = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
            {
                var all = sr.ReadToEnd() ?? "";
                return all.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        private FtpWebRequest CreateRequest(string remotePath, string method)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
                remotePath = "/";

            var uri = BuildUri(remotePath);
            var req = (FtpWebRequest)WebRequest.Create(uri);
            req.Method = method;
            req.UsePassive = _profile.UsePassive;
            req.UseBinary = _profile.UseBinary;
            req.EnableSsl = _profile.EnableSsl;
            req.KeepAlive = false;

            var pass = ResolvePassword();
            if (!string.IsNullOrWhiteSpace(_profile.User))
                req.Credentials = new NetworkCredential(_profile.User, pass ?? "");

            return req;
        }

        private Uri BuildUri(string remotePath)
        {
            var host = _profile.Host ?? "";
            host = host.Trim();
            if (host.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring("ftp://".Length);

            var root = _profile.RemoteRoot ?? "/";
            if (!root.StartsWith("/")) root = "/" + root;
            if (!root.EndsWith("/")) root += "/";

            if (remotePath.StartsWith("/"))
                remotePath = remotePath.Substring(1);

            var portPart = _profile.Port > 0 && _profile.Port != 21 ? (":" + _profile.Port) : "";
            var uri = $"ftp://{host}{portPart}{root}{remotePath}";
            return new Uri(uri);
        }

        private string ResolvePassword()
        {
            if (!string.IsNullOrWhiteSpace(_profile.PasswordEnv))
                return Environment.GetEnvironmentVariable(_profile.PasswordEnv);

            return _profile.Password;
        }
    }
}
