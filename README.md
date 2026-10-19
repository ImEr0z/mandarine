<p align="center"><img src="docs/icon.png" width="96" alt=""></p>

<h1 align="center">Mandarine</h1>

<p align="center">Convertisseur de fichiers pour Windows, inspiré de <a href="https://tangerineformac.com/">Tangerine pour Mac</a>.<br>
Faites glisser un fichier, appuyez sur <kbd>Maj</kbd>, relâchez-le sur un format : c'est converti, à côté de l'original.<br>
Tout se passe sur votre PC, rien n'est envoyé en ligne.</p>

---

## Fonctionnement

Mandarine tourne discrètement dans la zone de notification. Trois façons de l'utiliser :

1. **Le geste.** Commencez à faire glisser un fichier depuis l'Explorateur ou le Bureau, puis appuyez sur <kbd>Maj</kbd> : un menu de formats apparaît sous le curseur. Relâchez le fichier sur le format voulu, ou sur l'en-tête du menu pour le garder ouvert et choisir au clic. Appuyer sur <kbd>Maj</kbd> *avant* de cliquer étendrait la sélection de l'Explorateur : la touche se presse une fois le glisser commencé.
2. **Le clic droit.** « Convertir avec Mandarine » dans le menu contextuel de l'Explorateur (sous Windows 11 : « Afficher plus d'options »), ou « Envoyer vers > Mandarine ». Fonctionne avec une sélection de plusieurs fichiers.
3. **La zone de dépôt.** Clic sur l'icône de notification : une zone reste ouverte en bas à droite de l'écran, il suffit d'y glisser des fichiers.

Les fichiers créés sont placés à côté de l'original (ou dans un dossier choisi). Un fichier existant n'est jamais écrasé : `photo.png` devient `photo (2).png`. Les fichiers déposés sont toujours copiés, jamais déplacés.

## Formats

| Fichier | Convertir en | Outils |
| --- | --- | --- |
| Image (PNG, JPG, HEIC, WebP, AVIF, GIF, BMP, TIFF, PSD, SVG, JXL, RAW…) | PNG, JPG, WEBP, AVIF, GIF, BMP, TIFF, ICO, JXL | Réduire de 50 %, Max 1920 px, Compresser, Sans métadonnées, Pivoter, Miroir, Noir et blanc, Rogner les marges, Copier en Base64, Combiner en PDF |
| GIF animé | MP4\*, WEBM\* (et tous les formats image) | |
| Audio (MP3, WAV, FLAC, AAC/M4A, OGG, OPUS, AIFF, WMA…) | MP3\*, M4A\*, WAV\*, FLAC\*, OGG\*, OPUS\*, AIFF\* | Normaliser le volume\* |
| Vidéo (MP4, MOV, MKV, WEBM, AVI, WMV…) | MP4\*, MOV\*, MKV\*, WEBM\*, AVI\*, GIF\*, piste audio MP3\*/M4A\*/WAV\* | Compresser\*, Réduire en 720p\*, Supprimer le son\*, Première image\* |
| PDF | PNG, JPG (une image par page), TXT | Fusionner, Une page par fichier, Pivoter les pages |
| Word, ODT, RTF, TXT | PDF\*, DOCX\*, ODT\*, RTF\*, TXT\* | |
| Excel, ODS | PDF\*, XLSX\*, ODS\*, CSV\* | |
| PowerPoint, ODP | PDF\*, PPTX\*, ODP\* | |
| Markdown | HTML, PDF\*, DOCX\* | |
| HTML | Markdown, PDF\*, DOCX\* | |
| JSON | YAML, CSV, XML | |
| YAML, XML | JSON | |
| CSV, TSV | JSON, XLSX\*, CSV ↔ TSV | |
| Archive ZIP, TAR, TAR.GZ, GZ, 7Z, RAR | | Extraire |
| Tout fichier ou dossier | | Compresser en ZIP, Copier le chemin, Empreinte SHA-256 |

\* Nécessite un outil gratuit : **FFmpeg** pour l'audio et la vidéo, **LibreOffice** pour les documents Office. Au premier usage, Mandarine propose de l'installer via `winget`, puis reprend la conversion. Les tuiles concernées portent une petite flèche de téléchargement.

Quelques choix faits pour éviter les mauvaises surprises :

- les photos sont redressées selon leur orientation EXIF avant toute opération ;
- « Sans métadonnées » retire EXIF (dont la position GPS), XMP et IPTC mais conserve le profil couleur ;
- les formats sans transparence (JPG, BMP) reçoivent un fond blanc au lieu de noir ;
- un CSV exporté depuis Excel en français (séparateur `;`) est détecté automatiquement, et `06000` reste du texte en JSON.

## Installation

1. Téléchargez `Mandarine-win-x64.zip` (ou `win-arm64` pour les PC ARM) depuis la page [Releases](../../releases).
2. Décompressez `Mandarine.exe` où vous voulez (par exemple `%LOCALAPPDATA%\Programs\Mandarine`) et lancez-le.

Aucun droit administrateur n'est nécessaire et aucun runtime .NET n'est à installer : l'exécutable est autonome. Au premier lancement, Mandarine s'ajoute au démarrage de la session, au clic droit et à « Envoyer vers » (désactivable dans les paramètres).

L'exécutable n'est pas signé : Windows SmartScreen affichera un avertissement au premier lancement (« Informations complémentaires > Exécuter quand même »).

## Paramètres

Clic droit sur l'icône de notification > **Paramètres** :

- **Geste** : <kbd>Maj</kbd> (comme Tangerine), <kbd>Alt</kbd>, *secouer le fichier*, ou désactivé. Dans l'Explorateur, Maj+glisser sert aussi à *déplacer* des fichiers : si c'est un réflexe pour vous, choisissez « secouer » (trois allers-retours rapides pendant le glissement).
- **Source** : réagir seulement aux glissers depuis l'Explorateur et le Bureau (par défaut), ou depuis n'importe quelle application.
- **Destination** : à côté de l'original ou dans un dossier fixe.
- **Qualité** JPEG/WebP/AVIF, lancement au démarrage, intégration au clic droit, emplacement de FFmpeg et LibreOffice.

## Différences avec Tangerine

- Gratuit, open source, sans limite d'usage.
- Sur Windows, <kbd>Maj</kbd> pendant un glisser signifie « déplacer » : d'où le geste alternatif « secouer » et la touche <kbd>Alt</kbd>.
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
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

`.\run.ps1` recompile l'application dans `publish\` et la relance depuis là : le menu Démarrer, le clic droit et le lancement au démarrage utilisent alors toujours la dernière version du code.

Ajouter une conversion revient à écrire une classe dérivée de `PerFileAction` (un fichier en entrée, un fichier en sortie) ou de `FileAction`, puis à l'enregistrer dans `ActionCatalog`. Le panneau l'affiche automatiquement pour les fichiers qu'elle accepte.

La CI GitHub Actions compile, teste et publie `Mandarine-win-x64.zip` et `Mandarine-win-arm64.zip` à chaque push. Pousser un tag `v*` crée une release avec ces deux archives.

### Comment marche le geste

Un hook souris bas niveau (`WH_MOUSE_LL`, sur un thread dédié pour ne jamais ralentir le curseur) repère un glisser qui démarre dans l'Explorateur avec la touche choisie enfoncée. Mandarine ouvre alors sous le curseur une fenêtre quasi transparente, enregistrée comme cible de dépôt OLE. Dès qu'un fichier la survole, elle lit la liste des fichiers glissés, affiche les formats compatibles et devient visible. Si le bouton est relâché ailleurs, elle se referme.

## Statut et feuille de route

Version 0.1. Le moteur de conversion est couvert par 77 tests automatisés (exécutés avec FFmpeg et LibreOffice réels). L'interface Windows est compilée et empaquetée par la CI, mais doit encore être éprouvée sur différentes configurations (multi-écrans à échelles différentes, Windows 10, ARM).

Pistes :

- compression PDF, OCR, suppression d'arrière-plan ;
- progression dans la zone de notification pour les longues vidéos ;
- entrée dans le menu contextuel moderne de Windows 11 (nécessite un paquet MSIX signé) ;
- traduction anglaise.

## Licence

[MIT](LICENSE). Mandarine n'est pas affilié à Tangerine ni à ses auteurs.

Bibliothèques : [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache 2.0), [PDFsharp](https://www.pdfsharp.com/) (MIT, version 6.2), [PDFtoImage](https://github.com/sungaila/PDFtoImage) (MIT), [PdfPig](https://github.com/UglyToad/PdfPig) (Apache 2.0), [Markdig](https://github.com/xoofx/markdig) (BSD-2), [ReverseMarkdown](https://github.com/mysticmind/reversemarkdown-net) (MIT), [YamlDotNet](https://github.com/aaubry/YamlDotNet) (MIT). FFmpeg et LibreOffice ne sont pas redistribués : ils sont installés séparément par l'utilisateur.
