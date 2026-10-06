using System.IO;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Caca
{
    /// <summary>
    /// No bars: the urges show as status effects, like the cold. From the threshold (50 % by default) an icon appears
    /// with the key to press in its name; near the end ([General] UrgentAt) it is renamed and flashes. No penalty.
    /// </summary>
    internal static class Urges
    {
        public static StatusEffect CacaSE, PipiSE;

        public static void Create()
        {
            CacaSE = Make("SE_CacaUrge", "$se_caca", Icons.Load("se_caca.png") ?? PoopArt.Icon());
            PipiSE = Make("SE_PipiUrge", "$se_pipi", Icons.Load("se_pipi.png") ?? PoopArt.Icon());
        }

        private static StatusEffect Make(string id, string name, Sprite icon)
        {
            var se = ScriptableObject.CreateInstance<StatusEffect>();
            se.name = id;
            se.m_name = name;
            se.m_icon = icon;
            se.m_ttl = 0f;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, false));
            return se;
        }

        public static void Tick(Player p)
        {
            if (CacaSE == null) return;
            float urgent = Plugin.UrgentAt.Value;
            Sync(p, CacaSE, Plugin.EnablePoop.Value && !Need.Busy && Need.Value >= Plugin.MinNeed.Value, Need.Value >= urgent,
                "$se_caca", "$se_caca_urgent", "$se_caca_tip", Plugin.Key.Value);
            Sync(p, PipiSE, Plugin.EnablePee.Value && !Pee.Active && Pee.Value >= Plugin.PeeMinNeed.Value, Pee.Value >= urgent,
                "$se_pipi", "$se_pipi_urgent", "$se_pipi_tip", Plugin.PeeKey.Value);
        }

        private static void Sync(Player p, StatusEffect se, bool on, bool urgent, string name, string urgentName,
            string tip, KeyCode key)
        {
            var man = p.GetSEMan();
            if (man == null) return;
            var have = man.GetStatusEffect(se.NameHash());
            if (on && have == null) have = man.AddStatusEffect(se);
            else if (!on && have != null)
            {
                man.RemoveStatusEffect(have, true);
                return;
            }
            if (have == null) return;
            have.m_name = (urgent ? urgentName : name) + " [" + key + "]";
            have.m_flashIcon = urgent;
            have.m_tooltip = tip + " [<color=yellow>" + key + "</color>]";
        }
    }

    /// <summary>PNG icons shipped next to the DLL (icons/), made offline by tools/make_icons.py.</summary>
    internal static class Icons
    {
        public static Sprite Load(string file)
        {
            var path = Path.Combine(Wav.PluginDir, "icons", file);
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("Missing icon " + path);
                return null;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileNameWithoutExtension(file) };
            if (!tex.LoadImage(File.ReadAllBytes(path)))
            {
                Plugin.Log.LogWarning("Unreadable icon " + path);
                return null;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
