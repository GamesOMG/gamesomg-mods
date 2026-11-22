"""C56 0.2.6: the plugin version has ONE source, the csproj's <Version>; the build generates PluginVersion.Value from it
for the BepInPlugin attribute. An installed 0.2.5 DLL read FileVersion 0.1.0.0 because the csproj and manifest.json still
said 0.1.0 while Plugin.cs said "0.2.5". This fails when the csproj, manifest.json, the BepInPlugin attribute (in the
source, and with --dll in the built DLL) or the DLL's FileVersion disagree.

  python tests/version_test.py [--root <mod folder>] [--dll [<path to dll>]]
"""
import argparse
import json
import re
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
parser.add_argument("--dll", nargs="?", const="", default=None)
args = parser.parse_args()
root = args.root

csproj = (root / "GamesOMG.PlanetCrafterTeleport.csproj").read_text(encoding="utf-8")
found = re.findall(r"<Version>([^<]+)</Version>", csproj)
assert len(found) == 1, "Expected exactly one <Version> in the csproj"
version = found[0].strip()
problems = []
if not re.fullmatch(r"\d+\.\d+\.\d+", version):
    problems.append(f"csproj <Version> {version} is not x.y.z (Thunderstore's version_number)")
manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
if manifest["version_number"] != version:
    problems.append(f"manifest.json version_number {manifest['version_number']} != csproj {version}")
plugin = (root / "src" / "Plugin.cs").read_text(encoding="utf-8")
attribute = re.search(r'\[BepInPlugin\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*([^)\]]+?)\s*\)\]', plugin)
assert attribute, "No BepInPlugin attribute in Plugin.cs"
guid, name, given = attribute.groups()
if given != "PluginVersion.Value":
    literal = given.strip('"')
    problems.append(f"BepInPlugin's version is {given}, not PluginVersion.Value" + ("" if literal == version else f" (and not the csproj's {version})"))
if "PluginVersion.g.cs" not in csproj or "$(Version)" not in csproj:
    problems.append("the csproj does not generate PluginVersion.Value from $(Version)")

if args.dll is not None:
    dll_path = Path(args.dll) if args.dll else root / "bin/Release/netstandard2.1/GamesOMG.PlanetCrafterTeleport.dll"
    dll = dll_path.read_bytes()

    def ser(text):   # an attribute blob's SerString: a one-byte length (< 128) then the UTF-8 bytes
        data = text.encode("utf-8")
        return bytes([len(data)]) + data

    built = re.search(re.escape(ser(guid) + ser(name)) + rb"(.)", dll, re.S)
    built_version = dll[built.end():built.end() + built.group(1)[0]].decode("utf-8") if built else None
    if built_version != version:
        problems.append(f"the built BepInPlugin attribute says {built_version}, the csproj {version}")
    key = "FileVersion".encode("utf-16-le") + b"\x00\x00"
    stamp = re.search(re.escape(key) + rb"(?:\x00\x00)*((?:[0-9.]\x00)+)", dll)
    file_version = stamp.group(1).decode("utf-16-le") if stamp else None
    if file_version != version + ".0":
        problems.append(f"the DLL's FileVersion is {file_version}, not {version}.0")

if problems:
    raise SystemExit("FAIL version: " + "; ".join(problems))
print(f"version {version}: csproj, manifest.json and BepInPlugin agree" + (" and so does the built DLL" if args.dll is not None else ""))
