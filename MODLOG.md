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
