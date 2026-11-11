"""Writes docs/releases/vX.Y.Z.md for every release listed in ReleaseNotes.cs (the app's "Nouveautés" page).
The release workflow publishes that file as the GitHub release description. Run it before tagging."""
import os
import re

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
src = open(os.path.join(root, 'src', 'Mandarine.App', 'Services', 'ReleaseNotes.cs'), encoding='utf-8').read()
out = os.path.join(root, 'docs', 'releases')
os.makedirs(out, exist_ok=True)

release = re.compile(r'new\("(\d+\.\d+\.\d+)", "([^"]+)", "([^"]+)", \[(.*?)\]\),\n', re.S)
note = re.compile(r'new\("([^"]+)", "([^"]+)", "([^"]+)"\)')

for version, date, headline, body in release.findall(src):
    lines = [f'# Mandarine {version} : {headline}', '', f'*{date}*', '']
    for icon, title, text in note.findall(body):
        lines.append(f'- {icon} **{title}** : {text}')
    lines.append('')
    if version != '0.1.0':
        lines += [
            '## Télécharger',
            '',
            '- **`MandarineApp-win-Setup.exe`** : l\'installeur. Mandarine se met ensuite à jour toute seule.',
            '- **`Mandarine-win-x64.zip`** / **`Mandarine-win-arm64.zip`** : l\'exécutable seul, sans installation.',
            '',
        ]
    path = os.path.join(out, f'v{version}.md')
    open(path, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines))
    print(path, len(note.findall(body)), 'notes')
