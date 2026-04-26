# Changelog

Toutes les évolutions notables de **RomStation Rebase** sont documentées dans ce fichier.

**[English](CHANGELOG.md) · [Français](CHANGELOG.fr.md)**

Le format suit la convention [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/),  
et ce projet respecte le [versionnage sémantique](https://semver.org/lang/fr/).

---

## [1.2.0] - 2026-04-26

Release de polish et de robustesse, avec l'arrivée de la vérification automatique des mises à jour, du verrouillage à instance unique, et d'une refonte visuelle de la fenêtre Paramètres alignée sur le reste de l'application.

### Ajouté

- **Vérification automatique des mises à jour** — au démarrage, l'application vérifie en arrière-plan si une nouvelle version est disponible sur GitHub. Quand une mise à jour existe, un lien cliquable apparaît dans la barre de statut bas et dans le panneau Paramètres, pour ouvrir directement la page de la dernière release. Bouton "Vérifier les mises à jour" disponible dans Paramètres pour un check manuel à tout moment, avec affichage de la date de dernière vérification
- **Instance unique** — au lancement d'une seconde instance, la première fenêtre déjà ouverte est restaurée et passe au premier plan, au lieu de démarrer un nouvel exécutable. Évite les doublons accidentels et les conflits sur la base RomStation
- **Lien vers le wiki** — bouton "Ouvrir la documentation (wiki)" dans le panneau Paramètres, qui ouvre directement la documentation utilisateur dans le navigateur

### Corrigé

- **Icône de la barre des tâches** floue ou par défaut sur les affichages haute densité (HiDPI) — toutes les fenêtres pointent désormais vers l'icône multi-résolution, pour un rendu net à toutes les tailles d'affichage
- **Chemin de destination du rebase non mémorisé** si la fenêtre était fermée sans lancer le rebase — le dossier sélectionné est maintenant sauvegardé à la fermeture, peu importe que le rebase ait été lancé ou non
- **Fenêtre Paramètres** non redimensionnable et présentant une bordure parasite — alignée sur le pattern visuel des autres fenêtres de l'application, avec redimensionnement opérationnel et grip de redimensionnement visible
- **Bannière "Mise à jour disponible"** affichée à tort dans certains cas où la version courante de l'application avait dépassé la version persistée par un check antérieur — la comparaison de versions est désormais cohérente entre le check réseau et le rechargement depuis l'état persisté

### Modifié

- **Lien "Mise à jour disponible"** revu visuellement, avec une affordance hover/pressed cohérente entre le panneau Paramètres et la barre de statut bas. Libellé identique aux deux endroits
- **Préservation des préférences utilisateur** renforcée — en cas d'erreur de lecture transitoire du fichier de préférences (verrouillage par antivirus, problème I/O), les préférences existantes ne sont plus écrasées par un fichier vierge

---

## [1.1.0] - 2026-04-24

Refonte majeure de l'interface avec l'arrivée du thème sombre, d'une fenêtre dédiée aux détails du jeu, et d'une barre latérale plus propre. L'accent est mis sur le polish et l'ergonomie au quotidien, à partir d'un usage réel sur une bibliothèque conséquente.

### Ajouté

- **Thème sombre** avec bascule à chaud depuis les Paramètres (pas de redémarrage requis). Toutes les fenêtres, boîtes de dialogue et contrôles s'adaptent au thème choisi
- **Fenêtre de détail d'un jeu** : vue dédiée affichant la jaquette, le système, l'année, le développeur, l'éditeur, le nombre de joueurs, les genres, les langues disponibles (avec drapeaux) et la description complète. S'ouvre via une nouvelle affordance en forme d'œil au survol en mode mosaïque et liste, ou par double-clic sur une tuile ou une ligne. Inclut des boutons pour ouvrir le dossier du jeu dans l'Explorateur et pour voir la fiche sur le site RomStation
- **Icônes des systèmes** dans le panneau de filtres latéral, à côté de chaque nom de console, pour une identification visuelle plus rapide
- **Rail de navigation alphabétique** sur le bord droit de la fenêtre principale, pour sauter directement aux jeux commençant par une lettre donnée
- **Sélecteur de taille des vignettes** (Normal / Grand) dans la barre d'outils
- **Tri global** par Titre ou par Système en mode mosaïque, avec préférence mémorisée entre les sessions
- **Confirmation avant synchronisation** de la base RomStation, avec un rappel qu'il faut fermer RomStation au préalable (la base n'accepte qu'une seule connexion à la fois)
- **Masquage des consoles vides** dans la barre latérale (activé par défaut) pour alléger le panneau de filtres quand la bibliothèque ne couvre que quelques systèmes
- **Tooltip** sur les titres tronqués en mode mosaïque, affichant le titre complet au survol
- **Icônes système de secours** pour les consoles dont l'icône RomStation n'était pas utilisable (Windows et MacOS, dont les icônes d'origine étaient blanches sur transparent et invisibles en thème clair)

### Corrigé

- Crash au démarrage quand la base Derby de RomStation n'était pas encore initialisée
- Crash à l'ouverture de la fenêtre de rebase quand un lecteur cible invalide était encore mémorisé d'une session précédente
- Bouton "Ouvrir le dossier" qui ne faisait rien silencieusement dans certains cas limites
- Texte du tableau dans la fenêtre de rebase illisible en thème sombre (noir sur fond sombre) à cause d'une couleur système héritée par défaut
- Bordures des boutons secondaires à peine visibles sur le fond du thème clair

### Modifié

- Filtre "Problèmes uniquement" déplacé du bas de la barre latérale vers la ligne de filtres principale, à côté du compteur "Tous les jeux". Le bouton est désormais automatiquement masqué quand la bibliothèque ne contient aucun jeu problématique, évitant une option morte
- Les bascules "Problèmes uniquement" et "Masquer les consoles vides" sont maintenant mémorisées entre les sessions
- Tri par Fichiers (nombre total de fichiers) retiré — non actionnable du point de vue utilisateur, remplacé par le tri global
- Passe de polish mineur sur le rendu des icônes dans la barre latérale et sur l'espacement du mode mosaïque

---

## [1.0.0] - 2026-04-19

Première version publique.

### Ajouté

- Workflow complet de rebase : sélection des jeux depuis la bibliothèque RomStation, choix d'un dossier cible, copie des ROMs dans la structure conventionnelle attendue par RetroArch, Lakka ou les consoles portables Anbernic
- Deux modes d'affichage : mosaïque (jaquettes) et liste détaillée, avec défilement virtualisé pour les grandes bibliothèques
- Filtrage par système avec compteur de jeux en temps réel
- Moteur de copie parallélisé avec niveau de concurrence configurable et réessais automatiques en cas d'échec transitoire
- Politique de gestion des doublons (ignorer ou écraser)
- Détection intelligente de RomStation : automatique via le registre Windows, avec sélection manuelle du dossier en repli pour les installations ZIP portables
- Mémorisation des préférences utilisateur : dernier dossier cible, architecture cible, paramètres de copie, positions des fenêtres, mode d'affichage et langue de l'interface
- Runtime .NET 10 embarqué (MSI et ZIP portable self-contained, aucune installation externe nécessaire)
- Interface localisée (français et anglais avec détection automatique)
- Rapport d'exécution avec suivi en temps réel par jeu et export du journal
- Panneau Paramètres avec langue, thème (clair uniquement), raccourcis de dossiers et informations projet

### Limitations connues

- Seul le thème **clair** est disponible (thème sombre prévu pour une version ultérieure)
- Vérification automatique des mises à jour pas encore implémentée