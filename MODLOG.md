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
