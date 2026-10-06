"""Builds the Thunderstore / r2modman zip -> dist/<Team>-Pipi_Caca_Mod-<version>.zip (nothing is uploaded).

usage: python tools/package.py [--team Lekiteam]
Layout: manifest.json, icon.png (256 px), README.md, CHANGELOG.md, plugins/Caca.dll, plugins/sfx/*.wav,
plugins/icons/*.png. A mod manager installs plugins/ as BepInEx/plugins/<Team>-Pipi_Caca_Mod/, so sfx/ and icons/ stay
next to the DLL, where the mod looks for them. Run `dotnet build -c Release` first.
"""
import json
import re
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
team = sys.argv[sys.argv.index("--team") + 1] if "--team" in sys.argv else "Lekiteam"
version = re.search(r'Version = "([\d.]+)"', (ROOT / "src" / "Plugin.cs").read_text(encoding="utf-8")).group(1)

manifest = {
    "name": "Pipi_Caca_Mod",                  # Thunderstore names: letters, digits, underscores ("Pipi + Caca Mod")
    "version_number": version,
    "website_url": "",
    "description": "Your Viking has needs: poop (K), pee with a simulated, aimable stream (L) and farts. Synthesised sounds.",
    "dependencies": ["denikson-BepInExPack_Valheim-5.4.2351", "ValheimModding-Jotunn-2.30.2"],
}
dll = ROOT / "bin" / "Release" / "Caca.dll"
if not dll.exists():
    sys.exit("build first: dotnet build -c Release")
out = ROOT / "dist" / f"{team}-Pipi_Caca_Mod-{version}.zip"
out.parent.mkdir(exist_ok=True)
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("manifest.json", json.dumps(manifest, indent=2))
    z.write(ROOT / "assets" / "icons" / "mod_icon.png", "icon.png")
    z.write(ROOT / "README.md", "README.md")
    z.write(ROOT / "CHANGELOG.md", "CHANGELOG.md")
    z.write(dll, "plugins/Caca.dll")
    for f in sorted((ROOT / "assets" / "sfx").glob("*.wav")):
        z.write(f, f"plugins/sfx/{f.name}")
    for name in ("caca.png", "se_caca.png", "se_pipi.png"):
        z.write(ROOT / "assets" / "icons" / name, f"plugins/icons/{name}")
print(out, f"{out.stat().st_size // 1024} KB")
for i in zipfile.ZipFile(out).infolist():
    print(f"  {i.filename:28s} {i.file_size:>9d}")
