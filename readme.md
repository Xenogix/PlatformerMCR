# Rapport d'implémentation - Pattern Commande

**MCR 2025-2026**

## 1. Contexte et présentation du projet

> _Décrire le jeu : le concept (plateforme + rewind temporel + clones qui rejouent les
> actions passées), le but du joueur, et en quoi cette idée met en valeur le pattern Command._

- Concept et boucle de jeu
- Captures d'écran / aperçu
- Lien avec le modèle choisi

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

> _Tout ce qu'il faut pour compiler / lancer l'application._

- Prérequis (version d'Unity, paquets)
- Cloner et ouvrir le projet
- Générer un build / lancer dans l'éditeur

## 6. Utilisation

> _Comment jouer / utiliser l'application._

- Contrôles
- Déroulement d'un niveau (jouer, rembobiner, rejouer avec le clone)

## 7. Conclusion

> _Bilan : apports du pattern, limites rencontrées, pistes d'amélioration._

## Annexes

- Membres du groupe et répartition du travail
- Références