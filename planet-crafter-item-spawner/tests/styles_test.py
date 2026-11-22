"""GamesOMG Item Spawner: every text style Theme.Build makes has its plates in Palette.Pairs (so the contrast check in
tests/Program.cs measures it), and no style in Pairs is missing from Theme. The panel must not draw text in a colour
outside Ink, Muted, Good, Bad and OnOrange: the teleport panel's Faint is decoration only here.

  python tests/styles_test.py
"""
import pathlib
import re
import sys

MOD = pathlib.Path(__file__).resolve().parents[1]
theme = (MOD / "src" / "Theme.cs").read_text(encoding="utf-8")
palette = (MOD / "src" / "Palette.cs").read_text(encoding="utf-8")
build = theme[theme.index("public void Build("):]
built = set(re.findall(r"^\s*(\w+) = (?:Style|Btn)\(", build, re.M))
paired = set(re.findall(r'new\[\] \{ "(\w+)", "\w+", "\w+" \}', palette))
text_colours = set(re.findall(r'new\[\] \{ "\w+", "(\w+)", "\w+" \}', palette))
problems = []
for s in sorted(built - paired):
    problems.append(f"Theme builds {s} but Palette.Pairs never measures it")
for s in sorted(paired - built):
    problems.append(f"Palette.Pairs measures {s} but Theme never builds it")
if "Faint" in theme or "Faint" in text_colours:
    problems.append("Faint is used for text")
for src in (MOD / "src").glob("*.cs"):
    t = src.read_text(encoding="utf-8")
    # a style made on the fly must copy one of Theme's (same colours): new GUIStyle(t.X)
    for m in re.finditer(r"new GUIStyle\(([^)]*)\)", t):
        arg = m.group(1).strip()
        base = arg.split(".")[-1]
        if src.name != "Theme.cs" and base not in built:
            problems.append(f"{src.name}: new GUIStyle({arg}) is not a copy of a measured style")
    if src.name not in ("Theme.cs", "Palette.cs") and re.search(r"textColor\s*=", t):
        problems.append(f"{src.name} sets a text colour of its own")
if problems:
    print("\n".join(problems))
    sys.exit(1)
print(f"styles: {len(built)} built, each measured on its plates; text colours {sorted(text_colours)}")
