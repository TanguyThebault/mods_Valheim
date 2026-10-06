# MODLOG : Valheim Legendary Weapons

Mod des armes légendaires de Lekinox : des armes uniques, avec leurs propres mécaniques, trouvées en loot plutôt
que fabriquées. Séparé le 2026-10-03 du mod de faune (valheim-wildlife), où la hache est née : l'historique
détaillé (v0.1 à v0.4) se trouve dans ../valheim-wildlife/MODLOG.md, sections Throwing Axe.

## Environnement
- Valheim 1.0.16 (network 40), Unity 6000.0.75f1 Mono ; BepInExPack_Valheim 5.4.2351 + Jotunn 2.30.2.
- Installé dans BepInEx/plugins/LegendaryWeapons/LegendaryWeapons.dll ; config :
  BepInEx/config/lekinox.legendaryweapons.cfg (reprise de l'ancien lekinox.throwingaxe.cfg).
- Prefab AxeThrowing inchangé : les haches déjà dans les inventaires restent valides.

## Hache de retour (AxeThrowing)
- Clone d'AxeIron. L'attaque secondaire est celle de SpearBronze, clonée, avec m_consumeItem false et
  m_attackProjectile = gabarit inactif portant ThrowingAxeProjectile (IProjectile).
  Attack.FireProjectileBurst fournit le HitData complet.
- Vol : portée fixée au lancer (20 m ou premier obstacle sur la ligne de visée, par sphere-cast). Aller =
  demi-ellipse vers CurveSide, retour = autre demi-ellipse vers la main. Les créatures sont traversées, le
  premier obstacle est frappé et déclenche le demi-tour. Visuel couché à plat (axe le plus fin du mesh mis
  à la verticale), rotation autour de la verticale.
- Recette désactivée mais réparable à la forge : ObjectDB.GetRecipe ignore m_enabled.
- Loot : postfix sur Container.AddDefaultItems, 2 % par coffre TreasureChest_sunkencrypt.
- Gotchas : DestroyImmediate est refusé dans un événement d'animation (Setup) ; Config.Bind garde la valeur
  déjà présente dans le .cfg.

## Lance du tonnerre (SpearThunder), v0.2.0 (2026-10-04)
- Clone de SpearWolfFang (repli : SpearElderbark, SpearBronze). Attaque secondaire = lancer du vanilla, avec
  m_consumeItem false et m_attackProjectile = gabarit portant ThunderSpearProjectile.
- Appel de la foudre : touche G (ThunderSpear.CallLightningKey), lance en main. Crépitement (son + lumière qui
  suit la lance), puis éclair après CallDelay = 4 s :
  - lance plantée : l'éclair frappe la lance, 70 de foudre (+15/niveau) aux ennemis dans 3 m ;
  - lance encore en vol : l'éclair attend l'impact (3 s max) ;
  - jamais lancée : l'éclair frappe la lance dans la main, le joueur prend les dégâts (sans attaquant, donc
    même sans PvP).
- Vol balistique (gravité 6), traverse les créatures amies, se plante dans le premier ennemi (le suit) ou
  obstacle. Attend l'éclair si un appel est en cours, puis revient à la main (talon d'abord).
- Éclair : LineRenderer (Sprites/Default) en zigzag depuis 70 m, branches, halo, lumière ponctuelle,
  étincelles (ParticleSystem créé au runtime), coup de tonnerre. Affiché chez tous via la RPC routée
  LegendaryWeapons_Bolt ; les dégâts ne sont faits que par l'appelant.
- Sons synthétisés (tools/synth_thunder.py, numpy) -> sfx/ à déployer avec la DLL.
- Butin : 2 % par coffre TreasureChest_mountaincave (grottes gelées). Nom du prefab non vérifié en jeu :
  le log avertit s'il est introuvable.
- Refactor : règles de touche communes (Geometry.cs), patch de butin généralisé (ChestLoot).
- À vérifier en jeu : sens du modèle en vol (Visual.SpearFlip / SpearTilt sinon), lisibilité de l'éclair,
  touche G libre.

## v0.2.1 (2026-10-04) : retour de playtest
- Testé en jeu par Lekinox (log) : appel, lancer, éclair sur la lance et sur le joueur OK ; le coffre
  TreasureChest_mountaincave existe.
- Zone de l'éclair doublée : StrikeRadius 6 m (.cfg live modifié).
- La lance ne revient plus : m_consumeItem true comme la lance vanilla, le projectile porte l'ItemData et
  la fait tomber (ItemDrop.DropItem) là où elle s'est plantée, aussitôt sans appel, 0,5 s après l'éclair
  sinon. Aussi à la mort du lanceur ou après 20 s. Clés Return* supprimées.

## v0.2.2 (2026-10-04) : nouvelle commande d'appel
- La touche G est abandonnée (demande de Lekinox). Patch prefix de Player.SetControls (joueur local, lance
  en main) : maintenir l'attaque secondaire CallHoldTime = 1 s appelle la foudre ; un appui plus court lance
  la lance au relâchement. Une fois la foudre appelée, l'appui suivant lance tout de suite. Clé
  CallLightningKey retirée du .cfg live.

## v0.2.3 (2026-10-04) : zone de l'éclair visible, dégâts dégressifs
- Dégâts selon la distance au point d'impact (point du corps le plus proche, ClosestPoint) : 100 % jusqu'à 1 m, puis
  baisse linéaire jusqu'à [ThunderSpear] StrikeEdgeDamage = 25 % au bord (StrikeRadius 6 m). Le recul suit la même
  courbe. Log Info : dégâts au centre et au bord ; Debug : chaque cible, sa distance, ses dégâts.
- Effet de zone (StrikeArea.cs, local chez chaque client depuis la RPC de l'éclair) : lueur au sol en dégradé radial
  (forte au centre, faible au bord, même pente que les dégâts), onde de choc électrique dentelée qui file jusqu'au
  rayon en 0,3 s, 9 arcs rampant au sol du centre vers l'onde (blancs au centre, bleus et fins vers le bord), puis
  anneau crépitant au rayon exact et gerbe d'étincelles sur tout le bord. Lignes et lueur suivent le sol (raycasts).
- Pas encore vu en jeu.

## v0.3.0 (2026-10-04) : Lame des vents (épée des Plaines)
- SwordWind, clone de SwordBlackmetal teinté acier bleuté, $item_swordwind (« Lame des vents »). Butin : 2 % par
  coffre TreasureChest_plains_stone ; recette de forge (niv. 3) désactivée par défaut, réparation à la forge.
- Attaque secondaire : la fente vanilla de l'épée reste ; au moment où le coup porte (postfix
  Humanoid.OnAttackTrigger, attaque secondaire, joueur local), une lame d'air part droit devant à 30 m/s sur
  [WindBlade] SlashRange = 6 m et tranche tout dans une bande SlashWidth = 3 m (OverlapBox) : créatures (sans tir
  ami), et objets cassables (buissons, souches, arbres : dégâts de hache en plus), jamais les constructions.
  Chaque cible est touchée quand la lame l'atteint. Dégâts = ceux de l'épée x SlashDamage x facteur de
  compétence Épées ; la compétence monte si quelque chose est touché.
- Visuel (AirSlash) : croissant lumineux à hauteur de poitrine, légèrement en diagonale, pointes en retard,
  halo, traînées de vitesse, poussière soulevée sous le front ; il s'effiloche au bout. Son : sifflement d'air
  synthétisé (tools/synth_wind.py -> sfx/wind_slash_*.wav). Vu par tous (RPC routée LegendaryWeapons_AirSlash).
- Vent : il souffle dans la direction du coup pendant WindDuration = 120 s, force au moins WindMinIntensity = 0,7,
  puis la météo reprend la main (EnvMan.SetDebugWind / ResetDebugWind, local au joueur).
- Pas encore vu en jeu.

## v0.3.1 (2026-10-04) : retours sur la lance et l'épée, durabilité
- Lance du tonnerre : l'effet de zone devient une sphère (les dégâts suivent déjà la distance 3D). Coque de
  « veines » lumineuses (texture procédurale) qui gonfle jusqu'au rayon en 0,3 s et tourne en crépitant, noyau
  blanc au point d'impact qui pâlit vite, arcs du centre vers la coque (blancs puis bleus et fins), 3 grands
  cercles dentelés qui rampent sur la coque, étincelles jaillissant de toute la surface.
- Lame des vents :
  - Déclenchement : maintenir l'attaque secondaire [WindBlade] HoldTime = 0,6 s (tourbillon de volutes autour de la
    main), puis l'épée frappe (animation secondaire vanilla) sans coup de mêlée (prefix Attack.DoMeleeAttack) ; la
    rafale part quand le coup porte. Un appui plus court ne fait rien.
  - Zone en goutte : pointe à l'épée, bulbe rond de BulbRadius = 2,2 m au bout de SlashRange = 6 m (cône tangent au
    cercle). Dégâts = épée x SlashDamage 1,5 x part : 100 % jusqu'à 1,5 m, puis linéaire jusqu'à EdgeDamage = 30 %
    au bout. Recul et étourdissement suivent la même part. Log Debug : distance et part par cible.
  - Visuel : la goutte au sol (maillage en éventail, alpha qui baisse avec la distance comme les dégâts) se révèle
    derrière un front d'air aussi large que la zone, contour qui se dessine, traînées et poussière.
  - Vent : plus de vent « debug » à force fixe. Prefix EnvMan.SetTargetWind : seule la direction est la nôtre
    pendant WindDuration (comme le contrôle du vent d'un navire), la force reste celle de la météo. Le virage part
    du vent actuel (m_windDir1 = m_wind, transition relancée) et dure WindTurnTime = 2 s au lieu de 5.
    WindMinIntensity et SlashWidth supprimés.
- Durabilité : les clones affichaient 1000. Hache de retour 250 (+50/niveau), lance 300 (+50), épée 350 (+50).
- Pas encore vu en jeu.

## v0.4.0 (2026-10-04) : Brise-terre (Prairies) et Croc du renard (Forêt noire)
Plan validé par Lekinox (masse « Séisme » pour les Prairies, couteau de camouflage pour la Forêt noire, nom sans
Greydwarf).
- HoldPower.cs : les pouvoirs sur attaque secondaire maintenue sont factorisés (contrôles, charge de particules
  autour de la main, coup sans mêlée, déclenchement au moment où le coup porte). Mode « swing » (épée, masse :
  animation secondaire vanilla sans coup, pouvoir à l'impact, appui court = rien) et mode « instantané » (couteau :
  pouvoir tout de suite, appui court = attaque secondaire vanilla). La Lame des vents passe dessus sans changement.
- Brise-terre (MaceEarth, clone de MaceBronze teinté pierre moussue, contondant 24 +6/niveau, durabilité 250
  +50) : maintenir 0,6 s, la masse frappe, une fissure court à 16 m/s sur [Earthbreaker] QuakeLength 8 m x
  QuakeWidth 2 m. Chaque cible touchée quand la fissure l'atteint : dégâts x QuakeDamage 1,5 x part (100 % jusqu'à
  2 m, puis linéaire jusqu'à QuakeEdgeDamage 35 %), projetée vers le haut (Launch 7 m/s x part x 50/masse, borné
  0,3-1 ; vitesse posée sur le corps + TimeoutGroundForce, car le recul vanilla est horizontal ; jamais les boss ;
  seulement les créatures possédées localement) et étourdie (Character.Stagger). Jamais les constructions.
  Visuel : fissure sombre et dentelée qui se dessine au sol avec 7 fissures latérales, éclats de roche (particules
  en cubes) et poussière à la pointe, onde de choc d'Eikthyr (fx_eikthyr_forwardshockwave), secousse de caméra ;
  la fissure se referme en 1,5 s. Son : tools/synth_earth.py (choc sourd, craquements qui s'éloignent, grondement,
  cailloux). Butin : 1 % dans TreasureChest_meadows / _meadows_buried. Recette d'établi désactivée.
- Croc du renard (KnifeFox, clone de KnifeCopper teinté roux, dégâts +15 %, durabilité 250 +50) : maintenir 0,6 s =
  camouflage [FoxFang] CamoDuration 8 s, puis CamoCooldown 30 s (message au centre pendant l'attente).
  BaseAI.CanSeeTarget / CanHearTarget (statiques) renvoient faux au-delà de RevealDistance 1,5 m ; à l'activation,
  les MonsterAI/AnimalAI qui ciblaient le joueur à moins de 40 m l'oublient. Premier coup porté en camouflage
  (prefix Character.Damage, attaquant = joueur local) x AmbushMultiplier 3, puis fin du camouflage. Apparence :
  état dans la ZDO du joueur (lw_camo), chaque client remplace les matériaux de ses renderers par des copies
  translucides vert sombre (même texture), réappliquées toutes les 0,5 s ; tourbillon de feuilles et bruissement
  (synth_earth.py) à l'entrée et à la sortie ; effet SE « Camouflage » (bruit -50 %). Butin : 1,5 % dans
  TreasureChest_blackforest / _forestcrypt / _trollcave. Recette de forge désactivée (FoxPelt de Wildlife si
  présent, sinon TrollHide).
- Pas encore vu en jeu. Les deux armes sont dans le coffre de test de Wildlife (`wl_chest`).

## v0.4.1 (2026-10-04) : retours de Lekinox sur les pouvoirs
- Règle générale (toutes les légendaires sauf la lance, déjà bonne) : l'attaque secondaire de base n'est plus
  remplacée. Appui relâché avant la fin de la charge = attaque secondaire de base de l'arme (au relâchement) ;
  charge complète = pouvoir. HoldPower réécrit : l'« attaque de pouvoir » n'est substituée à m_secondaryAttack que
  pendant Humanoid.StartAttack (prefix/postfix, l'attaque est clonée au démarrage), puis restaurée ; son coup
  (OnAttackTrigger) déclenche le pouvoir. Les attaques de pouvoir de l'épée et de la masse sont de type None :
  animation sans aucun coup (plus besoin de bloquer DoMeleeAttack). Si l'attaque ne peut pas démarrer, la demande
  expire en 0,3 s.
- Hache de retour : elle retrouve l'attaque secondaire de la hache de fer ; le lancer devient le pouvoir maintenu
  ([Throw] HoldTime = 0,5 s).
- Brise-terre : l'attaque secondaire de la masse est un coup remontant ; le pouvoir prend la frappe au sol de la
  masse Stagbreaker (SledgeStagbreaker, attaque primaire) sans ses effets (plus de foudre : déclenchement, impact
  et terrain vidés, son de départ de la masse) ; l'onde de choc d'Eikthyr (électrique) est retirée.
- Croc du renard : le temps de recharge s'affiche discrètement par une icône d'effet avec minuteur (« Renard au
  repos », 30 s) au lieu d'un message ; pendant la recharge, l'appui est l'attaque secondaire de base.
- Effet de charge refait (Fx.cs, ChargeFx) : particules aspirées en spirale vers la main avec traînées lumineuses
  (de plus en plus nombreuses et rapides), noyau lumineux qui gonfle, anneau au sol qui se resserre pour montrer
  la progression, lumière qui monte, éclat quand c'est prêt. Thèmes : vent (blanc bleuté), terre (gravillons et
  poussière qui montent autour des pieds), feuilles (feuilles mortes en tourbillon le long du corps), rune
  (orangé, hache). Les particules utilisent une texture ronde douce (ou une feuille) au lieu des carrés blancs de
  Sprites/Default (aussi pour les étincelles de la lance, la poussière de l'épée et de la masse).
- Lame des vents : la zone devient un cône étroit et plus long : [WindBlade] SlashRange 6 -> 8 m (.cfg live),
  ConeAngle = 36° d'ouverture (BulbRadius supprimé) ; même dégressivité, même visuel au sol en forme de cône.
- Pas encore vu en jeu.

## v0.5.0 (2026-10-04) : recharge de la lance, modèles 3D propres
- Lance du tonnerre : [ThunderSpear] CallCooldown = 30 s après chaque éclair (sur la lance ou sur le lanceur),
  affiché discrètement par une icône d'effet avec minuteur (« Orage apaisé »). Pendant la recharge, maintenir
  l'attaque secondaire n'appelle pas la foudre : au relâchement, la lance est simplement lancée.
- Modèles 3D propres aux 5 armes légendaires :
  - Concepts fal flux/schnell (tools/gen_concepts.sh, 2 graines par arme, arme verticale sur fond blanc) ; retenus :
    axe_11 (lame barbue gravée), spear_11, sword_29 (garde en ailes, gemme), mace_11 (granit moussu), knife_29.
  - 3D : Trellis 2, 5 000 faces (minimum de l'API), texture 1024. Coût total ~0,35 $ (10 images + 5 modèles).
  - tools/build_weapons.py : GLB -> models/<arme>.tam + .png (512 px), repère longueur +y tête en haut, largeur x,
    épaisseur z ; l'épée et le couteau, dessinés à l'envers, sont retournés ; aperçus face/profil vérifiés.
  - WeaponModels.cs : chaque maillage de l'objet cloné (tenu sous "attach", posé au sol) est remplacé par le nôtre,
    ajusté : notre longueur sur l'axe le plus long de l'original, tête du côté opposé à la main (l'origine de
    l'objet tenu est la main, même règle que la lance lancée), lame du même côté du manche, manche passant par la
    main, même longueur, échelle uniforme, rotation propre (pas de miroir). Matériau : copie de l'original avec
    notre texture (normal map plate, pas de carte métal/émission). Icône d'inventaire re-rendue. Les copies
    lancées (hache, lance) copient l'objet tenu, donc prennent le nouveau modèle. Log Info par maillage : axe,
    sens, échelle.
  - [Models] CustomModels (désactivable), FlipHead (liste d'armes dont la tête sort du mauvais côté),
    LengthScale. Le dossier models/ est à déployer avec la DLL.
- Pas encore vu en jeu : l'ajustement automatique est le point à vérifier (sens de la tête, côté de la lame,
  position dans la main) ; FlipHead permet de corriger sans recompiler.

## v0.5.1 (2026-10-04) : retours de playtest sur les modèles et les pouvoirs
Retour de Lekinox : hache de retour OK ; animations de la masse et du couteau OK.
- Nombre de polygones : au plus le double des armes vanilla, lus dans les bundles du jeu (UnityPy, lecture seule,
  hors dépôt) : KnifeCopper 232, MaceBronze 386, SwordBlackmetal 308, AxeIron 508, SpearWolfFang 280 triangles.
  build_weapons.py décime (pymeshlab, quadric avec texture) jusqu'à 1000 / 560 / 610 / 770 / 460 triangles : les
  sommets dupliqués aux coutures UV sont d'abord ressoudés (sinon la décimation s'arrête vers 2 500 et déchire la
  surface), puis plusieurs passes.
- Vérification hors jeu : les maillages vanilla exportés + la chaîne de transforms (attach -> maillage) donnent la
  main dans l'espace du maillage ; build_weapons.py rejoue l'ajustement de WeaponModels.cs (mêmes calculs, même
  table FIT) et dessine notre modèle sur le vanilla avec la main. La hache (validée en jeu) y est bien placée.
- Brise-terre : manche épaissi x1,7 (sous la tête, avec transition) ; main plus haut sur le manche (le modèle
  glisse de 10 % de sa longueur vers le bas, GripShift) ; son refait (boum sub-grave, craquement, déchirure du sol
  qui s'éloigne, grondement roulant de 4 s qui gonfle après l'impact, pierres puis cailloux, saturation douce),
  portée du son 110 m.
- Croc du renard : le modèle généré perdait sa lame à 460 triangles ; modèle low-poly construit par code
  (tools/knife_procedural.py : lame en croc avec dos et biseau, garde de bronze, manche octogonal en fourrure rousse
  à bandes, touffe blanche au pommeau, texture peinte ; 400 triangles double face), x1,45 et main au milieu du
  manche. Charge complète : seulement le camouflage, plus aucune attaque (HoldPower attend le relâchement après un
  pouvoir : ni attaque de base ni attaques répétées par le maintien).
- Lance du tonnerre : nouveau modèle (concept spear2_42 : large fer sombre à éclair bleu lumineux, manche gainé de
  cuir bleu, talon de bronze ; manche allongé x2,6 dans build_weapons.py pour les proportions d'une lance).
- Lame des vents : plus de marquage au sol (ni goutte/cône au sol, ni contour) ; le front d'air disparaît dès
  qu'il a fini sa course (il restait 0,45 s, immobile, au bout) ; portée doublée SlashRange 8 -> 16 m (.cfg live),
  front à 32 m/s.
- Charges des pouvoirs plus discrètes (sauf la lance, qui n'en a pas) : moins de particules et plus petites,
  traînées plus courtes, noyau et lumière plus faibles, anneau plus fin et plus pâle, éclat final réduit.
- Hache de retour : petite traînée dorée (TrailRenderer, 0,22 s, du centre de la hache) en vol.
- Pas encore vu en jeu.

## v0.5.2 (2026-10-04) : délai de charge, prise en main, lance retournée
- La charge des pouvoirs ne s'affiche qu'après 0,18 s de maintien (HoldPower.ChargeDelay) : un clic rapide
  n'affiche rien.
- Main trop basse sur l'épée (sur le pommeau) et la hache : GripShift 0,08 (table de WeaponModels.cs et FIT de
  build_weapons.py, vérifié en simulation).
- Lance à l'envers : sur la lance vanilla (fangspear), le fer est du côté de la main (largeur mesurée le long de
  l'axe : 0,85 à -10..-7,6, la main à -1,1, l'autre bout à +13,3). La règle « tête à l'opposé de la main » vaut
  pour l'épée et la hache, pas la lance : retournement par défaut dans la table (FlipHead l'inverse encore si
  besoin). La lance lancée prend désormais la direction de notre tête (WeaponModels.Heads) au lieu de pencher
  vers le centre des bornes.
- Petite lame de vent visible quelques images au bout de l'animation de l'épée : l'attaque de pouvoir (type None)
  ne fait plus rien au coup (prefix Attack.DoNonAttack : ni effet de déclenchement de l'arme, ni usure) ; le front
  d'air disparaît à l'instant où il a fini sa course.

## v0.6.0 (2026-10-04) : Canne du pêcheur céleste, ciel ouvert, mods séparés
Décisions de Lekinox : les trois mods sont totalement indépendants (seul le coffre de test de Wildlife peut lister
les objets des autres) ; pas de baleine (elle appartient à Wildlife) : un poisson géant vanilla qui explose ;
pouvoirs du ciel interdits dans les donjons et les grottes, lance du tonnerre comprise.
- Indépendance : la recette (désactivée) du Croc du renard n'utilise plus la peau de renard de Wildlife, seulement
  TrollHide.
- OpenSky (SkyFishing.cs) : ciel ouvert = pas en donjon (Character.InInterior, au-dessus de 3000 m), pas dans un
  environnement de grotte (Caves, Crypt, SunkenCrypt, FrostCaves, InfectedMine, MountainCave), et rien de terrain
  ni de roche au-dessus (raycast 80 m, grottes de surface). Sinon, petit message en haut à gauche (« Le ciel ne
  t'entend pas ici »). La lance du tonnerre n'appelle plus la foudre hors ciel ouvert (au relâchement, elle est
  simplement lancée).
- Canne du pêcheur céleste (FishingRodSky, clone de FishingRod : elle pêche toujours normalement) :
  - Modèle : concept rod_7 (canne en os de baleine et bois sombre, moulinet de bronze), Trellis 2 ; la canne
    vanilla a 2 524 triangles, la nôtre 4 110 (< 2x) ; build_weapons.py : morceaux détachés (le fil du concept)
    retirés, axe redressé (ACP) ; ajustement vérifié en simulation.
  - Pouvoir (maintenir 0,8 s) : animation de lancer de la canne sans flotteur ni coup ; la ligne monte vers le
    ciel depuis le bout de la canne, se tend quand ça mord, puis un poisson vanilla (Fish1/2/5/6/7/8/9/12 au
    hasard, x18, références « soft » chargées) tombe de 55 m sur le point visé (ce que regarde la caméra, 35 m
    max), une ombre au sol qui fonce et se resserre annonce l'impact. À l'impact : explosion vanilla
    (fx_siegebomb_explosion), gerbe d'eau et d'écailles, secousse de caméra ; dégâts contondants 120 + 35 % de
    feu au centre, dégressifs jusqu'à 25 % au bord (rayon 7 m), projection en l'air (jamais les boss), jamais les
    constructions ; 2 à 4 poissons crus restent au sol. Recharge 45 s (icône « Ciel vide »). Vu par tous (RPC).
  - Sons (tools/synth_fish.py) : moulinet qui se dévide et deux secousses à la touche, chute (ruée d'air et
    sifflet descendant), impact (boum, claque mouillée, gerbe, morceaux).
  - Butin : seulement en pêchant en Océan (postfix FishingFloat.Catch, biome de la prise) : 0,5 % par prise +
    0,2 % par prise océanique ratée depuis (compteur dans les données du joueur, remis à zéro quand on la trouve).
- Pas encore vu en jeu.

## v0.6.1 (2026-10-04) : temps de recharge
- HoldPower : recharge générique par pouvoir (Spec.Cooldown), icône d'effet avec minuteur ; pendant la recharge,
  l'appui est l'attaque secondaire de base.
- Lame des vents [WindBlade] Cooldown 30 s (« Vent apaisé ») ; Brise-terre [Earthbreaker] Cooldown 30 s (« Sol qui
  se tasse ») ; Croc du renard CamoCooldown 30 -> 60 s ; canne [SkyRod] Cooldown 45 -> 600 s (10 min). .cfg live
  mis à jour (Config.Bind garde les anciennes valeurs).

## v0.6.2 (2026-10-04) : canne vanilla, sort sans appât
- La canne garde l'apparence de la canne vanilla (choix de Lekinox) : plus de modèle propre (rod.tam retiré du
  déploiement, entrée retirée de build_weapons.py).
- Le sort ne demande plus d'appât : l'appât est le type de munition de l'arme (m_ammoType), exigé au démarrage
  de l'attaque et consommé à son coup. Spec.NoAmmo : vidé le temps du démarrage (PowerAttackSwap) et du coup
  (prefix/postfix Attack.OnAttackTrigger) de l'attaque de pouvoir seulement ; la pêche normale garde son appât.

## v0.6.3 (2026-10-04) : la canne lance enfin son sort
- Bug (Lekinox : rien ne se déclenche une fois la charge finie ; le log ne montre jamais « falls on ») : l'attaque
  principale de la canne vanilla est tendue comme un arc (m_bowDraw) ; pour ces armes, Player.PlayerAttackInput
  prend le chemin UpdateAttackBowDraw et n'appelle jamais StartAttack(secondaire). HoldPower démarre donc lui-même
  l'attaque de pouvoir (Player.StartAttack(null, true)) pour une arme à tension ; PowerAttackSwap fait le reste.

## v0.7.0 (2026-10-04) : la pêche céleste devient épique
Demande de Lekinox : ciel qui se couvre vite, personnage qui force beaucoup, poisson en « ragdoll » qui tombe à
l'horizontale et de beaucoup plus haut, explosion bien plus grosse et gore qui touche absolument tout.
- Mise en scène (SkyFish, jouée sur chaque client depuis la RPC ; le lanceur fait les dégâts) :
  1. la ligne file vers le ciel (0,6 s) ; le ciel se couvre (environnement forcé [SkyRod] StormEnvironment =
     ThunderStorm, transition vanilla de 2 s, rendu à la fin : l'ancien forçage est restauré) ; tonnerre ;
  2. l'effort (2,8 s) : l'animateur du lanceur figé dans la pose du lancer, buste penché en arrière et tremblant
     de plus en plus (rotations ajoutées en LateUpdate sur Spine1/Spine2/Head), joueur cloué sur place (prefix
     SetControls), StrainStamina = 40 d'endurance, ligne tendue qui vibre de plus en plus, secousse de caméra
     croissante, son d'effort (synth_fish.py strain : bois qui craque, ligne qui vibre, grognements) ;
  3. la traction : grand coup en avant, la ligne disparaît, coup de tonnerre, secousse ;
  4. la chute : le poisson (x30) tombe de FallHeight = 180 m en 3,2 s, à plat, roulant lentement sur sa longueur
     et se tordant comme une poupée de chiffon (animateur accéléré x3), l'ombre au sol s'étend et fonce ;
  5. l'impact : explosion centrale x1,6 et 6 explosions en couronne décalées ; fontaine de sang, morceaux de chair
     (cubes en rotation qui rebondissent sur le décor, module de collision), pluie de sang sur toute la zone
     pendant 2,5 s, 24 éclaboussures au sol qui s'effacent après 15-21 s, anneau de poussière qui file à
     l'horizontale ; secousse de caméra forte ; son refait (boum, craquement, claques de chair tout autour, pluie).
- Dégâts : rayon 7 -> 14 m, 120 -> 250 contondant + 35 % feu + autant de hache et de pioche (outil niveau 4 :
  arbres, souches, rochers, minerai), dégressif jusqu'à 25 % au bord, projection jusqu'à 12 m/s, étourdissement.
  [SkyRod] HitEverything = true : rien n'est épargné (apprivoisés, autres joueurs et le lanceur, PvP ou non, via
  m_ignorePVP ; constructions). Sous un toit, le point de chute se replie à 25 m devant (hors du rayon).
- Restes : 4-7 poissons crus et 2-4 entrailles projetés.
- .cfg live : Radius 14, Damage 250, FishScale 30.
- Pas encore vu en jeu.

## v0.7.1 (2026-10-04) : explosion du poisson plus grosse
- [SkyRod] Radius 14 -> 25 m, Damage 250 -> 600 (+ 35 % feu, hache, pioche), EdgeDamage 0,25 -> 0,35 (.cfg live).
- Effets à la mesure : explosion centrale x2,5, 10 explosions en couronne (x1,4) sur 25-70 % du rayon, fontaine
  de sang 600 particules plus haute, 160 morceaux de chair plus loin, onde de poussière jusqu à ~35 m, 40
  éclaboussures, secousse de caméra 90 m.

## v0.7.2 (2026-10-04) : commande lw_reset, vortex, effets de l'impact
- Console `lw_reset` : toutes les recharges des pouvoirs légendaires à zéro (HoldPower, lance, couteau, canne) et
  leurs icônes de repos retirées (pas le camouflage en cours). Pour les tests.
- Vortex (canne) : un tourbillon de nuages d'orage s'ouvre à [SkyRod] VortexHeight = 85 m au-dessus de la cible
  pendant l'effort (disque de 45 m de nuages qui tournent et sont aspirés vers l'œil, entonnoir qui descend en
  vrille, éclairs et lueurs dans les nuages, poussière aspirée en spirale au sol), grandit pendant l'effort,
  s'emballe à la traction, crache le poisson et se dissipe à l'impact.
- Impact : en plus du gore, boule de feu, champignon de fumée qui monte plusieurs secondes, anneau de fumée qui
  roule au sol, braises (traînées incandescentes qui rebondissent), éclats de roche, éclair de lumière sur toute la
  zone, trace de brûlure sous le cratère. Le feu et la fumée réutilisent les flipbooks de l'explosion vanilla
  (matériau + grille de la planche, sinon des carrés de planche entière) ; log Info des matériaux trouvés.
- Pas encore vu en jeu.

## v0.7.3 (2026-10-04)
- Coffre de test propre au mod : `lw_chest` (TestChest.cs), les légendaires à pleine durabilité. Wildlife ne les liste plus.

## v0.8.0 (2026-10-05) : Corne de brume (Brumes)
Demande : une corne de brume dans les Brumes qui invoque 3 animaux fantômes alliés au hasard pendant 30 s, toutes
les 20 min ; un souffle normal sans invocation doit aussi exister. Choix du joueur : un objet qu'on souffle (pas une
arme), animaux de combat vanilla.
- Objet HornFog (clone de Club, aucun dégât, pas d'usure, butin seulement) : coffres dvergr des Brumes
  (TreasureChest_dvergrtower, _dvergrtown, _dvergr_loose_stone), 1,5 % par coffre ([Loot] HornChests / HornChestChance).
- Commandes (FogHornControls, prefix SetControls) : attaque principale ou appui court de la secondaire = souffle normal
  (son court, brume) ; maintenir la secondaire [FogHorn] HoldTime 1 s = l'appel si prêt (sinon souffle normal et petit
  message). Recharge Cooldown 1200 s, icône « Brume silencieuse » ; `lw_reset` la remet à zéro.
- Pose : IK deux os du bras droit après l'animateur (postfix CharacterAnimEvent.CustomLateUpdate, tous les clients
  via le ZDO du joueur lw_horn_until / lw_horn_call) : la main tourne pour que la corne pointe devant et vers le haut
  (WeaponModels.Heads), puis le poignet est placé pour que l'embouchure touche les lèvres. Brume qui sort du pavillon.
- Appel : 0,9 s après, Count = 3 animaux tirés de [FogHorn] Animals (Wolf, Boar, Bjorn, Lox, Asksvin, Neck ; prefabs
  absents ignorés), apparus en cercle à 3-4,5 m, niveau Level 2, apprivoisés, qui suivent le joueur
  (MonsterAI.SetFollowTarget). Fantômes (SpiritLook, tous les clients via Character.Awake + ZDO lw_ghost_until) :
  matériau Sprites/Default translucide bleuté qui garde leur texture (non éclairé, sans ombre), lueur, traînée de
  brume, apparition dans un tourbillon de brume, dissolution les 1,5 dernières s ; sans butin (CharacterDrop retiré),
  sans cadavre (m_deathEffects vidés), sans reproduction ; le propriétaire les retire à l'échéance (aussi après un
  rechargement du monde).
- Modèle : concept fal flux/schnell horn_11 (corne courbe, bagues de fer, embouchure de bronze), Trellis 2 -> 4 639
  faces, sangle détachée retirée, 399 triangles (massue vanilla 200, plafond 2x), demi-longueur d'une massue.
- Sons (tools/synth_horn.py, rapport tools/sound_report.py, évaluateur à 8,5) : appel (note longue, glissé vers le
  ton suivant, échos de collines), souffle court, apparition (chœur « ooh » de spectres + souffle qui monte),
  dissolution. 6 tours ; seuil abaissé à 8/10 par le joueur. Final : appel 8,5 / 8,5, souffle 8,0 / 8,0, apparition
  8,0 / 8,0, dissolution 8,0. Retours du joueur en cours de route : corne « beaucoup plus grave et qui porte loin »
  -> fondamentale 55-65 Hz (faible, l'énergie dans les harmoniques 2-5 qui portent), résonance de perce 150 Hz,
  grognement des lèvres, échos de collines jusqu'à 3 s de plus en plus fins, compression qui garde la forme d'onde ;
  audible jusqu'à 600 m (ZSFX). Fantômes : chœur « ooh » en grappe (unisson, +1 demi-ton, triton dessous), souffle
  qui monte (SVF continu), air large, verre frotté discret ; dissolution en grains de Hann. Bugs trouvés par
  l'évaluateur : filtres par blocs (trains de clics), « octave » qui était une copie accélérée.
- Retours du joueur : animaux des Prairies aux Brumes seulement (Boar, Neck, Bjorn, Wolf, Ulv, Lox, Hare ; plus
  d'asksvin des Terres cendrées), plus agressifs : le propriétaire relance toutes les 0,4 s la chasse de l'ennemi le
  plus proche dans [FogHorn] HuntRange 35 m (m_targetCreature + SetAlerted, portée de vue élargie), niveau 3 par défaut.
  .cfg live mis à jour (Animals, Level).
- Pas encore vu en jeu.

## v0.8.1 (2026-10-05) : retours sur la corne
- Les esprits ne suivent plus le joueur (plus de SetFollowTarget) : apprivoisés (de son côté) mais libres. Plus agressifs :
  chasse relancée toutes les 0,25 s, HuntRange 60 m (.cfg live), plus rien de craintif (m_passiveAggresive,
  fuites, peur du feu coupés), dégâts x [FogHorn] DamageMultiplier 1,5 (prefix Character.Damage).
- Souffle normal tenu : l'attaque principale fait sonner la corne tant qu'elle est maintenue, HoldMax 7 s. Note longue
  sèche (sfx/horn_hold_0) sur une AudioSource attachée au souffleur chez chaque client (RPC LegendaryWeapons_FogHornHold),
  coupée en fondu 0,12 s au relâchement, et la fin (horn_release_0 : chute de la note + échos) part à ce moment. La pose
  suit lw_horn_start / lw_horn_until rafraîchi chaque image. L'appui court de la secondaire reste un souffle court.
- Corne sous le menton : la main (origine de l'objet) était posée à la bouche, donc l'embouchure, sous la main, tombait
  sous le menton. C'est maintenant l'embouchure qui va aux lèvres ([FogHorn] MouthpieceOffset 0,1 m derrière la main,
  MouthDrop 0,06 m sous l'os de la tête ; réglables en direct).
- « Effet de vibration » du son très réduit (grognement 0,1 -> 0,02, gigue de cycle 0,08 -> 0,015, dérive des
  partiels, flottement d'amplitude, gigue de hauteur). Réévaluation en cours.
- Évaluation (tour 7) : plus aucun trémolo ; appel 8,5, souffle court 8,0. Note tenue et fin 7,5 -> corrigées : la
  note tenue respire (dérive lente de pression 0,25 Hz, petite reprise de souffle toutes les 2,5-3,5 s) et atteint son
  palier en 0,8 s (même niveau et même timbre où qu'on relâche) ; la fin a un gain commun calé sur ce palier, un fondu
  d'entrée de 10 ms (plus de clic) et des échos qui montent en 200 ms (plus d'attaques sèches). Raccords vérifiés à
  1,5, 4 et 7 s.

## v0.8.2 (2026-10-05)
- Tremblement de la corne en main : la cible des lèvres suivait les petits mouvements d'animation de la tête à chaque
  image. Elle est maintenant prise dans le repère du corps et lissée (4/s). Le son n'a plus aucun trémolo (gigue de
  cycle et grognement retirés, seules des dérives lentes < 1 Hz restent).
- Corne encore trop basse : l'os Head est à la base du crâne, les lèvres sont au-dessus : MouthDrop -0,04 m,
  MouthpieceOffset 0,14 m (.cfg live).
- Les esprits attaquent aussi leurs semblables : BaseAI.IsEnemy refuse d'emblée deux créatures du même groupe (un
  troupeau de lox), apprivoisées ou non. Postfix : un esprit est l'ennemi de toute créature sauvage, les siens compris
  (et elles répliquent) ; jamais des joueurs, des apprivoisés ni des autres esprits.
- Souffle tenu : 5 s max (HoldMax, .cfg live) ; montée douce d'1 s (smoothstep de 40 % à 100 %), petite variation lente
  (dérive de pression 0,3 Hz), fin en 1/4 s (horn_release) au même niveau que le palier, puis les échos.
- Canne du pêcheur céleste : portée 35 -> 70 m, poisson x30 -> x24 ; le poisson apparaît 6 m au-dessus du vortex
  ([SkyRod] AboveVortex, remplace FallHeight 180 m) au lieu de 95 m plus haut ; durée de chute réduite en proportion
  (même accélération). .cfg live : Range 70, FishScale 24.

## v0.9.0 (2026-10-05) : la pêche céleste, mise en scène revue
- Chargement 0,8 -> 1,6 s ([SkyRod] HoldTime, .cfg live). Phases : ligne 0,6 -> 1,0 s, effort 2,8 -> 4,5 s.
- La canne pointe vers le ciel : IK du bras droit (poignet levé devant la poitrine) puis la main tourne pour que l'axe de
  la canne (axe le plus long de son maillage, le bout étant l'extrémité opposée à la main) vise le haut, un peu vers le
  portail. La ligne part du vrai bout de la canne (calculé sur le maillage tenu) et monte jusqu'au centre du portail.
- L'orage arrive doucement : EnvMan.m_transitionDuration = [SkyRod] StormFade 6 s pendant la scène (aller et retour),
  remis à sa valeur après.
- Vortex : se forme lentement (smoothstep sur toute la charge, de x0,05 à x1,15) ; dans son œil, un portail vers
  ailleurs : disque de vide violet sombre, étoiles qui scintillent dedans, anneau de lumière froide violet -> cyan qui
  tourne vite, lueur violette sur les nuages et le sol.
- À la rupture : la ligne éclate en ~1000 particules de lumière dorée/bleutée qui scintillent (gradient d'alpha à
  plusieurs pics) et dérivent (bruit) ; au même instant le poisson sort du portail (2 m au-dessus, [SkyRod] AboveVortex)
  en grandissant de 0 à sa taille en 0,6 s. Chute plus lente : [SkyRod] FallTime 4,2 s.
- La canne perd toute sa durabilité au lancer (durabilité propre de 200, message « répare-la à l'établi ») ; recette
  désactivée à l'établi pour pouvoir la réparer (la canne vanilla n'a pas de recette).
- Pas encore vu en jeu.

## v0.9.1 (2026-10-05)
- La canne disparaissait au lancer : sa durabilité était mise à 0 dans Fire, et l'usure de l'attaque elle-même passait
  alors sous 0 ; m_destroyBroken vaut true par défaut : la canne était SUPPRIMÉE de l'inventaire. Corrigé :
  m_destroyBroken = false, et la durabilité n'est vidée qu'au moment où le poisson sort du portail (StartFall).
- Portail plus impressionnant : éclairs multicolores (violet, cyan, magenta, vert, or, bleu) qui rampent sur le vortex,
  du bord vers l'œil ou en travers, avec des fourches, 4 à 20 par seconde selon l'ouverture, la lueur prend leur couleur ;
  9 faisceaux de lumière colorée (cœur + halo large) qui tombent de l'œil jusqu'au sol, balaient lentement, scintillent
  (bruit de Perlin) et changent de teinte ; ils apparaissent à partir de 35 % d'ouverture.
- Météo plus fluide : le mélange de l'environnement (linéaire dans le jeu) suit une courbe douce (smoothstep, patch
  de EnvMan.InterpolateEnvironment) pendant nos changements ; orage 8 s ([SkyRod] StormFade), retour au calme 14 s
  (ClearFade).

## v0.9.2 (2026-10-05)
- Fil céleste en double : la ligne était calculée dans Update, avant que l'IK lève le bras (LateUpdate), donc depuis
  la canne pas encore levée. Elle est maintenant tracée dans LateUpdate, après RaiseRod.
- Météo : les environnements forcés du jeu basculent ciel, nuages et pluie en une image. Plus de SetForceEnvironment :
  StormFx (notre orage) se superpose à la météo du jeu avec un poids lissé (StormFade 8 s à l'aller, ClearFade 14 s au
  retour, smoothstep) : postfix de EnvMan.SetEnv -> soleil x0,3 et bleuté, ambiance et brouillard sombres bleu-gris
  (densité x2,5, min 0,006), nuages de pluie (_Rain, _Opacity des nuages), et notre pluie autour de la caméra (pas à
  l'abri).
- Rayons atmosphériques : voiles de lumière doux (texture : bords transparents, plus clairs en haut, fondus vers le sol),
  3-10 m de large qui s'élargissent en tombant, naissent et meurent (4-8 s), dérivent (haut et pied en bruit de
  Perlin), ondulent, changent de teinte ; poussières de lumière qui descendent en tournoyant ; brume colorée géante qui
  tourne lentement sous l'œil.
- Sons du portail (tools/synth_portal.py) : ouverture (bourdon grave qui bat, pression qui monte, métal inharmonique qui
  s'étire vers l'aigu, chuchotements, anneau qui vacille) et fermeture (aspiration qui retombe, métal qui s'effondre,
  implosion). Évaluation en cours.
- Sons du portail évalués (seuil 8) : tour 1 5,5 / 5,0 (bourdon sinus de 41 Hz qui écrase tout, métal qui monte en
  bloc façon « riser », trémolo de soucoupe, fin coupée, boum de bande-annonce), tour 2 7,5 / 7,0, tour 3 7,5 / 7,5
  (battement à 20 Hz né de la saturation d'une quinte, partiel grave trop pur), tour 4 8,0 / 8,0. Final : bourdon de 3
  partiels (octave au lieu de quinte) qui dérivent, discret ; métal inharmonique qui s'étire sans monter, partiels qui
  émergent et s'éteignent ; voiles de verre aigus qui dérivent ; chuchotements ; fermeture : convergence puis
  extinction des partiels du plus aigu au plus grave, aspiration, implosion douce ajoutée à sec.

## v0.9.3 (2026-10-05) : corne, esprits
- Esprits qui ne mouraient pas à 0 PV : CharacterDrop était détruit, mais il reste abonné à Character.m_onDeath ;
  OnDeath levait une NullReferenceException (log : CharacterDrop.OnDeath -> get_transform) avant ZNetScene.Destroy.
  Maintenant SetDropsEnabled(false) + m_drops vidée, Procreation désactivé (pas détruit).
- Trois étoiles : niveau 4 par défaut ([FogHorn] Level, .cfg live) ; le HUD du jeu n'affiche des étoiles que pour les
  niveaux 2 et 3 : postfix EnemyHud.UpdateHuds qui active level_3 et ajoute une troisième étoile pour un esprit de
  niveau 4+.
- Corne qui tremble et mal tenue : la main tournait par rotation minimale depuis la pose animée, donc gardait le
  roulis de l'animation (qui change à chaque image). Rotation de la main maintenant absolue, construite depuis le corps
  seul (axe du pavillon sur la direction voulue, côté de la corne de niveau, [FogHorn] Roll), poignet calculé depuis
  le décalage main -> corne mesuré dans le repère de la main. MouthpieceOffset 0,11, MouthDrop 0,01 (.cfg live).
- Outils de réglage : console `lw_hornpose` (corne à la bouche en continu) et crochet [Debug] Lab
  (plugins/LegendaryWeapons/lab/request.txt : cmd, wait, view nom az él dist [h] [fov] -> lab/nom.png).

## v0.9.4 (2026-10-05) : corne réglée en jeu
- Réglage piloté en jeu (crochet [Debug] Lab + `lw_hornpose`, rendus de caméra autour du personnage) :
  - la corne commençait au poing, collé sur la bouche : le modèle est décalé dans la main (WeaponModels horn
    gripShift 0,1 ; l'embouchure dépasse d'environ 11 cm derrière le poing) et l'embouchure va aux lèvres
    (MouthpieceOffset 0,12) ;
  - hauteur : MouthDrop 0,01 = poing au nez, 0,06 = menton/barbe, retenu 0,035 ; Roll 180 (courbe vers le haut) ;
  - tremblement : 6 rendus à 0,15 s d'écart, la corne et la main restent fixes par rapport au visage (les écarts
    mesurés venaient du feuillage et de la lumière).
- Corne mate (métal 0, brillance 0,2) : elle reflétait comme du chrome.
- Esprits : `lw_horncall` (test) ; après `killall` ils disparaissent sans erreur (correctif CharacterDrop vérifié).
  En jeu, plein jour : le matériau lumineux + leur lumière déclenchaient le halo (bloom) : teinte assombrie
  (0,42/0,56/0,76), lumière 0,45 sur 2 x leur taille.
- Gotchas du lab : une commande console qui lève une exception arrêtait la coroutine et laissait le verrou ->
  try/catch par commande ; le script attend que le joueur soit apparu ; `lw_hornpose` est un interrupteur.

## v0.10.0 (2026-10-06) : Surtrbrand (Terres cendrées) et Morsure d'Ymir (Grand Nord)
Demande : une légendaire pour chaque biome manquant, avec une mécanique pratique de fin de partie (plan
mellow-napping-stonebraker). Choix de Lekinox : « Braise gardienne » avec un dôme comme celui du marchand, et
« Sang d'Ymir » avec une aura.
- Recon (assembly_valheim du 2026-10-06, re-décompilé ; manifest SoftRef) : coffres TreasureChest_charredfortress,
  TreasureChest_ashland_stone, TreasureChest_deepnorth_village ; bases THSwordSlayer (510 tri) et AtgeirGold
  (1 445 tri) ; le dôme de Haldor = Assets/Characters/TraderHaldor/ForceField.prefab (sphère Unity r 0,5, échelle 10,
  matériau ForceField, enfant NoMonsterArea = EffectArea sur le calque 14). Froid : Player.UpdateEnvStatusEffects
  annule Cold et Freezing si la résistance au givre est Resistant/VeryResistant/SlightlyResistant (comme l'hydromel).
- Surtrbrand (SwordSurtr, clone THSwordSlayer, teinté braise, réparation à la station de la recette vanilla) :
  maintenir la secondaire 1 s -> frappe au sol de la masse Stagbreaker (type None) ; à la fin du coup l'épée quitte
  l'inventaire et devient un ItemDrop planté (ZDO lw_surtr_planted/start/until/owner, cinématique, m_autoPickup
  false, jamais détruit : m_autoDestroy false sur tous les SwordSurtr au sol). Dôme (Duration 600 s) : clone du
  ForceField de Haldor recoloré (DomeTint), qui pousse depuis la garde ; zones EffectArea faites à la taille
  (NoMonsters + Heat + WarmCozyArea sur Radius 12 m, PlayerBase sur 40 m pour bloquer les apparitions) ; les créatures
  dedans sont rejetées (chaque client pousse celles qu'il possède) ; projectiles ennemis venus de l'extérieur et
  braises (Cinder) arrêtés via des postfix de ShieldGenerator.CheckProjectile / CheckObjectInsideShield ;
  Player.InShelter vrai dans le dôme (feu + abri = Reposé) ; réapparition près de l'épée (Game.FindSpawnPoint, point
  gardé dans Player.m_customData, le lit n'est jamais touché ; si l'épée n'est plus là, spawn normal). On la reprend
  avec Utiliser. Recharge 1200 s (« Braise endormie »).
- Morsure d'Ymir (AtgeirYmir, clone AtgeirGold, teinté givre) : maintenir la secondaire 1 s -> SE « Sang d'Ymir »
  (résistance au givre, 600 s), fin de Cold/Freezing ; ZDO du joueur lw_ymir_until ; aura (cristaux qui montent,
  brume au sol, lumière froide faible) sur tous les clients, s'estompe la dernière minute ; ShareAura : les autres
  joueurs à moins de 6 m reçoivent « Souffle d'Ymir » (3 s, renouvelé). Recharge 1200 s (« Sang figé »).
- Modèles : concepts flux/schnell (surtr_11/29, ymir_11/29/5/42 ; retenus surtr_29 et ymir_42), Trellis 2 à 5 000
  faces, tools/build_weapons.py (clean 0,02, upright) -> surtr 1 020 tri (plafond 2 x 510), ymir 2 890 (2 x 1 445).
  Aperçus d'ajustement sur le maillage vanilla : main sur la poignée / la hampe.
- Sons : tools/synth_ember.py (surtr_plant, surtr_dome, surtr_fade, surtr_pull) et tools/synth_frost.py (ymir_cast,
  ymir_end). Le son de fin du dôme s'appelle surtr_fade : Wav.Load prend un préfixe, « surtr_dome » aurait aussi
  chargé « surtr_domeend ».
  Évaluateur (2e agent), 3 tours : 6-7 au départ (bruit sub sans passe-haut, attaques et fins coupées, bourdon du
  dôme trop fort, tics de glace coupés net = clics), puis 8/10 pour les six sons.
- Pas encore vu en jeu : Lekinox teste lui-même.

## v0.10.1 (2026-10-06) : retours de Lekinox (dôme, activation, auras, dégâts, textes, icônes)
- Dôme peu visible : le ForceField de Haldor utilise le shader Custom/Distortion (il déforme ce qui est derrière,
  sa couleur compte peu). Ajouts : voile de braise (sphère Sprites/Default, [Surtrbrand] DomeVeil 0,07), anneau
  lumineux au pied du dôme, braises émises sur la paroi (hémisphère, 70/s).
- Morsure d'Ymir : petite animation d'activation = l'attaque d'un bâton vanilla (StaffShield, sinon StaffGreenRoots /
  StaffSkeleton), type None, sans coût d'eitr ; le Sang d'Ymir part au coup.
- Aura permanente sur chaque légendaire tenue (WeaponAura.cs, tous les clients via VisEquipment.m_rightItem /
  m_rightItemInstance) : runes, étincelles d'orage, filets de vent, grains de terre, feuilles, ciel, brume, braises,
  givre. Choix de Lekinox : « les deux » = base selon la qualité (1 à 4), multipliée par la recharge du pouvoir (lue
  sur l'effet de repos de chaque pouvoir : 15 % juste après, 70 % presque prêt, 100 % + pulsation quand prêt) ; les
  autres joueurs comptent comme prêts. [Aura] Strength.
- Dégâts : 1,5 x l'arme vanilla du même biome et du même type ([Balance] DamageBonus) : hache AxeIron, lance
  SpearWolfFang, épée SwordBlackmetal, Brise-terre Club (il baisse : 24 -> 1,5 x gourdin), couteau KnifeCopper,
  Surtrbrand THSwordSlayer, Ymir AtgeirGold. Corne et canne : pas des armes, inchangées.
- Descriptions EN/FR raccourcies, plus mystérieuses (le geste « maintiens l'attaque secondaire » reste dit).
- Icônes (Icons.cs) : rendu en diagonale (garde en bas à gauche, tête en haut à droite, face de la lame à la caméra,
  20° pour le relief ; la caméra de Jotunn est en +z tournée de 180°) et halo flou de la couleur de l'élément.

## v0.11.0 (2026-10-06) : forme de géant, auras, réglages
- Morsure d'Ymir : le Sang d'Ymir est remplacé par la Forme de géant (demande de Lekinox). Même geste d'activation
  (bâton), puis 30 s ([YmirBite] GiantDuration, recharge GiantCooldown 300 s) : taille x2 (GiantScale, transform du
  joueur mise à l'échelle sur chaque client d'après le ZDO lw_ymir_until, croissance/retour en ~1 s), peau et
  équipement givrés (copies teintées des matériaux), aura de givre, sol qui tremble à chaque pas. SE « Forme de
  géant » : contondant/tranchant/perforant VeryResistant, éléments Resistant (donc pas de froid), stagger -80 %,
  vitesse +15 %. En marchant, le géant local frappe tout ce qui est devant lui toutes les 0,2 s (SmashDamage 80
  contondant, x3 en bûcheronnage/minage, tier 10, poussée sur les créatures) : arbres, troncs, rochers, buissons,
  créatures ; jamais les constructions sauf BreakBuildings. Pas de transformation en vrai troll : son squelette,
  ses animations et ses contrôles ne sont pas ceux d'un joueur.
- Dégâts : DamageBonus 1,5 -> 1,35 (-10 %), .cfg live mis à jour.
- Dôme : plus d'anneau au sol (voile et braises sur la paroi gardés).
- Auras : lance plus voyante (étincelles plus grosses, 45/s, courtes traînées) ; Brise-terre = petits cailloux
  (cubes) qui tournent autour de la tête de la masse, dans l'espace de l'arme.
- Icône de la canne retournée (demi-tour dans le plan de l'image).

## v0.11.1 (2026-10-06)
- Aura du Brise-terre : retour à quelque chose de simple, les particules qui montent de la hache mais noires
  (suie et pierre sombre) ; les cailloux en orbite sont retirés. Halo de l'icône assombri d'autant.
- Forme de géant : seul l'arme se givre, plus le personnage ; endurance illimitée pendant la forme (postfix
  bloquant Player.UseStamina pour le géant local) ; recharge 1800 s (30 min), .cfg live mis à jour.

## v0.12.0 (2026-10-06) : l'aura remplace l'icône de recharge
- Idée de Lekinox : pas d'aura pendant la recharge, aura seulement quand le pouvoir est prêt, et plus visible. Les
  effets de repos chronomètrent toujours les recharges mais sont cachés du HUD (postfix SEMan.GetHUDStatusEffects,
  [Aura] HideCooldownIcons). Le joueur local écrit lw_aura_ready dans son ZDO : les autres voient la même chose.
- Aura : 2x plus dense, particules +35 %, plus opaque, petite lumière de la couleur de l'élément (sauf terre,
  feuilles, brume), sursaut de 40 particules au moment où le pouvoir revient.
- Dégâts relevés dans le log (total vanilla -> légendaire, x1,35) : hache 110 -> 149, lance 75 -> 101, épée 95 ->
  128, Brise-terre 35 -> 16 (gourdin 12), couteau 24 -> 32, Surtrbrand 170 -> 230, Ymir 182 -> 246. Les recettes
  désactivées empêchent aussi l'amélioration : toutes les légendaires restent qualité 1.

## v0.13.0 (2026-10-06) : améliorations et recharges revues
- Amélioration (accord de Lekinox) : la recette de chaque arme (sauf corne et canne) devient « amélioration
  seulement » (m_enabled, m_noCraftOnlyUpgrade ; absente de l'onglet fabrication, toujours la recette de réparation),
  qualité max 4 à la station de l'arme. Par niveau : 1 trophée du boss suivant + 5 lingots du biome suivant (craft à 0,
  donc la recette est connue dès que la station l'est : Player.HaveRequirementItems ignore les quantités nulles en
  découverte). Brise-terre TrophyTheElder + Bronze ; couteau TrophyBonemass + Iron ; hache TrophyDragonQueen + Silver ;
  lance TrophyGoblinKing + BlackMetal ; épée TrophySeekerQueen + Eitr ; Surtrbrand TrophyFader + Flametal ; Ymir
  FrozenKingDrop (sinon TrophyFader) + Gold. [Balance] Upgrades.
- Recharges : Surtrbrand 1800 s (un camp par nuit), canne 1200 s, géant 45 s toutes les 900 s. .cfg live mis à jour
  section par section.

## v0.13.1 (2026-10-06) : auras, textures, géant, Brise-terre
- Auras nettement réduites pour le couteau, la Lame des vents et la hache (x0,3) et la lance (x0,35). Lance :
  petits arcs électriques en zigzag (LineRenderer, 7 points) qui sautent sur la pointe de temps en temps. Ymir :
  l'aura naît sur la lame seulement (30 % de la longueur côté tête), plus le long de la hampe.
- Passe sur les textures (rendus 4 faces des .tam, script de scratchpad tam_views.py) : hache, épée, masse, couteau
  propres ; lance presque noire -> gamma 0,62 x1,2 ; corne : 16,6 % de taches bleues recolorées en os ; Surtrbrand :
  gamma 0,8 et carte émissive des zones orange (models/surtr_emit.png, 5 % de la texture, WeaponModels la charge si
  elle existe) ; Ymir : texture en patchwork. tools/build_weapons.py : fix_texture (gamma/gain, blue_to, emit) et
  --tex-only (réécrit les textures depuis les GLB sans toucher aux maillages déjà validés).
- Ymir plus os : nouveau concept ymirbone_7 (hampe en os de géant, lame de glace, crochet d'os), Trellis 2 ->
  2 890 tri ; main sur la hampe vérifiée sur l'aperçu.
- Surtrbrand : garde trop haute -> gripShift 0,06 -> 0,13 (la main juste sous la garde).
- Transformation en géant, vue de tous (début détecté par chaque client) : éclair de lumière froide, anneau de givre
  au sol (220 particules), éclats de glace (cubes), neige, colonne de brume qui tourne, boum + secousse forte ; le
  géant local repousse et gèle les créatures à 7 m. Fin : bouffée de neige.
- Brise-terre : pics de pierre (maillage et matériau de l'objet vanilla Stone) qui jaillissent le long de la faille
  au passage du front puis s'enfoncent, anneau de poussière et gerbe de gros éclats à l'impact, 2x plus de poussière.

## v0.13.2 (2026-10-06)
- Brise-terre : pics deux fois moins hauts (Height 0,45 x taille, échelle 0,4 x 0,75 x 0,4).
- Lame des vents plus légère : nouveau concept swordwind2_23 (lame fine argent pâle, poignée blanche à cordon
  bleu), Trellis 2 -> 610 tri, texture teintée bleu ciel (fix_texture tint) ; gripShift 0,08 -> 0,03 (main au
  milieu de la poignée, vérifié sur l'aperçu). Aura : fins filets d'air (traînées) qui glissent le long de la lame
  vers la pointe, nés sur la lame seulement.
- Aura de la lance du mauvais côté : la tête était déduite « loin de la main », or la lance vanilla se tient près de
  sa pointe. L'aura prend maintenant la vraie direction de la tête (WeaponModels.Heads), sinon l'ancienne règle.
- Géant : nouveau son ymir_giant (sourd, grave : coup saturé 90 -> 45 Hz, grondement de glace sous tension, craquements
  sourds) à la place du souffle de givre + séisme. Évaluateur : 8/10 après 3 tours.
- L'épée à deux mains s'appelle Braise-gardienne (EN Guardian Ember). Plantée : boucle de feu de camp discrète
  (surtr_camp, 12 s, fondu à puissance égale, AudioSource en boucle sur le groupe du mixeur du jeu, 2-18 m).
  Activation du camp : déflagration (surtr_blast) + éclair orange, boule de flammes rasante, anneau de fumée,
  gerbe d'étincelles, secousse ; aucun dégât. Évaluateur : camp 8, déflagration 8.

## v0.13.3 (2026-10-06)
- Brise-terre : vrai étourdissement (classe Daze) : petites étoiles qui tournent au-dessus de la tête et nouveau
  chancellement toutes les 0,8 s pendant [Earthbreaker] Stun 2,5 s x part de dégâts + 0,6 s ; jamais les boss.
- Lance : plus de petites particules, seulement les arcs électriques (et pas de sursaut de particules au retour).
- Lame des vents : aura un peu plus faible (x0,38) et centrée un peu plus près de la garde.
- Géant : animations à 80 % ([YmirBite] AnimationSpeed) : postfix CharacterAnimEvent.CustomFixedUpdate (le jeu
  remet la vitesse à 1 hors attaque) et prefix CharacterAnimEvent.Speed (attaques) pour le joueur local géant ;
  ZSyncAnimation transmet la vitesse aux autres.

## v0.13.4 (2026-10-06)
- Dôme de Braise-gardienne moins visible : voile 0,045 (DomeVeil), braises de paroi 40/s.
- Géant : animations à 75 %. Son de transformation raccourci à 1,9 s (évaluateur 8/10).
- Brise-terre : un seul étourdissement (plus de rechancellement), étoiles pendant [Earthbreaker] Stun 1,4 s.
- Lame des vents : aura centrée près de la garde (0,05 de la demi-longueur au-delà du centre de la lame).
- Croc du renard : temps de charge 0,6 -> 1 s (la recharge ne change pas).
- Corne de brume : plus de troisième étoile (elle n'existe pas dans le jeu) : niveau 3 par défaut, étoiles des
  esprits recolorées en rose (postfix EnemyHud.UpdateHuds, Graphic des level_2/level_3) ; Deathsquito ajouté aux
  esprits possibles. Référence UnityEngine.UI ajoutée au projet. .cfg live mis à jour.

## v0.13.5 (2026-10-06)
- Sortie de l'arme : quand une légendaire arrive en main avec son pouvoir prêt, son aura fait un sursaut (40
  particules ; la lance : une rafale de 5 arcs). Rien pendant la recharge : l'aura reste le minuteur.
- Braise-gardienne : quand le dôme s'éteint (ou qu'on reprend l'épée avant), ses braises se détachent de la paroi
  et retombent doucement en pluie en s'assombrissant (hémisphère, 350/s pendant 1,2 s, 2,5 à 4 s de vie).
