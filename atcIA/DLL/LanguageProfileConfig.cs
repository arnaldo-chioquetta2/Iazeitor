namespace GptBolDll
{
    public class LanguageProfileConfig
    {
        public string Id { get; set; }
        public string Nome { get; set; }
        public string PromptPath { get; set; }
        public bool PromptAtivo { get; set; }
        public bool Sistema { get; set; }

        public LanguageProfileConfig()
        {
            Id = string.Empty;
            Nome = string.Empty;
            PromptPath = string.Empty;
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Nome) ? Id : Nome;
        }
    }
}
