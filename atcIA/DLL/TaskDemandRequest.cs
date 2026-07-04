namespace GptBolDll
{
    public sealed class TaskDemandRequest
    {
        public string Objetivo { get; set; }
        public string Escopo { get; set; }
        public string Restricoes { get; set; }
        public string EntregaEsperada { get; set; }
    }
}
