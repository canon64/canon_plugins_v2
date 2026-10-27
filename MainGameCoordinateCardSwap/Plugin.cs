using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameCoordinateCardSwap
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "canon.maingame.coordinatecardswap";
        public const string PluginName = "MainGameCoordinateCardSwap";
        public const string Version = "0.3.0";

        private const int SexMale = 0;
        private const int SexFemale = 1;

        private static readonly string[] ClothesKindNames =
        {
            "top", "bot", "bra", "shorts", "gloves", "panst", "socks", "shoes_inner", "shoes_outer"
        };

        private static readonly FieldInfo FiLstFemale = typeof(HSceneProc).GetField("lstFemale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo FiLstMale = typeof(HSceneProc).GetField("lstMale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object LogLock = new object();

        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<string> _cfgFemalePath;
        private ConfigEntry<bool> _cfgFemaleButton;
        private ConfigEntry<KeyboardShortcut> _cfgFemaleKey;
        private ConfigEntry<string> _cfgMalePath;
        private ConfigEntry<bool> _cfgMaleButton;
        private ConfigEntry<KeyboardShortcut> _cfgMaleKey;
        private ConfigEntry<bool> _cfgVerboseLog;

        private string _pluginDir;
        private string _logFilePath;

        private sealed class SideState
        {
            public byte[] BackupBytes;
            public Version BackupVersion;
            public int AppliedCharId;
            public ChaControl AppliedActor;
            public HSceneProc AppliedScene;
            public string AppliedPath = string.Empty;
            public string Status = string.Empty;
        }

        private readonly SideState _female = new SideState();
        private readonly SideState _male = new SideState();

        private void Awake()
        {
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            _logFilePath = Path.Combine(_pluginDir, PluginName + ".log");
            Directory.CreateDirectory(_pluginDir);
            File.WriteAllText(_logFilePath, $"[{DateTime.Now:HH:mm:ss}] === {PluginName} {Version} started ==={Environment.NewLine}", Utf8NoBom);

            _cfgEnabled = Config.Bind(
                "00.Main", "Enabled", true,
                "プラグイン全体の有効/無効");

            _cfgFemalePath = Config.Bind(
                "01.Female", "FemaleCoordinatePath", string.Empty,
                "女性に適用するコーディネートカード(.png)のフルパス");

            _cfgFemaleButton = Config.Bind(
                "01.Female", "ApplyFemale", false,
                new ConfigDescription(
                    "押下で女性に適用、もう一度押すと適用前のコーディネートに戻す",
                    null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 90,
                        HideDefaultButton = true,
                        CustomDrawer = DrawFemaleButton
                    }));
            _cfgFemaleButton.SettingChanged += (_, __) =>
            {
                if (!_cfgFemaleButton.Value) return;
                _cfgFemaleButton.Value = false;
                Toggle(SexFemale, "cfg-trigger");
            };

            _cfgFemaleKey = Config.Bind(
                "01.Female", "ApplyFemaleKey", new KeyboardShortcut(KeyCode.None),
                "女性側ボタンと同等動作のショートカット");

            _cfgMalePath = Config.Bind(
                "02.Male", "MaleCoordinatePath", string.Empty,
                "男性に適用するコーディネートカード(.png)のフルパス");

            _cfgMaleButton = Config.Bind(
                "02.Male", "ApplyMale", false,
                new ConfigDescription(
                    "押下で男性に適用、もう一度押すと適用前のコーディネートに戻す",
                    null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 80,
                        HideDefaultButton = true,
                        CustomDrawer = DrawMaleButton
                    }));
            _cfgMaleButton.SettingChanged += (_, __) =>
            {
                if (!_cfgMaleButton.Value) return;
                _cfgMaleButton.Value = false;
                Toggle(SexMale, "cfg-trigger");
            };

            _cfgMaleKey = Config.Bind(
                "02.Male", "ApplyMaleKey", new KeyboardShortcut(KeyCode.None),
                "男性側ボタンと同等動作のショートカット");

            _cfgVerboseLog = Config.Bind(
                "99.Debug", "VerboseLog", false,
                "詳細ログ（適用前後の全部位ID/色をログに出す）");

            Log("awake complete version=" + Version);
        }

        private void Update()
        {
            if (!_cfgEnabled.Value)
                return;
            if (Bindings.Down("MainGameCoordinateCardSwap.female", _cfgFemaleKey.Value.ToString()))
                Toggle(SexFemale, "hotkey");
            if (Bindings.Down("MainGameCoordinateCardSwap.male", _cfgMaleKey.Value.ToString()))
                Toggle(SexMale, "hotkey");
        }

        private void DrawFemaleButton(ConfigEntryBase _) => DrawSideButton(SexFemale, _female);
        private void DrawMaleButton(ConfigEntryBase _) => DrawSideButton(SexMale, _male);

        private void DrawSideButton(int sex, SideState side)
        {
            GUILayout.BeginVertical();
            string label = IsAppliedNow(sex, side) ? "元に戻す" : "適用";
            if (GUILayout.Button(label, GUILayout.MinHeight(28f)))
                Toggle(sex, "ui-button");
            if (!string.IsNullOrEmpty(side.Status))
                GUILayout.Label(side.Status);
            GUILayout.EndVertical();
        }

        private bool IsAppliedNow(int sex, SideState side)
        {
            string currentPath = GetPathFor(sex);
            return side.AppliedCharId != 0
                && side.BackupBytes != null
                && !string.IsNullOrEmpty(side.AppliedPath)
                && string.Equals(side.AppliedPath, currentPath, StringComparison.OrdinalIgnoreCase);
        }

        private string GetPathFor(int sex)
        {
            string raw = (sex == SexFemale ? _cfgFemalePath.Value : _cfgMalePath.Value) ?? string.Empty;
            return raw.Trim('"', ' ');
        }

        private void Toggle(int sex, string source)
        {
            if (!_cfgEnabled.Value)
            {
                SetStatus(sex, "プラグイン無効");
                return;
            }

            ChaControl target = ResolveTarget(sex);
            if (target == null)
            {
                SetStatus(sex, (sex == SexFemale ? "女性" : "男性") + "キャラが見つかりません");
                return;
            }

            SideState side = sex == SexFemale ? _female : _male;
            int chaId = target.GetInstanceID();
            string path = GetPathFor(sex);

            Log($"[toggle] sex={SexName(sex)} src={source} chaId={chaId} chaName={SafeName(target)} path={path}");

            bool restorePhase = side.AppliedCharId == chaId
                && side.BackupBytes != null
                && string.Equals(side.AppliedPath, path, StringComparison.OrdinalIgnoreCase);

            if (restorePhase)
                Restore(target, sex, side, source);
            else
                Apply(target, sex, side, path, source);
        }

        private void Apply(ChaControl cha, int sex, SideState side, string path, string source)
        {
            if (string.IsNullOrEmpty(path))
            {
                SetStatus(sex, "カードパスが未設定");
                return;
            }
            if (!File.Exists(path))
            {
                SetStatus(sex, "カードが存在しない: " + path);
                return;
            }

            // ---- 1) バックアップ ----
            byte[] backup;
            Version backupVer;
            try
            {
                backup = cha.nowCoordinate?.SaveBytes();
                backupVer = cha.nowCoordinate?.loadVersion ?? ChaFileDefine.ChaFileCoordinateVersion;
            }
            catch (Exception ex)
            {
                SetStatus(sex, "バックアップ失敗: " + ex.Message);
                return;
            }
            if (backup == null || backup.Length == 0)
            {
                SetStatus(sex, "バックアップ失敗（空データ）");
                return;
            }

            DumpCoordinate("before-apply", sex, cha);

            // ---- 2) カード読み込み（事前にデータ確認用） ----
            ChaFileCoordinate srcCoord = new ChaFileCoordinate();
            srcCoord.MemberInit();
            if (!srcCoord.LoadFile(path))
            {
                SetStatus(sex, "カードのデコード失敗 errCode=" + srcCoord.GetLastErrorCode() + " path=" + path);
                return;
            }
            DumpCoordinateRaw("card-content", sex, srcCoord);

            // ---- 3) 適用 ----
            bool ok;
            try
            {
                ok = cha.SetNowCoordinate(srcCoord);
            }
            catch (Exception ex)
            {
                SetStatus(sex, "適用例外: " + ex.Message);
                Log("[apply] exception sex=" + SexName(sex) + " path=" + path + " ex=" + ex);
                return;
            }
            if (!ok)
            {
                SetStatus(sex, "SetNowCoordinate失敗: " + Path.GetFileName(path));
                return;
            }

            // ---- 4) Reload（致命的に重要）----
            // SetNowCoordinate は nowCoordinate.LoadBytes するだけで Reload を呼ばない。
            // 描画反映には Reload(noChangeClothes:false, noChangeHead:true, noChangeHair:true, noChangeBody:true) が必須。
            try
            {
                cha.Reload(noChangeClothes: false, noChangeHead: true, noChangeHair: true, noChangeBody: true);
            }
            catch (Exception ex)
            {
                SetStatus(sex, "Reload例外: " + ex.Message);
                Log("[apply] reload exception sex=" + SexName(sex) + " ex=" + ex);
                return;
            }

            DumpCoordinate("after-apply", sex, cha);

            side.BackupBytes = backup;
            side.BackupVersion = backupVer;
            side.AppliedCharId = cha.GetInstanceID();
            side.AppliedActor = cha; side.AppliedScene = FindObjectOfType<HSceneProc>();
            side.AppliedPath = path;
            SetStatus(sex, "適用: " + Path.GetFileName(path) + " (src=" + source + ")");
        }

        private void Restore(ChaControl cha, int sex, SideState side, string source)
        {
            if (!ReferenceEquals(side.AppliedActor, cha) || side.AppliedScene != FindObjectOfType<HSceneProc>() || CanonPlugins.Shared.IndependentActorFilter.IsIndependent(cha)) {
                side.BackupBytes = null; side.BackupVersion = null; side.AppliedCharId = 0; side.AppliedActor = null; side.AppliedScene = null;
                SetStatus(sex, "対象が変わったため古い復元状態を破棄しました"); return;
            }
            if (side.BackupBytes == null || side.BackupBytes.Length == 0)
            {
                SetStatus(sex, "バックアップなし");
                return;
            }

            DumpCoordinate("before-restore", sex, cha);

            ChaFileCoordinate coord = new ChaFileCoordinate();
            try
            {
                coord.MemberInit();
                Version ver = side.BackupVersion ?? ChaFileDefine.ChaFileCoordinateVersion;
                if (!coord.LoadBytes(side.BackupBytes, ver))
                {
                    SetStatus(sex, "バックアップのデコード失敗");
                    return;
                }
                coord.loadVersion = ver;
            }
            catch (Exception ex)
            {
                SetStatus(sex, "デコード例外: " + ex.Message);
                Log("[restore] decode ex sex=" + SexName(sex) + " ex=" + ex);
                return;
            }

            bool ok;
            try
            {
                ok = cha.SetNowCoordinate(coord);
            }
            catch (Exception ex)
            {
                SetStatus(sex, "復元例外: " + ex.Message);
                Log("[restore] apply ex sex=" + SexName(sex) + " ex=" + ex);
                return;
            }
            if (!ok)
            {
                SetStatus(sex, "復元失敗（SetNowCoordinate）");
                return;
            }

            // Reload も必須
            try
            {
                cha.Reload(noChangeClothes: false, noChangeHead: true, noChangeHair: true, noChangeBody: true);
            }
            catch (Exception ex)
            {
                SetStatus(sex, "復元Reload例外: " + ex.Message);
                Log("[restore] reload ex sex=" + SexName(sex) + " ex=" + ex);
                return;
            }

            DumpCoordinate("after-restore", sex, cha);

            side.BackupBytes = null;
            side.BackupVersion = null;
            side.AppliedCharId = 0;
            side.AppliedPath = string.Empty;
            SetStatus(sex, "復元 (src=" + source + ")");
        }

        private ChaControl ResolveTarget(int sex)
        {
            HSceneProc proc = FindObjectOfType<HSceneProc>();
            if (proc != null)
            {
                FieldInfo field = sex == SexFemale ? FiLstFemale : FiLstMale;
                ChaControl picked = PickFirstAlive(GetCharaList(proc, field, sex));
                if (picked != null)
                    return picked;
            }

            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindHActors();
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl c = all[i];
                if (c == null) continue;
                if (c.sex != sex) continue;
                if (!c.visibleAll) continue;
                if (!c.GetActiveTop()) continue;
                return c;
            }
            return null;
        }

        private static List<ChaControl> GetCharaList(HSceneProc proc, FieldInfo field, int sex)
        {
            var result = new List<ChaControl>();
            if (proc == null || field == null)
                return result;
            IList list = field.GetValue(proc) as IList;
            if (list == null)
                return result;
            for (int i = 0; i < list.Count; i++)
            {
                ChaControl cha = list[i] as ChaControl;
                if (cha == null || cha.sex != sex)
                    continue;
                result.Add(cha);
            }
            return result;
        }

        private static ChaControl PickFirstAlive(List<ChaControl> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                ChaControl c = list[i];
                if (c != null && c.visibleAll)
                    return c;
            }
            return list.Count > 0 ? list[0] : null;
        }

        // ---- ログ系 ----

        private void DumpCoordinate(string label, int sex, ChaControl cha)
        {
            if (cha == null) { Log("[" + label + "] sex=" + SexName(sex) + " cha=null"); return; }
            DumpCoordinateRaw(label, sex, cha.nowCoordinate);
        }

        private void DumpCoordinateRaw(string label, int sex, ChaFileCoordinate coord)
        {
            if (coord == null)
            {
                Log("[" + label + "] sex=" + SexName(sex) + " coord=null");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("[").Append(label).Append("] sex=").Append(SexName(sex))
              .Append(" name='").Append(coord.coordinateName ?? "")
              .Append("' ver=").Append(coord.loadVersion);

            // Clothes parts
            if (coord.clothes?.parts != null)
            {
                sb.Append(" clothes={");
                for (int i = 0; i < coord.clothes.parts.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    string kind = i < ClothesKindNames.Length ? ClothesKindNames[i] : ("k" + i);
                    var p = coord.clothes.parts[i];
                    if (p == null) { sb.Append(kind).Append("=null"); continue; }
                    sb.Append(kind).Append("=").Append(p.id);
                    if (_cfgVerboseLog.Value)
                    {
                        sb.Append("[c=");
                        if (p.colorInfo != null && p.colorInfo.Length > 0 && p.colorInfo[0] != null)
                        {
                            var c0 = p.colorInfo[0].baseColor;
                            sb.Append(FormatColor(c0));
                        }
                        else sb.Append("?");
                        sb.Append(",emb=").Append(p.emblemeId);
                        sb.Append("/").Append(p.emblemeId2);
                        sb.Append(",slv=").Append(p.sleevesType);
                        sb.Append("]");
                    }
                }
                sb.Append("}");

                if (coord.clothes.subPartsId != null)
                {
                    sb.Append(" sub=[");
                    for (int i = 0; i < coord.clothes.subPartsId.Length; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append(coord.clothes.subPartsId[i]);
                    }
                    sb.Append("]");
                }

                if (_cfgVerboseLog.Value)
                {
                    sb.Append(" hideBra=").Append(BoolArr(coord.clothes.hideBraOpt));
                    sb.Append(" hideShorts=").Append(BoolArr(coord.clothes.hideShortsOpt));
                }
            }
            else
            {
                sb.Append(" clothes=null");
            }

            // Accessory parts
            if (coord.accessory?.parts != null)
            {
                int activeCount = 0;
                StringBuilder ab = new StringBuilder();
                for (int i = 0; i < coord.accessory.parts.Length; i++)
                {
                    var ap = coord.accessory.parts[i];
                    if (ap == null) continue;
                    if (ap.type == 120) continue; // empty
                    activeCount++;
                    if (ab.Length > 0) ab.Append(",");
                    ab.Append(i).Append(":").Append(ap.type).Append("/").Append(ap.id);
                    if (_cfgVerboseLog.Value)
                    {
                        ab.Append("(parent=").Append(ap.parentKey ?? "");
                        if (ap.color != null && ap.color.Length > 0) ab.Append(",c=").Append(FormatColor(ap.color[0]));
                        ab.Append(",hideCat=").Append(ap.hideCategory);
                        ab.Append(")");
                    }
                }
                sb.Append(" acc=").Append(activeCount).Append("/20{").Append(ab).Append("}");
            }
            else
            {
                sb.Append(" acc=null");
            }

            // Makeup
            sb.Append(" makeup=").Append(coord.enableMakeup ? "ON" : "off");

            Log(sb.ToString());
        }

        private static string FormatColor(Color c)
        {
            return $"({c.r:F2},{c.g:F2},{c.b:F2},{c.a:F2})";
        }

        private static string BoolArr(bool[] arr)
        {
            if (arr == null) return "null";
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(arr[i] ? "1" : "0");
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string SexName(int sex) => sex == SexFemale ? "female" : "male";

        private static string SafeName(ChaControl cha)
        {
            try
            {
                return cha?.chaFile?.parameter?.fullname ?? "?";
            }
            catch { return "?"; }
        }

        private void SetStatus(int sex, string text)
        {
            SideState side = sex == SexFemale ? _female : _male;
            side.Status = text;
            Log("[" + SexName(sex) + "] " + text);
        }

        private void Log(string message)
        {
            if (Logger != null)
                Logger.LogInfo(message);
            WriteLogLine(message);
        }

        private void WriteLogLine(string message)
        {
            if (string.IsNullOrEmpty(_logFilePath))
                return;
            try
            {
                lock (LogLock)
                {
                    File.AppendAllText(_logFilePath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}", Utf8NoBom);
                }
            }
            catch
            {
            }
        }
    }
}
