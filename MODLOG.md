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

## Installé dans le jeu (2026-10-03, accord de Lekinox)
Fichiers ajoutés à la racine de Valheim : winhttp.dll, doorstop_config.ini, .doorstop_version, doorstop_libs/,
start_game_bepinex.sh, start_server_bepinex.sh, changelog.txt, BepInEx/ (core, config, plugins/Jotunn,
plugins/ThrowingAxe). BepInEx.cfg [Logging.Disk] LogLevels += Debug.
Désactiver : renommer/supprimer winhttp.dll. Désinstaller : supprimer la liste ci-dessus.
Redéployer le plugin : dotnet build -c Release puis copier bin/Release/ThrowingAxe.dll dans BepInEx/plugins/ThrowingAxe/.

## Test 1 (v0.1, Lekinox en jeu)
- Fonctionne : lancer, retour, obstacles détectés (log : "Obstacle Terrain at 7,8 m", "Hit Rock_3(Clone)").
- Console : Paramètres > Gameplay > Activer la console (F5 ne marche pas sans ça).
- Retours : la hache tourne sur le mauvais axe (doit tourner à plat) ; trajectoire à rendre elliptique.
- Constat dans le log : la visée du lancer pique un peu vers le bas (dir.y ≈ -0.14), donc le sol peut être
  touché vers 8-10 m sur terrain plat.

## v0.2
- Visuel : axe le plus fin du mesh (épaisseur de lame) aligné sur la verticale, centre des bounds au pivot,
  rotation autour de la verticale du monde. Config [Visual] Tilt pour corriger.
- Trajectoire : portée fixée au lancer (20 m ou premier obstacle sur la ligne de visée, par sphere-cast).
  Aller = demi-ellipse vers CurveSide, retour = autre demi-ellipse vers la main (qui bouge). Paramètre u,
  avancée à vitesse constante en longueur d'arc (dérivée numérique). Obstacle imprévu sur la courbe :
  demi-tour depuis le point de contact.
- Config relue à chaud (date du .cfg vérifiée chaque seconde) : CurveWidth, CurveSide, vitesses, etc.

## Test 2 (v0.2) : validé par Lekinox ("Parfait")

## v0.3 : lapins des prairies
- Créature `Rabbit` = clone de `Hare` (lièvre des Brumes : modèle et animations de bonds d'Iron Gate),
  matériaux clonés et teintés (FurTint), émission coupée, échelle 0.85, santé 10, vitesse de course x1.2,
  accélération x1.5, virages x1.6/x1.8. Faction AnimalsVeg.
- IA : AnimalAI (remplace l'IA du Hare si ce n'en est pas une, en copiant les champs de BaseAI). Fuite en
  zigzag sans patch : BaseAI.Flee retire une direction aléatoire de ±m_fleeAngle toutes les m_fleeInterval s
  -> 70°, 0.6 s, portée 15 m. Errance courte (3 s / 3 m), a peur du feu, évite l'eau.
- Spawn (Jotunn SpawnConfig) : Meadows, hors forêt, jour et nuit, 40 %, toutes les 90 s, max 3, groupes 1-3.
- Drops : RabbitHide x1 (clone de DeerHide, teinté, échelle 0.7) et RabbitMeat x1-2 (clone de DeerMeat).
- RabbitMeatCooked (clone de CookedDeerMeat) : 22 PV, 18 endurance, 1200 s, régén 2. Broche 20 s.
- Icônes : RenderManager.Render(prefab, IsometricRotation) sur les clones teintés.
- Le log dumpe Hare, DeerHide, DeerMeat et CookedDeerMeat (composants, IA, matériaux, drops) pour ajuster.

## Test 3 (v0.3) : échec, puis v0.3.1
- `spawn Rabbit` / `spawn RabbitMeat` ne faisaient rien. Log : "Failed to clone prefab, name already exists:
  RabbitMeat", puis NullReferenceException dans RegisterItems, qui interrompt tout l'enregistrement des lapins.
  **Le jeu vanilla contient déjà un prefab RabbitMeat.** Prefabs renommés : MeadowRabbit, MeadowRabbitHide,
  MeadowRabbitMeat, MeadowRabbitCooked.
- Dump du Hare : AnimalAI, santé 10, AnimalsVeg, marche 1, vitesse 4, course 7, virages 200/300,
  accél 0.9, vue 25, ouïe 15, fuite 25/45/2, errance 11 s/20 m. Drops HareMeat, ScaleHide 1-3, TrophyHare 5 %.
  Matériau Hare_mat (Custom/Creature, _Color blanc), donc la teinte multiplicative fonctionne.
- SpeedFactor passé à 1.35 (course 9.45). Attention : Config.Bind garde la valeur déjà présente dans le .cfg,
  il faut donc aussi modifier le .cfg quand on change une valeur par défaut.
- Hache : DestroyImmediate est refusé dans un événement d'animation (Setup est appelé depuis l'event
  d'attaque). Remplacé par disable + Destroy.

## Test 4 (v0.3.1) : spawns OK, fuite validée ; trop agités au calme
- Cause (BaseAI.RandomMovement) : à plus de 2 x m_randomMoveRange du point de spawn, le déplacement aléatoire
  se fait EN COURANT. Avec une portée de 3 m, chaque lapin qui avait fui (> 6 m) revenait au sprint. De plus,
  la vitesse au calme (m_speed) était multipliée par SpeedFactor.

## v0.3.2
- m_speed = CalmSpeed 2.2 (Hare : 4), non multiplié ; seule la course (m_runSpeed) est multipliée.
- Errance : IdleInterval 9 s (le jeu tire au hasard entre 9 et 13.5 s), IdleRange 4 m.
- Patch Harmony AnimalAI.SetAlerted (postfix, alert=false, lapins seulement grâce à RabbitTag) :
  m_spawnPoint = position actuelle, écrit aussi dans la ZDO (ZDOVars.s_spawnPoint) si on en est
  propriétaire. Le lapin s'installe là où sa fuite s'est arrêtée.

## v0.4 : réparation, tapis, intégration de la hache
- Réparation (InventoryGui.CanRepair) : il faut une recette trouvée par ObjectDB.GetRecipe (comparaison sur
  m_shared.m_name, **sans tenir compte de m_enabled**) dont m_craftingStation ou m_repairStation correspond au
  poste. Hache : Enabled = CraftableAtForge (false par défaut), CraftingStation et RepairStation = forge, donc
  pas fabricable mais réparable à la forge.
- Loot : postfix sur Container.AddDefaultItems (appelé une seule fois par coffre, quand il est rempli). Si le
  prefab est TreasureChest_sunkencrypt, tirage Random < SunkenCryptChestChance (0.02) et la hache est ajoutée
  avec sa durabilité max. Choix : Cryptes englouties (Marais), même palier que les dégâts d'AxeIron.
  Warning au chargement si le prefab du coffre est introuvable.
- Tapis rug_rabbit : CustomPiece clone de rug_deer, marteau, catégorie Mobilier, établi, 3 MeadowRabbitHide,
  échelle 0.6 x 1 x 0.6, teinte claire, icône rendue.
- Gotcha build : référencer UnityEngine.AssetBundleModule (signatures de CustomPiece).

## v0.5 : bottes en peau de lapin et moineaux
- Bottes MeadowRabbitBoots : Valheim n'a pas d'emplacement pieds, ce sont donc des jambières (clone
  d'ArmorLeatherLegs, m_armorMaterial cloné et teinté). Armure 2 (+1/niveau), poids 1, m_movementModifier
  +0.05. Effet d'équipement SE_RabbitFeet (SE_Stats) : m_jumpModifier y +0.1, m_jumpStaminaUseModifier -0.25,
  m_fallDamageModifier -0.3. Établi niv. 1, 4 peaux + 2 chutes de cuir (+2 et +1 par niveau).
- Moineau MeadowSparrow : clone de `Crow` (RandomFlyingBird : vol, atterrissage, s'envole si un joueur est à
  moins de m_avoidDangerDistance), échelle 0.3, vol bas (2-7 m), virages vifs, m_randomNoise vidé (plus de
  croassements). DropOnDestroyed/CharacterDrop supprimés, y compris dans m_spawnWhenDestroyed (clone _death
  si besoin). Santé 1.
- Couleur : le corbeau est noir, une teinte multiplicative ne marche pas. La texture passe par le GPU
  (Graphics.Blit + ReadPixels, ce qui marche même sur une texture non lisible), puis sa luminance est
  remappée sur un dégradé brun moineau.
- Perchoir : patch prefix de RandomFlyingBird.FindLandingPoint (moineaux seulement). Raycast vers le bas sur
  24 points autour du point de spawn. Refusés : Heightmap (le sol), pente (normal.y < 0.7), hauteur au-dessus
  du sol < 0.5 m, eau, joueur proche. Sans perchoir trouvé, le code vanilla continue de voler.
- Spawn : CreatureManager.SpawnList de Jotunn est interne, donc SpawnSystemList maison ajoutée à chaque
  SpawnSystem dans un postfix de SpawnSystem.Awake. Prairies, de jour, max 4, groupes 1-3, GroundOffset 6 (+0-3)
  pour apparaître en l'air.
- Chants : fal refuse ("User is locked. Reason: Exhausted balance"). Chants synthétisés à la place avec
  tools/synth_sparrow.py (numpy : glissandos 2.8-7 kHz, trilles, gazouillis FM, écho), 6 WAV 16 bits mono
  dans BepInEx/plugins/ThrowingAxe/sfx/. Lecture RIFF maison puis AudioClip.Create. AudioSource 3D (3-35 m)
  sur AudioMan.m_ambientMixer (suit le volume ambiant). Toutes les 3-8 s posé, 8-16 s en vol, rarement la
  nuit, pitch 0.92-1.1. Spectrogrammes vérifiés visuellement.

## Test 5 (v0.5) -> v0.5.1
- Retours : chants trop fréquents et un peu trop forts ; bottes trop puissantes (pas d'armure, seulement la
  vitesse et un peu de saut).
- Chants : intervalle moyen 11 s posé (le double de l'ancien 5.5 s), x2.5 en vol (tirage entre 0.55 et 1.45 fois
  la moyenne), volume 0.7 -> 0.5. Réglables à chaud : [Sparrow] SongInterval et SongVolume.
- Bottes : armure 0, m_maxQuality 1 (rien à améliorer), vitesse +5 % (m_movementModifier), SE réduit à
  saut +8 %. Plus de réduction d'endurance de saut ni de dégâts de chute.
