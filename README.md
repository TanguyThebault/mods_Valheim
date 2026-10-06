# mods_Valheim

Valheim mods by **Lekinox** (Lekiteam). BepInEx 5 + Jötunn. Each folder is a separate mod with its own history,
journal (`MODLOG.md`) and build (`dotnet build -c Release`, the game folder set by `-p:ValheimDir=...`).

| Folder | Mod | What it adds |
|---|---|---|
| [`wildlife/`](wildlife) | **Wildlife** | New fauna with their own models, rigs and animations, from the Meadows to the sea, each with loot and recipes. |
| [`legendary-weapons/`](legendary-weapons) | **Legendary Weapons** | One loot-only legendary per biome, each with a held-secondary power, an element aura that shows when the power is ready, and upgrades. |
| [`pipi-caca/`](pipi-caca) | **Pipi + Caca Mod** | Hidden pee, poop and fart urges, with a simulated pee stream. |

## Credits and AI disclosure
- 3D models: concept images generated with fal (FLUX schnell), turned into meshes with Trellis 2, then cleaned,
  decimated and fitted with the scripts in each mod's `tools/`.
- Sounds: synthesized from code (`tools/synth_*.py`), no recorded or third-party audio.
- Code written with the help of an AI coding agent (Claude).
- No Valheim game files, extracted assets or decompiled code are included. Valheim is © Iron Gate AB.
