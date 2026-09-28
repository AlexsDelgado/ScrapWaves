import html
import re
import subprocess
import sys
from pathlib import Path

import markdown
from pypdf import PdfReader

ROOT = Path(r"C:\Alexs\Github\ScrapWaves2\docs\_trabajo")
OUT = Path(r"C:\Alexs\Github\ScrapWaves2\docs\impresion")
WORK = Path(__file__).parent / "html"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"

S = ROOT / "sistemas"
E2 = ROOT / "etapa2"

NOTE_SYS = ("Las referencias a los documentos 01–07 (GDD, Overheat, Spawning, Game Loop, Progresión, QA, Deltas) "
            "remiten a la documentación alpha del 22-09-2026, que no forma parte de esta carpeta. "
            "Las rutas <code>archivo:línea</code> son relativas a <code>Assets/Scripts/</code> salvo que se indique otra cosa. "
            "Cuando un valor serializado en escena o prefab difiere del default del script, manda el de la escena.")

TOMOS = [
    {
        "slug": "Tomo-1-Sistemas-de-juego",
        "num": "1",
        "title": "Sistemas de juego",
        "subtitle": "Armas, enemigos, economía, progresión, meta y nivel",
        "intro": NOTE_SYS,
        "parts": [
            ("Armas", [S / "30-Armas-arquitectura.md", S / "31-Armas-catalogo.md", S / "32-Firepoints-automaticos.md"]),
            ("Enemigos y nivel", [S / "50-Catalogo-de-enemigos.md", S / "52-Nivel-y-entorno.md"]),
            ("Economía y progresión", [S / "20-Economia-y-balance-CSV.md", S / "21-Powerups-y-scrap.md",
                                       S / "23-Level-up-y-stats-automaticas.md", S / "41-Stats-del-jugador.md",
                                       S / "22-Meta-challenges-y-logros.md"]),
        ],
    },
    {
        "slug": "Tomo-2-Sistemas-tecnicos-y-presentacion",
        "num": "2",
        "title": "Sistemas técnicos y presentación",
        "subtitle": "Jugador, daño, cámara, feedback, UI, audio, infraestructura y herramientas",
        "intro": NOTE_SYS,
        "parts": [
            ("Jugador", [S / "40-Movimiento.md", S / "42-Animacion-del-jugador.md", S / "44-Rear-threat.md"]),
            ("Combate y presentación", [S / "10-Dano.md", S / "43-Feedback-de-combate.md", S / "11-Camara-y-aim.md",
                                        S / "45-UI-y-HUD.md", S / "14-Audio.md"]),
            ("Infraestructura", [S / "12-Escenas-y-navegacion.md", S / "13-Settings-y-accesibilidad.md",
                                 S / "51-Dormancia-pools-y-rendimiento.md", S / "53-Herramientas-de-editor-y-tests.md"]),
        ],
    },
    {
        "slug": "Tomo-3-Auditoria-deudas-y-mejoras",
        "num": "3",
        "title": "Auditoría: deudas y mejoras",
        "subtitle": "Diferencias con la documentación, deuda técnica, riesgos de release y prioridades",
        "intro": ("Resultado de la revisión del 23-09-2026 sobre <code>main</code> @ <code>53ef237</code>. "
                  "El capítulo 1 es la lectura recomendada para la reunión de equipo: prioridades y quick wins. "
                  "Los capítulos 3 a 5 son el detalle de cada auditoría, en hoja apaisada, con evidencia "
                  "<code>archivo:línea</code>, impacto, propuesta y esfuerzo (S = horas, M = días, L = semanas). "
                  "Los IDs <code>E1-#</code> remiten al capítulo 2; <code>D-##</code> a los tomos 1 y 2."),
        "parts": [
            ("Resumen", [E2.parent / "etapa2-resumen.md"]),
            ("Diferencias con la documentación alpha", [ROOT / "etapa1-diferencias.md"]),
            ("Detalle de auditorías", [E2 / "deuda-gameplay.md", E2 / "deuda-sistemas.md", E2 / "deuda-proyecto.md"]),
        ],
        "wide": {"deuda-gameplay.md", "deuda-sistemas.md", "deuda-proyecto.md"},
    },
]

CSS = r"""
@page { size: A4; margin: 20mm 17mm 18mm 20mm;
  @bottom-left { content: "Dumpster Fire · Tomo %NUM% — %TITLE%"; font: 7.5pt 'Segoe UI', Arial; color: #777; }
  @bottom-right { content: counter(page) " / " counter(pages); font: 7.5pt 'Segoe UI', Arial; color: #777; }
  @top-right { content: "Uso interno · alpha 23-09-2026"; font: 7.5pt 'Segoe UI', Arial; color: #aaa; }
}
@page cover { margin: 0; @bottom-left { content: none } @bottom-right { content: none } @top-right { content: none } }
@page wide { size: A4 landscape; margin: 14mm 14mm 14mm 14mm; }
:root { --accent: #c2410c; --ink: #1c1917; --muted: #57534e; --rule: #d6d3d1; --band: #f5f5f4; }
* { box-sizing: border-box; }
html { font-family: 'Segoe UI', Arial, sans-serif; font-size: 9.6pt; color: var(--ink); line-height: 1.42; }
body { margin: 0; }
.cover { page: cover; height: 297mm; padding: 38mm 24mm 24mm; display: flex; flex-direction: column;
  background: #1c1917; color: #fafaf9; break-after: page; }
.cover .brand { font-size: 12pt; letter-spacing: .32em; text-transform: uppercase; color: #fb923c; font-weight: 600; }
.cover h1 { font-size: 40pt; line-height: 1.05; margin: 10mm 0 4mm; font-weight: 700; border: 0; padding: 0; break-before: auto; }
.cover .tomo { font-size: 15pt; color: #fdba74; margin-top: 18mm; font-weight: 600; }
.cover .sub { font-size: 12.5pt; color: #d6d3d1; margin-top: 3mm; max-width: 140mm; }
.cover .bar { height: 3px; width: 44mm; background: #ea580c; margin: 8mm 0; }
.cover .meta { margin-top: auto; font-size: 9.5pt; color: #a8a29e; line-height: 1.7; }
.cover .meta b { color: #e7e5e4; font-weight: 600; }
.front { break-after: page; }
.front h2 { font-size: 16pt; margin: 0 0 5mm; color: var(--ink); border: 0; }
.intro { border-left: 3px solid var(--accent); padding: 2mm 0 2mm 4mm; color: var(--muted); margin-bottom: 8mm; }
.toc { list-style: none; padding: 0; margin: 0; }
.toc .part { font-weight: 700; text-transform: uppercase; letter-spacing: .08em; font-size: 8.5pt; color: var(--accent);
  margin: 5mm 0 1.5mm; }
.toc .ch { display: flex; align-items: baseline; gap: 2mm; padding: 1.2mm 0; border-bottom: 1px dotted var(--rule); }
.toc .ch .n { width: 9mm; color: var(--muted); font-variant-numeric: tabular-nums; }
.toc .ch .t { flex: 1; }
.toc .ch .p { font-variant-numeric: tabular-nums; color: var(--muted); }
.part-title { break-before: page; page: auto; height: 230mm; display: flex; flex-direction: column; justify-content: center; }
.part-title .k { color: var(--accent); letter-spacing: .25em; text-transform: uppercase; font-size: 9pt; font-weight: 600; }
.part-title .v { font-size: 26pt; font-weight: 700; margin-top: 3mm; }
.chapter { break-before: page; }
.chapter.wide { page: wide; }
.chapter.wide html, .chapter.wide { font-size: 8.4pt; }
h1 { font-size: 19pt; margin: 0 0 4mm; padding-bottom: 2.5mm; border-bottom: 2px solid var(--accent); line-height: 1.15; }
h1 .chn { color: var(--accent); margin-right: 3mm; }
h2 { font-size: 12.5pt; margin: 6mm 0 2mm; break-after: avoid; color: #292524; }
h3 { font-size: 10.5pt; margin: 4.5mm 0 1.5mm; break-after: avoid; }
p { margin: 0 0 2.2mm; orphans: 3; widows: 3; }
ul, ol { margin: 0 0 2.5mm; padding-left: 5.5mm; }
li { margin: .6mm 0; }
code { font-family: Consolas, 'Cascadia Mono', monospace; font-size: .88em; background: var(--band); padding: 0 .6mm;
  border-radius: 2px; overflow-wrap: anywhere; }
pre { background: var(--band); padding: 2.5mm 3mm; border-radius: 2px; font-size: 8.4pt; white-space: pre-wrap;
  break-inside: avoid; border-left: 2px solid var(--rule); }
pre code { background: none; padding: 0; }
table { width: 100%; border-collapse: collapse; margin: 2mm 0 4mm; font-size: .9em; }
thead { display: table-header-group; }
tr { break-inside: avoid; }
th { text-align: left; background: #292524; color: #fafaf9; font-weight: 600; padding: 1.4mm 1.8mm; vertical-align: bottom; }
td { padding: 1.3mm 1.8mm; border-bottom: 1px solid var(--rule); vertical-align: top; overflow-wrap: break-word; hyphens: manual; }
.chapter.wide td:nth-child(2), .chapter.wide td:last-child { white-space: nowrap; } .chapter.wide td:nth-child(3) { width: 16mm; }
tbody tr:nth-child(even) td { background: #fafaf9; }
.chapter.wide table { font-size: 7.6pt; }
.chapter.wide td:nth-child(1) { white-space: nowrap; font-weight: 600; }
strong { font-weight: 650; }
.mk { color: #fff; font-size: 1px; line-height: 0; }
a { color: inherit; text-decoration: none; }
input[type=checkbox] { margin: 0 1.5mm 0 0; }
"""


def md_to_html(text):
    # checklist items -> checkbox glyph
    text = re.sub(r"^(\s*)- \[ \] ", r"\1- ☐ ", text, flags=re.M)
    text = re.sub(r"^(\s*)- \[x\] ", r"\1- ☑ ", text, flags=re.M | re.I)
    # python-markdown needs a blank line before a list that follows a paragraph line
    lines, out = text.split("\n"), []
    is_item = lambda l: re.match(r"\s*([-*+]|\d+\.)\s", l) is not None
    for i, line in enumerate(lines):
        if is_item(line) and out and out[-1].strip() and not is_item(out[-1]) \
                and not out[-1].lstrip().startswith("|") and not out[-1].startswith((" ", "\t")):
            out.append("")
        out.append(line)
    text = "\n".join(out)
    return markdown.markdown(text, extensions=["tables", "fenced_code", "sane_lists"])


def split_title(text):
    m = re.match(r"\s*#\s+(.+?)\s*\n", text)
    title = m.group(1) if m else "Sin título"
    body = text[m.end():] if m else text
    title = re.sub(r"\s+—\s+alpha\s+\d\d-\d\d-\d{4}\s*$", "", title)
    title = re.sub(r"\s*\(\d\d-\d\d-\d{4}\)\s*$", "", title)
    title = re.sub(r"^Etapa \d+\s+—\s+", "", title)
    title = title[:1].upper() + title[1:]
    return title.strip(), body


def build_html(tomo, pages=None):
    chapters, toc = [], []
    n = 0
    for part_i, (part, files) in enumerate(tomo["parts"], 1):
        toc.append(f'<li class="part">Parte {part_i} · {html.escape(part)}</li>')
        chapters.append(f'<section class="part-title"><div class="k">Parte {part_i}</div>'
                        f'<div class="v">{html.escape(part)}</div></section>')
        for f in files:
            n += 1
            title, body = split_title(f.read_text(encoding="utf-8"))
            wide = " wide" if f.name in tomo.get("wide", set()) else ""
            p = pages.get(n, "") if pages else ""
            toc.append(f'<li class="ch"><span class="n">{n}</span><span class="t">{html.escape(title)}</span>'
                       f'<span class="p">{p}</span></li>')
            chapters.append(f'<section class="chapter{wide}"><h1><span class="mk">@@CH{n:02d}@@</span>'
                            f'<span class="chn">{n}</span>{html.escape(title)}</h1>{md_to_html(body)}</section>')
    css = CSS.replace("%NUM%", tomo["num"]).replace("%TITLE%", tomo["title"])
    cover = f"""<section class="cover">
<div class="brand">Dumpster Fire</div>
<div class="tomo">Tomo {tomo['num']}</div>
<h1>{html.escape(tomo['title'])}</h1>
<div class="sub">{html.escape(tomo['subtitle'])}</div>
<div class="bar"></div>
<div class="meta"><b>Documentación técnica interna</b> · uso del equipo de desarrollo<br>
Estado: alpha · 23-09-2026<br>
Base de código: rama <b>main</b>, commit <b>53ef237</b> · Unity 6000.3.13f1 · URP 17.3</div>
</section>"""
    front = (f'<section class="front"><h2>Contenido</h2><div class="intro">{tomo["intro"]}</div>'
             f'<ul class="toc">{"".join(toc)}</ul></section>')
    return (f'<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Dumpster Fire — Tomo {tomo["num"]}: '
            f'{html.escape(tomo["title"])}</title><style>{css}</style></head><body>{cover}{front}{"".join(chapters)}'
            f'</body></html>')


def render(html_text, name):
    WORK.mkdir(parents=True, exist_ok=True)
    src = WORK / f"{name}.html"
    src.write_text(html_text, encoding="utf-8")
    pdf = WORK / f"{name}.pdf"
    subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
                    f"--print-to-pdf={pdf}", src.as_uri()], check=True, capture_output=True, timeout=180)
    return pdf


def find_pages(pdf):
    pages = {}
    for i, page in enumerate(PdfReader(str(pdf)).pages, 1):
        for m in re.finditer(r"@@CH(\d\d)@@", page.extract_text() or ""):
            pages.setdefault(int(m.group(1)), i)
    return pages


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for tomo in TOMOS:
        first = render(build_html(tomo), tomo["slug"])
        pages = find_pages(first)
        final = render(build_html(tomo, pages), tomo["slug"])
        check = find_pages(final)
        dest = OUT / f"Dumpster Fire - {tomo['slug'].replace('-', ' ', 2).replace('Tomo ', 'Tomo ')}.pdf"
        dest = OUT / f"DumpsterFire_{tomo['slug']}.pdf"
        dest.write_bytes(final.read_bytes())
        total = len(PdfReader(str(dest)).pages)
        print(f"{dest.name}: {total} páginas, capítulos {check}", "OK" if check == pages else "REVISAR numeración")


if __name__ == "__main__":
    sys.exit(main())
