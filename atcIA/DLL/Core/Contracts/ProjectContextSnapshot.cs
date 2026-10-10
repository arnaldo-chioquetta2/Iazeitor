using System;

namespace GptBolDll.Core.Contracts
{
    public sealed class ProjectContextSnapshot
    {
        public string Context { get; set; }
        public string ProjectRoot { get; set; }
        public string OperationId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
