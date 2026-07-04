using GptBolDll.Configuration;
using System;
using System.IO;
using System.Text;

namespace GptBolDll
{
    public static class PromptLoader
    {
        public static string BuildSystemPrompt()
        {
            return
                "Voce deve responder sempre em JSON valido, sem texto fora do JSON.\n" +
                "O schema obrigatorio e:\n" +
                "{\n" +
                "  \"mensagem_usuario\": \"string\",\n" +
                "  \"explicacao\": \"string\",\n" +
                "  \"acoes\": [\n" +
                "    {\n" +
                "      \"tipo\": \"Nenhuma|ArquivoLocal|ComandoDos|Ftp\",\n" +
                "      \"descricao\": \"string\",\n" +
                "      \"requer_confirmacao\": true,\n" +
                "      \"dados\": { }\n" +
                "    }\n" +
                "  ],\n" +
                "  \"requer_confirmacao\": false\n" +
                "}\n" +
                "Regras:\n" +
                "- sempre preencher mensagem_usuario e explicacao;\n" +
                "- listar em acoes apenas o que for realmente executavel;\n" +
                "- quando receber uma requisicao com ETAPAS, cubra todas as etapas no mesmo JSON; nao retorne apenas a primeira etapa se houver trabalho restante;\n" +
                "- antes de finalizar, compare as acoes geradas com cada OBJETIVO/ESCOPO da requisicao e inclua na explicacao um resumo CHECK_ETAPAS com etapas cobertas ou ja existentes;\n" +
                "- nao considere a tarefa concluida enquanto faltar ligar eventos de botoes, chamadas de telas, handlers, designer ou metodos citados explicitamente no escopo;\n" +
                "- nao encerre dizendo que uma etapa foi concluida e pedindo confirmacao para prosseguir; quando houver trabalho restante, gere as proximas acoes na mesma resposta;\n" +
                "- nao use frases como 'confirme para prosseguir', 'proxima etapa' ou 'a etapa seguinte sera' quando puder continuar executando;\n" +
                "- quando criar arquivo .cs novo, nao gere acao manual para editar .csproj; a ferramenta inclui automaticamente o arquivo quando o projeto exigir;\n" +
                "- se o contexto mostrar um .csproj SDK-style com inclusao automatica, nao inventar edicao no .csproj;\n" +
                "- nunca duplique Compile Include no .csproj;\n" +
                "- quando criar arquivo novo, deixar claro se o arquivo e novo;\n" +
                "- implemente somente o que foi pedido; nao adicione logs, auditoria, telemetria, mensagens de debug ou chamadas Console/Debug/Trace se o pedido nao solicitar;\n" +
                "- nao chame metodos, classes, servicos ou propriedades que nao existam no contexto, a menos que tambem crie essa implementacao na mesma resposta;\n" +
                "- em projetos C# legados, considere disponivel apenas o codigo listado no .csproj como Compile Include; nao use arquivos soltos que nao participam da compilacao;\n" +
                "- para classes simples de regra de negocio, prefira codigo autocontido usando apenas bibliotecas padrao do .NET e dependencias ja existentes no projeto;\n" +
                "- em acoes ArquivoLocal, use apenas comandos de protocolo aceitos: ARQ=, SEARCH=, REPLACE=, DELETE, SEARCH_BLOCK, END_SEARCH, REPLACE_BLOCK, END_REPLACE e DELETE_BLOCK;\n" +
                "- SEARCH= e REPLACE= sao comandos de linha unica; nunca podem conter quebras de linha;\n" +
                "- toda linha SEARCH= deve ser seguida imediatamente por REPLACE=, REPLACE_BLOCK ou DELETE; nunca deixe SEARCH= sem acao correspondente;\n" +
                "- para busca ou substituicao com multiplas linhas, use obrigatoriamente SEARCH_BLOCK, END_SEARCH, REPLACE_BLOCK e END_REPLACE;\n" +
                "- em SEARCH_BLOCK, use o menor bloco estavel possivel, preferencialmente ate 12 linhas, copiado exatamente do contexto; nao gere blocos grandes de metodo inteiro se uma ancora menor resolver;\n" +

                // Regras novas para reduzir falha de SEARCH_BLOCK
                "- nunca use SEARCH_BLOCK= inline para blocos multilinha; para blocos use sempre SEARCH_BLOCK em linha isolada, depois as linhas buscadas, depois END_SEARCH;\n" +
                "- nunca use REPLACE_BLOCK= inline para blocos multilinha; para blocos use sempre REPLACE_BLOCK em linha isolada, depois as linhas novas, depois END_REPLACE;\n" +
                "- o formato correto para bloco e: ARQ=caminho, SEARCH_BLOCK, linhas exatas existentes, END_SEARCH, REPLACE_BLOCK, linhas novas, END_REPLACE;\n" +
                "- antes de gerar SEARCH_BLOCK, confirme que todas as linhas do bloco aparecem exatamente no contexto recebido; se nao aparecerem, nao invente o bloco;\n" +
                "- SEARCH_BLOCK deve usar linhas reais existentes no contexto recebido, nao linhas deduzidas, reconstruidas ou aproximadas;\n" +
                "- nao use comentarios, textos com acentos, titulos visuais ou mensagens como ancora principal de SEARCH_BLOCK; prefira linhas de codigo reais e estaveis;\n" +
                "- se o contexto mostrar texto corrompido por encoding, como BotÃ, Ãƒ, Â ou caracteres estranhos, nao use esse comentario como ancora; use linhas de codigo proximas sem acento;\n" +
                "- evite qualquer ancora sujeita a acento ou encoding; prefira chamadas de metodo, atribuicoes, assinaturas, eventos, inicializacao de campos ou Controls.Add;\n" +
                "- para inserir codigo novo, prefira ancorar em uma linha de codigo existente imediatamente antes ou depois do ponto de insercao;\n" +
                "- se uma linha, metodo, propriedade, campo ou chamada buscada nao existir no contexto, nao gere substituicao para ela;\n" +
                "- se precisar criar metodo novo, insira-o proximo a um membro real existente mostrado no contexto, usando uma ancora segura;\n" +
                "- se precisar alterar uma chamada existente, use no SEARCH_BLOCK a chamada exatamente como aparece no contexto, nao uma versao deduzida;\n" +
                "- se uma acao anterior no mesmo JSON alterar um trecho, as acoes seguintes devem considerar o arquivo ja alterado por essa acao anterior;\n" +
                "- evite usar SEARCH_BLOCK contendo apenas comentarios; use pelo menos uma linha de codigo executavel ou declaracao real;\n" +
                "- quando possivel, para inserir codigo depois de uma linha existente, use SEARCH_BLOCK pequeno contendo somente a linha anterior e repita essa linha no REPLACE_BLOCK seguida do novo codigo;\n" +

                "- se precisar alterar varias regioes do mesmo arquivo, use varias substituicoes pequenas e sequenciais em vez de um SEARCH_BLOCK grande;\n" +
                "- em projetos WinForms, evite editar arquivos *.Designer.cs; prefira criar/configurar controles no arquivo .cs principal apos InitializeComponent ou em metodo proprio chamado pelo construtor;\n" +
                "- se for inevitavel alterar *.Designer.cs, nao misture regioes diferentes do InitializeComponent na mesma acao; separe declaracao, instanciacao, configuracao visual, eventos e Controls.Add em acoes pequenas;\n" +
                "- em WinForms Designer, nunca use um SEARCH_BLOCK que combine linhas distantes como instanciacao de controle e configuracao visual; ancore cada trecho no local real em que aparece no contexto;\n" +
                "- para criar ou substituir arquivo inteiro, use ARQ= seguido de REPLACE= ou REPLACE_BLOCK/END_REPLACE;\n" +
                "- nunca misture explicacao, markdown, comentarios XML ou texto livre dentro das acoes ArquivoLocal; se houver mais de uma linha de codigo, use SEARCH_BLOCK/REPLACE_BLOCK;\n" +
                "- nunca use SEARCH_END, REPLACE_END ou outros marcadores inexistentes no protocolo;\n" +
                "- definir requer_confirmacao=true quando houver escrita, exclusao, upload, comando sensivel ou risco;\n" +
                "- se nao houver acao, retornar acoes vazia;\n" +
                "- nao usar markdown, nao usar explicacao fora do JSON, nao usar texto antes ou depois do JSON.";
        }

        public static string BuildLanguageProfileInstruction(LanguageProfile languageProfile)
        {
            switch (languageProfile)
            {
                case LanguageProfile.CSharp:
                    return
        @"# Perfil de linguagem: C#

A IA deve priorizar soluções em C# e .NET.
Use preferencialmente C# como linguagem principal para exemplos, correções, geração de código e alterações em arquivos.
Quando aplicável, gere código compatível com C# 7.3+ e .NET Framework 4.8.1.
Considere que o projeto utiliza Windows Forms no FrontEnd e a DLL GptBolDll como BackEnd.
Respeite a arquitetura existente: Main.cs, ConfigManager, AgentCore, PromptLoader, ToolDispatcher, repositórios e serviços já existentes.
Prefira soluções simples, nativas e compatíveis com o projeto antes de sugerir bibliotecas externas.
Use async/await quando houver I/O, chamadas HTTP, operações demoradas ou processamento assíncrono.
Mantenha compatibilidade com Newtonsoft.Json quando trabalhar com JSON.
Evite recursos modernos não suportados pelo C# 7.3, como record, init, nullable reference types e switch expression.
Ao alterar métodos existentes, preserve nomes, fluxo e responsabilidades sempre que possível.
Ao propor código para Windows Forms, mantenha padrão compatível com .NET Framework e System.Windows.Forms.
Quando o usuário pedir análise técnica, priorize explicações voltadas para C#, .NET, WinForms e padrões internos do projeto.
Quando gerar ações, mantenha os caminhos, namespaces e estruturas compatíveis com o projeto atual.";

                case LanguageProfile.PHP:
                    return
        @"# Perfil de linguagem: PHP

A IA deve priorizar solucoes em PHP.
Use PHP como linguagem principal para exemplos, correcoes, geracao de codigo e alteracoes em arquivos.
Quando aplicavel, considere arquivos .php, .phtml, .htaccess, composer.json, rotas, controllers, views e configuracoes comuns de hospedagem PHP.
Em projetos Laravel, URLs normalmente passam por routes/web.php ou routes/api.php; antes de dizer que falta contexto, procure rotas, controllers em app/Http/Controllers e views em resources/views.
Ao alterar arquivos locais, gere acoes ArquivoLocal pequenas e seguras.
Quando o usuario pedir envio por FTP, gere tambem acoes Ftp do tipo PUT para os arquivos alterados, usando caminhos remotos relativos a raiz FTP configurada no projeto.
Nao use frameworks PHP especificos sem evidencias no contexto do projeto.
Prefira compatibilidade ampla e evite recursos que exijam versoes recentes de PHP sem necessidade.";

                case LanguageProfile.Geral:
                default:
                    return
        @"# Perfil de linguagem: Geral

A IA deve atuar de forma neutra quanto à linguagem de programação.
Não assuma C#, JavaScript, Python ou qualquer outra linguagem automaticamente.
Use uma linguagem específica somente quando o usuário solicitar explicitamente.
Também pode inferir a linguagem quando o contexto do projeto deixar isso claro.
Quando o pedido for conceitual, arquitetural ou de análise, responda sem depender de linguagem específica.
Quando precisar propor código e não houver linguagem clara, explique a escolha antes de gerar o exemplo.
Não force padrões, bibliotecas ou estruturas de C# no modo Geral.";
            }
        }

        public static string LoadPrompt(string projectRoot, string promptFilePath = null)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new ArgumentException("Pasta do projeto não configurada.");

            if (!Directory.Exists(projectRoot))
                throw new DirectoryNotFoundException("Pasta do projeto não encontrada: " + projectRoot);

            string promptFile = promptFilePath;
            if (string.IsNullOrWhiteSpace(promptFile))
                promptFile = Path.Combine(projectRoot, "prompt.txt");

            if (!File.Exists(promptFile))
                return string.Empty;

            return File.ReadAllText(promptFile);
        }

        public static string BuildSystemAnalystPrompt(
            int nivelMaximo,
            string contextoProjeto,
            TaskDemandRequest demanda,
            string analystPromptPath = null)
        {
            if (demanda == null)
                throw new ArgumentNullException(nameof(demanda));

            if (string.IsNullOrWhiteSpace(analystPromptPath))
                return BuildSystemAnalystPromptPadrao(nivelMaximo, contextoProjeto, demanda);

            if (!File.Exists(analystPromptPath))
                return BuildSystemAnalystPromptPadrao(nivelMaximo, contextoProjeto, demanda);

            string content = TryLoadAnalystPromptExterno(
                analystPromptPath,
                nivelMaximo,
                contextoProjeto,
                demanda);

            if (string.IsNullOrWhiteSpace(content))
                return BuildSystemAnalystPromptPadrao(nivelMaximo, contextoProjeto, demanda);

            return content;
        }

        private static string BuildSystemAnalystPromptPadrao(
            int nivelMaximo,
            string contextoProjeto,
            TaskDemandRequest demanda)
        {
            var sb = new StringBuilder();
            sb.AppendLine("NIVEL_MAXIMO: " + nivelMaximo);
            sb.AppendLine("Regra critica: O NIVEL_MAXIMO e teto absoluto. Nenhuma etapa pode exceder esse nivel.");
            sb.AppendLine("MODO_OPERACAO: analista_de_sistemas -> gerador_de_requisicao");
            sb.AppendLine("IMPORTANTE - FORMATO OBRIGATORIO DA RESPOSTA:");
            sb.AppendLine("A resposta deve ser texto puro e deve iniciar exatamente com a linha:");
            sb.AppendLine("TAREFA_ID: <identificador_curto>");
            sb.AppendLine("Nao escreva nada antes de TAREFA_ID.");
            sb.AppendLine("Nao use markdown.");
            sb.AppendLine("Nao use bloco de codigo.");
            sb.AppendLine("Nao use explicacao antes do formato.");
            sb.AppendLine("Nao use JSON nesta etapa.");
            sb.AppendLine("A resposta deve conter obrigatoriamente estes blocos, nesta ordem:");
            sb.AppendLine("TAREFA_ID:");
            sb.AppendLine("TITULO:");
            sb.AppendLine("OBJETIVO:");
            sb.AppendLine("ESCOPO:");
            sb.AppendLine("ETAPAS:");
            sb.AppendLine("CRITERIO_ACEITE:");
            sb.AppendLine("O bloco ETAPAS nunca pode ficar ausente.");
            sb.AppendLine("Se houver apenas uma etapa, escreva uma unica etapa.");
            sb.AppendLine("Se nao souber dividir, escreva:");
            sb.AppendLine("ETAPAS:");
            sb.AppendLine("1. Executar a alteracao solicitada no escopo.");
            sb.AppendLine("Exemplo de formato correto:");
            sb.AppendLine("TAREFA_ID: ajuste_botao_equalizacao");
            sb.AppendLine("TITULO: Ajustar botao de equalizacao");
            sb.AppendLine("OBJETIVO:");
            sb.AppendLine("Descrever o objetivo aqui.");
            sb.AppendLine("ESCOPO:");
            sb.AppendLine("- Item de escopo aqui.");
            sb.AppendLine("ETAPAS:");
            sb.AppendLine("1. Executar a alteracao solicitada no escopo.");
            sb.AppendLine("CRITERIO_ACEITE:");
            sb.AppendLine("- Critério de aceite aqui.");
            sb.AppendLine();
            sb.AppendLine("CONTEXTO_DO_PROJETO:");
            sb.AppendLine(contextoProjeto ?? string.Empty);
            sb.AppendLine();
            sb.AppendLine("OBJETIVO:");
            sb.AppendLine(demanda.Objetivo ?? string.Empty);
            sb.AppendLine("ESCOPO:");
            sb.AppendLine(demanda.Escopo ?? string.Empty);
            sb.AppendLine("RESTRICOES:");
            sb.AppendLine(demanda.Restricoes ?? string.Empty);
            sb.AppendLine("ENTREGA_ESPERADA:");
            sb.AppendLine(demanda.EntregaEsperada ?? string.Empty);
            sb.AppendLine();
            sb.AppendLine("FORMATO EXATO DE SAIDA:");
            sb.AppendLine("TAREFA_ID: identificador_unico");
            sb.AppendLine("NIVEL_ATRIBUIDO: numero_1_a_10");
            sb.AppendLine("ETAPAS_EXECUTAVEIS_PELO_ATCIA:");
            sb.AppendLine("  1. OBJETIVO: ...");
            sb.AppendLine("     ESCOPO: ...");
            sb.AppendLine("     DEPENDENCIAS: ...");
            sb.AppendLine("     VERIFICACAO: ...");
            sb.AppendLine("VALIDACOES_MANUAIS_RECOMENDADAS:");
            sb.AppendLine("  - ...");
            sb.AppendLine("RESTRICOES_GERAIS: ...");
            sb.AppendLine("FORMATO_EXECUCAO: sequencial_ou_paralelo");
            sb.AppendLine("TRIGGER_PROXIMO: aguarda_confirmacao_manual_ou_automacao");
            sb.AppendLine();
            sb.AppendLine("Nao use markdown, comentarios, explicacoes extras, texto antes de TAREFA_ID ou texto depois de TRIGGER_PROXIMO.");
            sb.AppendLine("Nao inclua como etapa executavel acoes que o atcIA nao consegue realizar automaticamente, como abrir navegador, testar visualmente uma pagina, validar layout manualmente, clicar em elementos da interface ou conferir comportamento em producao.");
            sb.AppendLine("Coloque essas acoes em VALIDACOES_MANUAIS_RECOMENDADAS.");
            return sb.ToString();
        }

        private static string TryLoadAnalystPromptExterno(
            string analystPromptPath,
            int nivelMaximo,
            string contextoProjeto,
            TaskDemandRequest demanda)
        {
            if (string.IsNullOrWhiteSpace(analystPromptPath))
                return null;

            if (!File.Exists(analystPromptPath))
                return null;

            string content = File.ReadAllText(analystPromptPath);
            if (string.IsNullOrWhiteSpace(content))
                return null;

            return AplicarVariaveisPromptAnalista(
                content,
                nivelMaximo,
                contextoProjeto,
                demanda);
        }


        private static string AplicarVariaveisPromptAnalista(
            string template,
            int nivelMaximo,
            string contextoProjeto,
            TaskDemandRequest demanda)
        {
            string resultado = template;

            int tamanhoArquivo = (template ?? string.Empty).Length;
            int ocorrenciasContexto = 0;
            if (!string.IsNullOrEmpty(template))
            {
                string busca = "{{contextoProjeto}}";
                int idx = 0;
                while ((idx = template.IndexOf(busca, idx, System.StringComparison.OrdinalIgnoreCase)) != -1)
                {
                    ocorrenciasContexto++;
                    idx += busca.Length;
                }
            }
            System.Diagnostics.Debug.WriteLine("[DIAG] Prompt Analista externo tamanho arquivo: " + tamanhoArquivo);
            System.Diagnostics.Debug.WriteLine("[DIAG] Prompt Analista externo ocorrencias contextoProjeto: " + ocorrenciasContexto);

            resultado = resultado.Replace("{{nivelMaximo}}", nivelMaximo.ToString());
            resultado = resultado.Replace("{{contextoProjeto}}", contextoProjeto ?? string.Empty);
            resultado = resultado.Replace("{{demanda.Objetivo}}", demanda.Objetivo ?? string.Empty);
            resultado = resultado.Replace("{{demanda.Escopo}}", demanda.Escopo ?? string.Empty);
            resultado = resultado.Replace("{{demanda.Restricoes}}", demanda.Restricoes ?? string.Empty);

            resultado = resultado.Replace("{{demanda.EntregaEsperada}}", demanda.EntregaEsperada ?? string.Empty);

            int tamanhoFinal = (resultado ?? string.Empty).Length;
            System.Diagnostics.Debug.WriteLine("[DIAG] Prompt Analista externo tamanho final: " + tamanhoFinal);

            return resultado;
        }
    }
}
