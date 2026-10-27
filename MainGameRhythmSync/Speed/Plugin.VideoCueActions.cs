using HarmonyLib;
using Mono.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        private sealed class CueFaceDbRow
        {
            public int FileId;
            public string HFaceName;
            public string Chara;
            public int VoiceKind;
            public int Action;
            public int FaceId;
        }

        private static readonly FieldInfo LstUseAnimInfoField = AccessTools.Field(typeof(HSceneProc), "lstUseAnimInfo");
        private const string DefaultFaceDbRelativePath = @"..\..\..\work\FaceExpression\hface_extract\hface_dict.db";

        private bool ExecuteVideoCueActions(VideoTimeSpeedCue cue, double videoTimeSec)
        {
            if (cue == null)
            {
                return false;
            }

            bool appliedAny = false;

            if (cue.FaceDbEnabled)
            {
                if (TryApplyCueFace(cue, out string faceResult))
                {
                    appliedAny = true;
                    LogInfo($"[video-cue] face applied time={videoTimeSec:0.###} {faceResult}");
                }
                else
                {
                    LogWarn($"[video-cue] face skipped time={videoTimeSec:0.###} reason={faceResult}");
                }
            }

            if (cue.TaiiId.HasValue || !string.IsNullOrWhiteSpace(cue.TaiiName) || cue.TaiiMode.HasValue)
            {
                if (TryApplyCueTaii(cue, out string taiiResult))
                {
                    appliedAny = true;
                    LogInfo($"[video-cue] taii applied time={videoTimeSec:0.###} {taiiResult}");
                }
                else
                {
                    LogWarn($"[video-cue] taii skipped time={videoTimeSec:0.###} reason={taiiResult}");
                }
            }

            if (cue.CoordinateType.HasValue)
            {
                if (TryApplyCueCoordinate(cue, out string coordinateResult))
                {
                    appliedAny = true;
                    LogInfo($"[video-cue] coordinate applied time={videoTimeSec:0.###} {coordinateResult}");
                }
                else
                {
                    LogWarn($"[video-cue] coordinate skipped time={videoTimeSec:0.###} reason={coordinateResult}");
                }
            }

            if (cue.ClothesStates != null && cue.ClothesStates.Count > 0)
            {
                if (TryApplyCueClothes(cue, out string clothesResult))
                {
                    appliedAny = true;
                    LogInfo($"[video-cue] clothes applied time={videoTimeSec:0.###} {clothesResult}");
                }
                else
                {
                    LogWarn($"[video-cue] clothes skipped time={videoTimeSec:0.###} reason={clothesResult}");
                }
            }

            if (!string.IsNullOrWhiteSpace(cue.ClickKind))
            {
                if (TryApplyCueClick(cue, out string clickResult))
                {
                    appliedAny = true;
                    LogInfo($"[video-cue] click applied time={videoTimeSec:0.###} {clickResult}");
                }
                else
                {
                    LogWarn($"[video-cue] click skipped time={videoTimeSec:0.###} reason={clickResult}");
                }
            }

            return appliedAny;
        }

        private bool TryApplyCueFace(VideoTimeSpeedCue cue, out string result)
        {
            result = "unknown";
            if (!TryGetFemaleByIndex(cue.TargetFemaleIndex, out var female, out int resolvedIndex))
            {
                result = "female-not-found";
                return false;
            }

            FaceListCtrl faceCtrl = GetFaceCtrlByFemaleIndex(resolvedIndex);
            if (faceCtrl == null)
            {
                result = $"facectrl-null femaleIndex={resolvedIndex}";
                return false;
            }

            if (!TrySelectFaceFromDb(cue, out var row, out string dbReason))
            {
                result = dbReason;
                return false;
            }

            bool ok = faceCtrl.SetFace(row.FaceId, female, row.VoiceKind, row.Action);
            if (!ok)
            {
                result = $"SetFace-failed chara={row.Chara} voiceKind={row.VoiceKind} action={row.Action} faceId={row.FaceId}";
                return false;
            }

            result =
                $"femaleIndex={resolvedIndex} chara={row.Chara} voiceKind={row.VoiceKind} " +
                $"action={row.Action} faceId={row.FaceId} file={row.HFaceName}";
            return true;
        }

        private bool TryApplyCueTaii(VideoTimeSpeedCue cue, out string result)
        {
            result = "unknown";
            if (_hSceneProc == null || _hSceneProc.flags == null)
            {
                result = "hscene-or-flags-null";
                return false;
            }

            if (!TryResolveCueTaii(cue, out var info, out string resolveReason))
            {
                result = resolveReason;
                return false;
            }

            _hSceneProc.flags.selectAnimationListInfo = info;
            if (string.IsNullOrWhiteSpace(cue.ClickKind))
            {
                _hSceneProc.flags.click = HFlag.ClickKind.actionChange;
            }

            result = $"id={info.id} mode={info.mode} name={info.nameAnimation}";
            return true;
        }

        private bool TryApplyCueCoordinate(VideoTimeSpeedCue cue, out string result)
        {
            result = "unknown";
            if (!TryGetFemaleByIndex(cue.TargetFemaleIndex, out var female, out int resolvedIndex))
            {
                result = "female-not-found";
                return false;
            }

            if (!cue.CoordinateType.HasValue)
            {
                result = "coordinate-not-set";
                return false;
            }

            int coordinateType = cue.CoordinateType.Value;
            if (!Enum.IsDefined(typeof(ChaFileDefine.CoordinateType), coordinateType))
            {
                result = $"invalid-coordinate-type={coordinateType}";
                return false;
            }

            var type = (ChaFileDefine.CoordinateType)coordinateType;
            bool ok = female.ChangeCoordinateTypeAndReload(type);
            if (!ok)
            {
                result = $"ChangeCoordinateTypeAndReload-failed type={type}";
                return false;
            }

            result = $"femaleIndex={resolvedIndex} type={type}({coordinateType})";
            return true;
        }

        private bool TryApplyCueClothes(VideoTimeSpeedCue cue, out string result)
        {
            result = "unknown";
            if (!TryGetFemaleByIndex(cue.TargetFemaleIndex, out var female, out int resolvedIndex))
            {
                result = "female-not-found";
                return false;
            }

            if (cue.ClothesStates == null || cue.ClothesStates.Count == 0)
            {
                result = "clothes-empty";
                return false;
            }

            int applied = 0;
            int skipped = 0;
            for (int i = 0; i < cue.ClothesStates.Count; i++)
            {
                var entry = cue.ClothesStates[i];
                if (entry == null)
                {
                    skipped++;
                    continue;
                }

                if (entry.Kind < 0 || entry.Kind > 8 || entry.State < 0 || entry.State > 3)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    female.SetClothesState(entry.Kind, (byte)entry.State, next: true);
                    applied++;
                }
                catch
                {
                    skipped++;
                }
            }

            if (applied <= 0)
            {
                result = $"clothes-no-valid-entry skipped={skipped}";
                return false;
            }

            result = $"femaleIndex={resolvedIndex} applied={applied} skipped={skipped}";
            return true;
        }

        private bool TryApplyCueClick(VideoTimeSpeedCue cue, out string result)
        {
            result = "unknown";
            if (_hSceneProc == null || _hSceneProc.flags == null)
            {
                result = "hscene-or-flags-null";
                return false;
            }

            string raw = cue.ClickKind?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                result = "click-empty";
                return false;
            }

            if (!Enum.TryParse(raw, true, out HFlag.ClickKind click))
            {
                result = $"click-parse-failed raw={raw}";
                return false;
            }

            _hSceneProc.flags.click = click;
            result = $"click={click}";
            return true;
        }

        private bool TryResolveCueTaii(VideoTimeSpeedCue cue, out HSceneProc.AnimationListInfo info, out string reason)
        {
            info = null;
            reason = "unknown";

            if (_hSceneProc == null)
            {
                reason = "hscene-null";
                return false;
            }

            var lists = LstUseAnimInfoField?.GetValue(_hSceneProc) as List<HSceneProc.AnimationListInfo>[];
            if (lists == null)
            {
                reason = "lstUseAnimInfo-null";
                return false;
            }

            bool hasId = cue.TaiiId.HasValue;
            bool hasName = !string.IsNullOrWhiteSpace(cue.TaiiName);
            bool hasMode = cue.TaiiMode.HasValue && Enum.IsDefined(typeof(HFlag.EMode), cue.TaiiMode.Value);

            string name = hasName ? cue.TaiiName.Trim() : string.Empty;
            HFlag.EMode mode = hasMode ? (HFlag.EMode)cue.TaiiMode.Value : HFlag.EMode.none;

            int bestScore = int.MinValue;
            HSceneProc.AnimationListInfo best = null;

            for (int i = 0; i < lists.Length; i++)
            {
                var list = lists[i];
                if (list == null)
                {
                    continue;
                }

                for (int j = 0; j < list.Count; j++)
                {
                    var candidate = list[j];
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (hasMode && candidate.mode != mode)
                    {
                        continue;
                    }

                    if (hasId && candidate.id != cue.TaiiId.Value)
                    {
                        continue;
                    }

                    int score = 0;
                    if (hasMode)
                    {
                        score += 500;
                    }

                    if (hasId && candidate.id == cue.TaiiId.Value)
                    {
                        score += 1000;
                    }

                    if (hasName)
                    {
                        if (string.Equals(candidate.nameAnimation, name, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 400;
                        }
                        else if (!string.IsNullOrWhiteSpace(candidate.nameAnimation) &&
                                 candidate.nameAnimation.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            score += 200;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            if (best == null)
            {
                reason =
                    $"taii-not-found mode={(hasMode ? cue.TaiiMode.Value.ToString() : "any")} " +
                    $"id={(hasId ? cue.TaiiId.Value.ToString() : "any")} " +
                    $"name={(hasName ? cue.TaiiName : "any")}";
                return false;
            }

            info = best;
            return true;
        }

        private bool TrySelectFaceFromDb(VideoTimeSpeedCue cue, out CueFaceDbRow row, out string reason)
        {
            row = null;
            reason = "unknown";

            string dbPath = ResolveCueFaceDbPath(cue);
            if (!File.Exists(dbPath))
            {
                reason = "face-db-not-found path=" + dbPath;
                return false;
            }

            var sql = new StringBuilder();
            sql.Append("SELECT f.file_id, d.hface_name, d.chara, d.voice_kind, d.action, d.face_id ");
            sql.Append("FROM face_dict d INNER JOIN hface_files f ON f.hface_name = d.hface_name ");
            sql.Append("WHERE 1=1 ");

            if (cue.FaceDbFileId.HasValue)
            {
                sql.Append("AND f.file_id = @fileId ");
            }

            if (cue.FaceDbFaceId.HasValue)
            {
                sql.Append("AND d.face_id = @faceId ");
            }

            if (!string.IsNullOrWhiteSpace(cue.FaceDbChara))
            {
                sql.Append("AND d.chara = @chara ");
            }

            if (cue.FaceDbVoiceKind.HasValue)
            {
                sql.Append("AND d.voice_kind = @voiceKind ");
            }

            if (cue.FaceDbAction.HasValue)
            {
                sql.Append("AND d.action = @action ");
            }

            if (!string.IsNullOrWhiteSpace(cue.FaceDbNameContains))
            {
                sql.Append("AND d.name LIKE @nameLike ESCAPE '\\' ");
            }

            if (cue.FaceDbRandom)
            {
                sql.Append("ORDER BY RANDOM() LIMIT 1;");
            }
            else
            {
                sql.Append("ORDER BY d.chara, d.voice_kind, d.action, d.face_id LIMIT 1;");
            }

            try
            {
                using (var connection = new SqliteConnection("Data Source=" + dbPath + ";Version=3;Read Only=True;"))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = sql.ToString();
                        AddCueDbParams(command, cue);

                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                reason = "face-db-no-match";
                                return false;
                            }

                            row = new CueFaceDbRow
                            {
                                FileId = reader.GetInt32(0),
                                HFaceName = reader.GetString(1),
                                Chara = reader.GetString(2),
                                VoiceKind = reader.GetInt32(3),
                                Action = reader.GetInt32(4),
                                FaceId = reader.GetInt32(5)
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                reason = "face-db-query-failed: " + ex.Message;
                return false;
            }

            return true;
        }

        private static void AddCueDbParams(SqliteCommand command, VideoTimeSpeedCue cue)
        {
            if (cue.FaceDbFileId.HasValue)
            {
                AddDbParam(command, "@fileId", cue.FaceDbFileId.Value, DbType.Int32);
            }

            if (cue.FaceDbFaceId.HasValue)
            {
                AddDbParam(command, "@faceId", cue.FaceDbFaceId.Value, DbType.Int32);
            }

            if (!string.IsNullOrWhiteSpace(cue.FaceDbChara))
            {
                AddDbParam(command, "@chara", cue.FaceDbChara.Trim(), DbType.String);
            }

            if (cue.FaceDbVoiceKind.HasValue)
            {
                AddDbParam(command, "@voiceKind", cue.FaceDbVoiceKind.Value, DbType.Int32);
            }

            if (cue.FaceDbAction.HasValue)
            {
                AddDbParam(command, "@action", cue.FaceDbAction.Value, DbType.Int32);
            }

            if (!string.IsNullOrWhiteSpace(cue.FaceDbNameContains))
            {
                string escaped = EscapeLikePattern(cue.FaceDbNameContains.Trim());
                AddDbParam(command, "@nameLike", "%" + escaped + "%", DbType.String);
            }
        }

        private static void AddDbParam(SqliteCommand command, string name, object value, DbType dbType)
        {
            var p = command.CreateParameter();
            p.ParameterName = name;
            p.DbType = dbType;
            p.Value = value;
            command.Parameters.Add(p);
        }

        private static string EscapeLikePattern(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
        }

        private string ResolveCueFaceDbPath(VideoTimeSpeedCue cue)
        {
            string raw = cue != null ? cue.FaceDbPath : string.Empty;
            raw = raw?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = DefaultFaceDbRelativePath;
            }

            return ResolveVideoCueFilePath(raw);
        }

        private bool TryGetFemaleByIndex(int requestedIndex, out ChaControl female, out int resolvedIndex)
        {
            female = null;
            resolvedIndex = -1;

            if (_hSceneProc == null)
            {
                return false;
            }

            var females = LstFemaleField?.GetValue(_hSceneProc) as List<ChaControl>;
            if (females == null || females.Count == 0)
            {
                return false;
            }

            int clamped = Mathf.Clamp(requestedIndex, 0, Mathf.Max(0, females.Count - 1));
            if (clamped >= 0 && clamped < females.Count && females[clamped] != null)
            {
                female = females[clamped];
                resolvedIndex = clamped;
                return true;
            }

            for (int i = 0; i < females.Count; i++)
            {
                if (females[i] != null)
                {
                    female = females[i];
                    resolvedIndex = i;
                    return true;
                }
            }

            return false;
        }

        private FaceListCtrl GetFaceCtrlByFemaleIndex(int femaleIndex)
        {
            if (_hSceneProc == null)
            {
                return null;
            }

            if (femaleIndex == 1 && _hSceneProc.face1 != null)
            {
                return _hSceneProc.face1;
            }

            return _hSceneProc.face;
        }
    }
}
