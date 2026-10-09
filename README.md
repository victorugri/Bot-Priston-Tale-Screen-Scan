# BotPriston

Bot de farm para **Priston Tale**, para um servidor privado que permite automação.

Ele **só olha a tela e simula teclado e mouse**, como uma pessoa faria. Não lê nem escreve memória do jogo, não injeta DLL, não faz hook no cliente e não mexe em pacotes de rede.

O que ele faz hoje: acha os mobs colados no personagem, ataca segurando o botão esquerdo, usa a skill do botão direito sempre que ela fica pronta, confirma cada morte, toma poções, repõe as poções da barra com as do inventário (opcional) e para sozinho se algo der errado. O personagem fica parado no lugar.

Numa sessão real de 11 minutos foram **159 mobs abatidos, 3,2 s cada em média**.

---

## Situação por etapa

| Etapa | O que é | Situação |
|---|---|---|
| 1 | Esqueleto do projeto, captura da janela, comando `capture` | ✅ Pronto e testado no jogo |
| 2 | Leitura das barras de HP/MP/STM, overlay de debug, testes offline | ✅ Pronto e testado no jogo |
| 3 | Input (teclado/mouse), hotkeys de segurança, poção automática | ✅ Pronto e testado no jogo |
| 4 | Detecção de alvo | ✅ Pronto e testado no jogo |
| 5 | Loop de combate (achar, atacar, confirmar morte, próximo) + skill do botão direito | ✅ Pronto e testado no jogo |
| 6 | Loot | ⏸️ Não feito: precisa clicar item por item, e por enquanto não é necessário |
| 7 | Robustez: morte, desconexão, travamento, não se afastar do ponto de farm | ⏸️ Parcial; veja [O que falta fazer](#o-que-falta-fazer) |

---

## Antes de rodar

1. **Windows 10/11** com o **.NET 10 SDK** instalado.
2. **Jogo em modo janela, 1600x900**, com gráficos no máximo, expansão de câmera ligada e fading desligado. Todas as posições que o bot usa dependem disso.
3. **Rode o bot num terminal aberto como administrador.** O jogo roda como administrador, e o Windows descarta *em silêncio* as teclas e cliques que um programa comum manda para ele. O bot detecta isso e avisa ao iniciar.
4. **Não use o Snap do Windows** (arrastar a janela para a borda, ou Win+seta) na janela do jogo, porque ele muda o tamanho dela. Para pôr o jogo à direita e deixar espaço para o terminal à esquerda, use o comando `layout`.
5. Desligue overlays de FPS/GPU (MSI Afterburner, RivaTuner), porque eles aparecem dentro da imagem do jogo.

## Uso rápido: janela (V2)

No terminal de administrador, na pasta do projeto:

```bash
dotnet run --project src/BotPriston.Ui
```

A janela é estreita, para caber à esquerda do jogo:

- **Aba Bot:** status (jogo encontrado no tamanho certo, administrador), botões **▶ Iniciar / ⏸ Pausar / ■ Parar**, números da sessão (mobs, tempo por mob, skill direita, desistências, andadas cortadas, atacantes distantes), barras de HP/MP/STM e o log ao vivo.
  - **Ajustar janela** faz o mesmo que o comando `layout`.
  - **Reabrir como admin** aparece se a janela não estiver como administrador.
  - **Iniciar** salva os ajustes, traz o jogo para frente e começa. F12 e Ctrl+F12 continuam valendo.
- **Aba Ajustes:** os ajustes do dia a dia (poções, pegar poções do inventário, combate, skill direita, alcance da busca, ataque de quem bate de longe, descanso, segurança, teclas). Só dá para mudar com o bot parado.

**Como os ajustes são guardados:** o `botconfig.json` tem todos os padrões, com comentários, e fica no git. O que você muda na janela vai para o **`settings.json`**, que fica fora do git e guarda só as diferenças. O bot (janela ou terminal) junta os dois ao iniciar. **Restaurar padrões** apaga o `settings.json`.

## Uso rápido: terminal

Sempre a partir da pasta do projeto (`D:\Projetos\BotPriston`), no terminal de administrador:

```bash
dotnet run --project src/BotPriston.App -- layout
```
```bash
dotnet run --project src/BotPriston.App -- run
```

Clique na janela do jogo para dar foco, e o bot começa.

| Tecla | Efeito |
|---|---|
| **F12** | Pausa/retoma (com bipe). Solta os botões do mouse na hora. |
| **Ctrl+F12** | Encerra o bot e mostra o resumo da sessão no log. |
| Trocar de janela | O bot pausa sozinho e retoma quando o jogo volta ao foco. |

Ao encerrar, o log mostra algo como:
`Session: 159 kills (avg 3,2s each), 1 targets given up, right skill cast 44x (0 attempts without effect)`

---

## Como funciona

### O ciclo

A cada 0,1 s o bot **captura** a janela, **percebe** o que está na tela, **decide** e **age**.

1. **Segurança primeiro.** Se apertaram Ctrl+F12, o jogo fechou, está pausado, sem foco, com a janela no tamanho errado ou sem a interface na tela, o bot não age (e solta os botões).
2. **Sobrevivência.** Se alguma barra está abaixo do limite, ele toma a poção (prioridade HP > MP > STM).
3. **Reposição de poções** (se ligada, `Potions.Restock`). Quando uma pilha da barra fica abaixo de 5 (ou vazia), repõe na hora, **mesmo no meio de uma luta**: solta os botões, larga o alvo e volta a procurar depois.
   1. Abre o inventário (V) e confere na tela que ele abriu.
   2. Procura **a mesma poção que está na barra**, comparando com o ícone do slot. Assim, qualquer poção que você puser na barra é a que ele repõe.
   3. Passa o mouse em cima dela e aperta **Shift + a tecla da poção**, o que soma a pilha ao slot. Depois confere que o número na barra subiu.
   4. Se não achar, olha a página 2 (E). Ao terminar, fecha o inventário (V) e confere.
   - Se a poção não estiver em nenhuma página, avisa no log e só tenta de novo daqui a 5 minutos.
   - Se for pausado no meio, fecha o inventário ao voltar.
4. **Combate**, uma máquina de estados. Cada mudança de estado aparece no log ("Brain X -> Y (motivo)"):

```
Recover ──► SearchTarget ──► Engage ──► Attack ──► (Loot) ──► Recover
```

| Estado | O que faz |
|---|---|
| **Recover** | Entre lutas. Se o HP ou o MP está baixo **e a poção daquela barra está desligada ou acabou**, fica parado regenerando (no máximo 60 s). |
| **SearchTarget** | Garante os dois botões soltos e passa o mouse pelos pontos da área de busca (`Targeting`) até a **pedra do cursor ficar vermelha** (mob embaixo). Não clica. Primeiro procura **dentro do alcance do ataque** (`ReachX/ReachY`). Um mob visto fora do alcance **não é clicado**, porque o jogo faria o personagem andar até ele: o bot procura de novo em 0,3 s, já que o mob costuma vir para perto. Se não acha nada no alcance **e o HP caiu ≥3% nos últimos 4 s** (mob atacando de longe, como o Cão Abelha), faz uma busca numa **área maior** (450x300) e ataca o que achar; só nesse ataque o personagem pode andar. |
| **Engage** | Confere de novo que o cursor ainda está sobre o mob e segura o botão esquerdo. |
| **Attack** | Mantém o ataque até a barra de HP do alvo zerar. Usa a skill do botão direito sempre que ela fica pronta. |

**ATK contínuo** (`Combat.AutoAttack`, opção "ATK contínuo ligado no jogo" na janela): use junto com o botão de ATK contínuo do jogo, ao lado do minimapa.
- Em vez de segurar o botão, o bot **clica uma vez** no mob e espera ele morrer. O jogo continua atacando mesmo que o mob saia de baixo do cursor.
- **Novo clique:** se o HP do alvo não cair por 2 s (`ReclickMs`), o bot passa o mouse sobre o mob de novo (sem clicar) e só clica quando a pedra fica vermelha.
- **Skill direita:** continua igual (segura o direito até a esfera ficar cinza). Depois, um clique esquerdo retoma o ataque contínuo.
| **Loot** | Desligado; o bot passa direto para Recover. |

**Regras importantes do ataque:**
- **Nunca segura botão fora de um mob.** Com o botão segurado sobre o chão, o personagem anda até o cursor. Se a pedra deixa de estar vermelha, os botões são soltos no mesmo instante.
- **O mob se mexeu:** se o cursor fica fora dele por 0,4 s, o bot procura de novo perto de onde o mob estava.
- **Desiste do alvo:** se o HP dele não cair por 5 s, ou depois de 60 s de luta.
- **Anti-andada:** se o chão deslizar por 3 quadros seguidos (o personagem está andando), o bot força os dois botões para cima na hora, abandona o alvo e registra no log o que estava fazendo ("Character is WALKING ... while brain ..."). Tremores de golpe duram 1 quadro e não disparam o sensor.
- **Botão direito:** quando a esfera da skill direita fica colorida, o bot solta o esquerdo e **segura o direito até a esfera ficar cinza** (a skill saiu, ~1 s), no máximo 2,5 s, e volta ao esquerdo. Um clique curto não funciona, porque o jogo o ignora no meio do golpe do ataque básico.

### O que o bot enxerga

Todas as posições são em pixels da área do jogo (1600x900) e ficam no `botconfig.json`.

| O que | Como é lido |
|---|---|
| **Interface na tela?** | Procura as letras "C V S D Q X" do menu (imagem de referência em `templates/hud_menu_letters.png`). Sem elas é tela de login, mapa aberto, loading ou desconexão, e nada mais é confiável. |
| **HP / MP / STM** | Altura do líquido em cada tubo, contando de baixo para cima até o líquido acabar. Precisão de ~1%. |
| **Mob sob o cursor** | **A pedra do cursor do jogo: verde sobre o chão, vermelha sobre um mob.** É o sinal usado na busca e no ataque. |
| **Alvo (painel superior direito)** | Nome, retrato e barra de HP do alvo. Serve para medir o dano e **confirmar a morte** (barra vazia). **Não serve para saber o que está sob o cursor**, porque o painel continua mostrando o último mob por um tempo. |
| **Skills prontas?** | As duas esferas entre os tubos: coloridas = prontas, cinzas = recarregando. |
| **Poções na barra** | O número branco no canto de cada slot, lido dígito a dígito comparando com o formato exato da fonte do jogo (8 px de altura). Slot sem ícone = 0. |
| **Inventário** | Aberto ou não pelos botões "!" e "▲" (`templates/inventory_buttons.png`). A página atual é o botão "<" ou ">" aceso em laranja. A poção é achada comparando a parte de baixo do ícone (abaixo do número) com a grade. |
| **Personagem andando?** | A câmera segue o personagem, então quando ele anda o chão inteiro desliza. O bot compara quatro áreas de chão entre quadros seguidos (correlação de fase) e só considera movimento quando a maioria delas concorda. |

### Segurança

| Mecanismo | Comportamento |
|---|---|
| F12 / Ctrl+F12 | Pausa/retoma e encerra; interrompem até uma busca em andamento. |
| Perda de foco / janela minimizada | Pausa automática; nada é enviado para outra janela. |
| Janela fora de 1600x900 | Pausa automática até o tamanho voltar (`layout`). |
| Interface sumiu por 20 s | Para o bot (morte, desconexão, tela cheia). |
| Barra de HP vazia por 2 s | Para o bot (personagem morreu). |
| Sem progresso por 120 s | Para o bot. Progresso = dano no alvo ou mob morto. |
| Personagem andando | Força os botões para cima, abandona o alvo e registra a causa (`Safety.StopWalking`). |
| Botão "preso" no jogo | Antes de cada busca e sempre que o jogo volta ao foco, o bot manda "soltar" para os dois botões, mesmo achando que já estão soltos. |
| Poção sem efeito 3 vezes seguidas | Considera que acabou, avisa e espera 60 s antes de tentar de novo. |
| `--dry-run` | Percebe e decide normalmente, mas não manda nenhum input (só registra no log). |
| Input | Teclas como códigos de hardware (scan codes), com pausas e variação aleatória. O mouse nunca vai às áreas da interface (barras, chat, minimapa, painel); a única exceção é o inventário durante a reposição. |

---

## Comandos

Todos no formato `dotnet run --project src/BotPriston.App -- <comando> [opções]`. O `help` lista tudo.

| Comando | Para que serve |
|---|---|
| `run` | **Roda o bot.** Opções: `--dry-run`, `--no-combat` (só poções), `--no-right-skill`, `--record` (grava cada quadro com o estado do bot desenhado em `samples/record_*`) |
| `layout` | Volta o jogo para 1600x900 e o encosta à direita da tela (`--left` para a esquerda) |
| `windows` | Lista as janelas abertas (para configurar `Window.ProcessName`) |
| `info` | Mostra a geometria da janela e testa a captura (tempo por quadro) |
| `capture` | Salva prints em `samples/`: `--delay`, `--count`, `--interval`, `--hotkey` (F11 salva, Ctrl+F12 sai), `--label`, `--window` |
| `debug` | Janela ao vivo com tudo o que o bot enxerga desenhado por cima, inclusive a pedra do cursor (`--source samples` para usar prints salvos; `s` salva o quadro) |
| `detect` | Roda os detectores nos prints salvos e mostra uma tabela (`--csv`, `--overlay <pasta>`, `--sweep`) |
| `probe` | Calibração da busca: passa por todos os pontos e salva tabela, prints e mapa em `samples/probe_*` (não clica) |
| `find` | Procura um mob e deixa o cursor sobre ele (não clica) — `--repeat n` |
| `press` / `mouse` | Testes manuais de input: `press --key 3`, `mouse --x 800 --y 300 --click left` |
| `motion` | Passa uma gravação pelo sensor de andada e lista os quadros com movimento: `--source samples/record_...` |
| `crop` | Recorta uma imagem de referência de um print: `--source a.png --roi x,y,w,h --out t.png` |

Opções comuns: `--config <arquivo>`, `--backend auto|wgc|bitblt`, `-v` (mostra os detalhes de debug no terminal).

---

## Configuração (`botconfig.json`)

Nada fica fixo no código. O arquivo aceita comentários, e o bot valida tudo ao iniciar, com mensagens claras se algo estiver errado.

Os ajustes que mais importam no dia a dia:

| Ajuste | Onde | Hoje |
|---|---|---|
| Limite de cada poção | `Potions.Hp/Mp/Stm.BelowPercent` | HP 30, MP 10, STM 10 |
| Teclas das poções | `Potions.*.Key` | 1 = STM, 2 = HP, 3 = MP |
| Pegar poções do inventário | `Potions.Restock` (`Enabled`, `BelowCount`, `InventoryKey`, `PageKey`) | desligado por padrão; repõe abaixo de 5; V abre, E troca de página |
| Área de busca | `Targeting.RadiusX/RadiusY` | 130x95 px (pode ser maior: o que está fora do alcance só é visto) |
| Alcance do ataque | `Targeting.ReachX/ReachY` | 130x95 px; só clica em mob dentro desta elipse |
| Atacante distante | `Combat.UnderAttack` | ligado; HP −3% em 4 s sem mob perto → busca 450x300 |
| Desistir de um alvo | `Combat.NoProgressSeconds`, `MaxTargetSeconds` | 5 s, 60 s |
| ATK contínuo (clique único) | `Combat.AutoAttack`, `ReclickMs` | desligado; novo clique após 2 s sem dano |
| Anti-andada | `Safety.StopWalking`, `Vision.Motion` | ligado; 3 quadros seguidos com ≥2,5 px |
| Skill do botão direito | `Combat.RightSkill` (`Enabled`, `MinMpPercent`, `HoldMaxMs`, `RetryMs`) | ligada, sempre que pronta |
| Descanso sem poção | `Combat.Rest` | HP < 40 → 80, MP < 10 → 50, máx 60 s |
| Hotkeys | `Hotkeys` | F12, Ctrl+F12, F11 (captura) |
| Tempos de input | `Input` | tecla segura 60 ms, 120 ms entre ações |

**Sugestão:** o ataque do mago gasta mana, e com a mana muito baixa ele pode virar um ataque corpo a corpo (que faz o personagem andar). Vale considerar `Potions.Mp.BelowPercent` em ~25.

As seções `Vision` (posições e cores) e `Targeting.Exclusions` (áreas proibidas para o mouse) só precisam mudar se a interface do jogo mudar.

---

## Logs, gravações e diagnóstico

- **Logs:** `logs/botpriston-AAAAMMDD.log`, sempre com todos os detalhes. Se um arquivo estiver em uso por outro processo, o bot cria `_001`, `_002`...
- **Gravação:** `run --record` salva cada quadro em JPEG com estado, alvo, botões segurados e posição do mouse desenhados. O nome do arquivo tem o horário, para cruzar com o log. Ocupa ~3 MB/s.
- **Prints e calibrações:** `samples/` (prints soltos, `probe_*`, `record_*`). As pastas `probe_*` e `record_*` podem ser apagadas quando não forem mais úteis; os prints soltos citados em `samples/labels.json` são usados pelos testes e devem ficar.

## Testes

```bash
dotnet test
```

São 192 testes, todos sem precisar do jogo:
- **Visão com prints reais:** `samples/labels.json` lista prints do jogo com o resultado esperado: interface visível, HP/MP/STM, estado e HP do alvo, cor da pedra do cursor, estado das skills, número de poções na barra e inventário aberto/página. Para cobrir uma situação nova, salve o print, acrescente uma entrada e rode os testes.
- **Inventário com prints reais:** números das pilhas, células vazias e a poção da barra achada no lugar certo (e a de outro tipo não).
- **Comportamento com jogo simulado:** relógio, janela, input e busca falsos testam o runner, as poções, a máquina de estados, os watchdogs e a pausa. A reposição roda contra um inventário simulado, em que V abre, E troca de página e Shift+tecla soma a pilha.

---

## Estrutura do projeto

| Pasta | Conteúdo |
|---|---|
| `src/BotPriston.Core` | Tudo o que não depende do Windows: config, visão (detectores), busca de alvo, poções, máquina de estados, runner. Testável offline. |
| `src/BotPriston.Platform` | Windows: achar a janela, captura (Windows.Graphics.Capture com fallback BitBlt), `SendInput`, hotkeys, privilégio de administrador, `layout`. |
| `src/BotPriston.Hosting` | Monta um bot rodando (`BotSession`): acha o jogo, cria captura/visão/input/cérebro e roda o loop numa thread própria. Usado pelo terminal e pela janela. |
| `src/BotPriston.App` | O executável de terminal `BotPriston.exe` e os comandos. |
| `src/BotPriston.Ui` | A janela `BotPristonUi.exe` (WPF). |
| `tests/BotPriston.Tests` | Testes xUnit. |
| `templates/` | Imagens de referência (letras do menu, botões do inventário). |
| `samples/` | Prints do jogo, gabarito dos testes, calibrações e gravações. |

Pontos de extensão já previstos:
- **Detecção de alvo:** atrás de `ITargetFinder`, para trocar de estratégia.
- **Detectores:** cada um é uma classe que recebe a imagem e devolve um resultado tipado.
- **Rotação de skills:** pode crescer a partir de `Combat.RightSkill`.
- **Perfis por mapa:** o retrato do alvo é sempre igual para o mesmo tipo de mob, e o nome aparece no painel.

---

## O que falta fazer

### Etapa 7 — robustez
- [x] **Sensor de andada:** detecta o personagem andando, solta tudo e registra a causa no log.
- [ ] **Descobrir a causa da andada.** Quando o sensor disparar, o log diz o que o bot estava fazendo; com isso dá para atacar a causa, não só o sintoma. Ainda não houve uma sessão com o sensor ligado.
- [ ] **Retorno ao ponto inicial:** somar os deslocamentos medidos pelo sensor para saber o quanto o personagem se afastou e voltar até lá (andar de volta exige clicar no chão na direção oposta).
- [ ] **Morte:** hoje o bot só **para**. Falta decidir o que fazer (reviver na cidade? avisar?).
- [ ] **Desconexão:** hoje o bot **para** quando a interface some por 20 s. Não há reconexão (e reconectar exigiria digitar senha, o que fica fora do que o bot deve fazer).
- [ ] **Travamento:** o watchdog para o bot após 120 s sem progresso. Falta tentar se recuperar antes de parar (por exemplo, fechar uma janela aberta por engano com Esc).
- [x] **Repor a barra com as poções do inventário** (`Potions.Restock`).
- [ ] **Testar a reposição numa sessão longa no jogo** (só foi testada com prints e com o inventário simulado).
- [ ] **Poção acabou de vez:** hoje o bot avisa e passa a descansar. Falta parar ou avisar com mais destaque quando as poções de HP acabarem (na barra e no inventário).

### Etapa 6 — loot
- [ ] Pegar itens do chão. Precisa clicar em cada item; os itens aparecem no chão e mostram o nome ao passar o mouse.

### Fora do escopo por enquanto (o design já deixa espaço)
- [ ] Rotação de skills (mais skills além do botão direito).
- [ ] Voltar à cidade para reabastecer.
- [ ] Perfis por mapa (quais mobs atacar, alcance, poções).
- [x] Interface gráfica, primeira versão (V2): ajustes do dia a dia, Iniciar/Pausar/Parar, números e log.
- [ ] Interface: prévia ao vivo do que o bot enxerga (o overlay do `debug`) dentro da janela.
- [ ] Perfis por resolução (ex.: 1280x720), para o jogo ocupar menos espaço.

### Melhorias menores
- [ ] Busca mais rápida: o bot espera 80 ms em cada ponto, mas o cursor reage em ~11 ms (`Targeting.HoverDelayMs` pode cair para ~30).
- [ ] Os prints de `samples/` são grandes (~2,4 MB cada). Se o projeto for para o git, usar Git LFS ou mover as gravações para fora.
- [ ] O projeto ainda não está em um repositório git.

---

## Lições aprendidas (armadilhas do jogo)

Para quem for mexer no código:

1. **O painel do alvo não indica o que está sob o cursor.** Ele continua mostrando o último mob por alguns segundos. Num teste, isso fez a busca "achar" mob em todos os pontos de uma tela vazia. O sinal certo é a cor da pedra do cursor.
2. **Snap do Windows redimensiona o jogo** e quebra todas as posições. Por isso o bot se recusa a agir fora de 1600x900, e existe o comando `layout`.
3. **Jogo como administrador:** o bot também precisa estar, senão o Windows descarta o input sem dar nenhum erro.
4. **Clique curto no botão direito é ignorado** quando chega no meio do golpe do ataque básico. Segurar até a esfera ficar cinza resolve (o bot leva ~1 s).
5. **Botão segurado sobre o chão faz o personagem andar**, e a câmera vai junto. O bot nunca segura botão fora de um mob, força os botões para cima antes de cada busca e tem um sensor que corta qualquer andada.
6. **O descanso não pode competir com a poção:** ele só entra para a barra cuja poção está desligada ou acabou.
7. **Alguns mobs atacam de longe** (Cão Abelha manda abelhas). Com a busca só perto do personagem, o bot apanhava 2 minutos sem alvo e o watchdog o parava, o que parecia morte. A busca ampliada "sob ataque" resolve sem abrir mão de ficar parado no resto do tempo.
8. **Shift + tecla só soma a mesma poção**, não troca uma pela outra. E poções parecidas são itens diferentes: a mana redonda da barra não é a mana em estrela do inventário. Por isso o bot procura pelo ícone exato do slot, não por "qualquer poção azul".
9. **Tooltips cobrem os tubos:** com o mouse sobre uma skill ou uma poção da barra, a caixa de informação cobre o topo dos tubos e a leitura de HP/MP cai. O mouse do bot nunca para ali.
10. **Clicar num mob fora do alcance faz o personagem andar até ele**, mesmo com um clique só. Na tela, o alcance é bem menor para cima e para baixo do que para os lados (a câmera olha de cima, inclinada): um mob a 104 px para cima fez andar, a 130 px para o lado não. Por isso a busca e o alcance são separados. Depois de cada clique de ataque o bot mede se o chão deslizou e registra no log (`made the character step`) onde estava o mob, para calibrar o alcance.
