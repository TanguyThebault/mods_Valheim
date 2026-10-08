# Pipi + Caca Mod

Your Viking has needs. Valheim gains three bodily functions: poop, pee and farts. It's silly, it's
synthesised, and it's surprisingly physical.

![A Viking peeing into a river from a rock](https://raw.githubusercontent.com/TanguyThebault/mods_Valheim/main/pipi-caca/media/pipi1.jpg)

## What it adds
- **Poop.** Eating fills a hidden urge, digested little by little (bigger meals, more urge).
  - From half-way, a status effect shows, like the cold: *Need to poop [K]*. Press **K** to squat and go.
  - At 100 % it happens on its own, wherever you are (as long as you're on solid ground).
  - The poop lands right behind you, and you can pick it up (E)... and throw it. It bursts into a brown splash
    on impact.

  ![Squatting for a poop between standing stones](https://raw.githubusercontent.com/TanguyThebault/mods_Valheim/main/pipi-caca/media/caca1.jpg)

- **Pee.** A second urge that fills on its own, slowly (full in about an hour by default).
  - From half-way: *Need to pee [L]*. Press **L** to go, and **L** again to stop early. At 100 % it starts by
    itself.
  - You keep walking while you pee (no running, jumping or fighting). A male Viking stands and holds it properly with
    the right hand, and you aim with the mouse.
  - A female Viking squats instead: she can shuffle along crouched, and the stream goes down toward the ground. The
    mouse nudges it sideways. Set `Pose` to force one or the other.
  - The stream is simulated: drops fly with gravity and your own speed, and bend when you turn. They splash on the
    ground, buildings, creatures or water, and leave wet patches that dry in a minute.
  - Strength follows the urge: a strong arc at first, dribbles at the end. 100 % empties in 10 seconds (50 % in 5).

  ![Peeing on a runestone](https://raw.githubusercontent.com/TanguyThebault/mods_Valheim/main/pipi-caca/media/pipi2.jpg)

- **Farts.** A hidden gauge, no status effect and no animation. It fills over time and with each meal. When it's
  full, your Viking lets one go: just the sound and a faint cloud of gas.
- **Sounds.** Every sound is synthesised from scratch (no recordings): 4 farts, 3 plops, 2 splats, and pee on the
  ground and in water.
- **Multiplayer.** Everyone sees the stream and the pose, and hears the farts.
- **Languages.** English and French.

## Requirements
- Valheim (tested on the current Steam build, October 2026)
- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4.2351
- [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) 2.30.2 or later

## Install
- **With r2modman or Thunderstore Mod Manager:** click Install. Dependencies come along.
- **By hand:** copy the contents of `plugins/` (`Caca.dll`, `sfx/`, `icons/`) into
  `Valheim/BepInEx/plugins/Caca/`. Keep `sfx/` and `icons/` next to the DLL.
- **Multiplayer:** every player (and the server) needs the mod. Jötunn checks this on connect.

**Uninstall:** delete the folder. The urges are stored in the character's custom data and are simply ignored
without the mod. Poop items left in the world or in your inventory disappear.

## Configuration
`BepInEx/config/lekinox.pipicacamod.cfg`. Every setting is reloaded live, a second after you save the file.

| Section | Settings |
|---|---|
| General | `EnablePoop`, `EnablePee`, `EnableFarts`, `UrgentAt` (85 %: the status turns urgent and flashes) |
| Controls | `Key` (K), `PeeKey` (L) |
| Urge (poop) | `MinNeed` (50 %), `DigestPerMinute`, `NeedPerFoodHealth`, `Volume` (sounds) |
| Pee | `MinNeed` (50 %), `FillPerMinute` (1.5 = about 1 h 07), `DrainSeconds` (10), `MinSpeed`, `MaxSpeed`, `AimLift`, `Volume`, `WetPatches`, `Pose` (Auto / Standing / Squatting), `SquatPitch`, `SquatSpeed` |
| Farts | `FillPerMinute` (3.03 = every 33 min), `PerMeal`, `Cloud`, `CloudOpacity` |
| Tuning | the hand pose and the stream's origin (`WristOffset`, `FingerDir`, `FingerCurl`, `TipFromHand`, `SquatTipOffset`), the poop in the hand (`HeldPos`, `HeldRot`) |
| Debug | `ShowBars` (the three gauges as bars), `Lab` (test hook) |

Console commands (F5, with `devcommands`):
- `caca [force]` and `caca_need <0-100>`
- `pipi [force]` and `pipi_need <0-100>`
- `pet` (fart now) and `pet_need <0-100>`
- `caca_throw [speed]`
- `caca_hold x y z [rx ry rz]`

## Troubleshooting
- **The log:** `BepInEx/LogOutput.log`. The mod's lines start with `[Info : Pipi + Caca Mod]`.
- **No sound or default icons:** check that `sfx/` and `icons/` sit next to `Caca.dll`.

## Compatibility
- Pure code, plus one item cloned from the vanilla ooze bomb. It shouldn't conflict with other mods.
- Mods that also drive the right arm with IK may fight over it while you pee.

## Credits
- [BepInEx](https://github.com/BepInEx/BepInEx), [Harmony](https://github.com/pardeike/Harmony), and
  [Jötunn](https://github.com/Valheim-Modding/Jotunn).
- Sounds are synthesised in Python/numpy (`tools/synth_caca.py`). Icons were rendered with Blender and Pillow.
  No generative AI art or audio.
- Built with [universal-modder](https://github.com/rehan-remade/universal-modder), a game-modding toolkit for AI
  coding agents. It provided recon, in-game testing and capture, the sound-analysis loop and the pre-release checks.
- **AI disclosure:** this mod was designed with and coded by an AI agent, Claude Code (Claude Opus 5.5). The
  sounds were tuned against a second agent that scored spectrograms. All of it was directed and playtested by
  Lekinox.

## License
MIT for the code. The synthesised sounds and icons are part of the mod and under the same license.
