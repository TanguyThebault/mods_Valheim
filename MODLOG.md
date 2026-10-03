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
