# Iazeitor

![Iazeitor](https://teletudo.com/img/Iazeitor.png)

O Iazeitor e uma ferramenta para analise, manutencao e evolucao de projetos de software com apoio de Inteligencia Artificial.

Ele permite que o desenvolvedor descreva uma necessidade em linguagem natural, como uma correcao, melhoria ou analise, e usa provedores de IA para interpretar a solicitacao, localizar arquivos relevantes, propor alteracoes e apoiar a aplicacao controlada de patches.

O foco do projeto nao e permitir que a IA altere codigo livremente. A IA gera sugestoes, mas o Iazeitor valida caminhos, arquivos, trechos de codigo, contexto e seguranca antes de aplicar qualquer mudanca. Quando uma resposta vem em formatos diferentes, como diff, JSON ou blocos de busca e substituicao, o sistema tenta normalizar a saida para um formato interno seguro.

O projeto possui suporte a multiplos provedores de IA, configuracao local de chaves, uso de contexto do projeto, validacao de alteracoes e fluxo baseado em Git. As chaves de API e configuracoes sensiveis devem permanecer apenas em arquivos locais ignorados pelo Git.

O objetivo do Iazeitor e ajudar programadores a manter sistemas existentes com mais produtividade, mantendo rastreabilidade, seguranca e controle sobre cada alteracao aplicada.

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

## Configuracao local

1. Abra a tela de configuracoes.
2. Ajuste os arquivos de prompt e os provedores desejados.
3. Use os arquivos em `dados.example` como referencia de estrutura.
4. Preencha suas chaves apenas em arquivos locais ignorados pelo Git.

## Como contribuir

Leia `CONTRIBUTING.md` antes de propor mudancas.
Este repositorio utiliza Pull Requests para alteracoes na branch main.

## Seguranca

Nunca commite chaves, tokens, senhas, logs com segredos ou arquivos locais de configuracao.

## Visao geral

O projeto usa um fluxo com interpretacao de resposta, normalizacao de acoes, validacoes de seguranca e dispatcher para executar alteracoes de forma controlada.
