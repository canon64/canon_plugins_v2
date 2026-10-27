using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;

namespace MainGameVMDPlay
{
    internal static class SettingsStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static DataContractJsonSerializer CreateSerializer()
            => new DataContractJsonSerializer(typeof(VMDPlaySettings));

        internal static VMDPlaySettings Load(string path)
        {
            if (!File.Exists(path))
                return new VMDPlaySettings();

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var loaded = CreateSerializer().ReadObject(fs) as VMDPlaySettings;
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MainGameVMDPlay] 設定読み込み失敗 path={path} ex={ex.GetType().Name}:{ex.Message}");
            }

            return new VMDPlaySettings();
        }

        internal static bool Save(string path, VMDPlaySettings settings)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    CreateSerializer().WriteObject(fs, settings ?? new VMDPlaySettings());
                    fs.Flush(true);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MainGameVMDPlay] 設定保存失敗 path={path} ex={ex.Message}");
                return false;
            }
        }
    }
}
