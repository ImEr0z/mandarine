"""Writes docs/images/banner.svg: the animated banner at the top of the README.

    python scripts/make-banner.py

The icon is embedded (an SVG shown through <img> cannot load other files). The animation is plain SVG/SMIL:
a slowly turning wheel and a few format chips floating, nothing that needs scripts.
"""
import base64
import io
import math
import os

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
W, H = 1280, 320

icon = Image.open(os.path.join(ROOT, 'docs', 'icon.png')).convert('RGBA').resize((180, 180), Image.LANCZOS)
buffer = io.BytesIO()
icon.save(buffer, 'PNG', optimize=True)
icon_b64 = base64.b64encode(buffer.getvalue()).decode()

cx, cy, outer, inner = 1128, 160, 112, 40
slices = []
for k in range(8):
    a0 = math.radians(k * 45 - 22.5 - 90)
    a1 = math.radians(k * 45 + 22.5 - 90)
    x0, y0, x1, y1 = (cx + outer * math.cos(a0), cy + outer * math.sin(a0), cx + outer * math.cos(a1), cy + outer * math.sin(a1))
    i0, j0, i1, j1 = (cx + inner * math.cos(a0), cy + inner * math.sin(a0), cx + inner * math.cos(a1), cy + inner * math.sin(a1))
    path = (f'M{i0:.1f},{j0:.1f} L{x0:.1f},{y0:.1f} A{outer},{outer} 0 0 1 {x1:.1f},{y1:.1f} '
            f'L{i1:.1f},{j1:.1f} A{inner},{inner} 0 0 0 {i0:.1f},{j0:.1f} Z')
    opacity = 0.26 if k % 2 == 0 else 0.15
    slices.append(f'<path d="{path}" fill="#fff" fill-opacity="{opacity}" stroke="#fff" stroke-opacity=".55" stroke-width="2"/>')
    mid = math.radians(k * 45 - 90)
    dx, dy = cx + 78 * math.cos(mid), cy + 78 * math.sin(mid)
    slices.append(f'<circle cx="{dx:.1f}" cy="{dy:.1f}" r="8" fill="#fff" fill-opacity=".9"/>')

chips = [(790, 44, 'PNG → JPG', 4.2, 0.0), (905, 104, 'PDF', 5.1, 0.8), (800, 168, 'HEIC → PNG', 4.6, 1.4), (930, 226, 'ZIP', 3.9, 0.4), (808, 252, 'WEBP', 5.6, 2.0)]
chip_svg = []
for x, y, label, dur, delay in chips:
    width = 26 + 13 * len(label)
    chip_svg.append(
        f'<g transform="translate({x} {y})"><g>'
        f'<animateTransform attributeName="transform" type="translate" values="0 0;0 -12;0 0" dur="{dur}s" begin="{delay}s" repeatCount="indefinite" calcMode="spline" keySplines=".4 0 .6 1;.4 0 .6 1" keyTimes="0;.5;1"/>'
        f'<rect width="{width}" height="40" rx="20" fill="#fff" fill-opacity=".95"/>'
        f'<text x="{width / 2}" y="26" text-anchor="middle" font-size="18" font-weight="700" fill="#D9660A" '
        f'font-family="Segoe UI, system-ui, -apple-system, Helvetica, Arial, sans-serif">{label}</text></g></g>')

svg = f'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="{W}" height="{H}" viewBox="0 0 {W} {H}" role="img" aria-label="Mandarine : convertir, signer, retoucher, ranger">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#FFB255"/>
      <stop offset="1" stop-color="#F26A0F"/>
    </linearGradient>
    <radialGradient id="glow" cx=".5" cy=".5" r=".5">
      <stop offset="0" stop-color="#fff" stop-opacity=".35"/>
      <stop offset="1" stop-color="#fff" stop-opacity="0"/>
    </radialGradient>
    <filter id="shadow" x="-20%" y="-20%" width="140%" height="150%">
      <feDropShadow dx="0" dy="8" stdDeviation="10" flood-color="#7A2E00" flood-opacity=".35"/>
    </filter>
  </defs>
  <rect width="{W}" height="{H}" rx="28" fill="url(#bg)"/>
  <circle cx="190" cy="40" r="260" fill="url(#glow)"/>
  <circle cx="{cx}" cy="{cy}" r="230" fill="url(#glow)"/>
  <image x="62" y="70" width="180" height="180" xlink:href="data:image/png;base64,{icon_b64}" filter="url(#shadow)"/>
  <g font-family="Segoe UI, system-ui, -apple-system, Helvetica, Arial, sans-serif" fill="#fff">
    <text x="270" y="158" font-size="88" font-weight="800" letter-spacing="-2">Mandarine</text>
    <text x="274" y="208" font-size="27" font-weight="600" fill-opacity=".97">Convertir, signer, retoucher, ranger.</text>
    <text x="274" y="246" font-size="20" fill-opacity=".85">Gratuit · pour Windows · tout reste sur votre PC</text>
  </g>
  {''.join(chip_svg)}
  <g filter="url(#shadow)">
    <g>
      <animateTransform attributeName="transform" type="rotate" from="0 {cx} {cy}" to="360 {cx} {cy}" dur="80s" repeatCount="indefinite"/>
      {''.join(slices)}
    </g>
    <circle cx="{cx}" cy="{cy}" r="{inner - 6}" fill="#fff" fill-opacity=".92"/>
    <text x="{cx}" y="{cy + 9}" text-anchor="middle" font-size="26" font-weight="800" fill="#D9660A" font-family="Segoe UI, system-ui, -apple-system, Helvetica, Arial, sans-serif">Maj</text>
  </g>
</svg>
'''
out = os.path.join(ROOT, 'docs', 'images', 'banner.svg')
os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, 'w', encoding='utf-8', newline='\n') as f:
    f.write(svg)
print(out, f'{len(svg) / 1024:.0f} KB')
