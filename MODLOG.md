# Caca — journal

Mod Valheim (BepInEx + Jotunn), GUID `lekinox.pipicacamod`, indépendant des autres mods (Wildlife, Legendary Weapons) :
aucun fichier ni lien partagé. Déploiement : `dotnet build -c Release`, copier `bin/Release/Caca.dll`,
`assets/sfx/*.wav` -> `plugins/Caca/sfx/`, `assets/icons/*.png` -> `plugins/Caca/icons/` (jeu fermé : la DLL est
verrouillée). Config : `BepInEx/config/lekinox.pipicacamod.cfg` (rechargée à chaud chaque seconde).

## v0.1.0 (2026-10-04)
- Envie de caca nourrie par les repas (digestion progressive), barre à l'écran, K à partir de 30 %, forcé à 100 %.
- Caca = clone de BombOoze retexturé (maillage procédural), se ramasse et se lance (éclate en éclaboussure brune).
- Sons synthétisés (tools/synth_caca.py) : pets, plop, splats.

## v0.2.0 (2026-10-05) : demandes de Lekinox
- **Barre invisible.** À la place, des effets de statut comme le froid (Urges.cs) : « Envie de caca [K] » dès 50 %
  (MinNeed 50, cfg live mise à jour), renommé « pressante » et clignotant à 85 %. Pas de malus. Barres de debug
  possibles : `[HUD] ShowBars = true`.
- **Pipi** (Pee.cs, PeeStream.cs, Body.cs) :
  - l'envie se remplit seule (FillPerMinute 4 = pleine en 25 min) ; statut « Envie de pipi [L] » dès 50 % ;
    L (ou `pipi`) pour y aller, L à nouveau pour s'arrêter ; à 100 %, pipi automatique ;
  - se vide en 6 s de 100 à 0 (DrainSeconds) ; la puissance suit ce qui reste (pow 0,55) : arc fort au début,
    gouttes à la fin ; 0,45 s de préparation (la main se place) avant le jet ;
  - le joueur marche (course, saut, roulade, attaques, blocage coupés via SetControls), le corps suit la caméra
    (AlwaysRotateCamera), le jet se vise à la souris (cône ±35° en lacet, -70..+45° en tangage, +12° de relevé) ;
  - jet simulé : ~70 gouttes/s avec la vitesse du joueur, gravité, traînée, raycast (sol, bâtiments, créatures) et
    surface de l'eau (Floating.GetLiquidLevel) ; le début du jet est une ligne continue (qui se courbe quand on
    tourne), la suite des gouttes étirées ; éclaboussures, taches mouillées qui sèchent en 1 min (24 max), boucle
    sonore sol/eau placée aux impacts, volume selon le débit ; teinte qui suit la lumière (shader non éclairé) ;
  - multijoueur : puissance, maintien et visée dans le ZDO du joueur (`caca_pee*`), chaque client simule ;
  - main : IK à deux os du bras droit après l'animator (patch CharacterAnimEvent.CustomLateUpdate), poignet relatif
    à l'os Hips, doigts refermés, pouce dans l'axe du jet ; l'arme en main droite est cachée pendant ce temps.
    Réglages live dans `[Tuning]` (TipOffset, WristOffset, FingerDir, FingerCurl).
- **Caca mal placé en main.** Cause : le nœud du maillage gardait la position de la bombe de suie
  (-0,034 ; -0,095 ; -0,112, lue avec UnityPy dans les bundles SoftRef) et une rotation remise à zéro : le caca était
  dans le poignet. Références vanilla : massue et torche ont la poigne à l'origine de `attach`, manche le long de +z.
  Le caca est maintenant tenu au milieu, le long de +z (HeldPos 0 0 0,03), l'égouttement de la bombe est retiré, le
  collider sphère devient une capsule autour du caca (posé à plat). `caca_hold x y z [rx ry rz]` pour régler en jeu.
- **Forme** : trois segments (étranglements) et pointe relevée (PoopArt.Mesh, 24 anneaux).
- **Icône d'inventaire** : rendu Blender de la même forme (tools/blender_icon.py), contour et ombre
  (tools/make_icons.py) ; icônes de statut dessinées (caca qui fume, goutte jaune). Fichiers dans icons/.
- **Sons** refaits (tools/synth_caca.py), évalués par un 2e agent sur spectrogrammes et mesures
  (tools/sound_report.py), seuil 8,5 :
  - tour 1 (nouveaux modèles) : pets 6,5 / 5,5 / 7,0 / 6,0, plop 5,5, splats 5,5, pipi sol 4,5, pipi eau 6,0
    (anciens : pets 3,5-4, plop 3,5, splats 5) ;
  - 6 tours en tout ; final, tous à 8,5 : pets (valve à impulsions, F1 qui suit la pression, battement 18-28 Hz,
    second coup de pression, crachotements finaux ; fart_2 glisse vers l'aigu, fart_3 de plus en plus mouillé),
    plop_1..3 (trois variantes : choc court, retombée, petit décollement collant), splat_1..2 (claque, squelch qui
    descend, fragments qui retombent de plus en plus mats), pipi sol et eau en boucles de 6 s (pas d'écrêtage :
    limiteur doux avant le passe-bas). Erreurs corrigées en route : écrêtage des boucles, argument de fragments
    oublié dans l'appel des splats, plop doublé en peigne.
  - À écouter en jeu : volume des boucles selon le débit, hauteur aléatoire des pets (0,94-1,08).
- RPC son générique `Caca_Sfx` (pet, plop) ; réenregistré à chaque session (avant : une seule fois).
- Test piloté : `plugins/Caca/lab/request.txt` (lignes `cmd ...`, `wait s`, `key PeeKey`, `view nom az él dist [h]`
  -> lab/nom.png rendu par une caméra autour du joueur).

## v0.2.1 (2026-10-05) : retours de Lekinox après test
- Son du pipi trop fort et réverbéré : volume 0,8 -> 0,3 (cfg live mise à jour) ; la boucle contourne les zones de
  réverbération (bypassReverbZones, comme les boucles d'ambiance du jeu).
- Sons du caca trop forts : nouveau `[Urge] Volume` = 0,4 (m_minVol/m_maxVol des ZSFX pets, plop, splat).
- Caca qui sortait trop en arrière : 0,12 m derrière au lieu de 0,35, poussée vers l'arrière 0,15 au lieu de 0,6.
- Jet trop bas : il part maintenant du poing droit placé par l'IK (entre poignet et phalanges, + TipFromHand 0,06 m
  dans l'axe du jet) ; TipOffset (depuis les hanches, relevé à -0,06) ne sert plus que de repli.

## v0.3.0 (2026-10-05)
- Jauge de pet cachée (Fart.cs) : se remplit seule et +8 % par repas ; pleine -> pet (son pour tous, nuage de gaz
  léger jaune-vert qui monte et s'étale), sans animation ni statut ; un caca la vide aussi. Commande `pet [0-100]`.
- Tout est paramétrable : interrupteurs EnablePoop/EnablePee/EnableFarts, UrgentAt, WetPatches, réglages des pets,
  volume des sons du caca appliqué en direct ; crochet de test `[Debug] Lab` désactivé par défaut.
- Relecture du code : raycast du jet qui pouvait traverser le sol derrière son propre corps (RaycastNonAlloc, plus
  proche impact hors soi) ; visée envoyée au ZDO seulement quand elle bouge ; commandes de test sans plantage
  sur un argument invalide ; collision de nom Plugin.Fart / classe Fart.
- Publication préparée (rien de publié) : README, CHANGELOG, icône 256 px (tools/make_icons.py), tools/package.py ->
  dist/Lekiteam-Pipi_Caca_Mod-<version>.zip (format Thunderstore / r2modman) ; `um publish check` sur le contenu du zip :
  seuls « échecs » = comparaison avec notre propre copie déployée dans plugins/Caca.
- Tournage : nouvelles commandes de lab (cam orbitale, aim, turn, tp, equip, hud). Vérifié en jeu : main en place,
  jet qui part du poing, arc ~2,5 m qui faiblit, nuage du pet visible. Leçons : en plein jour l'image est
  surexposée ; WinDrive en scanmode laisse W enfoncé (envoyer `key 0x57 up`) ; le filtre `grep valheim` attrape
  aussi le terminal (tuer par PID listé avec tasklist IMAGENAME). Captures finalement faites par Lekinox.

## v0.3.1 (2026-10-05)
- Pipi plein en 1,11 h (FillPerMinute 1,5), pet toutes les 33 min (3,03) ; cfg live mise à jour.
- Éclat blanc au sol : une tache mouillée neuve apparaissait 1 image en blanc opaque, 1 m (couleur et taille posées
  seulement au premier Update). Créée invisible et petite. Luminosité du jet plafonnée à 0,9 (pas de bloom).

## v0.3.2 (2026-10-05)
- Renommé « Pipi + Caca Mod » (nom BepInEx affiché, logs, README ; paquet Thunderstore `Pipi_Caca_Mod`, seuls lettres,
  chiffres et _ autorisés). Caca.dll gardé.
- `pet_need <0-100>` pour régler la jauge de pet (comme caca_need / pipi_need) ; `pet` seul fait péter.

## v0.4.0 (2026-10-05)
- Nouveau GUID `lekinox.pipicacamod` (config lekinox.pipicacamod.cfg). Les envies du personnage (m_customData caca_*)
  ne dépendent pas du GUID : gardées. Multijoueur : tout le monde en 0.4.0. Aucune référence au prénom de l'auteur.

## v0.5.0 (2026-10-05)
- Personnage femme (VisEquipment.GetModelIndex() == 1) : pipi accroupi. Crouch de Valheim forcé (champ m_crouchToggled,
  SetCrouch est protégé) : elle peut avancer accroupie, lentement ; jet depuis SquatTipOffset (sous les hanches), vers
  le sol (SquatPitch -62°), vitesse x0,45 (SquatSpeed), un peu plus dispersé ; la souris ne fait que l'orienter de
  ±15° ; pas d'IK de la main ni d'arme cachée. `[Pee] Pose` = Auto / Standing / Squatting. Posture publiée dans le ZDO
  (caca_pee_squat) pour les autres joueurs. Son du pipi 0,3 -> 0,2. À vérifier en jeu (pose et point de départ).

## 0.5.1 (2026-10-08)
- Pipi plus long : `DrainSeconds` 6 -> 10 (100 % en 10 s, 50 % en 5 s), cfg live mis à jour aussi.
- README : images en URL absolues raw.githubusercontent.com (monorepo mods_Valheim/pipi-caca/media), pour Thunderstore.
