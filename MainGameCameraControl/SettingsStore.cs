using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGameCameraControl
{
    internal static class SettingsStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static PluginSettings LoadOrCreate(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    using (var fs = File.OpenRead(path))
                    {
                        var ser = new DataContractJsonSerializer(typeof(PluginSettings));
                        var loaded = ser.ReadObject(fs) as PluginSettings;
                        if (loaded != null)
                            return loaded;
                    }
                }
            }
            catch
            {
            }

            return new PluginSettings();
        }

        public static void Save(string path, PluginSettings settings)
        {
            if (settings == null)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var fs = File.Create(path))
            {
                var ser = new DataContractJsonSerializer(typeof(PluginSettings));
                ser.WriteObject(fs, settings);
            }
        }
    }
}
