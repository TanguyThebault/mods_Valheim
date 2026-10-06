using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// Surtrbrand (Ashlands greatsword) and its Guardian Ember. Hold the secondary attack: the sword is driven into
    /// the ground (the sledge's overhead slam) and stays there, glowing, while a dome like Haldor's (his ForceField,
    /// tinted ember) rises from it for [Surtrbrand] Duration:
    /// - creatures keep out: the game's own no-monster area makes them flee, and any that stand inside anyway are
    ///   pushed back out (by whoever owns them);
    /// - enemy projectiles and falling embers stop on the dome;
    /// - no creature spawns within SpawnBlockRadius (a player-base area);
    /// - the sword counts as a fire, and the dome as a shelter, for Rested;
    /// - the wielder comes back to life at the sword (their bed is left alone).
    /// Afterwards the sword stays planted, never despawns, and is taken back with Use (which ends the dome early).
    /// The planted state lives in the dropped sword's ZDO, so every client sees it and it survives a reload.
    /// </summary>
    internal static class Surtrbrand
    {
        internal static readonly int PlantedHash = "lw_surtr_planted".GetStableHashCode();
        internal static readonly int UntilHash = "lw_surtr_until".GetStableHashCode();
        internal static readonly int StartHash = "lw_surtr_start".GetStableHashCode();
        internal static readonly int OwnerHash = "lw_surtr_owner".GetStableHashCode();
        internal const string SpawnKey = "lw_surtr_spawn";
        internal static GameObject PlantSfx, DomeSfx, DomeEndSfx, PickupSfx, BlastSfx;
        internal static AudioClip CampClip;
        internal static UnityEngine.Audio.AudioMixerGroup Mixer;
        private static GameObject s_dome;
        private static Coroutine s_planting;

        public static void Register(ItemDrop.ItemData.SharedData sword, Sprite icon, GameObject templates)
        {
            var sledge = PrefabManager.Cache.GetPrefab<ItemDrop>("SledgeStagbreaker");
            var slam = sledge != null ? sledge.m_itemData.m_shared.m_attack.Clone() : sword.m_secondaryAttack.Clone();
            if (sledge == null)
                Plugin.Log.LogWarning("SledgeStagbreaker not found: the sword is planted with its own secondary swing");
            slam.m_attackType = Attack.AttackType.None;
            slam.m_startEffect = sword.m_secondaryAttack.m_startEffect;
            slam.m_triggerEffect = new EffectList();
            slam.m_hitEffect = new EffectList();
            slam.m_hitTerrainEffect = new EffectList();
            slam.m_attackStamina = sword.m_secondaryAttack.m_attackStamina;
            slam.m_attackChainLevels = 0;
            HoldPower.Register(new HoldPower.Spec
            {
                Token = Plugin.SurtrToken, HoldTime = () => Plugin.SurtrHoldTime.Value, PowerAttack = slam, Fire = Fire,
                CanStart = CanStart, Theme = ChargeFx.Theme.Ember, Cooldown = () => Plugin.SurtrCooldown.Value, Icon = icon,
                RestName = "$se_swordsurtr_rest", RestTooltip = "$se_swordsurtr_rest_tooltip",
            });
            s_dome = DomeTemplate(templates);
            Plugin.Log.LogInfo("Surtrbrand plant: animation " + slam.m_attackAnimation + " (sword secondary: " + sword.m_secondaryAttack.m_attackAnimation + ")");
        }

        /// <summary>Haldor's force field (a sphere, radius 0.5, its own material), kept inactive and tinted ember.</summary>
        private static GameObject DomeTemplate(GameObject templates)
        {
            GameObject src = null;
            try
            {
                var sr = AssetManager.Instance.GetSoftReference<GameObject>("ForceField");
                if (sr.IsValid)
                {
                    sr.Load();
                    src = sr.Asset;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("ForceField: no soft reference (" + e.Message + ")");
            }
            GameObject dome;
            if (src != null)
            {
                dome = Object.Instantiate(src, templates.transform, false);
                foreach (var t in dome.GetComponentsInChildren<Transform>(true))
                    if (t != dome.transform && t.GetComponent<Renderer>() == null)
                        Object.DestroyImmediate(t.gameObject);   // its no-monster area: ours are built to size
                foreach (var c in dome.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            }
            else
            {
                Plugin.Log.LogWarning("Haldor's ForceField not found: the dome is a plain sphere");
                dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(dome.GetComponent<Collider>());
                dome.transform.SetParent(templates.transform, false);
                dome.GetComponent<Renderer>().sharedMaterial = new Material(Fx.Plain) { color = new Color(1f, 0.4f, 0.1f, 0.1f) };
            }
            dome.name = "surtrbrand_dome";
            dome.transform.localPosition = Vector3.zero;
            dome.transform.localRotation = Quaternion.identity;
            dome.transform.localScale = Vector3.one;
            foreach (var r in dome.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var m = new Material(mats[i]) { name = mats[i].name + "_ember" };
                    var props = new List<string>();
                    for (int p = 0; p < m.shader.GetPropertyCount(); p++)
                        if (m.shader.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Color)
                            props.Add(m.shader.GetPropertyName(p));
                    Plugin.Log.LogInfo("Dome material " + mats[i].name + ", shader " + m.shader.name + ", colours: " + string.Join(",", props));
                    foreach (var name in props)
                    {
                        var c = m.GetColor(name);
                        m.SetColor(name, Ember(c, Plugin.SurtrDomeTint.Value));
                    }
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return dome;
        }

        /// <summary>A colour pushed to ember: its brightness kept, its hue taken from the tint, alpha kept.</summary>
        private static Color Ember(Color c, Color tint)
        {
            float v = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return new Color(tint.r * v, tint.g * v, tint.b * v, c.a);
        }

        internal static GameObject NewDome() => s_dome != null ? Object.Instantiate(s_dome) : null;

        private static bool CanStart(Player p)
        {
            if (s_planting != null || p.IsSwimming() || p.InWater() || !p.IsOnGround())
                return false;
            return true;
        }

        /// <summary>At the slam's blow: the sword leaves the hand once the swing is over.</summary>
        public static void Fire(Player p, ItemDrop.ItemData weapon)
        {
            if (weapon == null || Plugin.Instance == null)
                return;
            s_planting = Plugin.Instance.StartCoroutine(PlantWhenDone(p, weapon));
        }

        private static IEnumerator PlantWhenDone(Player p, ItemDrop.ItemData weapon)
        {
            float t0 = Time.time;
            while (p != null && p.InAttack() && Time.time - t0 < 1.5f)
                yield return null;
            s_planting = null;
            if (p == null || p.IsDead() || !p.GetInventory().ContainsItem(weapon))
                yield break;
            Vector3 fwd = Vector3.ProjectOnPlane(p.transform.forward, Vector3.up).normalized;
            Vector3 at = p.transform.position + fwd * Plugin.SurtrPlantDistance.Value;
            at.y = GroundY(at, p.transform.position.y);
            p.UnequipItem(weapon, false);
            p.GetInventory().RemoveItem(weapon);
            var drop = ItemDrop.DropItem(weapon, 1, at + Vector3.up * 1.5f, Quaternion.LookRotation(fwd));
            var plant = drop != null ? drop.GetComponent<SurtrPlant>() : null;
            if (plant == null)
            {
                Plugin.Log.LogWarning("Surtrbrand: the dropped sword has no SurtrPlant, it just lies there");
                yield break;
            }
            plant.Plant(at, fwd, p);
            Remember(p, at);
        }

        private static int s_ground;

        internal static float GroundY(Vector3 p, float fallback)
        {
            if (s_ground == 0)
                s_ground = LayerMask.GetMask("terrain", "static_solid", "Default", "piece");
            return Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 6f, s_ground, QueryTriggerInteraction.Ignore)
                ? hit.point.y : (ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(p) : fallback);
        }

        /// <summary>The respawn point, in the player's own save: where, and until when (world ticks).</summary>
        private static void Remember(Player p, Vector3 at)
        {
            long until = ZNet.instance.GetTime().Ticks + (long)(Plugin.SurtrDuration.Value * 1e7);
            p.m_customData[SpawnKey] = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", at.x, at.y, at.z, until);
        }

        internal static bool SpawnPoint(Player p, out Vector3 at)
        {
            at = Vector3.zero;
            if (p == null || !p.m_customData.TryGetValue(SpawnKey, out var s))
                return false;
            var parts = s.Split(',');
            if (parts.Length != 4 || !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var until) ||
                ZNet.instance == null || ZNet.instance.GetTime().Ticks > until)
            {
                p.m_customData.Remove(SpawnKey);
                return false;
            }
            at = new Vector3(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture));
            return true;
        }

        internal static void Forget(Player p)
        {
            if (p != null) p.m_customData.Remove(SpawnKey);
        }

        /// <summary>Every frame: a respawn point found stale (the sword was gone) is dropped from the new player.</summary>
        public static void Tick()
        {
            if (SurtrRespawn.Stale && Player.m_localPlayer != null)
            {
                SurtrRespawn.Stale = false;
                Forget(Player.m_localPlayer);
            }
        }
    }

    /// <summary>
    /// On every Surtrbrand lying in the world. Once planted (ZDO flag): upright, blade in the ground, no physics,
    /// never picked up by walking past, never despawned; glowing embers; the dome and its areas while it lasts.
    /// </summary>
    public class SurtrPlant : MonoBehaviour
    {
        internal static readonly List<SurtrPlant> Active = new List<SurtrPlant>();
        private static readonly AccessTools.FieldRef<Character, Rigidbody> s_body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");

        private ZNetView _nview;
        private ItemDrop _item;
        private bool _setUp, _domeUp, _ended;
        private GameObject _dome, _areas, _veil;
        private Material _veilMat;
        private ParticleSystem _shell;
        private Material[] _domeMats;
        private float[] _domeAlpha;
        private ParticleSystem _embers;
        private Light _light;
        private float _nextPush, _domeAge;
        internal Vector3 Centre;
        internal float Radius;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _item = GetComponent<ItemDrop>();
            if (_item != null)
                _item.m_autoDestroy = false;          // a legendary never rots on the ground
        }

        private void Start()
        {
            if (!_setUp && _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(Surtrbrand.PlantedHash))
                SetUp();
        }

        internal bool Planted => _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(Surtrbrand.PlantedHash);

        private double Remaining =>
            _nview != null && _nview.IsValid() && ZNet.instance != null
                ? (_nview.GetZDO().GetLong(Surtrbrand.UntilHash) - ZNet.instance.GetTime().Ticks) / 1e7 : 0.0;

        /// <summary>On the wielder: drive the sword in at a point of the ground, facing that way.</summary>
        internal void Plant(Vector3 ground, Vector3 fwd, Player owner)
        {
            var zdo = _nview.GetZDO();
            long now = ZNet.instance.GetTime().Ticks;
            zdo.Set(Surtrbrand.PlantedHash, true);
            zdo.Set(Surtrbrand.StartHash, now);
            zdo.Set(Surtrbrand.UntilHash, now + (long)(Plugin.SurtrDuration.Value * 1e7));
            zdo.Set(Surtrbrand.OwnerHash, owner.GetPlayerID());
            SetUp();
            Upright(ground, fwd);
            Sfx.Play(Surtrbrand.PlantSfx, ground, null);
            if (GameCamera.instance != null)
                GameCamera.instance.AddShake(ground, 10f, 0.6f, false);
            Plugin.Log.LogInfo("Surtrbrand planted at " + ground.ToString("F1") + " for " + Plugin.SurtrDuration.Value + " s");
        }

        /// <summary>The blade straight down (a slight lean), its tip in the ground.</summary>
        private void Upright(Vector3 ground, Vector3 fwd)
        {
            transform.rotation = Quaternion.LookRotation(fwd);
            if (!Tip(out var tipLocal, out var gripLocal))
                return;
            Vector3 down = (tipLocal - gripLocal).normalized;
            var lean = Quaternion.AngleAxis(Plugin.SurtrLean.Value, Vector3.Cross(Vector3.up, fwd));
            // turn the item so its grip-to-tip line points straight down, then lean it a little
            transform.rotation = lean * Quaternion.FromToRotation(transform.TransformDirection(down), Vector3.down) * transform.rotation;
            Vector3 tipWorld = transform.TransformPoint(tipLocal);
            transform.position += ground - Vector3.up * Plugin.SurtrSink.Value - tipWorld;
        }

        /// <summary>The blade's tip and the grip end, in the item's space: the ends of its meshes' longest axis.</summary>
        private bool Tip(out Vector3 tip, out Vector3 grip)
        {
            if (_tipKnown)
            {
                tip = _tip;
                grip = _grip;
                return _tipFound;
            }
            _tipKnown = true;
            _tipFound = FindTip(out _tip, out _grip);
            tip = _tip;
            grip = _grip;
            if (_tipFound)
                Plugin.Log.LogDebug("Surtrbrand tip " + _tip.ToString("F2") + ", grip " + _grip.ToString("F2") + " (item space)");
            return _tipFound;
        }

        private bool _tipKnown, _tipFound;
        private Vector3 _tip, _grip;

        private bool FindTip(out Vector3 tip, out Vector3 grip)
        {
            tip = grip = Vector3.zero;
            if (!Geometry.LocalBounds(transform, transform, out var b))
                return false;
            var e = b.extents;
            int k = e.x >= e.y && e.x >= e.z ? 0 : (e.y >= e.z ? 1 : 2);
            Vector3 axis = k == 0 ? Vector3.right : k == 1 ? Vector3.up : Vector3.forward;
            float half = k == 0 ? e.x : k == 1 ? e.y : e.z;
            Vector3 a = b.center + axis * half, c = b.center - axis * half;
            // the blade is the end further from where the hand holds it (the item's attach point, else its origin)
            var attach = transform.Find("attach");
            Vector3 hand = attach != null ? transform.InverseTransformPoint(attach.position) : Vector3.zero;
            bool aFar = (a - hand).sqrMagnitude >= (c - hand).sqrMagnitude;
            tip = aFar ? a : c;
            grip = aFar ? c : a;
            return true;
        }

        private void SetUp()
        {
            _setUp = true;
            if (_item != null) _item.m_autoPickup = false;
            foreach (var rb in GetComponentsInChildren<Rigidbody>())
            {
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            BuildEmbers();
            CampSound();
            if (!Active.Contains(this)) Active.Add(this);
        }

        /// <summary>A quiet campfire burning at the planted sword, looped, as long as it stands in the ground.</summary>
        private void CampSound()
        {
            if (Surtrbrand.CampClip == null)
                return;
            var go = new GameObject("surtrbrand_campfire");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = Surtrbrand.CampClip;
            src.loop = true;
            src.outputAudioMixerGroup = Surtrbrand.Mixer;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            src.maxDistance = 18f;
            src.volume = 0.9f;
            src.time = Random.Range(0f, Surtrbrand.CampClip.length);
            src.Play();
        }

        /// <summary>
        /// The camp catching (no damage): a flash of orange light, a ball of flame thrown out low over the ground, a
        /// ring of smoke rolling away, a shower of sparks.
        /// </summary>
        private static void Blast(Vector3 at)
        {
            if (GameCamera.instance != null)
                GameCamera.instance.AddShake(at, 20f, 1.2f, false);
            var root = new GameObject("surtrbrand_blast");
            root.transform.position = at;
            Destroy(root, 4f);
            var lg = new GameObject("flash");
            lg.transform.SetParent(root.transform, false);
            lg.transform.localPosition = Vector3.up * 1.2f;
            var light = lg.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.range = 16f;
            light.intensity = 4f;
            lg.AddComponent<LightFade>().Seconds = 0.9f;
            Puff(root.transform, "flame", 260, 6f, 11f, 0.35f, 0.7f, 0.3f, 0.9f,
                new Color(1f, 0.75f, 0.3f, 0.9f), new Color(1f, 0.35f, 0.08f, 0.8f), 0.5f, true);
            Puff(root.transform, "smoke", 120, 3f, 5f, 1.2f, 2.2f, 0.8f, 1.6f,
                new Color(0.18f, 0.16f, 0.15f, 0.45f), new Color(0.3f, 0.28f, 0.26f, 0.35f), 0.3f, true);
            Puff(root.transform, "sparks", 90, 4f, 9f, 0.8f, 1.6f, 0.03f, 0.07f,
                new Color(1f, 0.8f, 0.4f, 1f), new Color(1f, 0.5f, 0.15f, 1f), 1.2f, false);
        }

        private static void Puff(Transform root, string name, int count, float speed0, float speed1, float life0, float life1,
            float size0, float size1, Color a, Color b, float y, bool flat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.up * y;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life0, life1);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed0, speed1);
            main.startSize = new ParticleSystem.MinMaxCurve(size0, size1);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = flat ? -0.05f : 0.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count + 10;
            var shape = ps.shape;
            shape.shapeType = flat ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.4f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = flat ? 2.5f : 0.6f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, flat ? 1.8f : 0.4f));
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fx.Soft;
            ps.Play();
        }

        private void BuildEmbers()
        {
            var go = new GameObject("surtrbrand_embers");
            go.transform.SetParent(transform, false);
            _embers = go.AddComponent<ParticleSystem>();
            _embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _embers.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.1f, 0.9f), new Color(1f, 0.75f, 0.3f, 0.9f));
            main.gravityModifier = -0.08f;
            main.maxParticles = 200;
            var shape = _embers.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;
            var noise = _embers.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.8f;
            var col = _embers.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.5f, 0.3f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fx.Soft;
            _embers.Play();
            var lg = new GameObject("surtrbrand_light");
            lg.transform.SetParent(transform, false);
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.5f, 0.2f);
            _light.range = 4f;
            _light.intensity = 0.8f;
        }

        private void LateUpdate()
        {
            if (!_setUp || _nview == null || !_nview.IsValid())
                return;
            // embers rise from the blade's base, at the ground
            var bladeBase = transform.position;
            if (Tip(out var tipLocal, out var gripLocal))
                bladeBase = Vector3.Lerp(transform.TransformPoint(gripLocal), transform.TransformPoint(tipLocal), 0.55f);
            _embers.transform.position = bladeBase;
            _light.transform.position = bladeBase + Vector3.up * 0.3f;
            double left = Remaining;
            bool active = left > 0.0;
            var em = _embers.emission;
            em.rateOverTime = active ? 14f : 3f;
            _light.intensity = (active ? 0.9f : 0.3f) * (0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 3f, 0.3f));
            if (active && !_domeUp)
                RaiseDome(bladeBase);
            if (_domeUp)
                UpdateDome(left);
        }

        private void RaiseDome(Vector3 at)
        {
            _domeUp = true;
            _ended = false;
            _domeAge = 0f;
            Radius = Plugin.SurtrRadius.Value;
            Centre = new Vector3(at.x, Surtrbrand.GroundY(at, at.y), at.z);
            _dome = Surtrbrand.NewDome();
            if (_dome != null)
            {
                _dome.transform.position = Centre;
                _dome.transform.localScale = Vector3.zero;
                _dome.SetActive(true);
                var rs = _dome.GetComponentsInChildren<Renderer>();
                var mats = new List<Material>();
                foreach (var r in rs)
                {
                    var own = r.materials;                      // our own copies, faded independently
                    mats.AddRange(own);
                }
                _domeMats = mats.ToArray();
                _domeAlpha = new float[_domeMats.Length];
                for (int i = 0; i < _domeMats.Length; i++)
                    _domeAlpha[i] = _domeMats[i].HasProperty("_Color") ? _domeMats[i].color.a : 1f;
            }
            BuildVeil();
            _areas = new GameObject("surtrbrand_areas");
            _areas.SetActive(false);
            _areas.transform.position = Centre;
            Area(_areas.transform, EffectArea.Type.NoMonsters | EffectArea.Type.Heat | EffectArea.Type.WarmCozyArea, Radius);
            Area(_areas.transform, EffectArea.Type.PlayerBase, Plugin.SurtrSpawnBlock.Value);
            _areas.SetActive(true);
            // only a fresh planting rises with a sound (not one found already standing after a reload)
            long start = _nview.GetZDO().GetLong(Surtrbrand.StartHash);
            if ((ZNet.instance.GetTime().Ticks - start) / 1e7 < 5.0)
            {
                Sfx.Play(Surtrbrand.BlastSfx, Centre, null);
                Blast(Centre);
            }
            else
                _domeAge = 10f;
        }

        /// <summary>
        /// What makes the dome visible (Haldor's force field only bends the view behind it): a faint ember veil and
        /// embers drifting up its wall.
        /// </summary>
        private void BuildVeil()
        {
            _veil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _veil.name = "surtrbrand_veil";
            Destroy(_veil.GetComponent<Collider>());
            _veilMat = new Material(Fx.Plain) { name = "surtrbrand_veil" };
            _veilMat.renderQueue = 3100;
            var r = _veil.GetComponent<MeshRenderer>();
            r.sharedMaterial = _veilMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _veil.transform.position = Centre;
            _veil.transform.localScale = Vector3.zero;

            var sg = new GameObject("surtrbrand_shell");
            sg.transform.position = Centre;
            sg.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            _shell = sg.AddComponent<ParticleSystem>();
            _shell.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _shell.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.12f, 0.9f), new Color(1f, 0.75f, 0.35f, 0.9f));
            main.gravityModifier = -0.03f;
            main.maxParticles = 600;
            var shape = _shell.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = Radius;
            shape.radiusThickness = 0f;
            var col = _shell.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var em = _shell.emission;
            em.rateOverTime = 0f;
            sg.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fx.Soft;
            _shell.Play();
        }

        private static void Area(Transform parent, EffectArea.Type type, float radius)
        {
            var go = new GameObject("area_" + type);
            go.layer = LayerMask.NameToLayer("character_trigger");
            go.transform.SetParent(parent, false);
            var col = go.AddComponent<SphereCollider>();
            col.radius = radius;
            col.isTrigger = true;
            var area = go.AddComponent<EffectArea>();
            area.m_type = type;
        }

        private void UpdateDome(double left)
        {
            _domeAge += Time.deltaTime;
            float grow = Mathf.Clamp01(_domeAge / 1.4f);
            grow = 1f - (1f - grow) * (1f - grow) * (1f - grow);          // fast, then easing
            float fade = left > 0.0 ? Mathf.Clamp01((float)left / 2f) : 0f;
            if (left <= 0.0 && !_ended)
            {
                _ended = true;
                Sfx.Play(Surtrbrand.DomeEndSfx, Centre, null);
                Ash(Centre, Radius);
                EmberRain(Centre, Radius);
                Plugin.Log.LogInfo("Surtrbrand: the dome fades");
            }
            float glow = fade * (0.85f + 0.15f * Mathf.Sin(Time.time * 1.3f));
            if (_veil != null)
            {
                _veil.transform.localScale = Vector3.one * (2f * Radius * grow);
                _veilMat.color = new Color(1f, 0.42f, 0.12f, Plugin.SurtrVeil.Value * glow);
                var em = _shell.emission;
                em.rateOverTime = _ended ? 0f : 40f * grow;
            }
            if (_dome != null)
            {
                _dome.transform.localScale = Vector3.one * (2f * Radius * grow);
                float pulse = 0.9f + 0.1f * Mathf.Sin(Time.time * 1.7f);
                for (int i = 0; i < _domeMats.Length; i++)
                    if (_domeMats[i].HasProperty("_Color"))
                    {
                        var c = _domeMats[i].color;
                        c.a = _domeAlpha[i] * fade * pulse;
                        _domeMats[i].color = c;
                    }
            }
            if (_ended && left < -2.0)
            {
                DropDome();
                return;
            }
            if (!_ended && Time.time >= _nextPush)
            {
                _nextPush = Time.time + 0.25f;
                KeepOut();
            }
        }

        /// <summary>Creatures standing inside are thrown back out (each client moves the ones it owns).</summary>
        private void KeepOut()
        {
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c.IsPlayer() || c.IsTamed() || c.IsDead())
                    continue;
                Vector3 d = c.transform.position - Centre;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist > Radius + 0.3f)
                    continue;
                Vector3 outDir = dist > 0.01f ? d / dist : transform.forward;
                Sparks(Centre + outDir * Radius + Vector3.up * (c.transform.position.y - Centre.y + 1f));
                var nview = c.GetComponent<ZNetView>();
                var body = s_body(c);
                if (nview == null || !nview.IsOwner() || body == null)
                    continue;
                if (dist < Radius - 0.5f)
                    body.position = Centre + outDir * (Radius + 0.4f) + Vector3.up * (body.position.y - Centre.y);
                float heavy = Mathf.Clamp(80f / Mathf.Max(1f, body.mass), 0.4f, 1f);
                body.linearVelocity = outDir * Plugin.SurtrPush.Value * heavy + Vector3.up * 2f;
                c.Stagger(outDir);
            }
        }

        private float _nextSparks;

        private void Sparks(Vector3 at)
        {
            if (Time.time < _nextSparks)
                return;
            _nextSparks = Time.time + 0.2f;
            Burst(at, 18, new Color(1f, 0.55f, 0.15f, 0.95f), 4f, 0.6f);
        }

        /// <summary>The dome's embers coming loose from its wall and drifting down, dimming as they fall.</summary>
        internal static void EmberRain(Vector3 centre, float radius)
        {
            var go = new GameObject("surtrbrand_ember_rain");
            go.transform.position = centre;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 1.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.15f, 0.95f), new Color(1f, 0.75f, 0.35f, 0.95f));
            main.gravityModifier = 0.06f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = radius;
            shape.radiusThickness = 0f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.6f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.6f, 0.3f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var em = ps.emission;
            em.rateOverTime = 350f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fx.Soft;
            ps.Play();
            Destroy(go, 6f);
        }

        internal static void Ash(Vector3 centre, float radius)
        {
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Burst(centre + new Vector3(Mathf.Cos(a), 0.3f + i * 0.5f, Mathf.Sin(a)) * radius * 0.7f, 25,
                    new Color(0.35f, 0.3f, 0.28f, 0.6f), 1f, 2.5f);
            }
        }

        private static void Burst(Vector3 at, int n, Color color, float speed, float life)
        {
            var go = new GameObject("surtrbrand_burst");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = color;
            main.gravityModifier = speed > 2f ? 0.6f : -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)n) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fx.Soft;
            ps.Play();
            Destroy(go, life + 0.5f);
        }

        private void DropDome()
        {
            _domeUp = false;
            if (_dome != null) Destroy(_dome);
            if (_areas != null) Destroy(_areas);
            if (_veil != null) Destroy(_veil);
            if (_shell != null) Destroy(_shell.gameObject, 3.5f);
            if (_shell != null) _shell.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _dome = _areas = _veil = null;
            _shell = null;
        }

        internal bool Shields(Vector3 p) => _domeUp && !_ended && (p - Centre).sqrMagnitude < Radius * Radius;

        private void OnDestroy()
        {
            Active.Remove(this);
            if (_domeUp && !_ended && _dome != null)
            {
                Ash(Centre, Radius);
                EmberRain(Centre, Radius);
                Sfx.Play(Surtrbrand.PickupSfx, Centre, null);
            }
            DropDome();
        }

        internal static bool InsideDome(Vector3 p)
        {
            foreach (var s in Active)
                if (s != null && s.Shields(p))
                    return true;
            return false;
        }

        internal static SurtrPlant DomeAt(Vector3 p)
        {
            foreach (var s in Active)
                if (s != null && s.Shields(p))
                    return s;
            return null;
        }

        internal static bool PlantedNear(Vector3 p, float range)
        {
            foreach (var s in Active)
                if (s != null && s.Planted && s.Remaining > 0.0 && Vector3.Distance(s.transform.position, p) < range)
                    return true;
            return false;
        }

        /// <summary>Hit by a projectile or an ember: sparks and a flash of the dome where it struck.</summary>
        internal void Struck(Vector3 at)
        {
            Burst(at, 24, new Color(1f, 0.6f, 0.2f, 0.95f), 5f, 0.5f);
            _domeAge = Mathf.Min(_domeAge, 10f);
        }
    }

    /// <summary>Picking up a planted sword ends the dome and forgets the respawn point.</summary>
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Pickup))]
    internal static class SurtrPickup
    {
        private static void Prefix(ItemDrop __instance, Humanoid character)
        {
            var plant = __instance.GetComponent<SurtrPlant>();
            if (plant == null || !plant.Planted)
                return;
            if (character is Player p && p == Player.m_localPlayer)
                Surtrbrand.Forget(p);
            Plugin.Log.LogInfo("Surtrbrand pulled out of the ground");
        }
    }

    /// <summary>Enemy projectiles (thrown from outside) stop on the dome.</summary>
    [HarmonyPatch(typeof(ShieldGenerator), nameof(ShieldGenerator.CheckProjectile))]
    internal static class SurtrBlocksProjectiles
    {
        private static readonly AccessTools.FieldRef<Projectile, Character> s_owner = AccessTools.FieldRefAccess<Projectile, Character>("m_owner");
        private static readonly AccessTools.FieldRef<Projectile, ZNetView> s_nview = AccessTools.FieldRefAccess<Projectile, ZNetView>("m_nview");

        private static void Postfix(Projectile projectile)
        {
            if (SurtrPlant.Active.Count == 0 || projectile == null)
                return;
            var nview = s_nview(projectile);
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
                return;
            var owner = s_owner(projectile);
            if (owner != null && (owner.IsPlayer() || owner.IsTamed()))
                return;
            var pos = projectile.transform.position;
            var dome = SurtrPlant.DomeAt(pos);
            if (dome == null || dome.Shields(projectile.m_startPoint))
                return;
            projectile.OnHit(null, pos, false, -projectile.transform.forward);
            if (nview.IsValid())
                ZNetScene.instance.Destroy(projectile.gameObject);
            dome.Struck(pos);
        }
    }

    /// <summary>Falling embers (Ashlands cinders) go out on the dome.</summary>
    [HarmonyPatch(typeof(ShieldGenerator), nameof(ShieldGenerator.CheckObjectInsideShield))]
    internal static class SurtrBlocksCinders
    {
        private static void Postfix(Cinder zinder)
        {
            if (SurtrPlant.Active.Count == 0 || zinder == null)
                return;
            var dome = SurtrPlant.DomeAt(zinder.transform.position);
            var nview = zinder.GetComponent<ZNetView>();
            if (dome == null || nview == null || !nview.IsValid() || !nview.IsOwner())
                return;
            dome.Struck(zinder.transform.position);
            ZNetScene.instance.Destroy(zinder.gameObject);
        }
    }

    /// <summary>Inside the dome counts as a shelter (with the sword's heat, the player can rest).</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.InShelter))]
    internal static class SurtrShelter
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (!__result && SurtrPlant.Active.Count > 0 && SurtrPlant.InsideDome(__instance.transform.position))
                __result = true;
        }
    }

    /// <summary>
    /// After a death, the wielder comes back next to the planted sword while its dome lasts. The bed spawn point is
    /// never touched: if the sword is gone when the area loads, the usual spawn takes over.
    /// </summary>
    [HarmonyPatch(typeof(Game), "FindSpawnPoint")]
    internal static class SurtrRespawn
    {
        private static readonly AccessTools.FieldRef<Game, bool> s_afterDeath = AccessTools.FieldRefAccess<Game, bool>("m_respawnAfterDeath");
        private static readonly AccessTools.FieldRef<Game, float> s_wait = AccessTools.FieldRefAccess<Game, float>("m_respawnWait");
        /// <summary>Taken when the player dies (the dead player is gone while the game looks for a spawn point).</summary>
        internal static Vector3? DiedWith;
        /// <summary>The sword was gone: the respawned player's saved point is stale.</summary>
        internal static bool Stale;

        private static bool Prefix(Game __instance, ref Vector3 point, ref bool usedLogoutPoint, float dt, ref bool __result)
        {
            if (!s_afterDeath(__instance) || !Plugin.SurtrRespawn.Value || DiedWith == null)
                return true;
            var at = DiedWith.Value;
            s_wait(__instance) += dt;
            usedLogoutPoint = false;
            ZNet.instance.SetReferencePosition(at);
            point = Vector3.zero;
            __result = false;
            if (s_wait(__instance) <= __instance.m_respawnLoadDuration || !ZNetScene.instance.IsAreaReady(at))
                return false;
            if (!SurtrPlant.PlantedNear(at, 4f))
            {
                Plugin.Log.LogInfo("Surtrbrand respawn: no planted sword at " + at.ToString("F0") + " any more, usual spawn point");
                DiedWith = null;
                Stale = true;
                s_wait(__instance) = 0f;
                return false;
            }
            // beside the sword
            Vector3 p = at + new Vector3(1.5f, 0f, -1.5f);
            p.y = Surtrbrand.GroundY(p, at.y) + 0.25f;
            point = p;
            __result = true;
            DiedWith = null;
            Plugin.Log.LogInfo("Surtrbrand respawn at the planted sword " + at.ToString("F0"));
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class SurtrDeath
    {
        private static void Prefix(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
                return;
            SurtrRespawn.DiedWith = Surtrbrand.SpawnPoint(__instance, out var at) ? at : (Vector3?)null;
        }
    }
}
