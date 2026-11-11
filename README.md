<p align="center"><img src="docs/icon.png" width="96" alt=""></p>

<h1 align="center">Mandarine</h1>

<p align="center">Convertisseur de fichiers pour Windows, inspiré de <a href="https://tangerineformac.com/">Tangerine pour Mac</a>.<br>
Faites glisser un fichier, appuyez sur <kbd>Maj</kbd>, relâchez-le sur un format : c'est converti, à côté de l'original.<br>
Tout se passe sur votre PC, rien n'est envoyé en ligne.</p>

---

## Fonctionnement

Mandarine tourne discrètement dans la zone de notification (un clic sur son icône ouvre le menu : paramètres, mise à jour, suspendre le geste, quitter). Les formats s'affichent sur une roue : survolez ou cliquez un secteur, appuyez sur <kbd>Maj</kbd> (même pendant le glisser, et même si la roue n'a pas le focus) ou cliquez au centre pour passer à la page suivante, ou tapez 1 à 8 pour choisir un secteur. Toutes les actions tiennent sur la roue, huit par page. La roue s'ouvre et se ferme avec une courte animation ; <kbd>Échap</kbd> ou un clic en dehors la ferme.

Quatre façons de l'ouvrir :

1. **Le geste.** Commencez à faire glisser un fichier depuis l'Explorateur ou le Bureau, puis appuyez sur <kbd>Maj</kbd> : un menu de formats apparaît sous le curseur. Relâchez le fichier sur le format voulu, ou sur l'en-tête du menu pour le garder ouvert et choisir au clic. Appuyer sur <kbd>Maj</kbd> *avant* de cliquer étendrait la sélection de l'Explorateur : la touche se presse une fois le glisser commencé (la roue s'ouvre aussi si la souris reste immobile).
2. **Un appui sur Maj, sans glisser.** Réglage « Appuyer sur Maj » : sélectionnez des fichiers dans l'Explorateur ou sur le Bureau et tapez sur <kbd>Maj</kbd> seule. La roue s'ouvre centrée sur le fichier. <kbd>Maj</kbd>+lettre, <kbd>Maj</kbd>+clic ou <kbd>Maj</kbd> maintenue ne déclenchent rien, ni un appui sans fichier sélectionné.
3. **Le clic droit.** « Convertir avec Mandarine » dans le menu contextuel de l'Explorateur (sous Windows 11 : « Afficher plus d'options »), ou « Envoyer vers > Mandarine ». Fonctionne avec une sélection de plusieurs fichiers.
4. **Le raccourci.** Sélectionnez des fichiers dans l'Explorateur ou sur le Bureau, puis appuyez sur <kbd>Ctrl</kbd>+<kbd>Maj</kbd>+<kbd>Espace</kbd> : la roue s'ouvre centrée sur le fichier sélectionné (sous la souris si son icône reste introuvable). Le raccourci se change dans les paramètres.

Les fichiers créés sont placés à côté de l'original (ou dans un dossier choisi). Un fichier existant n'est jamais écrasé : `photo.png` devient `photo (2).png`. Les fichiers déposés sont toujours copiés, jamais déplacés.

## Formats

| Fichier | Convertir en | Outils |
| --- | --- | --- |
| Image (PNG, JPG, HEIC, WebP, AVIF, GIF, BMP, TIFF, PSD, SVG, JXL, RAW…) | PNG, JPG, PDF, WEBP, AVIF, GIF, BMP, TIFF, ICO, JXL | Réduire de 50 %, Max 1920 px, Compresser, Sans métadonnées, Pivoter, Miroir, Noir et blanc, Rogner les marges, Copier en Base64 |
| PDF | PNG, JPG (une image par page), TXT | Une page par fichier, Pivoter les pages, Modifier le texte (fenêtre d'édition : cliquer une ligne et taper, ajouter du texte) |
| Markdown | HTML | |
| HTML | Markdown | |
| JSON | YAML, CSV, XML | |
| YAML, XML | JSON | |
| CSV, TSV | JSON, CSV ↔ TSV | |
| Archive ZIP, TAR, TAR.GZ, GZ, 7Z, RAR | | Extraire |
| Plusieurs images et/ou PDF | Fusionner en PDF (premier secteur de la roue, fichiers dans l'ordre de leur nom) | |
| Image ou PDF (fonctions de Windows) | | Extraire le texte (OCR), Copier le texte, Copier l'image, Fond d'écran, Imprimer |
| Tout fichier ou dossier | | Compresser en ZIP, Copier le chemin, Empreinte SHA-256 |

Tout est fait par Mandarine elle-même, sans aucun logiciel à installer à côté. L'audio, la vidéo et les documents Office (qui demandent FFmpeg ou LibreOffice) sont mis de côté dans cette version.

Quelques choix faits pour éviter les mauvaises surprises :

- les photos sont redressées selon leur orientation EXIF avant toute opération ;
- « Sans métadonnées » retire EXIF (dont la position GPS), XMP et IPTC mais conserve le profil couleur ;
- les formats sans transparence (JPG, BMP) reçoivent un fond blanc au lieu de noir ;
- un CSV exporté depuis Excel en français (séparateur `;`) est détecté automatiquement, et `06000` reste du texte en JSON.

## Installation

1. Téléchargez `MandarineApp-win-Setup.exe` depuis la page [Releases](../../releases) et lancez-le : Mandarine s'installe pour l'utilisateur courant (`%LOCALAPPDATA%\MandarineApp`), avec un raccourci du menu Démarrer et une entrée dans « Applications installées ».
2. Sans installation : `Mandarine-win-x64.zip` (ou `win-arm64` pour les PC ARM) contient seulement `Mandarine.exe`, à lancer où vous voulez. Cette version ne se met pas à jour toute seule.

Mises à jour : une version installée cherche une release plus récente 10 secondes après son lancement, la télécharge et redémarre dedans. Cela suppose que le dépôt (ou ses releases) soit public.

Aucun droit administrateur n'est nécessaire et aucun runtime .NET n'est à installer : l'exécutable est autonome. Au premier lancement, Mandarine s'ajoute au démarrage de la session, au clic droit et à « Envoyer vers » (désactivable dans les paramètres).

L'exécutable n'est pas signé : Windows SmartScreen affichera un avertissement au premier lancement (« Informations complémentaires > Exécuter quand même »).

## Paramètres

Icône de notification (clic gauche ou droit) > **Paramètres**. La fenêtre est organisée en sections ; **chaque changement est enregistré et appliqué tout de suite**, sans bouton à valider.

- **Ouvrir le menu de conversion**
  - *Geste de glisser-déposer* : glisser + <kbd>Maj</kbd>, <kbd>Alt</kbd> ou <kbd>Ctrl</kbd>, **appuyer sur <kbd>Maj</kbd> (sans glisser)**, *secouer le fichier*, ou désactivé. Dans l'Explorateur, Maj+glisser sert aussi à *déplacer* des fichiers : si c'est un réflexe pour vous, préférez « Appuyer sur Maj » ou « Secouer » (trois allers-retours rapides).
  - *Limiter à l'Explorateur et au Bureau* : ignore les fichiers glissés depuis les autres applications (activé par défaut).
  - *Raccourci clavier* : cliquez sur « Modifier » puis tapez la combinaison (au moins <kbd>Ctrl</kbd>, <kbd>Alt</kbd> ou <kbd>Win</kbd>, ou une touche F1 à F24) ; <kbd>Échap</kbd> annule, la corbeille supprime le raccourci.
- **Affichage** : roue de formats ou liste (aperçu sur des cartes). **Sons d'interface** : un son de survol et un son de fin de conversion, au choix parmi *Doux*, *Verre*, *Goutte*, *Soie* ou *Aucun*, avec un bouton ▶ pour les écouter. Les sons sont synthétisés par Mandarine, rien n'est à installer.
- **Fichiers créés** : à côté de l'original ou dans un dossier fixe ; **qualité** JPEG/WebP/AVIF.
- **Windows** : lancement à l'ouverture de session (réduit dans la zone de notification, sans message), intégration au clic droit et à « Envoyer vers ».

## Différences avec Tangerine

- Gratuit, open source, sans limite d'usage.
- Sur Windows, <kbd>Maj</kbd> pendant un glisser signifie « déplacer » : d'où les gestes alternatifs « Appuyer sur Maj » (sans glisser), « secouer » et la touche <kbd>Alt</kbd>.
- Sortie HEIC non prise en charge (l'encodeur HEVC est soumis à brevets) ; la lecture HEIC fonctionne.
- Pas encore d'équivalent à certains outils avancés de Tangerine (voir la feuille de route).

## Développement

Prérequis : [.NET 10 SDK](https://dotnet.microsoft.com/download). Le moteur se compile et se teste sur Windows, macOS et Linux ; l'application ne s'exécute que sur Windows.

```
src/
  Mandarine.Core/    moteur de conversion, multiplateforme, sans interface
    Actions/         FileAction, catalogue, contexte, résultats
    Converters/      images (Magick.NET), audio/vidéo (FFmpeg), PDF (PDFsharp, PDFtoImage, PdfPig),
                     Office (LibreOffice), Markdown/HTML, données, archives
  Mandarine.App/     application WPF : hook souris, panneau de formats, zone de notification, paramètres
tests/
  Mandarine.Core.Tests/   tests xUnit ; ceux qui demandent FFmpeg ou LibreOffice sont ignorés s'ils sont absents
```

```powershell
dotnet test                                   # tests du moteur
dotnet run --project src/Mandarine.App        # lancer l'application (Windows)
dotnet publish src/Mandarine.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`.\run.ps1` recompile l'application dans `publish\` et la relance depuis là : le menu Démarrer, le clic droit et le lancement au démarrage utilisent alors toujours la dernière version du code.

Ajouter une conversion revient à écrire une classe dérivée de `PerFileAction` (un fichier en entrée, un fichier en sortie) ou de `FileAction`, puis à l'enregistrer dans `ActionCatalog`. Le panneau l'affiche automatiquement pour les fichiers qu'elle accepte.

La CI GitHub Actions compile, teste et publie `Mandarine-win-x64.zip` et `Mandarine-win-arm64.zip` à chaque push. Pousser un tag `v*` (par exemple `git tag v0.2.0 && git push origin v0.2.0`) crée une release avec ces deux archives, l'installateur `MandarineApp-win-Setup.exe` et les fichiers que les copies installées lisent pour se mettre à jour. Le numéro de version est celui du tag.

### Comment marche le geste

Un hook souris bas niveau (`WH_MOUSE_LL`, sur un thread dédié pour ne jamais ralentir le curseur) repère un glisser qui démarre dans l'Explorateur avec la touche choisie enfoncée. Pendant le glisser, une courte surveillance (30 ms, seulement une fois la souris bougée) détecte une touche pressée sans que la souris bouge. Mandarine ouvre alors sous le curseur une fenêtre quasi transparente, enregistrée comme cible de dépôt OLE. Dès qu'un fichier la survole, elle lit la liste des fichiers glissés, affiche les formats compatibles et devient visible. Si le bouton est relâché ailleurs, elle se referme.

Un hook clavier bas niveau (`WH_KEYBOARD_LL`, également sur son propre thread) ne lit que des codes de touches, jamais le texte, et ne bloque aucune touche. Il sert à l'appui sur <kbd>Maj</kbd> seul (appuyé et relâché en moins de 0,4 s, sans autre touche ni clic), à tourner les pages et à fermer la roue avec <kbd>Échap</kbd> quand elle est ouverte pendant un glisser et n'a donc pas le focus.

Pour centrer la roue sur le fichier, la position de l'icône est lue par UI Automation dans l'Explorateur, ou par les messages de la liste d'icônes du Bureau. Cette recherche se fait hors du thread de l'interface et est limitée à une demi-seconde : au-delà, la roue s'ouvre sous la souris.

## Statut et feuille de route

Version 0.5. Le moteur de conversion est couvert par plus de 90 tests automatisés (certains sont ignorés si FFmpeg ou LibreOffice sont absents). L'interface Windows est compilée et empaquetée par la CI, mais doit encore être éprouvée sur différentes configurations (multi-écrans à échelles différentes, Windows 10, ARM).

Pistes :

- compression PDF, OCR, suppression d'arrière-plan ;
- progression dans la zone de notification pour les longues vidéos ;
- entrée dans le menu contextuel moderne de Windows 11 (nécessite un paquet MSIX signé) ;
- traduction anglaise.

## Licence

[MIT](LICENSE). Mandarine n'est pas affilié à Tangerine ni à ses auteurs.

Bibliothèques : [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0), [PDFsharp](https://www.pdfsharp.com/) (MIT, version 6.2), [PDFtoImage](https://github.com/sungaila/PDFtoImage) (MIT), [PdfPig](https://github.com/UglyToad/PdfPig) (Apache 2.0), [Markdig](https://github.com/xoofx/markdig) (BSD-2), [ReverseMarkdown](https://github.com/mysticmind/reversemarkdown-net) (MIT), [YamlDotNet](https://github.com/aaubry/YamlDotNet) (MIT). FFmpeg et LibreOffice ne sont pas redistribués : ils sont installés séparément par l'utilisateur.
