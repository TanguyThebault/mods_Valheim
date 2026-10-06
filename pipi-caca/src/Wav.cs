using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Caca
{
    internal static class Wav
    {
        public static string PluginDir => Path.GetDirectoryName(typeof(Plugin).Assembly.Location);

        /// <summary>Every &lt;prefix&gt;*.wav of the plugin's sfx folder, sorted.</summary>
        public static AudioClip[] Load(string prefix)
        {
            string dir = Path.Combine(PluginDir, "sfx");
            if (!Directory.Exists(dir))
            {
                Plugin.Log.LogWarning("No sound folder " + dir);
                return new AudioClip[0];
            }
            return Directory.GetFiles(dir, prefix + "*.wav").OrderBy(p => p).Select(Read).Where(c => c != null).ToArray();
        }

        /// <summary>Minimal RIFF reader: 16-bit PCM, mono or stereo (stereo is downmixed).</summary>
        private static AudioClip Read(string path)
        {
            try
            {
                var b = File.ReadAllBytes(path);
                int channels = 0, rate = 0, bits = 0, pos = 12;
                while (pos + 8 <= b.Length)
                {
                    string id = Encoding.ASCII.GetString(b, pos, 4);
                    int size = System.BitConverter.ToInt32(b, pos + 4);
                    int body = pos + 8;
                    if (id == "fmt ")
                    {
                        channels = System.BitConverter.ToInt16(b, body + 2);
                        rate = System.BitConverter.ToInt32(b, body + 4);
                        bits = System.BitConverter.ToInt16(b, body + 14);
                    }
                    else if (id == "data" && bits == 16 && channels > 0)
                    {
                        int frames = size / (2 * channels);
                        var data = new float[frames];
                        for (int f = 0; f < frames; f++)
                        {
                            float sum = 0f;
                            for (int c = 0; c < channels; c++)
                                sum += System.BitConverter.ToInt16(b, body + (f * channels + c) * 2) / 32768f;
                            data[f] = sum / channels;
                        }
                        var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), frames, 1, rate, false);
                        clip.SetData(data, 0);
                        return clip;
                    }
                    pos = body + size + (size & 1);
                }
                Plugin.Log.LogWarning("Unsupported WAV (need 16-bit PCM): " + path);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Failed to read " + path + ": " + e.Message);
            }
            return null;
        }
    }
}
