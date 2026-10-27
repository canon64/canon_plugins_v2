using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;

namespace MainGameBeatSyncSpeed
{
    [DataContract]
    internal sealed class SongBpmMapFile
    {
        [DataMember(Order = 0)]
        public List<SongBpmMapEntry> Items = new List<SongBpmMapEntry>();
    }

    [DataContract]
    internal sealed class SongBpmMapEntry
    {
        [DataMember(Order = 0)]
        public string VideoPath = "";

        [DataMember(Order = 1)]
        public int Bpm = 0;

        [DataMember(Order = 2)]
        public string UpdatedAtUtc = "";
    }

    public partial class Plugin
    {
        private const string SongBpmMapFileName = "SongBpmMap.json";
        private string SongBpmMapPath => Path.Combine(_pluginDir, SongBpmMapFileName);

        private void OnBpmSettingChanged(object sender, EventArgs e)
        {
            InvalidateAnalysis();

            // BeatSync側のBPM変更を常にLimitBreakへも同期する
            TryApplyToSpeedLimitBreak(_cfgBpm.Value, "bpm-setting-changed");
            if (_suppressSongBpmPersist)
                return;

            PersistCurrentSongBpm(_cfgBpm.Value, "bpm-changed");
        }

        private void RefreshSongBpmAutoLoad()
        {
            if (Time.unscaledTime < _nextSongPathPollTime)
                return;

            _nextSongPathPollTime = Time.unscaledTime + 0.5f;

            if (!TryGetVideoFilePath(out string videoPath))
                return;

            if (string.IsNullOrWhiteSpace(videoPath))
                return;

            string key = NormalizeSongPathKey(videoPath);
            if (string.IsNullOrEmpty(key))
                return;

            if (string.Equals(_currentSongPathKey, key, StringComparison.OrdinalIgnoreCase))
                return;

            _currentSongPathKey = key;

            if (_songBpmByPath.TryGetValue(key, out int savedBpm))
            {
                savedBpm = Mathf.Clamp(savedBpm, 1, 999);
                if (_cfgBpm.Value != savedBpm)
                {
                    _suppressSongBpmPersist = true;
                    _cfgBpm.Value = savedBpm;
                    _suppressSongBpmPersist = false;
                    InvalidateAnalysis();
                    TryApplyToSpeedLimitBreak(savedBpm, "song-auto-load");
                    LogInfo($"[song-bpm] auto load bpm={savedBpm} song={Path.GetFileName(key)}");
                }
                else
                {
                    LogInfo($"[song-bpm] detected song with same bpm={savedBpm} song={Path.GetFileName(key)}");
                }
            }
            else
            {
                LogInfo($"[song-bpm] no saved bpm for song={Path.GetFileName(key)}");
            }
        }

        private void PersistCurrentSongBpm(int bpm, string reason)
        {
            int safeBpm = Mathf.Clamp(bpm, 1, 999);
            string key = _currentSongPathKey;

            if (string.IsNullOrWhiteSpace(key) && TryGetVideoFilePath(out string currentPath))
            {
                key = NormalizeSongPathKey(currentPath);
                _currentSongPathKey = key;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                LogInfo($"[song-bpm] skip save (song unresolved) reason={reason} bpm={safeBpm}");
                return;
            }

            bool had = _songBpmByPath.TryGetValue(key, out int before);
            _songBpmByPath[key] = safeBpm;
            SaveSongBpmMap(reason);

            if (!had)
                LogInfo($"[song-bpm] saved new song bpm={safeBpm} song={Path.GetFileName(key)} reason={reason}");
            else if (before != safeBpm)
                LogInfo($"[song-bpm] updated song bpm={before}->{safeBpm} song={Path.GetFileName(key)} reason={reason}");
            else
                LogInfo($"[song-bpm] saved song bpm unchanged={safeBpm} song={Path.GetFileName(key)} reason={reason}");
        }

        private void LoadSongBpmMap()
        {
            try
            {
                _songBpmByPath.Clear();

                if (!File.Exists(SongBpmMapPath))
                {
                    SaveSongBpmMap("init");
                    LogInfo($"[song-bpm] map created: {SongBpmMapPath}");
                    return;
                }

                string json = File.ReadAllText(SongBpmMapPath, Encoding.UTF8);
                var file = DeserializeSongBpmMap(json);
                if (file?.Items == null)
                {
                    LogWarn("[song-bpm] map parse failed; using empty map");
                    return;
                }

                for (int i = 0; i < file.Items.Count; i++)
                {
                    var item = file.Items[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.VideoPath))
                        continue;

                    string key = NormalizeSongPathKey(item.VideoPath);
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    int bpm = Mathf.Clamp(item.Bpm, 1, 999);
                    _songBpmByPath[key] = bpm;
                }

                LogInfo($"[song-bpm] map loaded entries={_songBpmByPath.Count}");
            }
            catch (Exception ex)
            {
                LogError("[song-bpm] map load failed: " + ex.Message);
            }
        }

        private void SaveSongBpmMap(string reason)
        {
            try
            {
                var file = new SongBpmMapFile();
                foreach (var kv in _songBpmByPath)
                {
                    file.Items.Add(new SongBpmMapEntry
                    {
                        VideoPath = kv.Key,
                        Bpm = kv.Value,
                        UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                    });
                }

                string json = SerializeSongBpmMap(file);
                File.WriteAllText(SongBpmMapPath, json, new UTF8Encoding(false));
                LogInfo($"[song-bpm] map saved entries={file.Items.Count} reason={reason}");
            }
            catch (Exception ex)
            {
                LogError("[song-bpm] map save failed: " + ex.Message);
            }
        }

        private static string NormalizeSongPathKey(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                string full = Path.GetFullPath(path.Trim());
                return full.Replace('/', '\\');
            }
            catch
            {
                return null;
            }
        }

        private static SongBpmMapFile DeserializeSongBpmMap(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var serializer = new DataContractJsonSerializer(typeof(SongBpmMapFile));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                return serializer.ReadObject(ms) as SongBpmMapFile;
            }
        }

        private static string SerializeSongBpmMap(SongBpmMapFile file)
        {
            var serializer = new DataContractJsonSerializer(typeof(SongBpmMapFile));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, file ?? new SongBpmMapFile());
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }
}
