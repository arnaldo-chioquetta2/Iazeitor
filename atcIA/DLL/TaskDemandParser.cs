using System;
using System.Collections.Generic;

namespace GptBolDll
{
    public static class TaskDemandParser
    {
        private static readonly string[] Headers =
        {
            "OBJETIVO:",
            "ESCOPO:",
            "RESTRICOES:",
            "ENTREGA_ESPERADA:"
        };

        public static bool TryParse(string input, out TaskDemandRequest demanda, out string erro)
        {
            return TryParse(input, out demanda, out erro, out bool _);
        }

        public static bool TryParse(string input, out TaskDemandRequest demanda, out string erro, out bool fallbackUsado)
        {
            demanda = null;
            erro = string.Empty;
            fallbackUsado = false;

            if (string.IsNullOrWhiteSpace(input))
            {
                erro = "Informe a demanda estruturada.";
                return false;
            }

            if (!ContainsAnyHeader(input))
            {
                demanda = CriarDemandaMinima(input);
                fallbackUsado = true;
                return true;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Headers.Length; i++)
            {
                string header = Headers[i];
                int start = input.IndexOf(header, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                {
                    values[header] = string.Empty;
                    fallbackUsado = true;
                    continue;
                }

                start += header.Length;
                int end = input.Length;
                for (int j = 0; j < Headers.Length; j++)
                {
                    if (string.Equals(Headers[j], header, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int next = input.IndexOf(Headers[j], start, StringComparison.OrdinalIgnoreCase);
                    if (next >= 0 && next < end)
                        end = next;
                }

                values[header] = input.Substring(start, end - start).Trim();
            }

            if (string.IsNullOrWhiteSpace(values["OBJETIVO:"]))
            {
                values["OBJETIVO:"] = ExtrairTextoDisponivel(input);
                fallbackUsado = true;
            }
            if (string.IsNullOrWhiteSpace(values["ESCOPO:"]))
            {
                values["ESCOPO:"] =
                    "- Interpretar o pedido do usuario." + Environment.NewLine +
                    "- Identificar arquivos e alteracoes necessarias." + Environment.NewLine +
                    "- Gerar uma requisicao de tarefa executavel.";
                fallbackUsado = true;
            }
            if (!values.ContainsKey("RESTRICOES:") || string.IsNullOrWhiteSpace(values["RESTRICOES:"]))
            {
                values["RESTRICOES:"] =
                    "- Nao realizar alteracoes fora do pedido do usuario." + Environment.NewLine +
                    "- Respeitar o nivel maximo configurado para a IA." + Environment.NewLine +
                    "- Nao executar alteracoes nesta etapa.";
                fallbackUsado = true;
            }
            if (string.IsNullOrWhiteSpace(values["ENTREGA_ESPERADA:"]))
            {
                values["ENTREGA_ESPERADA:"] =
                    "- Atender ao objetivo descrito pelo usuario." + Environment.NewLine +
                    "- Manter o projeto compilando." + Environment.NewLine +
                    "- Requisicao de tarefa estruturada para execucao posterior pelo programador.";
                fallbackUsado = true;
            }

            demanda = new TaskDemandRequest
            {
                Objetivo = values["OBJETIVO:"],
                Escopo = values["ESCOPO:"],
                Restricoes = values["RESTRICOES:"],
                EntregaEsperada = values["ENTREGA_ESPERADA:"]
            };

            return true;
        }

        public static TaskDemandRequest NormalizarDemanda(string input, out bool fallbackUsado)
        {
            fallbackUsado = false;

            if (string.IsNullOrWhiteSpace(input))
            {
                fallbackUsado = true;
                return CriarDemandaMinima(string.Empty);
            }

            if (!ContainsAnyHeader(input))
            {
                fallbackUsado = true;
                return CriarDemandaMinima(input);
            }

            var demanda = new TaskDemandRequest();
            string objetivo = ExtrairBlocoOpcional(input, "OBJETIVO:");
            string escopo = ExtrairBlocoOpcional(input, "ESCOPO:");
            string restricoes = ExtrairBlocoOpcional(input, "RESTRICOES:");
            string entrega = ExtrairBlocoOpcional(input, "ENTREGA_ESPERADA:");

            if (string.IsNullOrWhiteSpace(objetivo))
            {
                objetivo = ExtrairTextoDisponivel(input);
                fallbackUsado = true;
            }

            if (string.IsNullOrWhiteSpace(escopo))
            {
                escopo =
                    "- Interpretar o pedido do usuario." + Environment.NewLine +
                    "- Identificar arquivos e alteracoes necessarias." + Environment.NewLine +
                    "- Gerar uma requisicao de tarefa executavel.";
                fallbackUsado = true;
            }

            if (string.IsNullOrWhiteSpace(restricoes))
            {
                restricoes =
                    "- Nao realizar alteracoes fora do pedido do usuario." + Environment.NewLine +
                    "- Respeitar o nivel maximo configurado para a IA." + Environment.NewLine +
                    "- Nao executar alteracoes nesta etapa.";
                fallbackUsado = true;
            }

            if (string.IsNullOrWhiteSpace(entrega))
            {
                entrega =
                    "- Atender ao objetivo descrito pelo usuario." + Environment.NewLine +
                    "- Manter o projeto compilando." + Environment.NewLine +
                    "- Requisicao de tarefa estruturada para execucao posterior pelo programador.";
                fallbackUsado = true;
            }

            demanda.Objetivo = objetivo;
            demanda.Escopo = escopo;
            demanda.Restricoes = restricoes;
            demanda.EntregaEsperada = entrega;
            return demanda;
        }

        private static TaskDemandRequest CriarDemandaMinima(string input)
        {
            return new TaskDemandRequest
            {
                Objetivo = ExtrairTextoDisponivel(input),
                Escopo =
                    "- Interpretar o pedido do usuario." + Environment.NewLine +
                    "- Identificar arquivos e alteracoes necessarias." + Environment.NewLine +
                    "- Gerar uma requisicao de tarefa executavel.",
                Restricoes =
                    "- Nao realizar alteracoes fora do pedido do usuario." + Environment.NewLine +
                    "- Respeitar o nivel maximo configurado para a IA." + Environment.NewLine +
                    "- Nao executar alteracoes nesta etapa.",
                EntregaEsperada =
                    "- Atender ao objetivo descrito pelo usuario." + Environment.NewLine +
                    "- Manter o projeto compilando." + Environment.NewLine +
                    "- Requisicao de tarefa estruturada para execucao posterior pelo programador."
            };
        }

        private static string ExtrairBlocoOpcional(string input, string header)
        {
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(header))
                return string.Empty;

            int start = input.IndexOf(header, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += header.Length;
            int end = input.Length;

            foreach (var possibleHeader in Headers)
            {
                if (string.Equals(possibleHeader, header, StringComparison.OrdinalIgnoreCase))
                    continue;

                int next = input.IndexOf(possibleHeader, start, StringComparison.OrdinalIgnoreCase);
                if (next >= 0 && next < end)
                    end = next;
            }

            return input.Substring(start, end - start).Trim();
        }

        private static string ExtrairTextoDisponivel(string input)
        {
            input = (input ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(input))
                return "Interpretar e atender ao pedido do usuario.";

            return input;
        }

        private static bool ContainsAnyHeader(string input)
        {
            foreach (var header in Headers)
            {
                if (input.IndexOf(header, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
