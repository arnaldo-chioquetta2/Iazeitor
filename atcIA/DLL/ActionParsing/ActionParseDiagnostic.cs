namespace GptBolDll
{
    public sealed class ActionParseDiagnostic
    {
        public string Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string ActionType { get; set; }
        public string SafePreview { get; set; }
    }
}
