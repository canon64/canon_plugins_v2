using System;
using System.Collections.Generic;
using HarmonyLib;

namespace AstralMode
{
    // アストラル界突入時に女キャラを指定スロットのコーディネートへ切り替え、帰還時に元スロットへ戻す。
    //   男キャラは対象外（コーデ切替の概念は女キャラ前提）。
    //   男キャラの着脱状態(clothesState[])は既存 StateSnapshot / RestoreSavedCharaVisualsFinal が
    //   性別無差別で復元するため、本機能から男を触らなくても維持される。
    //
    // 切替 API: ChangeCoordinateTypeAndReload(type, changeBackCoordinateType:false)
    //   - false 指定で backCoordinateType の汚染を回避するため、退避は coordinateType のみで足りる。
    //   - nowCoordinate の deep copy は取らない。他Modが乗せたライブ差分の保存は今回スコープ外。
    public sealed partial class Plugin
    {
        private static readonly System.Reflection.FieldInfo HSceneFemalesField =
            AccessTools.Field(typeof(HSceneProc), "lstFemale");

        private System.Collections.IEnumerable GetCurrentHSceneFemaleObjects()
        {
            return _hSceneProc != null
                ? HSceneFemalesField?.GetValue(_hSceneProc) as System.Collections.IEnumerable
                : null;
        }

        private sealed class SavedCoordinateState
        {
            public ChaControl Cha;
            public int CoordinateType;
        }

        private readonly List<SavedCoordinateState> _savedCoordinates = new List<SavedCoordinateState>();
        private bool _astralCoordinateApplied;

        // KKS の cha.sex は 1=女 / 0=男（KK と逆）。混乱回避のため HSceneProc.lstFemale を直接 reflect。
        private List<ChaControl> CollectCurrentHSceneFemales()
        {
            var result = new List<ChaControl>();
            var lstFemale = GetCurrentHSceneFemaleObjects();
            if (lstFemale == null) return result;

            foreach (object o in lstFemale)
                if (o is ChaControl c && c != null) result.Add(c);
            return result;
        }

        internal void CaptureAndApplyAstralCoordinate()
        {
            if (_cfgEnableAstralCoordinate == null || !_cfgEnableAstralCoordinate.Value) return;
            if (_astralCoordinateApplied)
            {
                SimpleFileLogger.Info("AstralCoord: enter skipped (already applied)");
                return;
            }

            ChaFileDefine.CoordinateType astralType = _cfgAstralCoordinateSlot != null
                ? _cfgAstralCoordinateSlot.Value
                : ChaFileDefine.CoordinateType.Plain;
            int astralIndex = (int)astralType;

            var females = CollectCurrentHSceneFemales();
            int applied = 0, skipped = 0;

            foreach (var cha in females)
            {
                if (cha == null) { skipped++; continue; }
                if (cha.chaFile == null || cha.chaFile.coordinate == null
                    || cha.chaFile.coordinate.Length <= astralIndex)
                {
                    SimpleFileLogger.Warn($"AstralCoord: slot out of range cha={cha.name} slot={astralIndex} length={(cha.chaFile != null && cha.chaFile.coordinate != null ? cha.chaFile.coordinate.Length : -1)}");
                    skipped++;
                    continue;
                }

                int savedType = cha.fileStatus.coordinateType;

                bool ok = false;
                try
                {
                    ok = cha.ChangeCoordinateTypeAndReload(astralType, false);
                    if (!ok)
                        SimpleFileLogger.Warn($"AstralCoord: ChangeCoordinateTypeAndReload returned false cha={cha.name} slot={astralIndex}");
                }
                catch (Exception ex)
                {
                    SimpleFileLogger.Warn($"AstralCoord: apply exception cha={cha.name}: {ex.Message}");
                }

                if (ok)
                {
                    _savedCoordinates.Add(new SavedCoordinateState
                    {
                        Cha = cha,
                        CoordinateType = savedType,
                    });
                    applied++;
                }
                else
                {
                    skipped++;
                }
            }

            _astralCoordinateApplied = _savedCoordinates.Count > 0;
            SimpleFileLogger.Info($"AstralCoord: enter applied={applied} skipped={skipped} slot={astralIndex}");
        }

        internal void RestoreOriginalCoordinate()
        {
            try
            {
                if (!_astralCoordinateApplied)
                {
                    SimpleFileLogger.Info("AstralCoord: exit skipped (nothing applied)");
                    return;
                }
                if (_savedCoordinates.Count == 0)
                {
                    SimpleFileLogger.Info("AstralCoord: exit skipped (saved list empty)");
                    return;
                }

                int restored = 0, skipped = 0;

                foreach (var s in _savedCoordinates)
                {
                    if (s == null || s.Cha == null) { skipped++; continue; }
                    try
                    {
                        if (s.Cha.chaFile == null || s.Cha.chaFile.coordinate == null
                            || s.Cha.chaFile.coordinate.Length <= s.CoordinateType)
                        {
                            SimpleFileLogger.Warn($"AstralCoord: restore slot out of range cha={s.Cha.name} slot={s.CoordinateType}");
                            skipped++;
                            continue;
                        }

                        bool ok = s.Cha.ChangeCoordinateTypeAndReload((ChaFileDefine.CoordinateType)s.CoordinateType, false);
                        if (ok)
                            restored++;
                        else
                        {
                            SimpleFileLogger.Warn($"AstralCoord: ChangeCoordinateTypeAndReload returned false on restore cha={s.Cha.name} slot={s.CoordinateType}");
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        SimpleFileLogger.Warn($"AstralCoord: restore exception cha={s.Cha.name}: {ex.Message}");
                        skipped++;
                    }
                }

                SimpleFileLogger.Info($"AstralCoord: exit restored={restored} skipped={skipped}");
            }
            finally
            {
                // 状態クリアは復元の成功/失敗に関わらず必ず行う。
                // 通常 Exit と ForceExit で状態リセット責務を分散させないため、ここに集約。
                _savedCoordinates.Clear();
                _astralCoordinateApplied = false;
            }
        }
    }
}
