#!/usr/bin/env bash
# Concept images for the legendary weapons' own models (fal flux/schnell, ~0.003 $ each), two seeds per weapon.
# Then: um fal model3d assets/gen/<best>.jpg --faces 4000 --texture 1024 --name <weapon>_3d --out assets/gen
# and tools/build_weapons.py turns the GLBs into the .tam/.png the plugin loads.
set -e
cd "$(dirname "$0")/.."
UM="../universal-modder/bin/um"
STYLE="single isolated weapon, standing perfectly vertical, handle at the bottom and tip at the top, straight front view, the whole weapon visible and centered, plain pure white background, stylized viking fantasy game asset, hand-painted texture, sharp details, no hands, no text, no shadow"
declare -A P
P[axe]="a one-handed viking throwing axe: a single bearded iron blade engraved with a glowing blue rune spiral, short dark ash-wood haft wrapped in brown leather, a small iron ring at the butt"
P[spear]="a viking war spear: a long leaf-shaped silver steel spearhead etched with a lightning bolt, bronze socket, ash-wood shaft with storm-blue leather grip and bronze bands"
P[sword]="a one-handed viking sword: slender pale silver-blue blade with swirling wind engravings, crossguard shaped like two spread bird wings, grip wrapped in white leather, round pommel set with a pale blue gem"
P[mace]="a one-handed stone mace: a heavy rough grey granite head with deep flanges, patches of green moss and small embedded quartz crystals, a sturdy wooden haft bound with leather straps"
P[knife]="a hunting knife shaped like a fox fang: a curved single-edged blade with a russet copper sheen, small bronze guard, handle wrapped in red-orange fox fur with a white tip"
for w in axe spear sword mace knife; do
  for seed in 11 29; do
    "$UM" fal run fal-ai/flux/schnell prompt="${P[$w]}, $STYLE" image_size=portrait_16_9 seed:=$seed \
      --out assets/gen --name "${w}_${seed}"
  done
done
