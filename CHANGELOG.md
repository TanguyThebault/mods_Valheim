# Changelog

## 0.5.1
- **Longer pee:** a full bladder now takes 10 s to empty (5 s from 50 %), up from 6 s (`DrainSeconds`).
- **README:** the screenshots now load from GitHub, so they also show on Thunderstore and in mod managers.

## 0.5.0
- **Female characters squat to pee:**
  - She can still shuffle along crouched.
  - The stream goes down toward the ground and is weaker; the mouse nudges it sideways.
  - There is no hand pose.
  - `Pose` (Auto / Standing / Squatting) forces one or the other. Other players see the right pose.
- **Volume:** the pee sound is a little quieter (0.2).

## 0.4.0
- **New identifier:** the internal ID (GUID) is now `lekinox.pipicacamod`.
  - The config file becomes `lekinox.pipicacamod.cfg`. Settings from earlier versions are not carried over.
  - The urges saved on your character are kept.
  - In multiplayer, everyone needs 0.4.0.

## 0.3.2
- **Renamed:** the mod is now "Pipi + Caca Mod". Your config file and saved urges are kept.
- **New command:** `pet_need <0-100>` sets the fart gauge, like `caca_need` and `pipi_need`.

## 0.3.1
- **Slower urges:** pee now fills in about 1 h 07 (was 25 min), and farts come every 33 minutes (was 40).
- **Fixes:**
  - A white flash sometimes appeared where the stream hit the ground (a new wet patch showed white for one frame).
  - The stream's brightness now stays under the bloom threshold.

## 0.3.0
- **Farts:** a hidden gauge that fills over time and with each meal. When full, a fart (sound only, with a faint
  gas cloud) and no animation.
- **Settings:** feature toggles (`EnablePoop`, `EnablePee`, `EnableFarts`), `UrgentAt`, `WetPatches`, and the
  fart settings. Sound volume is now live.
- **Fixes:**
  - The pee stream now hits the ground behind your own body (it could fall through).
  - Network traffic while aiming is lower.

## 0.2.1
- **Volume:** pee and poop sounds are quieter, and the pee stream no longer has reverb.
- **Positions:**
  - The poop lands closer, right behind you.
  - The stream now leaves from the hand.

## 0.2.0
- **No more bar:** status effects from 50 % (poop: K, pee: L), urgent from 85 %.
- **Pee:**
  - The urge fills on its own and drains in 6 s.
  - The stream is simulated: physics, aiming with the mouse, splashes, wet patches, ground and water sounds.
  - The right hand holds it (IK) and you keep walking.
- **Poop:**
  - It sits properly in the hand.
  - New segmented shape and a new icon.
- **Sounds:** all of them rebuilt and scored by an evaluator agent (8.5/10 minimum).

## 0.1.0
- First version: the poop urge, the squat, and a throwable poop.
