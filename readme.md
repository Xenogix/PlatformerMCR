# Rapport d'implémentation - Pattern Commande

**MCR 2025-2026**

## 1. Contexte et présentation du projet

Notre jeu propose de traverser des niveaux comme dans un Plateformer classique, avec un twist ; le joueur peut remonter dans le temps à volonté, laissant derrière lui un écho qui tente d'imiter ses actions. Le joueur doit donc composer avec plusieurs timelines pour résoudre de petites énigmes, et le chaos d'une multitude de clones se marchant dessus...

Les niveaux défilent en boucle, le joueur entrant en compétition avec l'ombre de sa meilleure performance enregistrée dès qu'il revisite un niveau. Il peut optimiser le temps réel de traversée, mais aussi le temps subjectif de ses clones.

Le pattern Commande est particulièrement utile pour enregistrer les actions du joueur 
 et les rejouer sur ses clones. 

- Captures d'écran / aperçu

## 2. Le modèle Command

> _Présentation théorique synthétique du pattern : intention, problème résolu, participants
> (Command, ConcreteCommand, Invoker, Receiver, Client)._

- Intention et motivation
- Structure générale (UML du pattern « pur »)
- Avantages / inconvénients

## 3. Mise en oeuvre du modèle dans l'application

> _Coeur du rapport : comment Command est concrètement implémenté dans le projet et
> pourquoi il est ici pertinent (enregistrement des entrées, rewind, rejeu sur les clones)._

- Correspondance participants du pattern <-> classes du projet
  - `ICommand`, `JumpCommand`, `MoveCommand`, `UseCommand` - les commandes
  - `PlayerCommandInvoker` - l'invocateur
  - `CommandTimeline` / `TickRecord` - l'historique des commandes
  - `ClonePlayback` - le rejeu des commandes par les clones
  - `Player` / `PlayerController` - le receiver
- Cycle d'une commande (capture -> exécution -> enregistrement -> rejeu)
- Choix de conception et alternatives écartées

## 4. Diagramme de classes

> _Diagramme de classes de l'implémentation (au minimum les classes liées au pattern)._

### 4.1 Aperçu global
![Aperçu global](rapport/diagrams/system-overview.svg)

### 4.2 Diagramme d'implémentation du pattern Command
![Implémentaiton du pattern Command](rapport/diagrams/command.png)

### 4.3 Diagramme d'implémentation du pattern Memento
![Implémentation du pattern Memento](rapport/diagrams/memento.svg)

## 5. Déploiement

### 5.1 Jouer à une version précompilée

Le moyen le plus simple d'essayer le jeu est de télécharger une version précompilée. Des binaires pour Windows et Linux (x86-64) sont disponibles dans la section [releases](https://github.com/Xenogix/PlatformerMCR/releases) du dépôt. Il suffit de télécharger l'archive correspondant à votre plateforme, de l'extraire, puis de lancer l'exécutable.

### 5.2 Ouvrir le projet pour le développement

Pour développer ou compiler le projet vous-même :

1. **Cloner le dépôt** sur votre machine.
2. **Installer [Unity Hub](https://docs.unity.com/en-us/hub/install-hub)**, qui gère les versions de l'éditeur Unity.
3. **Ouvrir le dossier racine du projet** depuis Unity Hub. Celui-ci détectera la version de Unity requise et vous proposera de l'installer automatiquement. Ce projet utilise **Unity 6**, version `6000.4.6f1`.

### 5.3 Compiler le projet

Une fois le projet ouvert dans l'éditeur Unity :

1. Allez dans `File` -> `Build and Run`.
2. Sélectionnez la plateforme cible (les plateformes disponibles dépendent des modules installés avec Unity).
3. Choisissez le dossier de destination de la compilation. Nous recommandons un dossier `Builds/` à la racine du projet. Ce dossier est déjà ignoré par git.

## 6. Utilisation

> _Comment jouer / utiliser l'application._

- Contrôles
- Déroulement d'un niveau (jouer, rembobiner, rejouer avec le clone)

## 7. Conclusion

> _Bilan : apports du pattern, limites rencontrées, pistes d'amélioration._

## Annexes

- Membres du groupe et répartition du travail
- Références