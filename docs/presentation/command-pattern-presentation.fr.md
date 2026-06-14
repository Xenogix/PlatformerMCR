# Le patron Command dans PlatformerMCR

Support de présentation — comment nous avons utilisé le patron, les choix effectués,
les forces exploitées, ce que nous avons laissé de côté, et comment nous l'avons adapté.

Diagrammes : `uml.sly` (diagramme de classes, édité dans Slyum → `command.pdf`) et
`command-pattern-sequence.fr.puml` (enregistrement → découpe → rejeu).
Régénérer la séquence avec `plantuml -tsvg command-pattern-sequence.fr.puml`.

---

## Slide 1 — Pourquoi Command ? La mécanique de jeu qui l'exige

La mécanique centrale de PlatformerMCR : ouvrir une timeline, remonter à un tick passé,
et **détacher un clone (« écho »)** qui rejoue exactement ce que vous avez fait, pendant
que vous continuez à jouer. Coopérer avec son propre passé (tenir un levier, lester une
plaque...).

L'exigence qui détermine tout : *ce que le joueur a fait* doit exister sous forme de
**données** — stockables, découpables, ré-exécutables sur un autre corps. C'est
précisément le rôle du patron Command : **transformer chaque action en objet**.

> Note orateur : partir du clip de gameplay, puis demander « que doit retenir le moteur
> pour que ça marche ? » — la réponse (les entrées, sous forme d'objets) introduit le patron.

---

## Slide 2 — Rappel théorique (GoF)

> « Encapsuler une requête sous forme d'objet, permettant ainsi de paramétrer les
> clients avec différentes requêtes, de mettre les requêtes en file d'attente ou de les
> journaliser, et de supporter des opérations annulables. »

Rôles canoniques : **Command** (interface avec `Execute()`), **ConcreteCommand** (lie un
*Receiver* + des arguments), **Invoker** (déclenche les commandes), **Receiver** (fait le
travail), **Client** (crée et configure les commandes).

Motivations classiques : éléments de menu/boutons, piles undo/redo, files de requêtes,
macro-commandes.

---

## Slide 3 — Correspondance des rôles : GoF vs notre implémentation

| Rôle GoF | Forme canonique | PlatformerMCR |
|---|---|---|
| Command | `Execute()` | `ICommand.Execute(Player target)` — le récepteur est un **paramètre** |
| ConcreteCommand | lie récepteur + arguments | `MoveCommand(dir)`, `JumpCommand`, `JumpHeldCommand(held)`, `UseCommand` — **arguments seuls, pas de récepteur** |
| Invoker | un seul (bouton, menu) | **Deux** : `PlayerCommandInvoker` (jeu en direct) et `ClonePlayback` (rejeu) |
| Receiver | n'importe quel objet | façade `Player` → délègue à `PlayerController` / `InteractionDetector` |
| Client | câble les commandes | `PlayerCommandInvoker` crée depuis l'input ; `RewindDirector` recible l'enregistrement vers un clone |
| Liste d'historique | pile d'annulation | `CommandTimeline` — un **journal de rejeu**, pas une pile d'undo |

Tout le code des commandes tient dans `Assets/Scripts/Commands/` (~7 petits fichiers).

---

## Slide 4 — Adaptation n°1 (la plus importante) : récepteur passé à `Execute`, pas stocké

```csharp
public interface ICommand
{
    void Execute(Player target);
}
```

Le GoF stocke le récepteur dans la ConcreteCommand. Nous le passons en paramètre.
Conséquence : **un enregistrement, plusieurs cibles** — la *même instance de commande*
est exécutée sur le joueur en direct pendant la partie, puis ré-exécutée sur un clone
pendant le rejeu. Pas de copie, pas de re-liaison, pas de couche de traduction.

C'est la variante que Nystrom recommande dans *Game Programming Patterns* (chapitre
Command) pour exactement ce cas d'usage : « pass in the actor » → le même flux d'entrées
peut piloter le joueur, une IA, ou un fantôme de rejeu.

---

## Slide 5 — Déroulement d'un tick en direct (invocateur)

`PlayerCommandInvoker.Tick(tick, dt)` — piloté par `GameClock` à ticks fixes :

```csharp
List<ICommand> changed = null;
if (!hasRecorded || move != lastMove)
    (changed ??= new List<ICommand>()).Add(new MoveCommand(move));
if (!hasRecorded || jumpHeld != lastJumpHeld)
    (changed ??= new List<ICommand>()).Add(new JumpHeldCommand(jumpHeld));
if (jumpPressedThisTick) (changed ??= new List<ICommand>()).Add(new JumpCommand());
if (usePressedThisTick)  (changed ??= new List<ICommand>()).Add(new UseCommand());

if (changed != null)
    foreach (ICommand cmd in changed) cmd.Execute(player); // 1) piloter le joueur
controller.Tick(tick, dt);                                 // 2) avancer la physique
Timeline.Record(tick, changed);                            // 3) enregistrer
```

Détail clé : les appuis discrets sont **verrouillés** dans les callbacks d'input
(`jumpPressedThisTick`) et consommés au tick fixe suivant — l'enregistrement est donc
*exactement* ce qui a été exécuté, même pour un appui tombé entre deux ticks.

---

## Slide 6 — Adaptation n°2 : commandes persistantes vs discrètes + journal creux

```csharp
public interface IStickyCommand : ICommand { }   // interface marqueur
```

- **Persistantes / « sticky »** (`MoveCommand`, `JumpHeldCommand`) : l'effet dure
  jusqu'au prochain changement → enregistrées **uniquement au changement** ; entre
  deux, le contrôleur reconduit l'état.
- **Discrètes** (`JumpCommand`, `UseCommand`) : one-shot → enregistrées à chaque occurrence.

`CommandTimeline` ne stocke donc un `TickRecord {Tick, List<ICommand>}` qu'aux *ticks de
changement* (journal creux), adressé par **tick absolu** via recherche dichotomique —
jamais par position dans la liste. La plupart des ticks ne stockent rien : les
allocations par tick sont quasi nulles alors même que chaque action est un objet alloué
sur le tas.

Un journal naïf « une commande par tick et par entrée » à 50 ticks/s allouerait des
milliers d'objets par minute de jeu, par timeline. Le journal creux est ce qui a rendu
le patron viable.

---

## Slide 7 — Adaptation n°3 : découper l'historique au point de rewind

À la création d'un clone au tick T (`RewindDirector`) :

```csharp
CommandTimeline echoScript = livePlayer.Timeline.SliceFromTick(target);
livePlayer.Timeline.TruncateAfterTick(target - 1);
```

`SliceFromTick(T)` remet au clone une copie figée `[T, fin]` — **avec la dernière
commande persistante de chaque type ré-établie en T**. Si vous étiez en pleine course au
moment de la découpe, le clone reçoit un `MoveCommand` synthétisé à son premier tick et
reprend en pleine foulée. Le joueur en direct garde `[.., T-1]` et ré-enregistre vers
l'avant.

C'est ici que le journal creux se retourne contre nous : une tranche qui démarre entre
deux ticks de changement commencerait sinon *sans aucun* état de mouvement. L'interface
marqueur « sticky » existe précisément pour résoudre ce problème.

---

## Slide 8 — Le rejeu : le second invocateur

`ClonePlayback.Tick(tick, dt)` — même horloge, même cadence :

```csharp
TickRecord record = timeline.GetAtTick(tick);
if (record != null)
    foreach (ICommand cmd in record.Commands)
        cmd.Execute(player);      // le Player du CLONE — mêmes objets commande
controller.Tick(tick, dt);
```

Le clone est un **`Player` complet** : même `PlayerController`, même physique, même
masse. Le rejeu n'est pas une animation : l'écho refait réellement tourner la
simulation, alimentée par l'intention enregistrée. Quand `tick > timeline.LastTick`,
l'écho se retire de lui-même.

Prérequis de déterminisme (que Command ne fournit pas tout seul) :
- une `GameClock` à ticks fixes (commandes estampillées d'un numéro de tick absolu) ;
- un ordre de tick strict : les observateurs d'état capturent **avant** que les acteurs agissent ;
- un instantané d'état complet restauré au point de découpe (cf. slide 10).

---

## Slide 9 — Les forces du patron réellement exploitées

1. **Les actions deviennent des objets** — on peut les stocker, les adresser
   par tick, les découper, les tronquer, les confier à un autre corps. Toute la
   mécanique d'écho, c'est « la liste d'historique du GoF, pointée vers un autre
   récepteur ».
2. **Découplage entrée / action** — l'invocateur connaît l'input, les récepteurs
   connaissent la physique et les interactions ; aucun ne connaît les détails de
   l'autre. Le rejeu n'a demandé *aucune* modification des récepteurs.
3. **Un seul chemin de code, direct et rejoué** — le même `Execute` tourne dans les
   deux modes : les bugs de divergence direct/rejeu sont structurellement impossibles
   au niveau des commandes.
4. **Ouvert/fermé en pratique** — l'action `Use` a été ajoutée *après* le système
   d'enregistrement : une nouvelle classe de 15 lignes + 2 lignes dans l'invocateur.
   Leviers et portes ont ensuite fonctionné dans les rejeux *gratuitement* (git :
   `feat(player): tick-driven command system` → `feat(interactables): port lever/door/Use system`).
5. **Le journal comme outil de debug** — la timeline est aussi une trace d'entrées
   parfaite d'une session de jeu.

---

## Slide 10 — Ce que nous n'avons PAS utilisé, et les limites du patron

**Non utilisé (délibérément) :**
- **`Undo()` sur les commandes.** Le rewind n'est pas un undo de commandes : la
  physique n'est pas inversible (on ne peut pas « dé-exécuter » un saut dans une
  simulation dynamique — frottements, collisions et intégration perdent de
  l'information). L'annulation est déléguée à un système **Memento** :
  `RewindCaretaker` + `RewindChannel<T>` par entité prennent des instantanés d'état à
  cadence fixe, et rebobiner = restaurer un instantané + jeter l'historique ultérieur.
  Command journalise *l'intention vers l'avant* ; Memento restaure *l'état vers
  l'arrière*. Chaque patron fait la moitié pour laquelle il est doué.
- **Macro-commandes / composites** — un tick multi-actions n'est qu'une `List<ICommand>`.
- **File d'attente / exécution différée** — les commandes s'exécutent au tick même de
  leur création ; le bénéfice « queue » du GoF est inutilisé.
- **Persistance** — les timelines vivent en mémoire, par essai ; pas de sérialisation
  (les commandes étant des données pures, ce serait pourtant trivial — extension naturelle).
- **Validation dans les commandes** — les commandes sont inconditionnelles ; les
  préconditions (au sol, coyote time...) vivent dans le récepteur. Les commandes
  restent des données bêtes.

**Limite intrinsèque rencontrée :** un journal de commandes rejoue *l'intention*, pas
*le résultat*. Si le monde diverge (vous poussez une caisse sur le chemin de l'écho),
les mêmes commandes produisent des résultats différents.

---

## Slide 11 — Transformer la limite en mécanique de jeu

`UseCommand` est hors contexte — elle n'enregistre pas *quel* levier a été actionné :

```csharp
public void Use() => interactor?.GetClosest()?.Interact();
```

Le clone en rejeu interagit avec **ce qui est le plus proche de sa position à ce
tick-là**. Nous avons choisi de rejouer l'intention plutôt que de figer les résultats,
accepté le risque de divergence, et l'avons contenu par le déterminisme (tick fixe +
restauration d'instantané au point de découpe ⇒ un écho non perturbé rejoue
parfaitement). La divergence résiduelle — le joueur qui interfère avec son propre
écho — *est la mécanique de puzzle*.

> Note orateur : c'est le moment « choix de conception » le plus fort de l'exposé — la
> faiblesse classique du rejeu par commandes, délibérément assumée plutôt que contournée.

---

## Slide 12 — À retenir

- Command a mérité sa place ici : la mécanique *est* le patron (un journal d'actions
  reciblable).
- Nous avons gardé des commandes **minimales** : une interface, quatre petites classes,
  pas de classe de base, pas d'undo, pas de champ récepteur — chaque omission est une
  décision, pas un oubli.
- Les adaptations qui ont compté : **récepteur en paramètre** (un enregistrement, N
  cibles), **séparation persistant/discret + journal creux** (mémoire), **découpe
  adressée par tick** (création de clones).
- Command ne travaille pas seul : **Memento** (instantanés/rewind) et une horloge à
  ticks fixes fournissent le déterminisme et la réversibilité que Command ne peut pas offrir.
- Les patrons sont des menus, pas des contrats : nous avons utilisé « paramétrer les
  clients avec différentes requêtes » et « journaliser les requêtes », et sciemment
  ignoré « file d'attente » et « annulables ».

---

## Annexe — carte des fichiers

| Fichier | Rôle |
|---|---|
| `Assets/Scripts/Commands/ICommand.cs` | `ICommand`, `IStickyCommand` |
| `Assets/Scripts/Commands/{Move,Jump,JumpHeld,Use}Command.cs` | commandes concrètes |
| `Assets/Scripts/Commands/PlayerCommandInvoker.cs` | invocateur direct : input → commandes → exécution + enregistrement |
| `Assets/Scripts/Commands/CommandTimeline.cs` | historique creux ; `Record/GetAtTick/SliceFromTick/TruncateAfterTick` |
| `Assets/Scripts/Commands/TickRecord.cs` | `{Tick, List<ICommand>}` |
| `Assets/Scripts/Commands/ClonePlayback.cs` | invocateur de rejeu sur les échos |
| `Assets/Scripts/Player/Player.cs` | façade récepteur (`Move/Jump/SetJumpHeld/Use`) |
| `Assets/Scripts/Commands/InteractionDetector.cs` | trouve l'`IInteractable` le plus proche pour `Use()` |
| `Assets/Scripts/Rewind/RewindDirector.cs` | découpe de clone : slice + truncate + spawn de l'écho |
| `Assets/Scripts/Rewind/RewindCaretaker.cs`, `Rewind/Channels/*` | le versant Memento (instantanés, rewind) |
| `Assets/Scripts/Time/GameClock.cs` | ticks fixes, ordre observateurs-avant-acteurs |
