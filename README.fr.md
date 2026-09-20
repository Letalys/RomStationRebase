![RomStation Rebase](docs/images/banner.png)

# RomStation Rebase

[![Version](https://img.shields.io/badge/version-1.3.0-blueviolet)](https://github.com/Letalys/RomStationRebase/releases/latest)
[![Licence](https://img.shields.io/badge/licence-MIT-green)](LICENSE)
[![Plateforme](https://img.shields.io/badge/plateforme-Windows%2010%2F11-blue)](https://github.com/Letalys/RomStationRebase/releases/latest)

**[English](README.md) · [Français](README.fr.md)**

Outil Windows qui copie vos jeux depuis RomStation vers une carte SD ou un dossier, rangés et nommés comme l'attend votre console portable ou votre frontend : ArkOS et dArkOS, RetroArch, Batocera, EmulationStation, ES-DE, Onion, Cocoon, firmware Anbernic d'origine.

---

## Pourquoi cet outil ?

[RomStation](https://www.romstation.fr/) range ses jeux à sa manière. Une console Anbernic, **RetroArch** ou **EmulationStation** attendent autre chose : un dossier par système, des noms lisibles, des jeux parfois extraits de leur archive, des jaquettes et des métadonnées au bon endroit.

**RomStation Rebase** fait ce travail pour les jeux que vous cochez, selon la cible que vous choisissez.

> ⚠️ RomStation Rebase travaille **toujours en copie**. Votre installation RomStation n'est jamais modifiée.

> ℹ️ Les sauvegardes faites dans RomStation ne sont pas transférées, et ni BIOS ni émulateur ne sont copiés. Voir [Limites](https://github.com/Letalys/RomStationRebase/wiki/Limites).

---

## Installation

Sur la page des [Releases](https://github.com/Letalys/RomStationRebase/releases/latest) :

- **Installeur MSI** (recommandé)
- **Version portable ZIP** : à extraire où vous voulez, puis lancer `RomStationRebase.exe`

### Prérequis

- **Windows 10 ou 11** (64 bits)
- **RomStation** installé et initialisé, c'est-à-dire exécuté une première fois
- Pour les conversions, facultatives : les émulateurs qui contiennent les outils (MAME, Dolphin…), installés par RomStation ou par un autre moyen

---

## Utilisation

1. **Cochez** les jeux à copier dans la bibliothèque
2. Cliquez sur **Rebase vers…**
3. Choisissez la **destination** et l'**architecture cible préconfigurée** de votre appareil
4. Cliquez sur **Démarrer**

---

## Fonctionnalités

- Huit cibles prêtes à l'emploi : ArkOS / dArkOS, RetroArch, Batocera, EmulationStation, ES-DE, Cocoon, Onion, Anbernic d'origine
- Des jeux rangés dans les bons dossiers, extraits quand il le faut, avec leurs playlists M3U
- Les jaquettes et les informations des jeux copiées avec eux
- La conversion des fichiers pendant la copie (CHD, CSO, RVZ)
- Des présélections pour retrouver ses jeux et ses paramètres

La liste complète est dans la page [Fonctionnalités](https://github.com/Letalys/RomStationRebase/wiki/Fonctionnalités) du wiki.

---

## Documentation

Le [wiki](https://github.com/Letalys/RomStationRebase/wiki) est en français et en [anglais](https://github.com/Letalys/RomStationRebase/wiki/Home-English). Pour signaler un problème ou un retour sur votre appareil, ouvrez une [issue](https://github.com/Letalys/RomStationRebase/issues/new/choose) : chaque message d'erreur porte un [code](https://github.com/Letalys/RomStationRebase/wiki/Codes-erreur) `RSR-nnnn` à y citer.

---

## Développement et tests

- La version **1.3.0** a été co-écrite avec une IA : [Claude Code](https://claude.com/claude-code), modèle Claude Fable 5.1 d'Anthropic
- Les tests sur console ont été faits sur une **Anbernic RG353V sous dArkOS**
- L'[architecture technique](https://github.com/Letalys/RomStationRebase/wiki/Architecture-technique) est décrite dans le wiki

---

## Changelog

L'historique des versions est dans [CHANGELOG.fr.md](CHANGELOG.fr.md).

---

## Licence

Distribué sous licence **MIT**. Voir le fichier [LICENSE](LICENSE).

---

© 2026 [Letalys](https://github.com/Letalys)
