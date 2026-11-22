"""GamesOMG Item Spawner: the string table (src/Strings.cs) against the code that asks for its lines, in every language.

Fails when code asks for an id that is not here (a player would see the id), when a table id is never asked for, when a plural
id (L.N) lacks its ".1" or ".n" form, when a language lacks an id English has (or has one English doesn't), when a
translation's blanks ({0}, {1}…) differ from the English line's, when a language the game ships has no table, or when a
translation still carries an English sentence. Ids picked at run time are listed in DYNAMIC with where they come from.

  python tests/strings_test.py
"""
import pathlib
import re
import sys

MOD = pathlib.Path(__file__).resolve().parents[1]
table_src = (MOD / "src" / "Strings.cs").read_text(encoding="utf-8")
code = {p: p.read_text(encoding="utf-8") for p in sorted((MOD / "src").glob("*.cs")) if p.name != "Strings.cs"}

# the game's own language list (GameConfig.TranslatedLangages, build 26411416)
GAME_LANGUAGES = ["english", "french", "spanish", "portuguese", "german", "schinese", "tchinese", "japanese", "korean",
                  "polish", "russian", "turk", "italian"]

# each language's table: from its opening line to the next one
starts = [(m.start(), "english" if m.group(1) == "English" else m.group(1).strip('"'))
          for m in re.finditer(r'\[(English|"\w+")\] = new Dictionary<string, string>', table_src)]
tables = {}
for i, (pos, lang) in enumerate(starts):
    end = starts[i + 1][0] if i + 1 < len(starts) else len(table_src)
    body = table_src[pos:end]
    tables[lang] = dict(re.findall(r'\["([a-zA-Z0-9_.-]+)"\]\s*=\s*"((?:[^"\\]|\\.)*)"', body))
table = set(tables["english"])

used, plural = set(), set()
for path, text in code.items():
    for m in re.finditer(r'L\.(T|N)\("([a-zA-Z0-9_.-]+)"', text):
        kind, ident = m.groups()
        (plural if kind == "N" else used).add(ident)
    for m in re.finditer(r'L\.T\([^;()"]*?\?\s*"([a-zA-Z0-9_.-]+)"\s*:\s*"([a-zA-Z0-9_.-]+)"', text):
        used.update(m.groups())
    # ids handed around as plain strings: "help.*", "btn.*", the spawner's "spawn.*" refusals, SpawnPlan's "result.*",
    # ItemInfo's gauge kinds "info.*"
    for m in re.finditer(r'"((?:help|btn|spawn|result|info)\.[a-zA-Z]+)"', text):
        used.add(m.group(1))

DYNAMIC = {
    # KeyCheck.Label (copied from GamesOMG Teleport): the D-pad and Space read through L.T inside a switch
    "key.none", "key.space", "key.dpadUp", "key.dpadDown", "key.dpadLeft", "key.dpadRight",
    # L.Number, inside Strings.cs itself: the language's decimal mark
    "num.decimal",
}
used |= DYNAMIC

problems = []
for ident in sorted(used - table):
    problems.append(f"code asks for {ident!r}, the table has no such line")
for ident in sorted(plural):
    for form in (ident + ".1", ident + ".n"):
        if form not in table:
            problems.append(f"L.N({ident!r}) needs {form!r}")
plural_forms = {p + s for p in plural for s in (".1", ".n")}
for ident in sorted(table - used - plural_forms):
    problems.append(f"the table's {ident!r} is never used")

for lang in GAME_LANGUAGES:
    if lang not in tables:
        problems.append(f"the game ships {lang}, the mod has no table for it")
for lang in tables:
    if lang not in GAME_LANGUAGES:
        problems.append(f"a table for {lang}, a language the game doesn't ship")

blank = re.compile(r"\{\d+\}")
# lines that may stay as they are in every language: the brand, the multiplication signs
SAME_OK = {"title", "btn.one", "btn.ten", "prompt.ten", "num.decimal", "list.sep", "sentence.sep"}
# the same word in that language too: French "Triangle", Portuguese "item", German "Tabs" and the game's own "Hydration"
SAME_IN = {("french", "pad.triangle"), ("portuguese", "count.1"), ("german", "prompt.tabs"), ("german", "info.water")}
english_words = re.compile(r"\b(the|your|and|backpack|items|added|game|search|press|nothing|fuse)\b", re.I)
for lang, t in tables.items():
    if lang == "english":
        continue
    for ident in sorted(table - set(t)):
        problems.append(f"{lang}: no {ident!r}")
    for ident in sorted(set(t) - table):
        problems.append(f"{lang}: {ident!r} is not an English id")
    for ident in sorted(table & set(t)):
        en, tr = tables["english"][ident], t[ident]
        if sorted(blank.findall(en)) != sorted(blank.findall(tr)):
            problems.append(f"{lang} {ident}: blanks {sorted(set(blank.findall(tr)))} differ from English {sorted(set(blank.findall(en)))}")
        if ident not in SAME_OK and (lang, ident) not in SAME_IN and tr == en and len(en) > 3:
            problems.append(f"{lang} {ident}: still the English line")
        # an English sentence left behind (the brand and file names aside)
        stripped = re.sub(r"GamesOMG Item Spawner|gamesomg\.planetcrafter\.spawn\.cfg|gamesomg\.com|Miju Games|GamesOMG|PadKey|Key", "", tr)
        if lang not in ("english",) and english_words.search(stripped) and not re.search(r"[^\x00-\x7f]", stripped) and len(stripped) > 25:
            problems.append(f"{lang} {ident}: reads as English: {tr!r}")

if problems:
    print("\n".join(problems))
    sys.exit(1)
print(f"strings: {len(table)} lines in each of {len(tables)} languages (the game's own list), every one used, "
      f"every id the code asks for is there, every translation's blanks match English")
