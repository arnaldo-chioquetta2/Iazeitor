# Iazeitor

Iazeitor e uma ferramenta para analise, manutencao e evolucao assistida de projetos de software com IA.

## Objetivo

Centralizar o fluxo de analise, interpretacao e aplicacao segura de alteracoes com apoio de diferentes provedores de IA.

## Status

Em desenvolvimento.

## Requisitos

- Windows
- .NET Framework compatível com a solucao
- Git
- GitHub CLI para tarefas de colaboracao opcional

## Como compilar

```powershell
dotnet build atcIA\DLL\GptBolDll.csproj
MSBuild.exe atcIA\atcIA.csproj
```

Se `MSBuild.exe` nao estiver no `PATH`, use o caminho do Visual Studio instalado.

## Como configurar provedores de IA

1. Abra a tela de configuracoes.
2. Ajuste os arquivos de prompt e os provedores desejados.
3. Use os arquivos em `dados.example` como referencia de estrutura.
4. Preencha suas chaves apenas em arquivos locais ignorados pelo Git.

## Aviso importante

Nunca commite chaves, tokens, senhas, logs com segredos ou arquivos locais de configuracao.

## Visao geral do funcionamento

O projeto usa um fluxo com interpretacao de resposta, normalizacao de acoes, validacoes de seguranca e dispatcher para executar alteracoes de forma controlada.

## Como contribuir

Leia `CONTRIBUTING.md` antes de propor mudancas.
