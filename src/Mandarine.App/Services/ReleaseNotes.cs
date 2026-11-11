namespace Mandarine.App.Services;

internal sealed record Note(string Icon, string Title, string Text);

internal sealed record Release(string Version, string Date, string Headline, Note[] Notes);

/// <summary>
/// What is new, release by release (newest first). Add an entry here with every version:
/// the "Nouveautés" window shows the first one big, and the rest in its history tab.
/// Write for someone who has never read the code: plain words, one idea per note.
/// Then run scripts/release-notes.py: it turns these entries into docs/releases/vX.Y.Z.md, which the release
/// workflow publishes as the GitHub description (the tag fails without that file).
/// </summary>
internal static class ReleaseNotes
{
    public static readonly Release[] All =
    [
        new("0.7.0", "novembre 2026", "PDF cherchable et réglages par type", [
            new("🔍", "PDF cherchable", "Un PDF scanné ou une photo de page devient un PDF dont on peut chercher et copier le texte. Le texte est lu sur ton PC, avec Windows."),
            new("🩹", "Réparer un PDF", "Un PDF abîmé est réécrit en copie propre, prête à s'ouvrir."),
            new("🎛️", "Réglages par type de fichier", "Pour les images, les PDF et le reste : dossier de destination, qualité, taille maximale, résolution des pages et un texte ajouté au nom."),
            new("🕘", "Conversions récentes", "Retrouve les derniers fichiers créés, avec une miniature, depuis le menu de l'icône. Un clic les ouvre."),
            new("🪄", "Nouveautés plus nettes", "La page Nouveautés s'ouvre directement au centre, sans sauter."),
        ]),
        new("0.6.0", "novembre 2026", "Signer, caviarder, retoucher et ranger", [
            new("✍️", "Signer un PDF", "Dessine ta signature ou ajoute une image, pose-la sur la page. Tes signatures sont gardées pour la prochaine fois."),
            new("🛡️", "Caviarder", "Cache des infos sensibles dans un PDF : le texte dessous est vraiment supprimé, pas seulement masqué."),
            new("💧", "Filigrane avec aperçu", "Un texte ou un logo répété en diagonale sur toute la page, avec un aperçu avant d'appliquer."),
            new("📄", "Plus d'outils PDF", "Supprimer, extraire et réorganiser des pages, numéroter, recadrer, comparer deux versions, résumer, protéger par mot de passe."),
            new("🧾", "Infos cachées d'un PDF", "Change le titre et l'auteur, ou efface le programme, les dates et les marque-pages avant de partager."),
            new("🖍️", "Annoter et flouter une image", "Flèches, cadres, texte, flou et zones noires, puis une copie enregistrée à côté."),
            new("✂️", "Recadrer et retoucher", "Recadre à la main en 1:1, 4:3, 16:9… ; ajuste la lumière et les couleurs ; redresse une photo ou un scan."),
            new("🏷️", "Renommer en masse", "Un modèle avec le nom, un numéro et la date, la liste des nouveaux noms avant d'appliquer, et un bouton pour annuler."),
            new("🗂️", "Ranger un dossier", "Trie par type, année ou mois, et retrouve les doublons pour les mettre à la corbeille."),
            new("↩️", "Annuler une conversion", "Une bulle en bas à droite dit ce qui a été fait, avec Ouvrir et Annuler. Le menu de la barre des tâches annule aussi la dernière."),
            new("🪶", "Plus léger", "Mandarine pèse 32 Mo de moins et nettoie les anciens fichiers temporaires laissés par les mises à jour."),
            new("⏳", "Mise à jour en direct", "Quand Mandarine se met à jour, une fenêtre montre l'avancement et le temps restant."),
            new("🆕", "Page Nouveautés", "Ouvre cette page depuis l'icône de la barre des tâches pour voir ce qui a changé."),
        ]),
        new("0.5.0", "11 novembre 2026", "Réglages refaits, geste Maj et éditeur PDF", [
            new("⌨️", "Ouvre la roue avec Maj", "Sélectionne des fichiers puis appuie sur Maj : la roue des actions s'ouvre. Pas besoin de glisser."),
            new("⚙️", "Réglages refaits", "Les paramètres sont rangés en cartes claires et s'enregistrent tout seuls."),
            new("📄", "Modifier un PDF", "Change le texte d'un PDF directement dans Mandarine."),
            new("🎵", "Sons au choix", "Choisis le son du survol et celui de fin de conversion, ou coupe-les."),
            new("🎯", "Roue bien placée", "Elle s'ouvre sur le fichier sélectionné, dans l'Explorateur comme sur le Bureau."),
            new("✨", "Animations plus douces", "La roue apparaît et disparaît en douceur."),
            new("🔔", "Menu de la barre des tâches", "L'ouvrir ne ferme plus la liste des icônes cachées."),
        ]),
        new("0.4.0", "23 octobre 2026", "Mises à jour automatiques", [
            new("🔄", "Mises à jour au démarrage", "Mandarine cherche une nouvelle version à chaque lancement. Tu peux aussi la chercher à la main depuis le menu."),
            new("🤫", "Lancement discret", "Mandarine peut démarrer sans rien afficher et rester dans la barre des tâches."),
            new("🖼️", "Icônes à jour", "Les icônes de Mandarine dans Windows se rafraîchissent après une installation ou une mise à jour."),
        ]),
        new("0.3.0", "23 octobre 2026", "Un menu plus simple", [
            new("🖱️", "Un clic suffit", "Le clic gauche sur l'icône de la barre des tâches ouvre le menu."),
            new("🧹", "Zone de dépôt retirée", "La petite zone où l'on déposait les fichiers n'existe plus : utilise la roue."),
        ]),
        new("0.2.0", "23 octobre 2026", "Roue en verre, outils Windows et installeur", [
            new("🎡", "Toutes les actions sur la roue", "Huit actions par page. Maj ou un clic au centre tourne la page."),
            new("🔎", "Outils Windows", "Lire le texte d'une image (OCR), la copier, la mettre en fond d'écran ou l'imprimer."),
            new("📚", "Fusionner en PDF", "Choisis plusieurs images ou PDF pour n'en faire qu'un seul."),
            new("🧿", "Icône 512 px", "Crée une image carrée de 512 px, prête pour un avatar Discord."),
            new("🪟", "Roue en verre", "Des tranches translucides et des sons plus doux."),
            new("📦", "Installeur", "Mandarine s'installe en un clic et se met à jour toute seule."),
        ]),
        new("0.1.0", "19 octobre 2026", "Première version", [
            new("📂", "Glisse, choisis, c'est converti", "Glisse des fichiers (images, PDF, données, archives) et choisis le format voulu."),
        ]),
    ];
}
