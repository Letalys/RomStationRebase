# Changelog

Toutes les évolutions notables de **RomStation Rebase** sont documentées dans ce fichier.

**[English](CHANGELOG.md) · [Français](CHANGELOG.fr.md)**

Le format suit la convention [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/),  
et ce projet respecte le [versionnage sémantique](https://semver.org/lang/fr/).

---

## [1.3.0] - Non publiée

La release « prête pour la cible » : les fichiers portent le nom qu'attendent les émulateurs, les archives peuvent être extraites pour les systèmes qui l'exigent, et chaque cible reçoit ses jaquettes et le fichier de métadonnées qu'elle sait lire. Validée d'abord sur les consoles Anbernic sous dArkOS / ArkOS.

### Ajouté

- **Extraction des archives** — trois modes dans la fenêtre de rebase : extraire selon l'architecture cible (défaut, par exemple PSP, GameCube, Saturn, Dreamcast sous ArkOS), ne jamais extraire, ou tout extraire sauf les romsets. Quels systèmes exigent l'extraction, lisent les playlists M3U ou gardent leur nom de romset se définit une fois, système par système, dans l'architecture cible. Un fichier extrait unique prend le nom du jeu ; un ensemble bin + cue garde ses noms internes dans un dossier au nom du jeu. Une image disque livrée seule en `.bin`, qu'aucun émulateur CD ne liste, reçoit le `.cue` qui la rend lançable. Taille estimée, barre de progression et temps restant sont fondés sur la taille décompressée
- **Jaquettes** — chaque jaquette est copiée dans le dossier `images` de son système sous `<nom de la ROM>-image.png`, la convention « art local » d'EmulationStation qui fonctionne même sans gamelist. Redimensionnée quand la cible l'impose (firmware Anbernic d'origine)
- **Métadonnées selon la cible** — chaque architecture écrit le fichier que lit son frontend, en français ou en anglais, avec titre, description, année, développeur, éditeur, genres et joueurs : `gamelist.xml` EmulationStation dans chaque dossier système (ArkOS, dArkOS, RetroPie, Batocera, Knulli), `gamelist.xml` ES-DE dans `ES-DE/gamelists` avec les jaquettes dans `ES-DE/downloaded_media` (ES-DE ignore un gamelist posé parmi les ROMs ; c'est aussi ce qu'importe Cocoon), `miyoogamelist.xml` pour Onion et Spruce, `metadata.pegasus.txt` pour Pegasus, ou `.dat` Logiqx pour Daijisho et les gestionnaires de ROMs. Un fichier existant est toujours fusionné : favoris, compteurs de parties, jeux masqués et entrées d'un autre outil sont préservés ; un fichier illisible est sauvegardé avant d'être recréé
- **Une architecture par défaut par cible réelle** — ArkOS / dArkOS (sélectionnée par défaut, limitée aux systèmes pris en charge par dArkOS), RetroArch / Lakka, Batocera / Knulli, EmulationStation / RetroPie, ES-DE, Cocoon (Android), Onion / Miyoo Mini et firmware Anbernic d'origine, chacune avec ses noms de dossiers, ses règles par système, son fichier de métadonnées et l'emplacement de ses jaquettes
- **Colonne « Sortie »** dans la fenêtre de rebase, qui annonce ce qui sera produit pour chaque jeu (copie, romset, disques + M3U, versions, extraction, dossier du jeu), avec la liste des fichiers à écrire au survol
- **Éditeur d'architectures cibles** — depuis les Paramètres ou la fenêtre de rebase, modifier n'importe quelle architecture sans toucher aux fichiers JSON : libellé, défauts de sortie, dossier et taille des jaquettes (avec le marqueur `{system}` pour sortir de l'arborescence des ROMs), fichier de métadonnées, et la table des systèmes (dossier, nom d'origine conservé, M3U, extraction). Toutes les architectures vivent dans un seul dossier de vos données utilisateur, où celles par défaut sont copiées au premier lancement : ajouter ou dupliquer une architecture pour créer la sienne, supprimer celles inutilisées, défauts compris, et retrouver à tout moment les fichiers par défaut d'origine avec « Restaurer les architectures par défaut »
- **Retirer un jeu de la liste du rebase** avec le bouton ✕ de sa ligne ; le jeu est décoché dans la bibliothèque en même temps
- **Badge de sélection** à côté du titre de la bibliothèque : nombre de jeux cochés pour le prochain rebase, y compris ceux masqués par les filtres en cours, avec une ✕ pour vider toute la sélection. Chaque système de la sidebar affiche son propre nombre de jeux cochés
- **Règles par jeu dans le tableau du rebase** — trois colonnes, Romset, M3U et Extraction, pré-remplies depuis l'architecture cible pour le système de chaque jeu et basculables jeu par jeu pour le rebase en cours, sans toucher à l'architecture
- **Sauvegarde optionnelle du fichier de métadonnées existant**, par exemple en `gamelist.xml.aaaammjj`, avant la fusion
- **Présélections** — une présélection (fichier `.rsr`) retient les jeux cochés, tous les paramètres du rebase et les règles basculées jeu par jeu, pour retrouver une configuration de travail sans tout recocher. Une zone « Présélection », à droite du logo, dit laquelle est chargée et réunit ses actions : `Ouvrir…` (Ctrl+O), toujours visible, coche les jeux et retient les paramètres, que « Rebase vers… » reprendra ; `Enregistrer` (Ctrl+S) est un bouton scindé, avec « Enregistrer sous… » (Ctrl+Maj+S) sous sa flèche ; `Fermer` décoche les jeux de la présélection. Dans la fenêtre de rebase, « Enregistrer cette configuration » se tient à côté de « Démarrer », et la barre de titre porte le nom de la présélection, suivi d'un point tant que des modifications restent à enregistrer. L'application propose d'enregistrer avant de quitter, d'ouvrir une autre présélection ou de la fermer, jamais ailleurs. Présélection chargée, ses paramètres lui appartiennent et ne remplacent plus les derniers paramètres utilisés sans elle. À l'ouverture, un seul dialogue annonce les écarts avant de rien modifier, avec la possibilité de renoncer : jeux absents de la bibliothèque, jeux retrouvés par leur titre, architecture cible disparue, dossier de destination introuvable, fichier d'une version plus récente. Les fichiers `.rsr` peuvent être associés à RomStation Rebase depuis les Paramètres (pour le compte Windows courant, sans droits administrateur) : un double-clic dans l'Explorateur ouvre alors la présélection, y compris quand l'application tourne déjà
- **Projet de tests unitaires** couvrant les règles de nommage, chaque format de métadonnées avec sa fusion et sa sauvegarde, les présélections, l'extraction et le redimensionnement des jaquettes

### Corrigé

- **Les romsets arcade étaient renommés** (`mslug.zip` devenait `Metal Slug.zip`), ce que FBNeo et MAME refusent de charger. Les archives Neo-Geo, Arcade, Naomi, Atomiswave, Model 2 et Model 3 gardent désormais leur nom d'origine
- **Plusieurs fichiers ne voulaient pas toujours dire plusieurs disques** — les versions régionales ou révisions d'un même jeu étaient numérotées comme des disques et réunies dans un M3U. Les disques sont désormais reconnus d'après le libellé du fichier dans RomStation ; les versions sont nommées d'après ce libellé et jamais regroupées
- **Deux jeux au même titre sur le même système** s'écrasaient. Ils sont désormais distingués par leur libellé RomStation, ou par leur identifiant RomStation quand les libellés sont identiques
- **Les playlists M3U** ne sont plus générées que pour les systèmes dont l'émulateur les lit sur la cible choisie
- **Les fenêtres maximisées recouvraient la barre des tâches** — la fenêtre principale et celle du rebase s'arrêtent désormais au bord de la zone de travail
- **Défilement de la bibliothèque** — les jaquettes sont décodées à leur taille d'affichage plutôt qu'en pleine taille, les cartes sont préparées une page à l'avance, et un cran de molette fait défiler exactement une ligne de cartes, alignée sur la grille
- **Masquer un système dans la sidebar ne décoche plus ses jeux** — les filtres ne changent que l'affichage, la sélection est conservée et reste visible dans les badges

### Modifié

- **Fenêtre de rebase** réorganisée en trois groupes d'options (Fichiers, Métadonnées, Copie), tous mémorisés entre les sessions
- **Le nommage** des jeux à plusieurs fichiers, des romsets arcade et des homonymes a changé : les copies faites par une version précédente ne seront pas reconnues comme doublons et seront recopiées. Les jeux à fichier unique gardent exactement leur nom précédent
- Les fichiers d'architecture décrivent désormais, par système, si le nom de l'archive doit être conservé, si le M3U est pris en charge et si l'extraction est requise

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