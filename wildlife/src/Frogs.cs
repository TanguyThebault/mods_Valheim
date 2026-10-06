using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// Swamp frogs. Underneath, an invisible vanilla hare (its skittish AnimalAI, physics and network); on top, our
    /// generated frog on its own skeleton (models/frog.tam + frog.rig, tools/blender_frog.py) with clips played by
    /// FrogAnim: breathing, hopping (paced on the real speed), swimming and croaking. They live in small groups by the
    /// water, call in chorus at night (and fall silent when someone comes near), squeal when hit, and leave frog legs
    /// and sometimes their skin.
    /// </summary>
    internal static class Frogs
    {
        public const string CreaturePrefab = "Frog";
        public const string LegsPrefab = "FrogLegs";
        public const string CookedPrefab = "CookedFrogLegs";
        public const string SkinPrefab = "FrogSkin";

        private static ConfigEntry<float> s_spawnChance, s_scale, s_length, s_volume, s_hopHeight;
        private static ConfigEntry<int> s_maxSpawned;
        internal static AudioClip[] Croaks = new AudioClip[0];
        internal static float Volume => s_volume.Value;
        internal static float HopHeight => s_hopHeight.Value;

        public static void BindConfig(ConfigFile config)
        {
            s_spawnChance = config.Bind("Frog", "SpawnChance", 25f, "Spawn chance per spawn check (every 90 s), % (restart).");
            s_maxSpawned = config.Bind("Frog", "MaxSpawned", 6, "Max frogs around a player (restart).");
            s_scale = config.Bind("Frog", "Scale", 1.3f, "Size of the invisible hare underneath (collider, speed) (restart).");
            s_length = config.Bind("Frog", "Length", 0.45f, "Frog length at creature scale 1, m (restart).");
            s_volume = config.Bind("Frog", "Volume", 0.55f, "Volume of the croaks, 0-1 (live).");
            s_hopHeight = config.Bind("Frog", "HopHeight", 1.0f, "Highest a hop rises, m (live): a hop rises 35 % of its length, which comes from the speed.");
        }

        public static void AddTranslations(CustomLocalization loc)
        {
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "enemy_frog", "Frog" },
                { "item_froglegs", "Frog legs" },
                { "item_froglegs_desc", "Raw frog legs. Grill them." },
                { "item_froglegs_cooked", "Grilled frog legs" },
                { "item_froglegs_cooked_desc", "Tender, a little like chicken. A swamp delicacy." },
                { "item_frogskin", "Frog skin" },
                { "item_frogskin_desc", "Thin, damp and supple. It never quite dries out." },
            });
            loc.AddTranslation("French", new Dictionary<string, string>
            {
                { "enemy_frog", "Grenouille" },
                { "item_froglegs", "Cuisses de grenouille" },
                { "item_froglegs_desc", "Des cuisses de grenouille crues. À griller." },
                { "item_froglegs_cooked", "Cuisses de grenouille grillées" },
                { "item_froglegs_cooked_desc", "Tendres, un peu comme du poulet. Une délicatesse des marais." },
                { "item_frogskin", "Peau de grenouille" },
                { "item_frogskin_desc", "Fine, humide et souple. Elle ne sèche jamais tout à fait." },
            });
        }

        public static void Register()
        {
            RegisterItems();
            RegisterCreature();
        }

        private static void RegisterItems()
        {
            var legs = new CustomItem(LegsPrefab, "DeerMeat", new ItemConfig { Name = "$item_froglegs", Description = "$item_froglegs_desc", Weight = 0.2f });
            legs.ItemPrefab.transform.localScale *= 0.4f;
            Rabbits.Tint(legs.ItemPrefab, new Color(0.95f, 0.85f, 0.75f));
            Rabbits.SetIcon(legs);
            ItemManager.Instance.AddItem(legs);

            var cooked = new CustomItem(CookedPrefab, "CookedDeerMeat", new ItemConfig { Name = "$item_froglegs_cooked", Description = "$item_froglegs_cooked_desc", Weight = 0.2f });
            cooked.ItemPrefab.transform.localScale *= 0.4f;
            var food = cooked.ItemDrop.m_itemData.m_shared;
            food.m_food = 30f;            // Swamp tier, light: less than sausages, more stamina
            food.m_foodStamina = 30f;
            food.m_foodBurnTime = 1500f;
            food.m_foodRegen = 2f;
            Rabbits.SetIcon(cooked);
            ItemManager.Instance.AddItem(cooked);
            ItemManager.Instance.AddItemConversion(new CustomItemConversion(new CookingConversionConfig
            {
                Station = "piece_cookingstation", FromItem = LegsPrefab, ToItem = CookedPrefab, CookTime = 15f,
            }));

            var skin = new CustomItem(SkinPrefab, "DeerHide", new ItemConfig { Name = "$item_frogskin", Description = "$item_frogskin_desc", Weight = 0.2f });
            skin.ItemPrefab.transform.localScale *= 0.45f;
            Rabbits.Tint(skin.ItemPrefab, new Color(0.55f, 0.65f, 0.35f));
            skin.ItemDrop.m_itemData.m_shared.m_maxStackSize = 50;
            Rabbits.SetIcon(skin);
            ItemManager.Instance.AddItem(skin);
        }

        private static void RegisterCreature()
        {
            var config = new CreatureConfig { Name = "$enemy_frog", Faction = Character.Faction.AnimalsVeg };
            config.AddDropConfig(new DropConfig { Item = LegsPrefab, Chance = 100f, MinAmount = 1, MaxAmount = 1 });
            config.AddDropConfig(new DropConfig { Item = SkinPrefab, Chance = 50f, MinAmount = 1, MaxAmount = 1 });
            config.AddSpawnConfig(new SpawnConfig
            {
                Name = CreaturePrefab + "_Swamp",
                Biome = Heightmap.Biome.Swamp,
                SpawnChance = s_spawnChance.Value,
                SpawnInterval = 90f,
                SpawnDistance = 25f,
                MaxSpawned = s_maxSpawned.Value,
                MinGroupSize = 2,
                MaxGroupSize = 4,
                GroupRadius = 4f,
                MinLevel = 1,
                MaxLevel = 1,
                SpawnInForest = true,
                SpawnOutsideForest = true,
                SpawnAtDay = true,
                SpawnAtNight = true,
                MinAltitude = -1f,      // by the water's edge (altitude above sea level)
                MaxAltitude = 2.5f,
                MaxTilt = 25f,
                HuntPlayer = false,
            });

            var frog = new CustomCreature(CreaturePrefab, "Hare", config);
            var go = frog.Prefab;
            go.transform.localScale *= s_scale.Value;
            var model = ModelData.Load("frog");
            var rig = RigFile.Get("frog");
            if (model == null || rig == null || rig.Weights.Length != model.Pos.Length)
            {
                Plugin.Log.LogError("Frog: model or rig missing, no frogs");
                return;
            }
            // the hare stays underneath, invisible
            var hareRenderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var template = hareRenderers.Length > 0 ? hareRenderers[0].sharedMaterial : null;
            foreach (var r in hareRenderers) r.enabled = false;
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true)) lod.enabled = false;
            var bones = ProcRig.Build(go, model, rig.Bones, v => null, template, s_length.Value, "frog_rig", v => rig.Weights[v], glossiness: 0.45f);
            var anim = go.AddComponent<FrogAnim>();
            anim.Init(bones);
            go.AddComponent<FrogVoice>();
            Look.NaturalLevels(go);

            var character = go.GetComponent<Character>();
            character.m_name = config.Name;
            character.m_health = 3f;
            character.m_speed = 1.2f;
            character.m_runSpeed = 4.5f;
            character.m_canSwim = true;
            character.m_swimDepth = 0.05f;
            character.m_swimSpeed = 2.5f;
            FrogCorpse(character, model);

            var ai = go.GetComponent<AnimalAI>();
            if (ai != null)
            {
                ai.m_viewRange = 10f;
                ai.m_hearRange = 8f;
                ai.m_fleeRange = 10f;
                ai.m_fleeAngle = 60f;
                ai.m_fleeInterval = 0.8f;
                ai.m_randomMoveInterval = 6f;
                ai.m_randomMoveRange = 2.5f;
                ai.m_timeToSafe = 5f;
                ai.m_avoidWater = false;           // water is where a frog runs to
            }

            Croaks = Look.LoadClips("sfx_frog", "frog_croak");
            var tpl = Look.FindSfxTemplate(character.m_hitEffects, character.m_deathEffects, ai?.m_alertedEffects, ai?.m_idleSound);
            var alarm = Look.MakeSfx(CreaturePrefab + "_sfx_alarm", tpl, Look.LoadClips("sfx_frog", "frog_alarm"), 0.9f, 1.15f);
            character.m_hitEffects = Look.Voice(character.m_hitEffects, alarm);
            character.m_critHitEffects = Look.Voice(character.m_critHitEffects, alarm);
            character.m_deathEffects = Look.Voice(character.m_deathEffects, alarm);
            if (ai != null)
            {
                ai.m_alertedEffects = Look.Voice(ai.m_alertedEffects, null);   // a frog flees in silence
                ai.m_idleSound = Look.Voice(ai.m_idleSound, null);
            }

            Spawns.AddDespawn(go);
            CreatureManager.Instance.AddCreature(frog);
            Plugin.Log.LogInfo("Registered " + CreaturePrefab + " (Hare underneath, own rig " + rig.Bones.Count + " bones, " + rig.Clips.Count + " clips, " + Croaks.Length + " croaks)");
        }

        /// <summary>No hare ragdoll: our frog lying on its back for a while (the loot drops at once).</summary>
        private static void FrogCorpse(Character character, ModelData model)
        {
            var kept = new List<EffectList.EffectData>();
            foreach (var ed in character.m_deathEffects.m_effectPrefabs)
                if (ed?.m_prefab != null && ed.m_prefab.GetComponent<Ragdoll>() == null)
                    kept.Add(ed);
            var corpse = PrefabManager.Instance.CreateEmptyPrefab(CreaturePrefab + "_corpse", false);
            foreach (var c in corpse.GetComponents<Collider>()) Object.DestroyImmediate(c);
            foreach (var c in corpse.GetComponents<MeshRenderer>()) Object.DestroyImmediate(c);
            foreach (var c in corpse.GetComponents<MeshFilter>()) Object.DestroyImmediate(c);
            var body = new GameObject("body");
            body.transform.SetParent(corpse.transform, false);
            body.transform.localScale = Vector3.one * (s_length.Value / model.Bounds.size.z);
            body.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);          // belly up
            body.transform.localPosition = Vector3.up * s_length.Value * 0.35f;
            var mesh = new Mesh { name = "frog_corpse" };
            var verts = new Vector3[model.Pos.Length];
            for (int i = 0; i < verts.Length; i++) verts[i] = model.Pos[i] - model.Bounds.center;
            mesh.vertices = verts;
            mesh.normals = model.Nrm;
            mesh.uv = model.Uv;
            mesh.triangles = model.Idx;
            mesh.RecalculateBounds();
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = body.AddComponent<MeshRenderer>();
            var template = character.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterial;
            mr.sharedMaterial = template;
            Models.UseTexture(mr, model.Tex, "frog_corpse");
            corpse.AddComponent<SelfDestruct>().Seconds = 12f;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(corpse, false));
            kept.Add(new EffectList.EffectData { m_prefab = corpse, m_enabled = true, m_inheritParentScale = true });
            character.m_deathEffects = new EffectList { m_effectPrefabs = kept.ToArray() };
        }
    }

    /// <summary>
    /// Plays the frog's clips (models/frog.rig) on its bones: breathing always, crossfaded with the hop loop while it
    /// moves (hop length and height from the speed, ballistic flight), the swim loop in water, and a croak on demand (FrogVoice).
    /// Purely visual and local: the speed is measured from the actual movement, so it works on every client.
    /// </summary>
    public class FrogAnim : MonoBehaviour, IProcAnimated
    {
        private struct Rest { public Transform T; public Vector3 Pos; }

        private readonly Dictionary<string, Rest> _bones = new Dictionary<string, Rest>();
        private readonly Dictionary<string, Vector3> _rot = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Vector3> _off = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, float> _scale = new Dictionary<string, float>();
        private RigClip _breathe, _hop, _swim, _croak;
        private Character _character;
        private Vector3 _lastPos;
        private float _speed, _time, _hopW, _swimW, _croakTime = -1f;
        private static bool s_loggedHop;

        /// <summary>Living frogs, for Interactions (vanilla creatures leave them alone).</summary>
        internal static readonly HashSet<Character> All = new HashSet<Character>();

        internal void Init(Dictionary<string, Transform> bones)
        {
            _bones.Clear();
            foreach (var kv in bones) _bones[kv.Key] = new Rest { T = kv.Value, Pos = kv.Value.localPosition };
        }

        private void Awake()
        {
            if (_bones.Count == 0)
            {
                var b = ProcRig.Collect(transform);
                if (b != null) Init(b);
            }
            var rig = RigFile.Get("frog");
            if (rig == null || _bones.Count == 0) { enabled = false; return; }
            rig.Clips.TryGetValue("breathe", out _breathe);
            rig.Clips.TryGetValue("hop", out _hop);
            rig.Clips.TryGetValue("swim", out _swim);
            rig.Clips.TryGetValue("croak", out _croak);
            _character = GetComponent<Character>();
            if (_character != null) All.Add(_character);
            _lastPos = transform.position;
            _time = Random.Range(0f, 5f);
        }

        /// <summary>Starts the croak (the throat balloons) with the call FrogVoice plays.</summary>
        public void Croak() => _croakTime = 0f;

        private void OnDestroy()
        {
            if (_character != null) All.Remove(_character);
        }

        // ---- model lab: each clip rendered by the game itself (Lab.cs), to check the Unity side of the rig
        private RigClip _labClip;
        private float _labT;

        public float[] LabSpeeds => new float[0];      // the lab uses LabClips instead
        public void LabPose(float speed, float time) { }

        internal IEnumerable<KeyValuePair<string, RigClip>> LabClips()
        {
            var rig = RigFile.Get("frog");
            return rig != null ? rig.Clips : new Dictionary<string, RigClip>();
        }

        internal void LabClip(RigClip clip, float t)
        {
            _labClip = clip;
            _labT = t;
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (_labClip != null)
            {
                foreach (var name in _bones.Keys) { _rot[name] = Vector3.zero; _off[name] = Vector3.zero; _scale[name] = 1f; }
                Apply(_labClip, _labT, 1f);
                Write();
                return;
            }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var p = transform.position;
            float planar = new Vector2(p.x - _lastPos.x, p.z - _lastPos.z).magnitude / dt;
            _lastPos = p;
            if (planar > 20f) planar = 0f;                                   // spawn / teleport
            _speed = Mathf.Lerp(_speed, planar, 1f - Mathf.Exp(-dt * 6f));
            _time += dt;
            bool swimming = _character != null && _character.IsSwimming();
            bool moving = !swimming && _speed > 0.15f;
            _swimW = Mathf.MoveTowards(_swimW, swimming ? 1f : 0f, dt / 0.25f);
            if (_hop != null)
                StepHop(dt, moving);
            _hopW = Mathf.MoveTowards(_hopW, _hopping ? 1f : 0f, dt / 0.12f);

            foreach (var name in _bones.Keys) { _rot[name] = Vector3.zero; _off[name] = Vector3.zero; _scale[name] = 1f; }
            if (_breathe != null) Apply(_breathe, _time, 1f - Mathf.Max(_hopW, _swimW));
            if (_hop != null && _hopW > 0f)
            {
                Apply(_hop, _hopClipT, _hopW);
                HopOffset();
            }
            if (_swim != null && _swimW > 0f) Apply(_swim, _time, _swimW);
            if (_croak != null && _croakTime >= 0f)
            {
                _croakTime += dt;
                Apply(_croak, _croakTime, 1f);
                if (_croakTime >= _croak.Length) _croakTime = -1f;
            }
            Write();
        }

        private void Write()
        {
            foreach (var kv in _bones)
            {
                var r = _rot[kv.Key];
                kv.Value.T.localRotation = Quaternion.Euler(r.x, r.y, r.z);
                kv.Value.T.localPosition = kv.Value.Pos + _off[kv.Key];
                kv.Value.T.localScale = Vector3.one * _scale[kv.Key];
            }
        }

        // ------------------------------------------------------------------ the hop (same rules as tools/blender_frog.py)
        // From the speed v: hop length L and height h (h = 0.35 L, at most [Frog] HopHeight), a ballistic flight of
        // Tf = 2 sqrt(2h/g). Clip segments: crouch 0-0.09 (0.09 s, 0.04 s between chained hops), push 0.09-0.20
        // (0.11 s: the unfolding legs lift and push the body, clip channels Root lift/push, the soles stay planted),
        // flight 0.20-0.50 (launch and arrival 0.12 s each, the cruise in between stretched to Tf), landing 0.50-0.60
        // (0.26 s, 0.16 s when the next hop follows: the legs fold in 0.06 s, the rear drops after). In the air the body pitches along the flight path. The visual
        // stays on its takeoff spot, flies a parabola from the takeoff height and lands L further on; the collider
        // underneath moves on evenly.
        private const float HopTakeoff = 0.20f, HopLand = 0.50f, HopClip = 0.60f, TPush = 0.11f, HopK = 0.35f;
        private const float TLaunch = 0.12f, TArrive = 0.12f, TFold = 0.06f, PitchK = 0.3f, PitchMax = 12f;
        private bool _hopping, _chained;
        private float _ht, _hopClipT, _hopL, _hopH, _hopTf, _tCrouch, _tLand, _hopPitch;
        private Vector3 _hopStart, _hopFwd, _landSpot;
        private bool _hasLand;
        private float[] _lift, _push;

        private float Period => _tCrouch + TPush + _hopTf + _tLand;

        private void PlanHop(float v)
        {
            float L = v * HopClip;
            for (int i = 0; i < 30; i++)
            {
                _hopH = Mathf.Min(HopK * L, Frogs.HopHeight);
                _hopTf = 2f * Mathf.Sqrt(2f * _hopH / 9.81f);
                L = v * (0.20f + _hopTf + 0.26f);
            }
            _hopL = L;
        }

        private void StartHop(float carry, bool chained)
        {
            PlanHop(Mathf.Clamp(_speed, 0.5f, 8f));
            _chained = chained;
            _tCrouch = chained ? 0.04f : 0.09f;
            _tLand = 0.26f;
            // start from where the visual last landed (no jump), unless the collider has wandered off
            var p = transform.position;
            _hopStart = _hasLand && Vector3.Distance(Flat(_landSpot), Flat(p)) < 1.5f ? _landSpot : p;
            _hopFwd = Flat(transform.forward).normalized;
            _ht = carry;
            _hopping = true;
            if (!s_loggedHop)
            {
                s_loggedHop = true;
                Plugin.Log.LogInfo("Frog hopping: speed " + _speed.ToString("0.00") + " m/s, hop " + _hopL.ToString("0.00") + " m, height " +
                                   _hopH.ToString("0.00") + " m, flight " + _hopTf.ToString("0.00") + " s");
            }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        private void StepHop(float dt, bool moving)
        {
            if (_lift == null)
                foreach (var ch in _hop.Keys)
                    if (ch.Bone == "Root" && ch.Kind == "lift") _lift = ch.Values;
                    else if (ch.Bone == "Root" && ch.Kind == "push") _push = ch.Values;
            if (!_hopping)
            {
                if (moving) StartHop(0f, false);
                else return;
            }
            _ht += dt;
            // the next hop will follow: a shorter settle
            if (_ht > _tCrouch + TPush + _hopTf) _tLand = moving ? 0.16f : 0.26f;
            if (_ht >= Period)
            {
                _landSpot = _hopStart + _hopFwd * _hopL;
                _hasLand = true;
                if (moving) StartHop(_ht - Period, true);
                else { _hopping = false; return; }
            }
            _hopPitch = 0f;
            if (_ht < _tCrouch) _hopClipT = 0.09f * _ht / _tCrouch;
            else if (_ht < _tCrouch + TPush) _hopClipT = 0.09f + 0.11f * (_ht - _tCrouch) / TPush;
            else if (_ht < _tCrouch + TPush + _hopTf)
            {
                float sf = _ht - _tCrouch - TPush, tf = _hopTf;
                float ta = Mathf.Min(TLaunch, tf / 2.5f), tr = Mathf.Min(TArrive, tf / 2.5f);
                if (sf < ta) _hopClipT = HopTakeoff + 0.12f * sf / ta;
                else if (sf < tf - tr) _hopClipT = 0.32f + 0.06f * (sf - ta) / Mathf.Max(tf - ta - tr, 1e-4f);
                else _hopClipT = 0.38f + 0.12f * Mathf.Clamp01((sf - tf + tr) / tr);
                // pitch along the flight path (nose up negative), faded in on the launch and out on the arrival
                float u = sf / tf;
                float scale = RootScale();
                float pushEnd = Ch(_push, HopTakeoff) * scale, liftEnd = Ch(_lift, HopTakeoff) * scale;
                float ang = Mathf.Atan2(-liftEnd + 4f * _hopH * (1f - 2f * u), Mathf.Max(_hopL - pushEnd, 1e-3f)) * Mathf.Rad2Deg;
                float fade = Smooth(sf / ta) * (1f - Smooth((sf - tf + tr) / tr));
                _hopPitch = Mathf.Clamp(-PitchK * ang, -PitchMax, PitchMax) * fade;
            }
            else
            {
                float sl = _ht - _tCrouch - TPush - _hopTf;
                _hopClipT = sl < TFold ? HopLand + 0.05f * sl / TFold : 0.55f + 0.05f * Mathf.Clamp01((sl - TFold) / Mathf.Max(_tLand - TFold, 1e-4f));
            }
        }

        private float Ch(float[] c, float t) => c != null ? RigClip.Sample(c, t, _hop.Fps, false) : 0f;

        private float RootScale() =>
            _bones.TryGetValue("Root", out var r) && r.T.parent != null ? Mathf.Max(1e-4f, r.T.parent.lossyScale.y) : 1f;

        /// <summary>Puts the visual where the hop has it: takeoff spot, push, parabola, landing spot.</summary>
        private void HopOffset()
        {
            if (!_hopping || !_bones.TryGetValue("Root", out var root) || root.T.parent == null)
                return;
            float scale = RootScale();                       // metres per model unit
            float liftEnd = Ch(_lift, HopTakeoff) * scale, pushEnd = Ch(_push, HopTakeoff) * scale;
            float x, y;
            float flightStart = _tCrouch + TPush;
            if (_ht < flightStart)
            {
                x = Ch(_push, _hopClipT) * scale;
                y = 0f;
            }
            else if (_ht < flightStart + _hopTf)
            {
                float u = (_ht - flightStart) / _hopTf;
                x = pushEnd + (_hopL - pushEnd) * u;
                y = liftEnd * (1f - u) + 4f * _hopH * u * (1f - u);
            }
            else
            {
                x = _hopL;
                y = 0f;
            }
            if (_hopClipT <= HopTakeoff || _hopClipT >= 0.38f)
                y += Ch(_lift, _hopClipT) * scale;          // the push, and whatever hangs lowest on the arrival
            var p = transform.position;
            var want = _hopStart + _hopFwd * x;
            var world = new Vector3(want.x - p.x, y, want.z - p.z);
            _off["Root"] += root.T.parent.InverseTransformVector(world) * _hopW;
            _rot["Root"] += new Vector3(_hopPitch * _hopW, 0f, 0f);
        }

        /// <summary>Adds a clip's pose with a weight (rotations and offsets scaled, scales blended toward 1).</summary>
        private void Apply(RigClip clip, float t, float w)
        {
            if (w <= 0f) return;
            foreach (var ch in clip.Keys)
            {
                if (!_bones.ContainsKey(ch.Bone)) continue;
                float v = RigClip.Sample(ch.Values, t, clip.Fps, clip.Loop);
                switch (ch.Kind)
                {
                    case "s": _scale[ch.Bone] *= Mathf.Lerp(1f, v, w); break;
                    case "rx": _rot[ch.Bone] += new Vector3(v * w, 0f, 0f); break;
                    case "ry": _rot[ch.Bone] += new Vector3(0f, v * w, 0f); break;
                    case "rz": _rot[ch.Bone] += new Vector3(0f, 0f, v * w); break;
                    case "px": _off[ch.Bone] += new Vector3(v * w, 0f, 0f); break;
                    case "py": _off[ch.Bone] += new Vector3(0f, v * w, 0f); break;
                    case "pz": _off[ch.Bone] += new Vector3(0f, 0f, v * w); break;
                }
            }
        }
    }

    /// <summary>
    /// Croaks, local to each client: often at night (and in the rain), rarely by day, never with someone within
    /// QuietDistance (a frog goes silent when approached) or while it flees. Each call makes the throat balloon.
    /// Many frogs calling at their own random times make the chorus.
    /// </summary>
    public class FrogVoice : MonoBehaviour
    {
        private const float QuietDistance = 7f;
        private AudioSource _source;
        private FrogAnim _anim;
        private BaseAI _ai;
        private float _timer;

        private void Awake()
        {
            _anim = GetComponent<FrogAnim>();
            _ai = GetComponent<BaseAI>();
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 2f;
            _source.maxDistance = 45f;
            _source.dopplerLevel = 0f;
            _timer = Random.Range(1f, 8f);
        }

        private void Start()
        {
            if (AudioMan.instance != null)
                _source.outputAudioMixerGroup = AudioMan.instance.m_ambientMixer;
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;
            bool night = EnvMan.IsNight();
            bool wet = EnvMan.instance != null && EnvMan.IsWet();
            _timer = night || wet ? Random.Range(3f, 10f) : Random.Range(25f, 60f);
            if (Frogs.Croaks.Length == 0 || _source.isPlaying || (_ai != null && _ai.IsAlerted()))
                return;
            if (Ghost.PlayerInRange(transform.position, QuietDistance))
                return;
            _source.pitch = Random.Range(0.92f, 1.1f);
            _source.volume = Frogs.Volume;
            _source.PlayOneShot(Frogs.Croaks[Random.Range(0, Frogs.Croaks.Length)]);
            _anim?.Croak();
        }
    }
}
