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

## v0.6 : moineaux colorés (touches de bleu et de rouge)
- Crow : texture crow_d de 64x64, presque uniformément noire, sans anatomie lisible. Deux maillages : posé
  (MeshFilter "Cube", 394 faces, y vers le haut, bec en -z) et en vol (SkinnedMeshRenderer "Cube.001",
  280 faces, en diagonale dans la pose de bind). **Ils partagent le même dépliage UV.** Extraits avec UnityPy
  depuis valheim_Data/StreamingAssets/SoftRef/Bundles (le bundle qui contient crow_d), en lecture seule.
- tools/paint_sparrow_mask.py : rasterise chaque triangle dans l'espace UV (256x256) et colore chaque pixel
  selon le point 3D interpolé : calotte bleue, joues chamois, gorge et poitrine rouges, ventre chamois, dos
  brun, aile repliée brune avec rémiges bleues, queue brun foncé, pattes et bec. Les ailes déployées (îlots
  propres au maillage en vol) sont brunes avec les pointes bleues. Dilatation de 6 px contre les coutures.
  Aperçu rendu et vérifié. Le PNG ne contient que nos couleurs ; les maillages sont extraits de
  l'install du joueur au build, jamais committés.
- En jeu : masque (sparrow_colors.png à côté du DLL) x ombrage du crow (luminance normalisée, 0.75-1.25).
  Repli sur le dégradé brun si le masque manque. Référence ImageConversionModule pour LoadImage.

## v0.7 : renards, set de chaman, moineaux sans bleu, lapins plus petits
- Moineaux : plus de bleu (calotte gris-brun, rémiges et bouts d'ailes brun foncé), poitrine rouge gardée.
- Lapins : échelle 0.85 -> 0.75 (modifiée aussi dans le .cfg existant).
- Renard MeadowFox : CustomCreature clone de Wolf (MonsterAI), échelle 0.5, teinte rousse, Tameable et
  Procreation supprimés, santé 20, course 9 (lapin 9.45 avec zigzag), m_attackPlayerObjects false,
  m_fleeIfLowHealth 0.4, sans hurlement (m_idleSound vidé). Morsure : chaque m_defaultItems est cloné
  (MeadowFox_<nom>) et ses dégâts ramenés à BiteDamage (8) ; les loups gardent les leurs. Spawn Prairies,
  20 %, toutes les 240 s, max 1.
- Ennemis : la faction AnimalsVeg est ennemie de tout (switch de BaseAI.IsEnemy), donc postfix sur IsEnemy.
  Pour un renard : ennemi = lapin, ou celui qui l'a frappé dans les 20 s (MonsterAI abandonne une cible
  non ennemie, ligne 300, d'où la mémoire de provocation, prefix sur MonsterAI.OnDamaged). Pour un lapin,
  le renard est un ennemi, donc il le fuit. Les autres gardent la réponse vanilla : le joueur peut chasser
  le renard.
- Renard pas provoqué et joueur à moins de KeepAwayDistance (10 m) : postfix sur MonsterAI.UpdateAI qui
  appelle BaseAI.Flee (le dernier MoveTo l'emporte).
- Registres statiques RabbitTag.All et FoxTag.All (HashSet<Character>) : pas de GetComponent dans IsEnemy.
- Butin : MeadowFoxMeat (clone de WolfMeat), MeadowFoxCooked (clone de CookedWolfMeat : 28 PV, 10 endurance,
  1200 s, broche 25 s), MeadowFoxPelt (clone de WolfPelt, sinon DeerHide, teinté).
- Coiffe MeadowFoxHeaddress : clone de HelmetBronze dont l'enfant `attach` est remplacé par celui de
  TrophyWolf. Le maillage du trophée est **lisible** (UnityPy m_IsReadable True) : triangles de la mâchoire
  supprimés en jeu (centroïde y < -0.03 et z > 0.02, seuils calibrés hors jeu sur l'OBJ exporté).
  VisEquipment instancie `attach` sur l'articulation de la tête (position locale 0) et ne fait que
  désactiver les colliders. FoxHeadTuner applique la pose depuis la config, à chaud : [FoxHeaddress]
  Offset, Rotation, Scale. Les renderers d'origine du casque sont désactivés (les colliders sont gardés pour
  l'objet au sol).
- Cape MeadowFoxCape : clone de CapeWolf (déjà une tête de loup sur l'épaule) teinté roux.
- Coiffe et cape : armure 0, pas d'améliorations, résistances et set vidés. Chacune a son SE_Stats (deux
  objets distincts) : régén. d'endurance x1.1, Unarmed +10 niveaux, dégâts à main nue x1.1 (cumulables).
  Établi : coiffe 3 peaux + 2 chutes de cuir, cape 5 peaux + 2 chutes.

## v0.8 : chaque créature a ses propres pièces, glapissements, corbeaux de la Forêt Noire
Retours : moineaux qui volent dans le sol ; coiffe à retirer ; le renard hurle comme un loup et enchaîne les
morsures ; renard un peu plus gros ; « des modèles différents, pas des modifications a posteriori » (à la mort
d'un renard, c'est le ragdoll du loup qui apparaît) ; cape +1 armure ; lapin plus petit ; corbeaux qui croassent
dans la Forêt Noire.
- **Cause du ragdoll** : Character.OnDeath crée m_deathEffects, qui contient un prefab Ragdoll séparé
  (Wolf_Ragdoll, Hare_ragdoll) resté vanilla, gris et à taille réelle. Look.OwnRagdolls le clone, le peint et
  force m_inheritParentScale (EffectList.Create recopie alors le localScale de la créature mourante).
- Look.cs (commun) : Paint(go, mask), qui clone chaque matériau et applique la texture masque x ombrage ;
  OwnRagdolls ; MakeSfx, qui clone un prefab ZSFX vanilla en remplaçant m_audioClips par nos clips ;
  Voice(list, sfx), qui remplace les sons d'une EffectList en gardant les autres effets ; LoadWav.
- tools/paint_masks.py remplace paint_sparrow_mask.py : un masque par créature, peint dans l'UV du modèle de
  base d'après sa géométrie. Orientations : Crow posé (haut y, bec -z), Wolf (haut x, tête -z, côtés y),
  Hare (haut z, tête +y, côtés x). Renard : roux, chaussettes noires, poitrail, gorge et bout de queue blancs,
  pointes d'oreilles et truffe noires. Lapin : gris-brun, ventre crème, queue blanche, pointes d'oreilles
  sombres. Aperçus vérifiés. L'export UnityPy ignore les bundles dont les maillages sont externes
  (FileNotFoundError).
- Glapissements : tools/synth_fox.py (fondamentale de 500-1200 Hz glissante, harmoniques, 2 formants, peu de
  souffle ; un premier essai trop bruité a été corrigé après lecture du spectrogramme). call x4 pour le cri
  au calme et l'alerte (BaseAI.m_idleSound toutes les 25 s, chance 0.3), hurt x2, attack x2 (caquètement),
  death x1.
- Morsure unique : le loup a plusieurs attaques et enchaîne en combo. On garde un seul item d'attaque (le
  premier sans chaîne), avec m_attackChainLevels 1 et m_aiAttackInterval 2.5 s. Dégâts toujours ramenés à 8.
- Renard échelle 0.6, lapin 0.68 (les deux aussi modifiés dans le .cfg), cape de renard armure 1.
- Coiffe retirée (code, recette, traductions, section [FoxHeaddress] du .cfg).
- Oiseaux (Birds.cs remplace Sparrows.cs) : composant PerchBird pour les patchs perchoir et altitude.
  Postfix sur RandomFlyingBird.CustomFixedUpdate : en vol, y >= max(sol, eau) + MinClearance (2 m, réglable à
  chaud), avec une remontée douce, sauf dans les 4 derniers mètres (horizontaux) avant un perchoir.
  m_minAlt 3. Le nuage de plumes de mort du moineau est cloné et recoloré en brun.
- Corbeau BlackForestCrow : clone de Crow, croassements vanilla (m_randomNoise toutes les 6-14 s), plumes
  gardées, perchoir uniquement, Forêt Noire, de jour, max 3.
- Au premier SpawnSystem.Awake, le log liste les spawners vanilla de la Forêt Noire, pour vérifier la liste
  des créatures.

## v0.9 : chouette, mulots, corbeaux audibles, moineaux au repos la nuit
Retours : pas de souci de performance (cause extérieure, le profileur reste en place) ; corbeaux inaudibles ;
moineaux qui doivent se percher en hauteur la nuit et se taire ; une chouette cachée en hauteur le jour,
active la nuit, qui chasse lapins et mulots et hulule ; des mulots minuscules qui donnent rarement de la
viande. fal à utiliser avec parcimonie : tout a été synthétisé, aucun crédit fal utilisé.
- Sons (tools/synth_birds.py) : croassements (fondamentale de 480-620 Hz descendante, 17 harmoniques,
  formants ; le premier essai trop rugueux a été corrigé après lecture du spectrogramme), hululements de
  hulotte (quasi sinus de 340-410 Hz, motif long « hou… hou-hou-hououou » avec trémolo alterné avec un motif
  court), couinements de mulot (4.5-6.5 kHz).
- BirdVoice (remplace SparrowSong) : voix par espèce (clips, intervalle, volume, portée, heures actives) ;
  m_randomNoise vanilla vidé. Corbeau : de jour, volume 0.8, portée 60 m. Chouette : de nuit, portée 70 m.
  Moineau : de jour seulement (avant, il chantait 15 % du temps la nuit).
- Repos (BirdBehaviour, ex-BirdClearance) : PerchBird.RestAtNight (moineau, corbeau) / RestByDay (chouette).
  Au repos, m_landDuration vaut 1e6 (seul un danger fait décoller) ; en vol, RandomizeWaypoint(true) toutes
  les 2 s. BirdPerch au repos : 40 tirages, perchoir le plus haut, au moins 2.5 m au-dessus du sol.
  m_noRandomFlightAtNight false (on gère nous-mêmes).
- Remodelage (maillages du corbeau lisibles) : bec et queue raccourcis, corps et tête élargis, dans les repères
  posé (haut y, bec -z) et en vol (haut z, bec +y, les ailes déployées ne sont pas élargies). Chouette : bec
  0.3, queue 0.45, largeur 1.3, tête 1.25. Moineau : bec 0.55, queue 0.85, largeur 1.1.
- Chouette MeadowOwl : masque fauve (disque facial pâle, poitrine striée, ailes et queue barrées, moucheture).
  OwlHunter : de nuit, toutes les 3 s, cherche le lapin ou le mulot le plus proche à moins de 40 m, plonge
  (waypoint au sol suivi en continu, l'altitude minimale est levée dans les 4 derniers mètres), frappe à
  1.2 m (25 perforants, réglable), puis redécolle avec un délai de 20-40 s. Les proies ne fuient que les
  Character, donc la chouette les surprend. Prairies et Forêt Noire, max 1.
- Mulot MeadowMouse : clone de Hare à l'échelle 0.22, os mis à l'échelle (Ear.l/r 0.55/0.45/0.55,
  Tail 0.5/3/0.5, Head 1.15), y compris sur le ragdoll cloné. Masque brun, ventre pâle, oreilles et queue
  rosées. Santé 3, course 6.5, fuite en zigzag (0.4 s, 80°), viande 15 % (MeadowMouseMeat, cuite 12 s :
  8 PV, 10 endurance, 600 s). Couinements sur coup, mort et alerte. Patch SettleAfterFlee étendu aux mulots.

## v0.10 : vrais modèles 3D pour la chouette et le mulot (fal, budget minimal)
Demande : des modèles plus fidèles à une chouette et à un mulot, fal avec parcimonie, mulots deux fois plus
gros.
- Crédit fal de nouveau disponible (check_account_status : ready). Modèles légers : fal-ai/flux/schnell
  (0.003 $/MP, 512 px, soit environ 0.001 $ l'image) et fal-ai/trellis (0.02 $, texture 512,
  mesh_simplify 0.98). 8 images + 3 modèles 3D, environ 0.07 $ au total. Traçabilité :
  assets/gen/fal_manifest.jsonl.
- Concepts : chouette posée (graine 7), en vol (graine 33 ; la graine 11 avait les ailes en V), mulot
  quadrupède de profil (graine 19 ; le premier essai avait 2 têtes et une posture humaine). Prompt :
  « low poly stylized 3D game asset, faceted flat-shaded… plain white background ».
- Trellis : chouette posée 4646 triangles, en vol 2963, mulot 2548, tous avec y en haut et la face vers +z.
- tools/build_models.py : GLB -> .tam (TAM1, positions, normales, UV, indices), passage en main gauche
  (x miroir, sens des triangles inversé), texture 512 px. Fichiers dans BepInEx/plugins/ThrowingAxe/models/.
- Models.cs :
  - OwlSitting : modèle statique à la place du maillage posé du corbeau (MeshFilter), retourné de 180°
    (le corbeau regarde vers -z), mis à l'échelle de la hauteur, pieds en bas.
  - OwlFlying : repère cible tiré du corbeau (axe des ailes par les os l_wing/r_wing, axe du corps par ACP
    des sommets du corps, tête du côté le plus étroit car la queue s'évase). Repère source tiré du modèle
    (gauche -x, axe du corps par ACP, tête en haut). Rotation repère vers repère, échelle sur l'envergure.
    Skinning : ailes sur les os d'aile de leur côté, corps sur les autres os. FlipFlyingModel en secours.
  - MouseOnHare : repère tiré des pieds (normale du plan des 4 pieds, avant = des pieds arrière vers les
    pieds avant). Le premier essai, avec haut = pieds -> hanches, cabrait le mulot de 36° parce que le lièvre
    est voûté ; repéré par la simulation hors jeu. Échelle : base de la queue -> tête du mulot calée sur
    Tail -> Head du lièvre, pieds au sol. Skinning par région du modèle (queue, oreilles g/d, tête,
    4 pattes, colonne), 2 os les plus proches par distance au segment, poids en 1/d².
  - Nos textures remplacent celles du jeu, avec _BumpMap vidé (UV différentes). Même chose sur le ragdoll du
    mulot. Repli sur l'ancien rendu (remodelage et peinture) si un modèle manque.
- Vérification hors jeu : squelettes (noms des os, poses de liaison, positions locales des enfants) extraits
  avec UnityPy, alignement et régions rejoués en Python et rendus en superposition : mulot à plat sur ses
  4 pattes, chouette alignée sur les os du corbeau.
- Mulot : échelle 0.22 -> 0.44 (modifiée aussi dans le .cfg).

## Test piloté par l'agent (um win), 2026-10-03
- `um win shot` échouait : le ffmpeg 6.0.1 de miniconda passe devant dans le PATH et n'a pas gfxcapture. Solution :
  UM_FFMPEG_WIN=<ffmpeg 9.0.2 WinGet>.
- Lancer via Steam avec des arguments déclenche l'invite Steam « Lancer le jeu avec des arguments
  personnalisés » ; l'agent ne la valide pas à la place de l'utilisateur (Annuler) et lance valheim.exe
  directement (steam_appid.txt présent). Dans ce cas la console BepInEx devient la MainWindow du processus,
  donc WinDrive visait la console : [Logging.Console] Enabled = false dans BepInEx.cfg.
- La souris relative (`rel`) fait tourner la caméra ; il faut de grandes valeurs (500-1200).
- Les captures fixes `um win shot` paraissent très surexposées, alors que les images extraites de `um win
  record` ont des couleurs normales : la surexposition est un artefact de la capture fixe, pas du jeu.
- Scène de test : commandes console `ta_show <prefab> [distance] [hauteur] [fly] [front]` (créature figée
  devant la caméra : IA coupée, sans gravité, modèle posé ou en vol pour les oiseaux) et `ta_clear`.
- Observé : le mulot généré est reconnaissable (grosses oreilles, œil noir, museau, corps ramassé) ; posture
  voûtée = idle du lièvre. La longue queue sur le seul os Tail se relevait en « aileron », d'où ce correctif :
  la moitié distale de la queue est liée à Hips, la base à Tail + Hips (pas encore vérifié en jeu). La
  chouette posée et en vol est reconnaissable (disque facial, bec crochu, ailes qui battent sur les os du
  corbeau).
- Cape de renard : fort reflet blanc à l'épaule (là où sont la tête de loup et le fermoir en métal,
  WolfCapeChain), qui disparaît quand on l'enlève. À traiter : ne pas teinter le métal, vérifier la
  brillance.
- Correctif de la cape : TintCape teinte seulement la fourrure ; le matériau WolfCapeChain (métal) garde sa
  couleur. Les paramètres flottants des shaders (brillance, etc.) de chaque matériau sont écrits dans le log
  pour pouvoir vérifier. Correctifs queue et cape installés ; c'est Lekinox qui teste.

## v0.11 : labo de modèles, overlay des créatures, vol des oiseaux plus souple
- Labo (Lab.cs) : on dépose BepInEx/plugins/ThrowingAxe/lab/request.txt (« <prefab> [images] [tuile] » ou
  « refit ») ; le jeu, même au menu principal et sans aucune entrée, rend <prefab>_bind.png (côté | face |
  dessus) et un PNG par animation (ligne de côté, ligne de face, N images), avec les os en jaune, puis écrit
  done.txt. Copie inerte : instanciée sous un parent inactif, MonoBehaviours détruits (ZDOMan est nul au
  menu), LOD coupés, calque 31, caméra orthographique et lumière dédiées, positionnée à y = 9000. Commande
  console ta_lab équivalente.
  **Gotcha** : Unity skinne une seule fois par frame ; rendre plusieurs échantillons
  (AnimationClip.SampleAnimation) dans la même frame montre toujours la même pose (les os bougent, pas le
  maillage). Le rendu est donc une coroutine qui attend 2 frames par échantillon.
- [MouseFit] Scale / Lift / Forward / Pitch (relus par « refit » ou au redémarrage). Valeurs actuelles :
  1.15 / 0 / 0.05 / -12.
- Diagnostic du mulot dans le labo : le squelette du lièvre a des pattes arrière immenses, se dresse en
  alerte et pique du nez à la course.
  Corrections : pattes du mulot liées seulement aux cuisses/épaules (plus de pattes étirées en baguettes
  vers les pieds du lièvre), queue en 3 segments (Tail+Hips, Hips+Root, Root : elle traîne derrière au lieu
  de pendre ou de se dresser), tête sur Neck+Spine2. Reste : tête basse à la course, torsion en alerte. Le
  vrai remède serait un squelette plus proche d'un mulot (celui du loup, ou un recalage des os du lièvre :
  pattes raccourcies et liaison sur une pose neutre).
- Oiseaux : fini le plancher invisible. On regarde devant soi (sol devant et à mi-chemin, sur
  ~0.9 x vitesse) et on relève le nez progressivement (60-240°/s selon l'urgence) dès que la trajectoire
  projetée passe sous sol + MinClearance + 1.5 m. Les waypoints de croisière sont remontés au-dessus du
  dégagement. Le recalage de position ne sert plus qu'en dernier recours (0.3 m). Pas encore vu en jeu.
- Overlay (Overlay.cs) : ta_overlay [filtre|all|off] [portée=100], avec cadre, nom et distance à travers les
  décors (OnGUI, projection des bounds des renderers), nos créatures en vert ; Character.GetAllCharacters() et
  RandomFlyingBird.Instances. Pas encore vu en jeu.
- Références ajoutées : assembly_guiutils (Localization), IMGUI, TextRendering, AnimationModule.

## v0.12 : séparation en deux mods ; corbeaux vanilla ; correctifs du renard ; nouvelles zones
- **Deux mods** (2026-10-03) :
  - valheim-wildlife (ce dépôt, ex-valheim-throwing-axe) : GUID lekinox.wildlife, Wildlife.dll,
    BepInEx/plugins/Wildlife/, config lekinox.wildlife.cfg. La faune, les loots et les recettes associées.
  - ../valheim-legendary-weapons : GUID lekinox.legendaryweapons, LegendaryWeapons.dll,
    BepInEx/plugins/LegendaryWeapons/, config lekinox.legendaryweapons.cfg. La hache de retour (prefab
    AxeThrowing inchangé).
  - L'ancien lekinox.throwingaxe.cfg a été copié vers les deux nouveaux et gardé en .bak ; plugins/ThrowingAxe
    a été supprimé.
- Corbeaux : BlackForestCrow supprimé ; le Crow vanilla est ajouté à nos spawns de la Forêt Noire (le jeu
  n'en fait pas apparaître). Le spawner dump confirme les créatures vanilla de la Forêt Noire : Deer,
  Greydwarf (+Elite, Shaman), Bjorn, Troll, Skeleton (après Bonemass), Draugr (après Elder, brume), Goblin
  (après Yagluth), Seeker, Charred, événements Fimbulvinter, FireFlies, Odin, plus mouettes et poissons.
- Zones : mulots Prairies + Forêt Noire ; chouettes Prairies + Forêt Noire ; moineaux Prairies + Plaines.
- Renard :
  - Hurlement : MonsterAI s'endort (m_fallAsleepDistance) et joue m_wakeupEffects (le hurlement) au réveil.
    m_fallAsleepDistance = 0 et m_sleeping = false ; wakeup/sleep, jump/slide et consumeItem passés à
    notre voix ou rendus muets.
  - Faim : FoxTag chasse 60-120 s puis se repose 150-300 s ; une proie tuée (cible morte) le rassasie.
    IsEnemy(renard, x) = (faim && lapin/mulot) || provocation.
  - Zigzag sur place : l'ancien postfix laissait MonsterAI se déplacer puis imposait Flee chaque frame. Il
    est remplacé par un prefix qui, quand un joueur menace (hystérésis 10/16 m), exécute BaseAI.UpdateAI en
    appel non virtuel (AccessTools.MethodDelegate virtualCall=false) puis Flee, et saute la décision de
    MonsterAI. Errance plus calme (8 s / 10 m), fuite rectiligne (20 m, 25°, 2.5 s).

## v0.13 : squelettes sur mesure (mulot) et cétacés (baleine, orque)
- ProcRig.cs : squelette construit depuis la géométrie du modèle généré (articulation = centroïde des
  sommets d'une région du corps), skinning sur nos os (2 os les plus proches par distance au segment,
  restreints à la région), bindposes = translation inverse, SkinnedMeshRenderer sous un enfant
  « Visual_rig ». Les animateurs retrouvent leurs os par leur nom dans Awake (les dictionnaires ne sont pas
  sérialisés par Instantiate).
- Mulot : le Hare reste dessous (IA, physique, réseau, Animator) mais ses renderers et son LODGroup sont
  désactivés. 27 os : Root, Hips, Chest, Neck, Head, Nose, Ear/EarTip L/R, 4 pattes x 3 (épaule/hanche,
  genou, pied), queue x 5. ProcQuadruped : vitesse mesurée sur le déplacement réel (marche aussi pour les
  autres clients) ; trot (paires diagonales) sous 3.5 m/s, galop bondissant au-dessus (paire avant puis
  arrière, flexion du dos), balancement vertical, tête stable, reniflements et regard alentour à l'arrêt,
  frémissements d'oreilles, queue en retard par segment. Plus de ragdoll de lièvre : un cadavre maison
  (maillage couché sur le flanc, SelfDestruct 12 s) ; le butin tombe tout de suite. [MouseRig] Enabled /
  Length (0.55 m à l'échelle 1, avant l'échelle de la créature 0.44).
- Baleine (OceanWhale, 14 m) et orque (OceanOrca, 7 m) : modèles fal (flux schnell + trellis, environ 0.05 $),
  orque réorientée de 90° dans build_models.py (générée le long de x). 13 os : 8 vertèbres de la tête à la
  queue, Fluke, nageoires pectorales. ProcSwimmer : onde verticale qui grandit vers la queue (amplitude
  baleine 10°, orque 15°), battement de nageoires. SeaSwimmer (piloté par le propriétaire, ZSyncTransform) :
  croisière en profondeur entre des points assez profonds, demi-tour devant les hauts-fonds, remontée pour
  respirer (baleine toutes les ~50 s, orque ~25 s), sauts hors de l'eau pour l'orque (balistique), jamais
  hors de la mer. Spawn Océan : baleine seule (profondeur > 16 m, max 1), orques en groupes de 2 à 4
  (> 9 m, max 3). Pas encore de points de vie.
- Labo : garde les IProcAnimated et rend une planche par vitesse (<prefab>_proc_<vitesse>.png) ; ne cadre et
  ne dessine que les renderers actifs (le lièvre caché sous le mulot faussait le cadrage).
- Vérifié au labo : mulot à l'arrêt, au trot et au galop crédibles ; baleine et orque texturées qui ondulent.
  Pas encore vu en monde ouvert : spawns marins, nage, mulot en mouvement réel, cadavre, renard
  (faim, mulots, fuite), corbeaux vanilla, overlay, vol des oiseaux.

## v0.13.1 : correctifs des modèles (mulot x2, onde de l'orque, nouvelle baleine)
- Mulot : [Mouse] Scale 0.44 -> 0.88 (défaut et .cfg).
- Orque, « petit problème de squelette » : les rotations des vertèbres s'additionnent le long de la chaîne, et
  donner à chaque vertèbre la courbe entière repliait la queue. Désormais chaque vertèbre n'ajoute que sa
  part (bend(k_i) - bend(k_i-1)) ; Amplitude = courbure totale au bout (orque 18°, baleine 14°). Battement
  des pectorales réduit à ±5° (il entraînait un bout du ventre).
- Baleine (modèle, texture et animation à revoir) : l'ancienne génération trellis avait la queue recourbée
  dans le maillage et une texture tachée. Nouveau concept flux (graine 27 : corps droit, caudale à plat,
  longues pectorales, vue 3/4 surélevée). Trellis 2 brut = 94 772 faces ; la décimation pymeshlab avec
  préservation de texture plafonnait (46 k, puis 8.6 k en passes assouplies) et hérissait le dos d'éclats.
  Solution : Trellis 2 avec --faces 5000 --texture 1024, d'où 4 824 faces propres (couchée le long de x,
  tournée de 90° dans build_models.py). Coût fal de cette passe : environ 0.15 $ (2 images, 2 Trellis 2).
- Vérifié au labo : baleine lisse et texturée qui ondule ; onde de l'orque sans repli.

## v0.14 : cétacés retravaillés, souffle de l'évent
- Orque : le « bug » entouré sur la capture de Lekinox (lambeau sous l'arrière du ventre) venait du modèle
  trellis v1 (seconde pectorale mal reconstruite), pas du squelette. Nouveau concept flux (graine 31,
  symétrique, vue 3/4 surélevée) puis Trellis 2 --faces 5000 : 4 960 faces, déjà orientée +z.
- Baleine : couleurs distinctes de l'orque grâce à RECOLOR["whale"] dans build_models.py (le bleu-noir devient
  un gris ardoise plus clair et chaud, avec des marbrures douces ; le bruit, d'abord agrandi par blocs, a
  laissé des carrés, d'où un agrandissement bicubique ; les blancs sont gardés).
- Animation moins raide (ProcSwimmer réécrit), en couches : onde verticale (exposant 1.25-1.45 pour que plus
  de corps participe), ondulation horizontale lente, flexion du corps dans les virages (taux de lacet
  mesuré), roulis à la racine, caudale plus libre (baleine x1.9), cambrure tête haute pendant le souffle.
  Trajectoire : SeaSwimmer serpente autour du cap (deux sinus lents, ±25-35°).
- Spawns plus rares : un test toutes les 15 min, baleine 10 %, orque 12 % (groupes de 2-3) ; réglables
  [Whale]/[Orca] SpawnChance.
- Évent : enfant « Blowhole » sur Spine1 (sommet de la tête, point le plus haut à z 0.7-0.88), ParticleSystem
  en rafales (cône vers le haut, espace monde, matériau de vapeur pris à fx_FoodSteam de CookedDeerMeat,
  alpha qui décroît, taille qui grandit, vitesse limitée/amortie). Un premier réglage (gravité 0.45, vie
  2.4 s) faisait retomber la brume sous l'animal ; désormais gravité 0.08, vie 1-1.8 s, limite à 0.5 x la
  vitesse, amortissement 0.18. Son : tools/synth_sea.py (souffle grave de ~2 s avec grondement pour la
  baleine, « pfff » de ~0.7 s pour l'orque), 3 variantes chacun. Déclenché par le propriétaire quand
  l'animal remonte (y > eau - 1.3) puis RPC WL_Spout à tout le monde (particules, son, cambrure).
- Labo : planche <prefab>_spout.png (simulation des particules dans le temps).
- Coût fal de cette passe : environ 0.06 $ (2 images + 1 Trellis 2).

## v0.15 — 2026-10-03
- Lab: 12 frames, 5 views (side, front, top, 3/4 front, 3/4 back), flashy green background, renderer dump in _index.txt; waits 2 frames after spawning (bounds were stale → fish framed off-screen).
- Swimmers: smooth spine weights + differential bend, whipping fluke; whale/orca amplitudes raised.
- Whale: texture painted from geometry, 4 zero normals fixed, 145 stray fragments (340 faces) removed, fins/flukes given real back faces.
- Sea: leash to the closest player + claiming orphaned ZDOs (whales stopped when ownership dropped) — untested in game.
- Vanilla fish: Fish1-3 on a 6-joint spine, Fish4-12 on a swaying pivot; soft references loaded so materials resolve. Verified in the lab from 5 angles.

## v0.15.1 — 2026-10-03 (test piloté par l'agent)
- Crash natif au chargement du monde Laboratory : ZNetScene.CreateObject -> Instantiate de Fish12 (poisson-globe,
  3 maillages dont 2 inactifs). Trouvé avec un journal CreateTrace (lab/created.txt, flush à chaque objet).
  Cause : le pivot oscillant déplaçait les maillages sous un nouvel objet dans le prefab. Correctif : plus aucune
  modification de hiérarchie, le nœud attachobj tourne en place autour d'un point derrière la tête.
- Banc d'essai `ta_sea goto|spawn|log` et lignes `cmd <commande>` dans lab/request.txt (commandes sans clavier).
- Persistance vérifiée : 9 créatures marines (2 baleines, 7 orques) nagent sans arrêt pendant plus de 8 min
  (1,7-3 m/s, 12-62 m du joueur, toutes possédées localement).
- Retour de Lekinox : « encore beaucoup trop raides ». Amplitude baleine 30 -> 62°, orque 28 -> 52°, peu
  réduite à vitesse de croisière (80 % au lieu de 55 %), flexion plus étalée (exposant 1.6), tangage du corps en
  contre-phase (4-5°). Vérifié au labo sous 5 angles, sans pliure.

## v0.16 — 2026-10-03 : nage des cétacés refaite, baleine reconstruite (évaluée par un 2e agent)
Références (Fish & Rohr, Strouhal des odontocètes ; tags de baleines à bosse) : flexion sur le tiers arrière, nageoire
caudale 15-25 % de la longueur crête à crête, pas ±20-30° en quadrature, Strouhal 0.2-0.4, baleine à bosse ~0.2 Hz.
- Le labo ne montrait que 1,2 s par planche (un tiers de battement de baleine) : il couvre maintenant un battement
  complet. Lumière d'appoint, ambiance neutre, globals du shader (_AmbientColor, _SunColor) neutres et lumières du
  menu exclues pendant le rendu : la « teinte brune » et la « nageoire noire » venaient du coucher de soleil du menu.
- Animation : pilotée par la ligne médiane y(s,t) = A(s) sin(phase - k s), A croît comme le carré de la distance à
  partir de 45 % (baleine) / 50 % (orque) et culmine au pédoncule ; k ≈ 6 rad/longueur (onde progressive) ; nageoire
  en « feathering » à ±22° de la trajectoire (une erreur de signe la faisait se dresser), pivot réparti 40/60 sur les
  deux dernières articulations ; tête en translation (~3 %), roulis en virage ; fréquence par Strouhal 0.37.
  Mesuré : queue 19 % de la longueur crête à crête, museau 2-3 %.
- Squelette : articulations resserrées vers l'arrière, articulation Fluke au pédoncule (point le plus étroit,
  détecté), peau répartie sur 4 articulations (plus de pli à l'intérieur de la courbe).
- Modèle baleine (build_models) : queue redressée (le modèle généré plongeait, -11 %), galettes remplacées par une
  nageoire en croissant procédurale (envergure 31 %, pointes, encoche, bord festonné, îlots UV dessus/dessous),
  nageoires pectorales x1,9 en flèche de 35°, abaissées de 20°, effilées, à tubercules ; normales soudées aux coutures ;
  flanc rendu symétrique (pan du maillage généré). Matériau : la gloss map du lièvre (autres UV) est retirée, normales
  double face activées ; tangentes réparées (SafeTangents).
- Évaluation par un 2e agent (vraisemblance, seuil 7,5) : 5,0 -> 6,5 -> 7,0 -> 7,5 (baleine 7,5, orque 8,0, modèle 7,5).

## v0.17 — 2026-10-04 : baleine refaite (Rodin), rig + cycle de nage faits dans Blender, vérifiés par étapes
Bilan de Lekinox sur la v0.16 : échec (défauts du modèle sous certains angles, animations peu convaincantes,
vérifications insuffisantes). Nouveau pipeline, avec une porte de validation à chaque étape :
- Modèle : 2 références flux/schnell (profil + dessus, ~0,01 $) -> Hyper3D Rodin 2.5 « 18K Quad » (0,40 $) :
  une seule pièce fermée, manifold, sans auto-intersection. tools/blender_fix_whale.py (Blender en module Python,
  bpy 5.0.1 dans ~/tools/blender-env) : soudure des coutures UV, symétrisation (côté sans poche
  miroité), nageoires en palettes (corde mise à l'échelle avec la longueur, bout arrondi, courbure), lissage du
  corps, 12k triangles. Texture repeinte depuis la géométrie (motif baleine à bosse symétrique).
- Contrôles : tools/mesh_qa.py (fragments, arêtes ouvertes/non manifold, enroulement, auto-intersections, triangles
  aberrants, asymétrie, proportions de baleine à bosse) et tools/blender_turntable.py (16 azimuts x 3 hauteurs,
  dessous compris, texturé + matcap, lit directement le .tam du jeu).
- Rig : tools/blender_rig_swimmer.py construit squelette + poids depuis le .tam (même ordre de sommets que le jeu),
  échantillonne le cycle de nage en courbes (32 phases), exporte models/<nom>.rig (texte) et rend des aperçus MP4 +
  mesures (course de queue, épaisseur du pédoncule). Les poids « heat » de Blender échouent sur ces maillages : poids
  lissés le long de la colonne (noyaux en tente, 4 influences), nageoires sur leur os. Le C# (RigFile, ProcSwimmer)
  joue exactement ces courbes et ajoute vitesse, virages, roulis et souffle.
- Orque : modèle conservé, caudale mise à plat (lobes relevés au repos -> crochet), même pipeline de rig.
- Mesures finales : baleine queue 19,9 % de la longueur, pédoncule >= 95 % de son épaisseur, articulations <= 12,8°,
  caudale <= 14° ; orque 18,4 %, >= 94 %, <= 9,3°.
- Évaluateur (2e agent, vraisemblance, seuil 7,5) : modèle 6,5 -> 7,0 -> 7,5 ; animation 7,0 -> 7,5 (baleine 7,5,
  orque 7,5).
- Vérification en jeu : `ta_sea film [s] [whale|orca]` (caméra en orbite, animal près de la surface) + um win record ;
  les vidéos sont surexposées par la capture (pas par le jeu).
- Coût fal de l'étape : ~0,42 $.

## v0.18.0 (2026-10-04) : spawns plus rares et mieux répartis, chouette farouche, paix avec le vanilla
- Taux de spawn divisés par 2 (lapin 20 %, renard 10 %, mulot 25 %, moineau 25 %, chouette 25 %, baleine
  5 %, orque 6 %) et par 3 pour les corbeaux de la Forêt Noire (16,7 %). Nouvelles clés SpawnChance pour
  Sparrow, Crow, Owl et Mouse ; .cfg live mis à jour pour Fox, Rabbit, Whale et Orca.
- Effet « tout à l'entrée du biome » : SpawnSystem.UpdateSpawnList compte les ZDO de l'espèce dans les zones
  voisines (FindSectorObjects) et s'arrête à m_maxSpawned ; une zone jamais visitée fait d'un coup jusqu'à
  max tentatives (horloge à 0). Nos créatures, sauvegardées dans le monde, restaient là où elles étaient
  apparues. Ajout de FarDespawn (Spawns.cs) : le propriétaire supprime une créature à plus de 100 m de tout
  joueur pendant 30 s (hors apprivoisées ; pas les baleines/orques). [Spawns] DespawnDistance/DespawnDelay.
  Hypothèse à confirmer en jeu.
- Chouette : [Owl] ScareDistance = 20 m (8 avant), appliqué en live dans BirdBehaviour (m_avoidDangerDistance,
  qui sert aussi à refuser les perchoirs proches d'un joueur).
- Interactions.cs : postfix BaseAI.IsEnemy (priorité basse, après FoxEnemies). Créatures vanilla et nôtres
  (lapin, mulot, renard) s'ignorent. Exceptions : joueurs, apprivoisés, renard provoqué, notre chaîne
  alimentaire, et [Interactions] VanillaHunters (liste de prefabs, vide par défaut). Les oiseaux ne sont pas
  des Character : déjà ignorés.

## v0.19.0 (2026-10-04) : retour de playtest de la v0.18
- Disparition peu effective : le log ne montrait que 4 mulots supprimés. Le jeu n'instancie les objets qu'à
  environ 2 zones d'un joueur ; un animal laissé derrière était déchargé (ZDO sauvegardée) avant ses 30 s à
  plus de 100 m, donc jamais supprimé. Ajout d'un balayage serveur toutes les 20 s
  (ZDOMan.GetAllZDOsWithPrefabIterative, quelques secteurs par image) : les ZDO de nos espèces non chargées,
  sans propriétaire ou à nous, à plus de DespawnDistance de tout joueur, sont détruites (SetOwner puis
  DestroyZDO). Animaux apprivoisés exclus. DespawnDistance passe à 150 m (.cfg live modifié).
- Étoiles : le jeu tire 10 % par niveau, multiplié par le secteur de biome. Prefix sur
  SpawnSystem.GetLevelUpChance : nos espèces utilisent [Spawns] StarChance = 3 %.
- Lapins violets : les LevelEffects du lièvre décalent la teinte (_Hue) du matériau principal, donc de notre
  fourrure. Look.NaturalLevels (lapin, mulot, renard) : teinte 0, pas d'émissif ni d'objet de niveau,
  fourrure un peu plus sombre et saturée par étoile (+0,05 sat., -0,08 valeur). La taille reste. Les valeurs
  vanilla d'origine sont écrites dans le log (Debug). Les oiseaux n'ont pas de niveaux.
- Renard : il fuit au pas de course dès qu'il voit ou entend un joueur à moins de [Fox] FleeDistance = 30 m
  (ou à moins de 8 m), jusqu'à 10 m de plus. Cible de fuite à 25 m dans un cône opposé, élargi si de l'eau
  barre la route. KeepAwayDistance supprimé. Toujours : un renard frappé riposte.
- Renard à la nage : m_swimDepth du loup non mis à l'échelle (tête sous l'eau). Maintenant
  loup x échelle x 0,75, ou [Fox] SwimDepth. Valeur écrite dans le log.

## v0.20.0 (2026-10-04) : noms sans « Meadow », traductions relues
- Prefabs renommés (demande de Lekinox, qui recrée un monde : pas de migration) : Rabbit, RabbitHide,
  RabbitMeat, CookedRabbitMeat, RabbitBoots ; Fox, FoxMeat, CookedFoxMeat, FoxPelt, CapeFox ; FieldMouse,
  MouseMeat, CookedMouseMeat ; Sparrow, Owl ; Whale, Orca (ex-Ocean*). Tokens : enemy_fox, enemy_fieldmouse,
  owl, whale, orca. rug_rabbit inchangé.
- Vérifié contre la liste des prefabs vanilla (5 935 noms, extraite de
  valheim_Data/StreamingAssets/SoftRef/manifest_extended) : aucun conflit, y compris les noms dérivés
  (Fox_ragdoll, Owl_*...). RabbitMeat n'y figure pas : le « conflit » de la v0.3 venait sans doute d'un
  double enregistrement. À confirmer au premier lancement (log Jotunn « Failed to clone prefab »).
- Traductions EN/FR relues : description de la viande de lapin sans « prairies », infobulle de la cape de
  renard précisée (compétence Mains nues), formulations FR retouchées.

## v0.21.0 (2026-10-04) : idle de la chouette, cétacés calés sur les vagues, glisse de l'orque, claque de queue
Workflow d'animation de la v0.17 : rig + clips dans Blender headless, exportés en courbes (.rig) et joués tels
quels en C#, aperçus + mesures, note d'un 2e agent (seuil 7,5), puis vérification en jeu pilotée par l'agent.
- Chouette posée : tools/blender_owl_idle.py (rig géométrique Root/Body/Chest/Neck1-3/Head/WingL/R/Tail, poids
  lissés en hauteur, clips breathe (boucle), look, tilt, bob, ruffle ; même maths que Unity en numpy, donc
  l'aperçu = le jeu). Format .rig étendu : blocs `clip <nom> <images> <fps> <boucle>` / `key <os> <canal> ...` /
  `water` / `event` / `endclip`. QA : étirement des arêtes et triangles retournés par rapport à leur os.
  - Une rotation de tête de 90° sur un seul os de cou retournait 276 triangles (repli du skinning linéaire) ;
    3 os de cou placés dans la vraie bande du cou (0,60-0,75 de la hauteur ; plus bas ils tordaient les épaules
    contre les ailes) ; restent ≤ 7 éclats de triangles invisibles. Mon premier test de retournement était faux
    (comparaison aux normales au repos, alors qu'une tête qui tourne les fait tourner).
  - Évaluateur : 5,5 (bob invisible, corps qui suit trop, maintien figé, tilt faible, ruffle discret) -> 7,6.
  - Jeu : Models.OwlRig crée un SkinnedMeshRenderer « Visual_owl » sous le modèle posé du corbeau (le
    MeshRenderer statique reste, éteint) ; OwlIdle joue la respiration + un geste toutes les 2,5-7 s (amplitude,
    miroir, vitesse aléatoires ; tourne la tête vers un joueur à moins de 30 m). Vérifié en jeu (ta_show Owl).
- Cétacés, vagues : SeaSwimmer utilisait le niveau de mer fixe (30 m) ; mesuré en jeu, la surface réelle s'en
  écarte de -1,42 à +0,69 m. Désormais Floating.GetLiquidLevel sous l'évent : en surface l'animal suit la vague,
  l'évent sort de 0,15 m ; souffle quand l'évent est à moins de 0,2 m de la surface réelle (log : -0,19 à
  +0,18 m). Glisse et claque de queue tiennent la profondeur fournie par le clip.
- Orque, glisse (clip « glide ») : nage lente et peu ample, dos ~0,1 m sous l'eau, aileron dehors 0,9-1,0 m, petit
  roulis/pilonnement de tout le corps ; 6-9 s, respiration (remontée 0,15 m, souffle) toutes les ~6 s.
  [Orca] GlideChance 0,5 après un souffle.
- Baleine, claque de queue (clip « lobtail », 13,6 s) : tête à -24°, arc du tiers arrière, caudale jusqu'à 4,1 m
  hors de l'eau, 3 frappes (3,7 / 7,1 / 10,5 s, 15,7 m/s), caudale qui traîne puis se met à plat à l'impact,
  continuation dans l'eau. Événements « slap » : gerbe (embruns + anneau d'écume) et son synthétisé
  (tools/synth_sea.py, whale_slap1-3). [Whale] LobtailChance 0,35. RPC WL_Lobtail, mode dans la ZDO.
  - Évaluateur : glisse 6,5 -> 7,5, claque 6,5 -> 7,5 (caudale qui claquait à plat en 1 image, arrêt net,
    queue au-delà de la verticale, courbure en coin corrigés).
  - Vérifié en jeu (ta_sea act lobtail|glide|blow [whale|orca], ta_sea film).
- Brume (souffle et gerbe) : la texture vanilla slowwispysmoke_hard est un atlas 8x8 ; sans réglage d'atlas,
  chaque particule affichait toute la grille (vu en jeu comme un nuage de petits carrés, déjà présent sur le
  souffle). Le réglage textureSheetAnimation de l'effet source est recopié.
- **Correctif v0.20** : le jeu a bien un prefab RabbitMeat (« Failed to clone prefab, name already exists ») et
  l'enregistrement des lapins échouait entièrement. Viande crue renommée RawRabbitMeat. Leçon : le manifeste
  SoftRef ne liste pas tous les prefabs ; seul le log de chargement fait foi.

## v0.22.0 (2026-10-04) : surfaces fiables, cétacés chassables, nouvelles voix, 21 recettes
Retour de Lekinox : glisse et claque de queue jamais vues en jeu naturellement ; sons des orques, baleines et
chouette à retravailler ; au moins 3 recettes par animal (débutant, mi-parcours, fin de partie).
- Cause probable : le choix glisse/claque ne se faisait qu'à l'image exacte où la fenêtre de surface (10 s baleine,
  5 s orque) expirait, et seulement si l'animal avait déjà soufflé ; la baleine (7 m de fond, 2,4 m/s, 12°/s) mettait
  ~8 s à remonter, le souffle arrivait au ras du seuil (les logs le montraient toujours à -0,18/-0,20 m) et les
  remontées sans souffle ne laissaient aucune trace. Le saut de l'orque (tiré à chaque image) mangeait aussi la glisse.
  Les tests `ta_sea film` mettaient la profondeur à 2,5 m, d'où des remontées rapides au banc d'essai seulement.
- SeaSwimmer réécrit en étapes explicites : remontée (jusqu'au souffle, 45 s max, souffle forcé après 25 s en
  surface) -> respiration (baleine 2-4 souffles espacés de ~6 s, orque 1-2 à ~3,5 s, nage lente) -> final tiré une
  seule fois : orque saut 25 % / glisse 45 % / plongée, baleine claque 50 % / plongée. Deux plongées simples de
  suite forcent un spectacle. Chaque final est écrit en Info dans le log (« surfacing over: glide, closest player
  42 m ») ; `ta_sea log` affiche l'état (cruise/ascend/breathe/glide/lobtail/breach/flee). [Orca] BreachChance.
- Chassables (choix de Lekinox) : capsule « Hitbox » (couche hitbox, rigidbody cinématique) + Destructible
  (Character) ; baleine 1500 PV, orque 600 ([Whale]/[Orca] Health). Sang du Serpent vanilla, nos cris
  (whale_hurt/orca_hurt, plus graves à la mort, sans ragdoll). Touché : fuite 20-30 s, 2x plus profond, 1,8x plus
  vite, à l'opposé du joueur ; tout le groupe d'orques à moins de 50 m fuit aussi ; une claque en cours est coupée.
  Mort : butin qui flotte en surface (baleine : 8-12 viande, 6-10 graisse, 3-5 fanons ; orque : 4-7 viande, 2-4
  dents, 1-3 graisse). `ta_sea act flee|breach`.
- Sons (tools/synth_voices.py, numpy + scipy, spectrogrammes vérifiés) : baleine, 4 phrases de chant (gémissements
  80-300 Hz à harmoniques dérivantes, « whoops » montants, grognements pulsés) dans une longue queue sous-marine ;
  orque, 4 appels pulsés (trains de clics dont la cadence 600-2000/s fait la hauteur, sauts de contour, sifflement
  aigu, écholocation) ; souffles refaits (claquement mouillé + gouttes, expiration filtrée par les formants des voies
  aériennes, inspiration « hhoo » puis fermeture de l'évent) ; chouette hulotte refaite (« hou-ou ... hou, hou-hou-
  houuuu » au trémolo de ~11 Hz, timbre creux, attaque soufflée, réverbération de forêt) + 2 « kewick » de femelle.
  Chants de baleine toutes les ~35 s et appels d'orque toutes les ~14 s, locaux, à moins de 140 m ([Sea] CallVolume).
- Recettes (Crafts.cs) : nouveaux matériaux patte de lapin (12 %), plume de moineau, plume de chouette, viande /
  steak de baleine, graisse, fanons, viande / grillade d'orque, dent d'orque.
  Lapin : bottes (existant) / civet (chaudron 2) / patte porte-bonheur (table d'artisan, ceinture : chute -50 %,
  saut +12 %). Renard : cape (existant) / toque (établi 3, résistance au froid) / cape du renard argenté (établi 4,
  bruit -30 %, discrétion). Mulot : mulots au miel (chaudron 1) / pâté aux navets (chaudron 2) / tourte des moissons
  (chaudron 4). Moineau : flèches (établi) / bannière à plumes / cape de plumes (chute lente + 5 % vitesse).
  Chouette : capuche (établi 2, pas silencieux) / statuette (tailleur de pierre, notre modèle) / flèches-aiguilles.
  Baleine : bouillon (chaudron 1) / lampe à huile (forge, brûle la graisse ~1,7 h par morceau) / bouclier en fanons
  (forge 3). Orque : orque fumée aux baies (chaudron 1) / lance en dent d'orque (forge 2, +10 givre) / statue
  d'orque bondissante en marbre noir (notre modèle penché à -32°).
- Pas encore vérifié en jeu (Lekinox teste) : rien de tout ça n'a tourné dans Valheim.

## v0.23.0 (2026-10-04) : souffles refaits, mode ghost, sons de mer plus discrets, mulots, coffre de test
- Souffles (« on dirait des pets ») : mesurés, les anciens avaient 44-51 % d'énergie sous 200 Hz, une périodicité
  voisée (~0,35), un battement d'amplitude à 16-24 Hz, un grondement < 120 Hz et une inspiration voisée à 95 Hz.
  Nouveau souffle (tools/synth_voices.py, blow) : bruit façonné dans le domaine fréquentiel, sans aucune
  composante tonale (< 3 % sous 120 Hz), bosses larges de voies aériennes (baleine ~760/2100 Hz, orque ~1000/2600
  + 4500 Hz), trois spectres en fondu à puissance égale (formants -20 %, passe-bas 9 -> 6 kHz), niveau qui ne fait
  que décroître (points en dB, fondu cosinus), « pop » + gouttelettes (clics de Poisson >= 1,5 ms, ±6 dB) à
  l'ouverture, inspiration en crescendo à -17/-19 dB. Orque : rafale vive de 40 ms, ~0,5 s au-dessus de -20 dB.
  Évaluateur (2e agent, analyse de signal : il ne peut pas écouter) : baleine 6,0 -> 7,5 -> 8,0 -> 8,5 ; orque
  5,5 -> 7,0 -> 7,5 -> 7,0 -> 8,5. La variante d'orque notée 8,0 (graine 400) est retirée : 3 souffles de baleine,
  2 d'orque, octet pour octet ceux qui ont été notés.
- Mode ghost : un lapin ou un mulot qui avait pris le joueur pour cible avant le ghost fuyait sans fin (AnimalAI ne
  lâche une cible qu'après l'avoir « sentie »). Ghost.cs : nos AnimalAI (lapin, mulot) oublient un joueur en ghost
  ou en vol debug ; nos oiseaux et les corbeaux (RandomFlyingBird.DangerNearby, choix de perchoir) l'ignorent ;
  le renard aussi (Threat).
- Sons d'orques et de baleines (hors souffle) / 4 : [Sea] CallVolume 0,8 -> 0,2 (.cfg live), CryVolume 0,25 (cris
  de blessure et de mort, ZSFX), SlapVolume 0,27 (claque de queue, avant 0,9 x 1,2).
- Mulots : la viande tombe toujours ([Mouse] MeatChance 15 -> 100, .cfg live).
- Coffre de test (ModChest.cs) : WL_ModChest, coffre de fer noir 8 x 6, rempli une seule fois (drapeau dans la ZDO)
  de tous les objets de Wildlife et de Legendary Weapons (ModRegistry de Jotunn) : 20 de chaque matériau ou
  nourriture, 1 de chaque équipement. `spawn WL_ModChest` ou `wl_chest` (le pose devant soi). Les pièces (tapis,
  bannière, lampe, statues) ne sont pas des objets : marteau.
- Pas encore vérifié en jeu.

## v0.23.1 (2026-10-04) : durabilité des équipements
- Les équipements clonés affichaient 1000 de durabilité. Valeurs propres (Crafts.SetDurability, log Info avant/après) :
  bottes de lapin 200, cape de renard 400, toque 500, capuche de chouette 300, cape du renard argenté 800, cape de
  plumes 700, bouclier en fanons 300 (+50/niveau), lance en dent d'orque 250 (+50) ; la patte porte-bonheur ne
  s'use plus. Les objets déjà en inventaire gardent leur valeur actuelle jusqu'à une réparation... qui ne la
  baisse pas : en jeter un et en reprendre un neuf (coffre `wl_chest`) pour voir la nouvelle valeur.

## v0.24.0 (2026-10-04) : vague 1 du plan « autres biomes » : le Marais (grenouilles, lucioles)
Plan validé par Lekinox : 9 animaux en 4 vagues (Marais ; Montagnes ; Plaines ; Brumes, Grand Nord, Terres cendrées),
chacun avec son modèle, 3 recettes, des sons synthétisés évalués, des animations évaluées, vérification en jeu.
- Plafonds de triangles (2 x le maillage vanilla de base, lus dans les bundles avec UnityPy, hors dépôt) :
  lièvre 2 164 (grenouille <= 4 328), cerf 2 418, loup 1 962, corbeau 394 + 280.
- Grenouille : concept fal flux/schnell frog_3, Trellis 2 (5 000 faces), tools/build_fauna.py (nouveau : morceaux
  détachés retirés, décimation pymeshlab après soudure des coutures UV, aperçus) -> 4 299 triangles.
  - Rig et clips : tools/blender_frog.py (même méthode que la chouette : maths de Unity en numpy, aperçus Blender,
    QA d'étirement) : squelette Root/Body/Chest/Head/Throat + cuisse, jambe, pied, orteils, bras, main par côté,
    poids lissés par régions ; clips breathe, hop, croak, swim ; --poses pour tester un os à la fois.
  - Constat : les pattes arrière générées sont un seul bloc replié ; toute vraie extension pend les pieds sous le
    ventre (vérifié os par os). Le saut garde les pattes repliées : arc du corps, bascule du nez (lève puis pique),
    bras tendus, mains posées à l'atterrissage.
  - Évaluateur d'animation (2e agent, seuil 7,5) : breathe 6,0 -> 7,5 ; hop 6,0 -> 6,8 -> 7,0 -> 7,5 ; croak 6,0 ->
    6,8 -> 7,2 -> 7,8 ; swim 4,0 -> 5,5 -> 6,0 -> 6,5 -> 6,0 (version 6,5 gardée) : sous le seuil, à vérifier en jeu
    (la grenouille nage en surface, l'eau cache le dessous ; conseil de l'évaluateur).
  - Jeu (Frogs.cs) : lièvre vanilla invisible dessous (AnimalAI craintif, physique, réseau ; m_avoidWater off, nage),
    notre rig (ProcRig.Build avec les poids du .rig) ; FrogAnim joue breathe, hop (cadencé : un saut par
    [Frog] HopDistance parcourue), swim dans l'eau, croak sur demande ; FrogVoice : chœur la nuit et sous la pluie,
    rare le jour, silence si un joueur (hors ghost) est à moins de 7 m ou si elle fuit. Dépouille propre sur le dos.
    Apparition Marais, au bord de l'eau (altitude -1 à 2,5), groupes de 2-4, 25 %, max 6 ; FarDespawn ; ghost.
    Butin : cuisses (100 %), peau (50 %).
- Sons (tools/synth_swamp.py, évaluateur audio, seuil 8,5) : appels à impulsions (chaque impulsion : bouffée de
  bruit + clic du larynx à travers des formants légèrement déplacés, espacement et amplitude irréguliers, souffle
  calé sur les impulsions) : grenouille rousse (ronronnement 26-38 impulsions/s, ~450/1 300 Hz) 6,5 -> 7,5 -> 8,5 ;
  grenouille verte (« crroa », ~100 impulsions/s, 3 formants) 7,0 -> 8,5 -> 8,0 -> 8,5 ; cri d'alarme (cris
  successifs, glissement d'attaque, gigue par segments, râpe, souffle, halètement) 5,0 -> 6,5 -> 7,0 -> 8,0 -> 8,5.
  Gardés : les 5 coassements et le cri notés 8,5 (variantes à 8,0 retirées).
- Lucioles (Fireflies.cs) : essaim d'ambiance (Swarm, réutilisable pour les papillons de la vague 4) : particules à
  texture douce qui clignotent et dérivent (bruit), petite lumière qui vacille ; la nuit seulement, au-dessus de
  l'eau du Marais ; le propriétaire le retire au jour ; attrapable (touche utiliser) : 2-4 lucioles.
- Recettes (Crafts.cs) :
  - grenouille : D cuisses grillées (broche) + soupe de grenouille (chaudron 1) ; M bottes de marais (établi 3,
    armure 12, endurance de nage -50 %) ; F élixir de bond (chaudron 4, orge : saut x2, chute /2, 2 min).
  - lucioles : D bocal à lucioles (lumière sans combustible, clone de lanterne dvergr) ; M lanterne de ceinture
    (forge, utilitaire : lueur attachée) ; F hydromel luminescent (chaudron 5, sève : 10 min de lueur, endurance
    +25 %).
- Pas encore vu en jeu.

## v0.24.1 (2026-10-04) : retours de Lekinox sur la vague 1
- Coffre de test séparé par mod : `wl_chest` ne contient plus que Wildlife ; il pose autant de coffres 8 x 6 que
  nécessaire, côte à côte (page dans le ZDO). Legendary Weapons a son `lw_chest` (v0.7.3, TestChest.cs).
- Grenouilles « trop petites » et « animations de déplacement qui ne se déclenchent pas » : à 0,23 m et 4 sauts/s
  d'à peine 4 cm de haut, elle semblait glisser. Désormais 0,42 m (Scale 1, Length 0,42), 1,2-2,6 sauts/s,
  arc du clip étiré selon la longueur du bond (35 %, max HopHeight 0,4 m). Log « Frog hopping » au premier bond.
- Lucioles : attrapables 1,5 m plus loin (GetHoverOffset).

## v0.24.2 (2026-10-04) : grenouille, membres qui bougent enfin (workflow vidéo)
- Retour de Lekinox : « les membres ne bougent jamais », grenouille trop petite. Taille : Scale 1,3, Length 0,45 (~0,58 m).
- Lab (Lab.cs) : rend désormais les clips .rig joués par code (FrogAnim implémente IProcAnimated, Frog_rig_<clip>.png),
  tools/lab_to_mp4.py en fait des MP4. Constat dans le moteur : le saut ne bougeait que les bras.
- Cause : squelette et poids. Articulations replacées sur la vraie géométrie (hanche dans la cuisse, genou au pli
  arrière, cheville, orteils ; épaule haute, poignet au sol). Poids des membres par « enveloppes » : distance au
  segment de l'os moins son épaisseur, mesurée le long de la surface (Dijkstra, sommets des coutures UV recollés),
  un seul membre par sommet, ce qui est à plat au sol hors du milieu va aux membres. Les poids automatiques de
  Blender (bone heat) échouaient sur ce maillage (la cuisse prenait l'œil).
- Le maillage généré soudait les bouts des orteils arrière aux mains, un pied au ventre, les doigts au menton :
  198 triangles de soudure coupés (sous la grenouille), 4 îlots retirés ; talon/cuisse et aisselle non coupés mais
  lissés (sinon des trous). frog.tam = maillage coupé, frog_uncut.tam = source.
- Clips : saut avec vraie détente (cuisse +25, jambe +70, pied +50, pattes tendues en vol, repli avant l'atterrissage) ;
  nage en vrai coup de pattes (repli, détente écartée, glisse bras plaqués, retour lent).
- Évaluateur : hop 7,6, swim 7,6 (breathe 7,5, croak 7,8 inchangés). Vérifié aussi dans le rendu du moteur.

## v0.24.3 (2026-10-04)
- Sauts plus hauts et plus longs : 0,9-1,6 bond/s, HopDistance 0,9, arc jusqu'à 45 % de la longueur, HopHeight 1 m
  (un bond de fuite ≈ 2,5-3 m de long, 1 m de haut).
- Paix avec le vanilla : les grenouilles manquaient dans Interactions.IsOurs (FrogAnim.All ajouté).
- `ta_census [range]` : compte créatures, ragdolls et cadavres autour du joueur (log). Test piloté (monde
  Laboratory, killall) : cadavres de grenouilles et de mulots partis en 12 s comme prévu ; Lekinox ne voit plus de
  souci de cadavres persistants (« ça doit être ok »).
