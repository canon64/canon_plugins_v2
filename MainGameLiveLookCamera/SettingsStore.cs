using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace MainGameLiveLookCamera
{
    internal static class SettingsStore
    {
        public static PluginSettings LoadOrCreate(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    using (FileStream fs = File.OpenRead(path))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(PluginSettings));
                        PluginSettings loaded = serializer.ReadObject(fs) as PluginSettings;
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

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using (FileStream fs = File.Create(path))
            {
                var serializer = new DataContractJsonSerializer(typeof(PluginSettings));
                serializer.WriteObject(fs, settings);
            }
        }
    }
}
