# Changelog

Toutes les évolutions notables de **RomStation Rebase** sont documentées dans ce fichier.

**[English](CHANGELOG.md) · [Français](CHANGELOG.fr.md)**

Le format suit la convention [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/),  
et ce projet respecte le [versionnage sémantique](https://semver.org/lang/fr/).

---

## [1.3.0] - Non publiée

La release « prête pour la cible » : les fichiers portent le nom qu'attendent les émulateurs, les archives peuvent être extraites pour les systèmes qui l'exigent, et chaque cible reçoit ses jaquettes et le fichier de métadonnées qu'elle sait lire. Validée sur une Anbernic RG353V sous dArkOS.

Cette version a été co-écrite avec une IA : Claude Code, modèle Claude Fable 5.1 d'Anthropic. Letalys a défini les besoins, arbitré chaque choix et validé le résultat, dans l'application puis sur la console.

### Ajouté

- **Extraction des archives** — trois modes dans la fenêtre de rebase : extraire selon l'architecture cible (défaut, par exemple PSP, Playstation, GameCube, Saturn, Dreamcast), ne jamais extraire, ou tout extraire sauf les romsets. Quels systèmes exigent l'extraction, lisent les playlists M3U ou gardent leur nom de romset se définit une fois, système par système, dans l'architecture cible. Un fichier extrait unique prend le nom du jeu ; un ensemble bin + cue garde ses noms internes dans un dossier au nom du jeu. Une image disque livrée seule en `.bin`, qu'aucun émulateur CD ne liste, reçoit le `.cue` qui la rend lançable. Taille estimée, barre de progression et temps restant sont fondés sur la taille décompressée
- **Jaquettes** — chaque jaquette est copiée dans le dossier `images` de son système sous `<nom de la ROM>-image.png`, la convention « art local » d'EmulationStation qui fonctionne même sans gamelist. Redimensionnée quand la cible l'impose (firmware Anbernic d'origine)
- **Métadonnées selon la cible** — chaque architecture écrit le fichier que lit son frontend, en français ou en anglais, avec titre, description, année, développeur, éditeur, genres et joueurs : `gamelist.xml` EmulationStation dans chaque dossier système (ArkOS, dArkOS, RetroPie, Batocera, Knulli), `gamelist.xml` ES-DE dans `ES-DE/gamelists` avec les jaquettes dans `ES-DE/downloaded_media` (ES-DE ignore un gamelist posé parmi les ROMs ; c'est aussi ce qu'importe Cocoon), `miyoogamelist.xml` pour Onion et Spruce, `metadata.pegasus.txt` pour Pegasus, ou `.dat` Logiqx pour Daijisho et les gestionnaires de ROMs. Un fichier existant est toujours fusionné : favoris, compteurs de parties, jeux masqués et entrées d'un autre outil sont préservés ; un fichier illisible est sauvegardé avant d'être recréé
- **Une architecture par défaut par cible réelle** — ArkOS / dArkOS (sélectionnée par défaut, limitée aux systèmes pris en charge par dArkOS), RetroArch / Lakka, Batocera / Knulli, EmulationStation / RetroPie, ES-DE, Cocoon (Android), Onion / Miyoo Mini et firmware Anbernic d'origine, chacune avec ses noms de dossiers, ses règles par système, son fichier de métadonnées et l'emplacement de ses jaquettes
- **Colonne « Sortie »** dans la fenêtre de rebase, qui annonce ce qui sera produit pour chaque jeu (copie, romset, disques + M3U, versions, extraction, dossier du jeu), avec la liste des fichiers à écrire au survol
- **Éditeur d'architectures cibles** — depuis les Paramètres ou la fenêtre de rebase, modifier n'importe quelle architecture sans toucher aux fichiers JSON : libellé, défauts de sortie, dossier et taille des jaquettes (avec le marqueur `{system}` pour sortir de l'arborescence des ROMs), fichier de métadonnées, et la table des systèmes (dossier, nom d'origine conservé, M3U, extraction). Toutes les architectures vivent dans un seul dossier de vos données utilisateur, où celles par défaut sont copiées au premier lancement : ajouter ou dupliquer une architecture pour créer la sienne, supprimer celles inutilisées, défauts compris, et retrouver à tout moment les fichiers par défaut d'origine avec « Restaurer les architectures par défaut »
- **Retirer un jeu de la liste du rebase** avec le bouton ✕ de sa ligne ; le jeu est décoché dans la bibliothèque en même temps
- **Badge de sélection** à côté du titre de la bibliothèque : nombre de jeux cochés pour le prochain rebase, y compris ceux masqués par les filtres en cours, avec une ✕ pour vider toute la sélection. Chaque système de la sidebar affiche son propre nombre de jeux cochés
- **Règles par jeu dans le tableau du rebase** — trois colonnes, Romset, M3U et Extraction, pré-remplies depuis l'architecture cible pour le système de chaque jeu et basculables jeu par jeu pour le rebase en cours, sans toucher à l'architecture. Chaque interrupteur montre ce qui arrivera réellement au jeu : une règle sans objet pour lui (M3U d'un jeu à un seul disque, extraction d'un romset, mode « Ne jamais extraire ») est grisée et éteinte, avec une infobulle qui dit pourquoi
- **Sauvegarde optionnelle du fichier de métadonnées existant**, par exemple en `gamelist.xml.aaaammjj`, avant la fusion
- **Présélections** — une présélection (fichier `.rsrgp`) retient les jeux cochés, tous les paramètres du rebase et les règles basculées jeu par jeu, pour retrouver une configuration de travail sans tout recocher. Une zone « Présélection », à droite du logo, dit laquelle est chargée et réunit ses actions : `Ouvrir…` (Ctrl+O), toujours visible, coche les jeux et retient les paramètres, que « Rebase vers… » reprendra ; `Enregistrer` (Ctrl+S) est un bouton scindé, avec « Enregistrer sous… » (Ctrl+Maj+S) sous sa flèche ; `Fermer` décoche les jeux de la présélection. Dans la fenêtre de rebase, « Enregistrer cette configuration » se tient à côté de « Démarrer », et la barre de titre porte le nom de la présélection, suivi d'un point tant que des modifications restent à enregistrer. L'application propose d'enregistrer avant de quitter, d'ouvrir une autre présélection ou de la fermer, jamais ailleurs. Présélection chargée, ses paramètres lui appartiennent et ne remplacent plus les derniers paramètres utilisés sans elle. À l'ouverture, un seul dialogue annonce les écarts avant de rien modifier, avec la possibilité de renoncer : jeux absents de la bibliothèque, jeux retrouvés par leur titre, architecture cible disparue, dossier de destination introuvable, fichier d'une version plus récente. Les fichiers `.rsrgp` peuvent être associés à RomStation Rebase (pour le compte Windows courant, sans droits administrateur) : l'application le propose une fois, au premier enregistrement d'une présélection, et le bouton des Paramètres reste disponible. La méthode est la même pour l'installeur MSI et pour la version ZIP. Un double-clic dans l'Explorateur ouvre alors la présélection, y compris quand l'application tourne déjà
- **Une seule entrée par jeu dans EmulationStation** — avec l'option « Dossier masqué pour les jeux à plusieurs fichiers » d'une architecture, activée pour ArkOS / dArkOS, les fichiers d'un jeu à plusieurs disques ou extrait en CUE et BIN vont dans un dossier au nom précédé d'un point, et un M3U à la racine du système lance le jeu. La liste n'affiche plus le M3U à côté de chacun de ses disques, ni un dossier à ouvrir par jeu. Les jeux déjà copiés par une version précédente restent à leur ancien emplacement
- **Conversion par outil externe** — une architecture peut désigner, système par système, un outil qui convertit les fichiers pendant le rebase : GDI ou CUE/BIN vers CHD avec chdman, ISO vers CHD ou CSO pour la PSP, GameCube et Wii vers RVZ avec DolphinTool. RomStation Rebase pilote tout lui-même : l'archive est extraite dans un dossier de travail sur le disque local, l'outil y est lancé (une conversion à la fois, pendant que les copies continuent), et seul le résultat atteint la destination, sous le nom décidé pour le jeu ; M3U, métadonnées et détection des doublons désignent le fichier converti. Le fichier `.sbi` d'un jeu Playstation protégé est copié à côté du fichier converti, sous le même nom. La progression de l'outil alimente la barre et le temps restant, l'annulation l'arrête net et efface le fichier partiel, un outil qui ne répond plus est arrêté de lui-même, et la bibliothèque RomStation n'est jamais modifiée. Souvent, il n'y a rien à télécharger : les émulateurs que RomStation installe contiennent déjà ces outils (chdman dans son MAME, DolphinTool dans son Dolphin), RomStation Rebase les retrouve d'après la base de RomStation, les propose en un clic, et suit l'emplacement quand RomStation met l'émulateur à jour. RomStation Rebase ne distribue aucun de ces programmes et n'en lance jamais un qu'il aurait trouvé seul : la nouvelle fenêtre « Outils externes » (depuis les Paramètres, l'éditeur d'architectures ou la fenêtre de rebase) sert à indiquer l'exécutable de chaque outil, à le tester (présence, version minimale) et à décrire ses propres outils sans script : arguments un par ligne, extensions acceptées, motif de progression. Dans la fenêtre de rebase, un interrupteur général « Convertir avec les outils externes » et une colonne « Conversion » : chaque jeu y affiche l'outil qui le convertira, pré-rempli par l'architecture et modifiable jeu par jeu, parmi les seuls outils dont l'exécutable est indiqué et qui savent lire son contenu (une image `.cdi`, que chdman ne lit pas, reste sur « Aucune »). La conversion se charge elle-même de l'extraction ; la colonne « Sortie » annonce le format produit ; un exécutable manquant est signalé, et les jeux concernés sont simplement copiés sans conversion. Chaque outil a son propre emplacement d'exécutable, même quand plusieurs partagent un programme. Aucune architecture par défaut n'active de conversion : c'est un choix à faire dans l'éditeur, colonne « Conversion » de la table des systèmes
- **Journal détaillé du rebase** — chaque rebase écrit un fichier `RSR_aaaammjj_hhmmss.log` dans le dossier `logs` de vos données utilisateur : toute la configuration utilisée (destination, architecture, options, outils externes et leur exécutable), le plan fichier par fichier, puis chaque étape avec son heure, sa durée et, en cas d'échec, le message d'erreur complet. Pour une conversion, le journal garde la ligne de commande et ce que l'outil a écrit. Le bouton « Afficher le journal » de la fenêtre de rebase ouvre ce fichier dans une fenêtre à part, indépendante de celle du rebase : les lignes arrivent en direct, les avertissements et les erreurs sont colorés, un filtre et un interrupteur « Avertissements et erreurs uniquement » réduisent l'affichage, « Suivre » garde la dernière ligne à l'écran, et « Ouvrir… » relit un journal plus ancien. Les trente derniers journaux sont conservés. Le fichier est un texte à tabulations, lisible aussi dans un outil comme CMTrace
- **Icône de la console dans l'éditeur d'architectures** — chaque ligne de la table des systèmes montre l'icône de sa console, comme la barre latérale de la bibliothèque
- **Tri de la table des systèmes** — un clic sur l'en-tête « Système RomStation » de l'éditeur d'architectures trie par nom, un deuxième inverse l'ordre, un troisième revient à l'ordre du fichier. Le tri ne change que l'affichage, pas le fichier de l'architecture
- **Codes d'erreur** — chaque message d'erreur se termine par un code, comme `[RSR-3004]`, dans les dialogues, la colonne Erreur du tableau et le journal du rebase. Le premier chiffre donne la famille (démarrage, contrôles avant le rebase, copie, conversion, métadonnées, présélections, architectures, erreurs inattendues). La page « Codes d'erreur » du wiki dit quoi faire pour chacun, et le code suffit à situer un problème dans une issue
- **Icône des présélections** — une fois associés, les fichiers `.rsrgp` (*RomStation Rebase Game Preset*) portent leur propre icône dans l'Explorateur
- **Projet de tests unitaires** couvrant les règles de nommage, chaque format de métadonnées avec sa fusion et sa sauvegarde, les présélections, l'extraction, le redimensionnement des jaquettes et le pilotage d'un outil externe (progression, annulation, blocage, nettoyage)

### Corrigé

- **Les romsets arcade étaient renommés** (`mslug.zip` devenait `Metal Slug.zip`), ce que FBNeo et MAME refusent de charger. Les archives Neo-Geo, Arcade, Naomi, Atomiswave, Model 2 et Model 3 gardent désormais leur nom d'origine
- **Plusieurs fichiers ne voulaient pas toujours dire plusieurs disques** — les versions régionales ou révisions d'un même jeu étaient numérotées comme des disques et réunies dans un M3U. Les disques sont désormais reconnus d'après le libellé du fichier dans RomStation ; les versions sont nommées d'après ce libellé et jamais regroupées
- **Deux jeux au même titre sur le même système** s'écrasaient. Ils sont désormais distingués par leur libellé RomStation, ou par leur identifiant RomStation quand les libellés sont identiques
- **Des fichiers CUE citaient un BIN qui n'existe pas** — dans beaucoup d'archives de RomStation, le BIN a été renommé mais le CUE cite encore l'ancien nom. Certains émulateurs le tolèrent, chdman non. La ligne `FILE` du CUE extrait est corrigée quand une seule image se trouve dans le dossier
- **Les playlists M3U** ne sont plus générées que pour les systèmes dont l'émulateur les lit sur la cible choisie
- **Les fenêtres maximisées recouvraient la barre des tâches** — la fenêtre principale et celle du rebase s'arrêtent désormais au bord de la zone de travail
- **Défilement de la bibliothèque** — les jaquettes sont décodées à leur taille d'affichage plutôt qu'en pleine taille, les cartes sont préparées une page à l'avance, et un cran de molette fait défiler exactement une ligne de cartes, alignée sur la grille
- **Une erreur d'affichage pouvait fermer l'application sans message** — le dialogue d'erreur se rouvrait sur lui-même jusqu'à l'arrêt du programme. Il ne s'ouvre plus qu'une fois, et le détail de l'erreur est écrit dans un fichier `RSR_crash_…txt` du dossier `logs`
- **Masquer un système dans la sidebar ne décoche plus ses jeux** — les filtres ne changent que l'affichage, la sélection est conservée et reste visible dans les badges

### Modifié

- **Fenêtre de rebase** réorganisée en trois groupes d'options (Fichiers, Métadonnées, Copie), tous mémorisés entre les sessions
- **« Exporter le journal » est remplacé par « Afficher le journal »** — l'export CSV du tableau, qui ne disait rien de plus que l'écran, laisse la place au journal détaillé
- **Fenêtre Paramètres** — la version installée et l'état des mises à jour sont affichés dans le pied de la fenêtre, toujours visible, et non plus en bas d'un contenu qui défile. La taille par défaut des fenêtres ne dépasse plus la zone de travail de l'écran
- **Le nommage** des jeux à plusieurs fichiers, des romsets arcade et des homonymes a changé : les copies faites par une version précédente ne seront pas reconnues comme doublons et seront recopiées. Les jeux à fichier unique gardent exactement leur nom précédent
- Les fichiers d'architecture décrivent désormais, par système, si le nom de l'archive doit être conservé, si le M3U est pris en charge, si l'extraction est requise et quel outil externe convertit les fichiers

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