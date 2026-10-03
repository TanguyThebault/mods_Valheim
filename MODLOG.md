# MODLOG: Valheim, hache de retour (Throwing Axe)

## Intake (2026-10-03)
- Jeu : Valheim 1.0.16 (network version 40), Steam 892970, Unity 6000.0.75f1, Mono. Pas d'anti-cheat.
- Idée : hache à une main lançable (attaque secondaire), vole jusqu'à 20 m en tournoyant, blesse tout ce
  qu'elle touche, revient dans la main. Obstacle solide avant 20 m : elle le touche et revient de là.
- Done = marche en jeu (solo) + clip.

## Chemins
- Jeu : C:\Program Files (x86)\Steam\steamapps\common\Valheim
- Saves : Steam\userdata\86975312\892970\remote (characters, worlds) + %USERPROFILE%\AppData\LocalLow\IronGate\Valheim
- Backups : `um backup` valheim-saves (20261003-114737) et valheim-locallow (20261003-114739)
  Restore : fermer Steam, puis `um backup restore valheim-saves --yes`
- Décompilation (hors repo) : ~/valheim-decomp/valheim (ilspycmd 11.1 sur assembly_valheim.dll)

## Route
BepInExPack_Valheim 5.4.2351 + Jotunn 2.30.2 (Thunderstore, 2026-09) + Harmony. Plugin netstandard2.1.
Raison : scène de mod établie, Jotunn gère items/recettes/traductions, le jeu est en Mono.

## Faits moteur
- `Attack.FireProjectileBurst` instancie `m_attackProjectile` et appelle `IProjectile.Setup(owner, vel, noise,
  hitData, weapon, ammo)` avec un HitData complet (dégâts, skill, qualité, SE). Un MonoBehaviour qui
  implémente IProjectile suffit : on garde anim, endurance et calcul de dégâts natifs.
- `Attack.m_consumeItem` : la lance le met à true (elle quitte l'inventaire) ; on le met à false.
- Masque de collision des projectiles : Default, static_solid, Default_small, piece, piece_nonsolid, terrain,
  character, character_net, character_ghost, hitbox, character_noenv, vehicle.
- `Projectile.FindHitObject(collider)` remonte à l'IDestructible (Character, TreeBase, MineRock, WearNTear...).
- Arme en main : `VisEquipment.m_rightItemInstance` (privé), main : `VisEquipment.m_rightHand`.
- `PrefabManager.PrefabContainer` de Jotunn n'est pas public : conteneur inactif maison.

## Design v0.1
- Item `AxeThrowing` = clone de `AxeIron`, forge niv. 1, 20 fer, 6 écorce ancienne, 4 chutes de cuir.
- Attaque secondaire = clone de celle de `SpearBronze` (anim de lancer), sans consommer l'item.
- `ThrowingAxeProjectile` : aller en ligne droite (30 m/s), sphere-cast à chaque frame. Les créatures sont
  traversées (1 coup chacune par trajet), le premier obstacle solide est touché et déclenche le retour. Le
  retour suit la main (32 m/s), touche à nouveau les créatures, ignore les obstacles.
  Tout se règle dans BepInEx/config/lekinox.throwingaxe.cfg.
- Pendant le vol : arme cachée dans la main, StartAttack bloqué (patch Harmony).
- Visuel local seulement (pas de ZNetView) : en multi, les autres voient les dégâts mais pas la hache voler.

## Étapes
- [x] Build OK (dotnet 10 SDK)
- [ ] Installer BepInEx + Jotunn + plugin (demander l'accord)
- [ ] Vérifier en jeu : log de chargement, spawn de l'item, lancer, portée 20 m, arrêt sur obstacle, retour
