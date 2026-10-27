using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    // Plugin.Handlers.cs
    //
    // 責務: 受信した ExternalVoiceFaceCommand に対する個別処理本体 (HandlePoseCommand,
    //       HandleCoordCommand, HandleClothesCommand, HandleCameraPresetCommand,
    //       HandleResponseTextCommand など) と、それぞれに直結する適用ロジック
    //       (体位継続、衣装/着衣解決、カメラ/動画解析、テキストからの抽出など)。
    //
    // 配線 (Awake/Update/dispatch) と共通基盤は Plugin.cs (本体)、ConfigEntry や
    // 設定 JSON I/O は Plugin.Setup.cs に置く。
    internal sealed partial class Plugin
    {
        // ----------------------------------------------------------------
        // response_text コマンド: 生テキストをパースして着替え/着衣を遅延実行
        // ----------------------------------------------------------------

        private void HandleResponseTextCommandCore(ExternalVoiceFaceCommand command)
        {
            string text = (command.text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text)) return;
            string originalTextForKaleidoscope = text;
            string traceId = string.IsNullOrWhiteSpace(command.traceId) ? "(none)" : command.traceId.Trim();
            string sessionId = (command.sessionId ?? string.Empty).Trim();
            if (!IsCurrentSession(sessionId))
            {
                LogWarn("[response_text] skip stale session trace=" + traceId + " session=" + sessionId + " active=" + _sequenceSessions.ActiveSessionId);
                return;
            }

            bool embeddedSequenceTrace = traceId.StartsWith("sequence:", StringComparison.Ordinal);
            if (!embeddedSequenceTrace
                && !string.IsNullOrEmpty(sessionId)
                && _sequenceSessions.IsTriggerRegistered(sessionId))
            {
                Log("[response_text] skip duplicate external response_text session=" + sessionId + " trace=" + traceId);
                return;
            }

            Log($"[response_text] received trace={traceId} len={text.Length} delay={Mathf.Max(0f, command.delaySeconds):F3} preview={text.Substring(0, Math.Min(60, text.Length))}");
            float _rtStart = Time.realtimeSinceStartup;

            float legacyBaseScheduleTime = Time.unscaledTime;
            float totalDelaySeconds = Mathf.Max(0f, command.delaySeconds);
            int main = command.ResolveMain(Settings?.TargetMainIndex ?? 0);

            // 行ごとタイミング: lineTexts/lineDurations がそろっていれば行ごと実尺で発火時刻を出す。
            // 検出器に渡すテキストも lineTexts 連結に揃えて matchIndex を行構造と一致させる。
            string[] lineTexts = command.lineTexts;
            float[] lineDurations = command.lineDurations;
            bool useLineTiming = HasPerLineTiming(lineTexts, lineDurations);
            int lineIndexOffset = Mathf.Max(0, command.lineIndexOffset);
            if (useLineTiming)
            {
                text = string.Join("\n", lineTexts);
                Log("[response_text] per-line timing enabled lines=" + lineTexts.Length + " offset=" + lineIndexOffset);
            }
            float ScheduleAction(int matchIndex, string label, Action action)
            {
                return ScheduleResponseTextAction(
                    sessionId,
                    lineIndexOffset,
                    useLineTiming,
                    lineTexts,
                    lineDurations,
                    text,
                    totalDelaySeconds,
                    legacyBaseScheduleTime,
                    matchIndex,
                    label,
                    action);
            }

            ScheduleKaleidoscopeThought(text, originalTextForKaleidoscope, ScheduleAction);

            ScheduleMorphBridgeTriggersFromText(text, ScheduleAction);
            ResponseTextTriggerApi.Publish(text, ScheduleAction);
            if (!ScheduleComposerMovementLines(command, ScheduleAction))
                ScheduleComposerMotionIntensityFromText(text, ScheduleAction);

            string[] coordTriggers = SplitKeywords(_cfgCoordPattern?.Value ?? "着替え");
            List<TimedCoordItem> coordItems = FindCoordMatchesFromText(text, coordTriggers, main);
            if (coordItems != null && coordItems.Count > 0)
            {
                foreach (TimedCoordItem timed in coordItems)
                {
                    if (timed == null || string.IsNullOrWhiteSpace(timed.CoordName))
                    {
                        continue;
                    }

                    string cn = timed.CoordName;
                    string trigger = timed.TriggerKeyword ?? string.Empty;
                    int matchIndex = timed.MatchIndex;
                    int m = main;
                    float coordDelaySeconds = ScheduleAction(matchIndex, "coord", () =>
                    {
                        HandleCoordCommand(new ExternalVoiceFaceCommand { type = "coord", coordName = cn, main = m });
                    });
                    Log($"[response_text] coord matched: '{cn}', trigger='{trigger}', pos={matchIndex}, scheduled delay={coordDelaySeconds:F2}s");
                }
            }
            else if (ContainsAny(text, coordTriggers))
            {
                Log("[response_text] coord trigger found but no slot name matched before trigger in same line");
            }
            else
            {
                Log("[response_text] no coord keyword matched");
            }

            if (command.skipNamedPose <= 0 && TryExtractNamedPoseFromText(text, out var namedPoseName, out var namedPoseMatchIndex))
            {
                string name = namedPoseName;
                int namedPoseCaptureCount = Mathf.Clamp(command.captureCount, 0, 10);
                float poseDelaySeconds = ScheduleAction(namedPoseMatchIndex, "named-pose", () =>
                {
                    HandleNamedPoseCommand(name, namedPoseCaptureCount);
                });
                Log($"[response_text] named pose matched: name='{name}' pos={namedPoseMatchIndex}, capture_count={namedPoseCaptureCount}, scheduled delay={poseDelaySeconds:F2}s");
            }
            else if (TryPickPoseFromText(text, out var poseName, out var poseMode, out var poseCategory, out var poseMatchIndex))
            {
                string pn = poseName;
                int pm = poseMode;
                int m = main;
                float poseDelaySeconds = ScheduleAction(poseMatchIndex, "pose", () =>
                {
                    HandlePoseCommand(new ExternalVoiceFaceCommand { type = "pose", poseName = pn, poseMode = pm, main = m });
                });
                Log($"[response_text] pose category matched: '{poseCategory}' -> '{poseName}' (mode={poseMode}) pos={poseMatchIndex}, scheduled delay={poseDelaySeconds:F2}s");
            }
            else
            {
                Log("[response_text] no pose keyword matched");
            }

            List<CameraPresetTriggerHit> cameraHits = FindCameraPresetTriggerHitsFromText(text);
            if (cameraHits != null && cameraHits.Count > 0)
            {
                foreach (CameraPresetTriggerHit hit in cameraHits)
                {
                    if (hit == null || string.IsNullOrWhiteSpace(hit.PresetName))
                    {
                        continue;
                    }

                    string presetNameCopy = hit.PresetName;
                    int lineIndexCopy = hit.LineIndex;
                    string triggerCopy = hit.TriggerKeyword ?? string.Empty;
                    float cameraDelaySeconds = ScheduleAction(hit.MatchIndex, "camera_preset", () =>
                    {
                        HandleCameraPresetCommand(new ExternalVoiceFaceCommand
                        {
                            type = "camera_preset",
                            cameraPresetName = presetNameCopy
                        });
                    });

                    Log($"[response_text] camera matched preset='{presetNameCopy}' trigger='{triggerCopy}' line={lineIndexCopy + 1} pos={hit.MatchIndex}, scheduled delay={cameraDelaySeconds:F2}s");
                }
            }
            else
            {
                Log("[response_text] no camera keyword matched");
            }

            bool subCameraEnabled = _cfgEnableSubCameraPresetForward != null
                ? _cfgEnableSubCameraPresetForward.Value
                : (Settings != null && Settings.EnableSubCameraPresetForward);
            if (subCameraEnabled)
            {
                List<CameraPresetTriggerHit> subCameraHits = FindSubCameraPresetTriggerHitsFromText(text);
                if (subCameraHits != null && subCameraHits.Count > 0)
                {
                    foreach (CameraPresetTriggerHit hit in subCameraHits)
                    {
                        if (hit == null || string.IsNullOrWhiteSpace(hit.PresetName))
                        {
                            continue;
                        }

                        string presetNameCopy = hit.PresetName;
                        int lineIndexCopy = hit.LineIndex;
                        string triggerCopy = hit.TriggerKeyword ?? string.Empty;
                        float subDelaySeconds = ScheduleAction(hit.MatchIndex, "subcamera_preset", () =>
                        {
                            string subReason;
                            bool ok = TryLoadSubCameraPresetByNameExternal(presetNameCopy, out subReason);
                            if (ok)
                                Log($"[subcamera_preset] apply name='{presetNameCopy}'");
                            else
                                LogWarn($"[subcamera_preset] apply failed name='{presetNameCopy}' reason={subReason}");
                        });

                        Log($"[response_text] subcamera matched preset='{presetNameCopy}' trigger='{triggerCopy}' line={lineIndexCopy + 1} pos={hit.MatchIndex}, scheduled delay={subDelaySeconds:F2}s");
                    }
                }
                else
                {
                    Log("[response_text] no subcamera keyword matched");
                }
            }

            List<TimedClothesItem> clothesItems = ParseTimedClothesFromText(text);
            if (clothesItems != null && clothesItems.Count > 0)
            {
                foreach (TimedClothesItem timed in clothesItems)
                {
                    if (timed == null || timed.Item == null)
                    {
                        continue;
                    }

                    ClothesItem itemCopy = new ClothesItem { kind = timed.Item.kind, state = timed.Item.state };
                    int m = main;
                    float clothesDelaySeconds = ScheduleAction(timed.MatchIndex, "clothes", () =>
                    {
                        HandleClothesCommand(new ExternalVoiceFaceCommand
                        {
                            type = "clothes",
                            clothesItems = new[] { itemCopy },
                            main = m
                        });
                    });

                    Log($"[response_text] clothes matched kind={itemCopy.kind} state={itemCopy.state} part='{timed.PartKeyword}' action='{timed.ActionKeyword}' pos={timed.MatchIndex}, scheduled delay={clothesDelaySeconds:F2}s");
                }
            }
            else
            {
                Log("[response_text] no clothes keywords matched");
            }

            if (TryParseTimedGlassesStateFromText(text, out int glassesState, out int glassesMatchIndex, out string glassesPart, out string glassesAction))
            {
                bool showGlasses = glassesState > 0;
                int m = main;
                bool s = showGlasses;
                float glassesDelaySeconds = ScheduleAction(glassesMatchIndex, "glasses", () =>
                {
                    HandleGlassesToggle(m, s);
                });
                Log($"[response_text] glasses matched action={(showGlasses ? "put_on" : "remove")} part='{glassesPart}' keyword='{glassesAction}' pos={glassesMatchIndex}, scheduled delay={glassesDelaySeconds:F2}s");
            }
            else
            {
                Log("[response_text] no glasses keywords matched");
            }

            if (TrySelectVideoFileNameFromText(text, out string videoFileName, out int httpPort, out string videoReason))
            {
                string[] videoTriggerKeywords = SplitKeywords((Settings?.VideoPlaybackTriggerKeywords ?? "流す").Replace('、', ','));
                int videoMatchIndex = FindFirstKeywordIndex(text, videoTriggerKeywords, StringComparison.Ordinal);
                string selectedVideo = videoFileName;
                int selectedPort = httpPort;
                float videoDelaySeconds = ScheduleAction(videoMatchIndex, "video", () =>
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        PostVideoPlayByFileName(selectedVideo, selectedPort);
                    });
                });
                Log($"[response_text] video matched: filename='{selectedVideo}' port={selectedPort} pos={videoMatchIndex}, scheduled delay={videoDelaySeconds:F2}s");
            }
            else
            {
                Log("[response_text] no video keyword matched (" + videoReason + ")");
            }

            // アストラル界突入／現実復帰トリガー。AstralMode 側の state 機械が
            // 「すでにアストラルなら突入無視／すでに現実なら復帰無視」を担保するので、
            // ここではキーワード一致箇所をスケジュールするだけでよい。
            // 発火は「キーワード位置」ではなく「キーワードを含む行を読み終えた瞬間(=行末)」に揃える。
            string[] astralEnterKeywords = SplitKeywords((Settings?.AstralModeTriggerKeywords ?? "アストラル").Replace('、', ','));
            int astralEnterIndex = FindFirstKeywordIndex(text, astralEnterKeywords, StringComparison.Ordinal);
            if (astralEnterIndex >= 0)
            {
                int astralEnterLineEnd = ComputeLineEndIndex(text, astralEnterIndex);
                float astralEnterDelay = ScheduleAction(astralEnterLineEnd, "astral_enter", () =>
                {
                    InvokeAstralTransition(true);
                });
                Log($"[response_text] astral-enter matched pos={astralEnterIndex}, lineEnd={astralEnterLineEnd}, scheduled delay={astralEnterDelay:F2}s");
            }

            string[] realWorldKeywords = SplitKeywords((Settings?.RealWorldTriggerKeywords ?? "現実").Replace('、', ','));
            int realWorldIndex = FindFirstKeywordIndex(text, realWorldKeywords, StringComparison.Ordinal);
            if (realWorldIndex >= 0)
            {
                int realWorldLineEnd = ComputeLineEndIndex(text, realWorldIndex);
                float realWorldDelay = ScheduleAction(realWorldLineEnd, "astral_exit", () =>
                {
                    InvokeAstralTransition(false);
                });
                Log($"[response_text] real-world matched pos={realWorldIndex}, lineEnd={realWorldLineEnd}, scheduled delay={realWorldDelay:F2}s");
            }

            Log($"[response_text] done trace={traceId} elapsed={(Time.realtimeSinceStartup - _rtStart) * 1000f:F1}ms len={text.Length}");
        }


        private bool TryPickPoseFromText(string text, out string poseName, out int poseMode, out string poseCategory, out int poseMatchIndex)
        {
            poseName = null;
            poseMode = -1;
            poseCategory = null;
            poseMatchIndex = -1;

            if (string.IsNullOrWhiteSpace(text) || _poseEntriesByCategory.Count <= 0)
            {
                return false;
            }

            if (!_poseChangeEnabled)
            {
                return false;
            }

            if (_poseSimpleModeEnabled)
            {
                bool picked = TryPickPoseFromTextSimple(text, out poseName, out poseMode, out poseCategory, out poseMatchIndex);
                if (!picked)
                {
                    Log($"[pose-simple] no-match triggers='{_poseSimpleModeTriggerKeywords}'");
                }
                return picked;
            }

            int bestAliasLen = -1;
            string bestCategory = null;

            foreach (var pair in _poseEntriesByCategory)
            {

                string[] aliases = ResolvePoseCategoryAliases(pair.Key);
                foreach (string alias in aliases)
                {
                    if (string.IsNullOrWhiteSpace(alias))
                    {
                        continue;
                    }

                    if (text.IndexOf(alias, StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    if (alias.Length > bestAliasLen)
                    {
                        bestAliasLen = alias.Length;
                        bestCategory = pair.Key;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(bestCategory) &&
                TryResolveInferredPoseCategory(text, out string inferredCategory, out string inferredRuleId))
            {
                bestCategory = inferredCategory;
                bestAliasLen = 0;
                Log($"[pose] category inferred: {bestCategory} (rule={inferredRuleId})");
            }

            if (string.IsNullOrWhiteSpace(bestCategory) &&
                TryPickPoseFromGlobalTokenScore(text, out var globalCategory, out var globalPose, out var globalMatch))
            {
                poseCategory = globalCategory;
                poseName = globalPose?.NameAnimation;
                poseMode = globalPose != null ? globalPose.ModeInt : -1;
                if (!string.IsNullOrWhiteSpace(poseName))
                {
                    poseMatchIndex = FindPoseMatchIndexFromText(text, poseName, poseCategory);
                    string level = globalMatch != null && globalMatch.Score >= _poseForceThreshold ? "force" : "prefer";
                    Log($"[pose] scored-{level} category+pose inferred category={poseCategory} rule={globalMatch?.Rule?.RuleId} score={globalMatch?.Score} pose={poseName}");
                    return true;
                }
            }

            if (string.IsNullOrWhiteSpace(bestCategory))
            {
                return false;
            }

            if (!_poseEntriesByCategory.TryGetValue(bestCategory, out var entries) || entries == null || entries.Count <= 0)
            {
                return false;
            }

            PoseCategoryEntry selected = TryPickPreferredPoseEntry(bestCategory, text, entries);
            if (selected == null)
            {
                int index;
                lock (_random)
                {
                    index = _random.Next(entries.Count);
                }

                selected = entries[index];
            }

            poseName = selected?.NameAnimation;
            poseMode = selected != null ? selected.ModeInt : -1;
            poseCategory = bestCategory;
            poseMatchIndex = FindPoseMatchIndexFromText(text, poseName, poseCategory);

            return !string.IsNullOrWhiteSpace(poseName);
        }

        private bool TryPickPoseFromTextSimple(
            string text,
            out string poseName,
            out int poseMode,
            out string poseCategory,
            out int poseMatchIndex,
            bool randomizeCategoryPose = true,
            bool logMatch = true)
        {
            poseName = null;
            poseMode = -1;
            poseCategory = null;
            poseMatchIndex = -1;

            if (string.IsNullOrWhiteSpace(text) || _poseEntriesByCategory.Count <= 0)
            {
                return false;
            }

            string[] triggerKeywords = GetPoseSimpleModeTriggerKeywords();
            if (triggerKeywords.Length <= 0)
            {
                return false;
            }

            List<TextLineSpan> lines = SplitTextLinesWithOffsets(text);
            foreach (TextLineSpan lineSpan in lines)
            {
                string rawLine = lineSpan?.Line ?? string.Empty;
                int trimStart = 0;
                while (trimStart < rawLine.Length && char.IsWhiteSpace(rawLine[trimStart]))
                {
                    trimStart++;
                }

                if (trimStart >= rawLine.Length)
                {
                    continue;
                }

                int trimEnd = rawLine.Length - 1;
                while (trimEnd >= trimStart && char.IsWhiteSpace(rawLine[trimEnd]))
                {
                    trimEnd--;
                }

                if (trimEnd < trimStart)
                {
                    continue;
                }

                string line = rawLine.Substring(trimStart, trimEnd - trimStart + 1);
                int lineStartAbs = lineSpan.StartIndex + trimStart;

                bool hasAnyTrigger = false;
                bool hasBest = false;
                bool bestIsExact = false;
                int bestDistance = int.MaxValue;
                int bestMatchLength = -1;
                int bestMatchStart = -1;
                int bestMatchStartAbs = -1;
                int bestTriggerIndexAbs = -1;
                string bestTrigger = null;
                string bestCategory = null;
                string bestAlias = null;
                PoseCategoryEntry bestEntry = null;

                foreach (string trigger in triggerKeywords)
                {
                    if (string.IsNullOrWhiteSpace(trigger))
                    {
                        continue;
                    }

                    int searchIndex = 0;
                    while (searchIndex < line.Length)
                    {
                        int triggerIndex = line.IndexOf(trigger, searchIndex, StringComparison.Ordinal);
                        if (triggerIndex < 0)
                        {
                            break;
                        }

                        hasAnyTrigger = true;
                        searchIndex = triggerIndex + Math.Max(1, trigger.Length);

                        if (TryPickExactPoseBeforeIndex(
                                line,
                                triggerIndex,
                                out string exactCategory,
                                out PoseCategoryEntry exactEntry,
                                out int exactDistance,
                                out int exactMatchStart,
                                out int exactMatchLength))
                        {
                            if (ShouldReplaceSimplePoseCandidate(
                                    hasBest,
                                    bestDistance,
                                    bestIsExact,
                                    bestMatchLength,
                                    bestMatchStart,
                                    exactDistance,
                                    true,
                                    exactMatchLength,
                                    exactMatchStart))
                            {
                                hasBest = true;
                                bestIsExact = true;
                                bestDistance = exactDistance;
                                bestMatchLength = exactMatchLength;
                                bestMatchStart = exactMatchStart;
                                bestMatchStartAbs = lineStartAbs + exactMatchStart;
                                bestTriggerIndexAbs = lineStartAbs + triggerIndex;
                                bestTrigger = trigger;
                                bestCategory = exactCategory;
                                bestAlias = null;
                                bestEntry = exactEntry;
                            }
                        }

                        if (TryPickCategoryRandomPoseBeforeIndex(
                                line,
                                triggerIndex,
                                out string randomCategory,
                                out string matchedAlias,
                                out PoseCategoryEntry randomEntry,
                                out int randomDistance,
                                out int randomAliasStart,
                                out int randomAliasLength,
                                randomizeCategoryPose))
                        {
                            if (ShouldReplaceSimplePoseCandidate(
                                    hasBest,
                                    bestDistance,
                                    bestIsExact,
                                    bestMatchLength,
                                    bestMatchStart,
                                    randomDistance,
                                    false,
                                    randomAliasLength,
                                    randomAliasStart))
                            {
                                hasBest = true;
                                bestIsExact = false;
                                bestDistance = randomDistance;
                                bestMatchLength = randomAliasLength;
                                bestMatchStart = randomAliasStart;
                                bestMatchStartAbs = lineStartAbs + randomAliasStart;
                                bestTriggerIndexAbs = lineStartAbs + triggerIndex;
                                bestTrigger = trigger;
                                bestCategory = randomCategory;
                                bestAlias = matchedAlias;
                                bestEntry = randomEntry;
                            }
                        }
                    }
                }

                if (!hasAnyTrigger)
                {
                    continue;
                }

                if (!hasBest || bestEntry == null || string.IsNullOrWhiteSpace(bestCategory))
                {
                    string noPickPreview = line.Length > 80 ? line.Substring(0, 80) : line;
                    if (logMatch)
                    {
                        Log($"[pose-simple] trigger-only line='{noPickPreview}' result=no-pose-before-trigger");
                    }
                    continue;
                }

                poseCategory = bestCategory;
                poseName = bestEntry.NameAnimation;
                poseMode = bestEntry.ModeInt;
                poseMatchIndex = bestMatchStartAbs >= 0 ? bestMatchStartAbs : bestTriggerIndexAbs;

                if (logMatch)
                {
                    string preview = line.Length > 80 ? line.Substring(0, 80) : line;
                    if (bestIsExact)
                    {
                        Log($"[pose-simple] exact-nearest line='{preview}' trigger='{bestTrigger}' triggerPos={bestTriggerIndexAbs} posePos={poseMatchIndex} distance={bestDistance} category='{poseCategory}' pose='{poseName}' mode={poseMode}");
                    }
                    else
                    {
                        Log($"[pose-simple] group-nearest line='{preview}' trigger='{bestTrigger}' triggerPos={bestTriggerIndexAbs} posePos={poseMatchIndex} distance={bestDistance} category='{poseCategory}' alias='{bestAlias}' pose='{poseName}' mode={poseMode}");
                    }
                }

                return true;
            }

            return false;
        }

        private static bool ShouldReplaceSimplePoseCandidate(
            bool hasBest,
            int bestDistance,
            bool bestIsExact,
            int bestMatchLength,
            int bestMatchStart,
            int candidateDistance,
            bool candidateIsExact,
            int candidateMatchLength,
            int candidateMatchStart)
        {
            if (!hasBest)
            {
                return true;
            }

            if (candidateIsExact && !bestIsExact)
            {
                return true;
            }

            if (!candidateIsExact && bestIsExact)
            {
                return false;
            }

            if (candidateDistance < bestDistance)
            {
                return true;
            }

            if (candidateDistance > bestDistance)
            {
                return false;
            }

            if (candidateMatchLength > bestMatchLength)
            {
                return true;
            }

            if (candidateMatchLength < bestMatchLength)
            {
                return false;
            }

            return candidateMatchStart > bestMatchStart;
        }

        private bool TryPickExactPoseBeforeIndex(
            string line,
            int triggerIndex,
            out string category,
            out PoseCategoryEntry selected,
            out int distance,
            out int matchStart,
            out int matchLength)
        {
            category = null;
            selected = null;
            distance = int.MaxValue;
            matchStart = -1;
            matchLength = -1;
            if (string.IsNullOrWhiteSpace(line) || triggerIndex <= 0)
            {
                return false;
            }

            foreach (var pair in _poseEntriesByCategory)
            {
                if (pair.Value == null || pair.Value.Count <= 0)
                {
                    continue;
                }

                foreach (PoseCategoryEntry entry in pair.Value)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.NameAnimation))
                    {
                        continue;
                    }

                    foreach (string matchName in EnumeratePoseMatchNames(entry))
                    {
                        if (string.IsNullOrWhiteSpace(matchName))
                        {
                            continue;
                        }

                        int start = FindLastIndexBefore(
                            line,
                            matchName,
                            triggerIndex,
                            StringComparison.OrdinalIgnoreCase);
                        if (start < 0)
                        {
                            continue;
                        }

                        int len = matchName.Length;
                        int d = triggerIndex - (start + len);
                        if (d < 0)
                        {
                            continue;
                        }

                        if (selected == null || d < distance || (d == distance && len > matchLength))
                        {
                            category = pair.Key;
                            selected = entry;
                            distance = d;
                            matchStart = start;
                            matchLength = len;
                        }
                    }
                }
            }

            return selected != null && !string.IsNullOrWhiteSpace(category);
        }

        private bool TryPickCategoryRandomPoseBeforeIndex(
            string line,
            int triggerIndex,
            out string category,
            out string matchedAlias,
            out PoseCategoryEntry selected,
            out int distance,
            out int aliasStart,
            out int aliasLength,
            bool randomizeSelection = true)
        {
            category = null;
            matchedAlias = null;
            selected = null;
            distance = int.MaxValue;
            aliasStart = -1;
            aliasLength = -1;
            if (string.IsNullOrWhiteSpace(line) || triggerIndex <= 0)
            {
                return false;
            }

            foreach (var pair in _poseEntriesByCategory)
            {
                if (!IsPoseCategoryEnabled(pair.Key) || pair.Value == null || pair.Value.Count <= 0)
                {
                    continue;
                }

                string[] aliases = ResolvePoseCategoryAliases(pair.Key);
                foreach (string alias in aliases)
                {
                    if (string.IsNullOrWhiteSpace(alias))
                    {
                        continue;
                    }

                    int start = FindLastIndexBefore(
                        line,
                        alias,
                        triggerIndex,
                        StringComparison.Ordinal);
                    if (start < 0)
                    {
                        continue;
                    }

                    int len = alias.Length;
                    int d = triggerIndex - (start + len);
                    if (d < 0)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(category) || d < distance || (d == distance && len > aliasLength))
                    {
                        category = pair.Key;
                        matchedAlias = alias;
                        distance = d;
                        aliasStart = start;
                        aliasLength = len;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(category))
            {
                return false;
            }

            if (!_poseEntriesByCategory.TryGetValue(category, out var entries) || entries == null || entries.Count <= 0)
            {
                return false;
            }

            if (!randomizeSelection)
            {
                selected = entries.FirstOrDefault(entry => entry != null && !string.IsNullOrWhiteSpace(entry.NameAnimation));
            }
            else
            {
                lock (_random)
                {
                    selected = entries[_random.Next(entries.Count)];
                }
            }

            return selected != null;
        }

        private static int FindLastIndexBefore(
            string line,
            string token,
            int endExclusive,
            StringComparison comparisonType)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(token) || endExclusive <= 0)
            {
                return -1;
            }

            int maxEnd = Math.Min(endExclusive, line.Length);
            int searchIndex = 0;
            int last = -1;
            while (searchIndex < maxEnd)
            {
                int index = line.IndexOf(token, searchIndex, comparisonType);
                if (index < 0 || index >= maxEnd)
                {
                    break;
                }

                last = index;
                searchIndex = index + 1;
            }

            return last;
        }

        private static List<TextLineSpan> SplitTextLinesWithOffsets(string text)
        {
            var lines = new List<TextLineSpan>();
            if (string.IsNullOrEmpty(text))
            {
                return lines;
            }

            int start = 0;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c != '\r' && c != '\n')
                {
                    i++;
                    continue;
                }

                int len = i - start;
                if (len > 0)
                {
                    lines.Add(new TextLineSpan
                    {
                        Line = text.Substring(start, len),
                        StartIndex = start
                    });
                }

                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                i++;
                start = i;
            }

            if (start < text.Length)
            {
                lines.Add(new TextLineSpan
                {
                    Line = text.Substring(start),
                    StartIndex = start
                });
            }

            return lines;
        }

        private List<TimedCoordItem> FindCoordMatchesFromText(
            string text,
            string[] coordTriggers,
            int main)
        {
            var results = new List<TimedCoordItem>();
            if (string.IsNullOrWhiteSpace(text) || coordTriggers == null || coordTriggers.Length <= 0
                || !ContainsAny(text, coordTriggers))
            {
                return results;
            }

            HSceneProc coordProc = CurrentProc;
            ChaControl coordFemale = coordProc != null ? ResolveFemale(coordProc, main) : null;
            if (coordFemale == null)
            {
                return results;
            }

            var coordSlots = coordFemale.chaFile?.coordinate;
            if (coordSlots == null)
            {
                return results;
            }

            MonoBehaviour moCtrl = null;
            MethodInfo moGetName = null;
            try
            {
                moCtrl = coordFemale.gameObject.GetComponents<MonoBehaviour>()
                    .FirstOrDefault(c => c.GetType().Name == "MoreOutfitsController");
                if (moCtrl != null)
                {
                    moGetName = moCtrl.GetType().GetMethod("GetCoodinateName",
                        BindingFlags.Public | BindingFlags.Instance);
                }
            }
            catch { }

            foreach (TextLineSpan lineSpan in SplitTextLinesWithOffsets(text))
            {
                string line = lineSpan?.Line ?? string.Empty;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                List<VideoTriggerHit> hits = FindVideoTriggerHits(line, coordTriggers);
                if (hits == null || hits.Count <= 0)
                {
                    continue;
                }

                foreach (VideoTriggerHit hit in hits.OrderByDescending(x => x.Index))
                {
                    string bestName = null;
                    int bestPos = -1;
                    int bestDistance = int.MaxValue;
                    int bestLength = -1;

                    int randomPos = FindLastIndexBefore(line, "ランダム", hit.Index, StringComparison.Ordinal);
                    if (randomPos >= 0)
                    {
                        bestName = "ランダム";
                        bestPos = randomPos;
                        bestDistance = hit.Index - (randomPos + "ランダム".Length);
                        bestLength = "ランダム".Length;
                    }

                    for (int i = 0; i < coordSlots.Length; i++)
                    {
                        string slotName = GetCoordSlotName(i, moCtrl, moGetName);
                        foreach (string candidate in GetCoordSearchNames(i, slotName))
                        {
                            int slotPos = FindLastIndexBefore(line, candidate, hit.Index, StringComparison.OrdinalIgnoreCase);
                            if (slotPos < 0) continue;

                            int distance = hit.Index - (slotPos + candidate.Length);
                            if (distance < 0) continue;

                            if (distance < bestDistance
                                || (distance == bestDistance && slotPos > bestPos)
                                || (distance == bestDistance && slotPos == bestPos && candidate.Length > bestLength))
                            {
                                bestName = slotName;
                                bestPos = slotPos;
                                bestDistance = distance;
                                bestLength = candidate.Length;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(bestName))
                    {
                        results.Add(new TimedCoordItem
                        {
                            CoordName = bestName,
                            MatchIndex = lineSpan.StartIndex + bestPos,
                            TriggerKeyword = hit.Keyword
                        });
                    }
                }
            }

            return results
                .OrderBy(x => x.MatchIndex)
                .ThenBy(x => x.CoordName, StringComparer.Ordinal)
                .ToList();
        }

        private static float ComputeActionDelaySecondsByTextPosition(string text, float totalDelaySeconds, int matchIndex)
        {
            float total = Mathf.Max(0f, totalDelaySeconds);
            if (total <= 0f || string.IsNullOrEmpty(text))
            {
                return 0f;
            }

            int maxIndex = Math.Max(0, text.Length - 1);
            int index = Mathf.Clamp(matchIndex, 0, maxIndex);
            float ratio = maxIndex > 0 ? index / (float)maxIndex : 0f;
            return total * Mathf.Clamp01(ratio);
        }

        // 行ごとの実尺を使った発火時刻。matchIndex は join("\n", lineTexts) 上の位置。
        // delay = Σ(先行行の実尺) + (行内位置 / max(1,行長-1)) × その行の実尺
        private static bool HasPerLineTiming(string[] lineTexts, float[] lineDurations)
        {
            return lineTexts != null && lineDurations != null
                && lineTexts.Length > 0 && lineTexts.Length == lineDurations.Length;
        }

        private void RegisterSequenceResponseTextTriggers(
            ExternalVoiceFaceCommand sequenceCommand,
            string sessionId,
            List<ExternalVoicePlaybackItem> playbackItems,
            int main,
            float totalDuration)
        {
            string normalizedSessionId = (sessionId ?? string.Empty).Trim();
            if (sequenceCommand == null || string.IsNullOrEmpty(normalizedSessionId))
            {
                return;
            }

            string[] lineTexts = ResolveSequenceTriggerLineTexts(sequenceCommand, playbackItems);
            float[] lineDurations = ResolveSequenceTriggerLineDurations(sequenceCommand, playbackItems, lineTexts != null ? lineTexts.Length : 0);
            if (!HasPerLineTiming(lineTexts, lineDurations))
            {
                LogWarn(
                    "[cmd][seq] trigger registration skipped reason=line_timing_unavailable"
                    + " session=" + normalizedSessionId
                    + " textLines=" + (lineTexts == null ? 0 : lineTexts.Length)
                    + " durations=" + (lineDurations == null ? 0 : lineDurations.Length));
                return;
            }

            string responseText = (sequenceCommand.responseText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(responseText))
            {
                responseText = string.Join("\n", lineTexts).Trim();
            }

            if (string.IsNullOrWhiteSpace(responseText))
            {
                return;
            }

            var triggerCommand = new ExternalVoiceFaceCommand
            {
                type = "response_text",
                sessionId = normalizedSessionId,
                text = responseText,
                delaySeconds = Mathf.Max(0f, totalDuration),
                traceId = "sequence:" + normalizedSessionId,
                lineTexts = lineTexts,
                lineDurations = lineDurations,
                lineIndexOffset = Mathf.Max(0, sequenceCommand.lineIndexOffset),
                lineSourceIndices = sequenceCommand.lineSourceIndices,
                lineSourceTexts = sequenceCommand.lineSourceTexts,
                lineMovementIntensities = sequenceCommand.lineMovementIntensities,
                source = sequenceCommand.source ?? string.Empty,
                skipNamedPose = sequenceCommand.skipNamedPose,
                captureCount = sequenceCommand.captureCount,
                main = main
            };

            HandleResponseTextCommand(triggerCommand);
            _sequenceSessions.MarkTriggerRegistered(normalizedSessionId);
            Log(
                "[cmd][seq] trigger registration completed"
                + " session=" + normalizedSessionId
                + " lines=" + lineTexts.Length);
        }

        private string[] ResolveSequenceTriggerLineTexts(ExternalVoiceFaceCommand command, List<ExternalVoicePlaybackItem> playbackItems)
        {
            int playbackCount = playbackItems == null ? 0 : playbackItems.Count;
            if (command != null && command.lineTexts != null && command.lineTexts.Length == playbackCount && command.lineTexts.Length > 0)
            {
                return command.lineTexts.Select(v => (v ?? string.Empty).Trim()).ToArray();
            }

            if (command != null && command.lineTexts != null && command.lineTexts.Length > 0 && command.lineTexts.Length != playbackCount)
            {
                LogWarn(
                    "[cmd][seq] lineTexts count mismatch"
                    + " payload=" + command.lineTexts.Length
                    + " playback=" + playbackCount
                    + " fallback=items");
            }

            if (playbackItems == null || playbackItems.Count <= 0)
            {
                return new string[0];
            }

            return playbackItems
                .Select(item => item == null ? string.Empty : (item.Subtitle ?? string.Empty).Trim())
                .ToArray();
        }

        private float[] ResolveSequenceTriggerLineDurations(
            ExternalVoiceFaceCommand command,
            List<ExternalVoicePlaybackItem> playbackItems,
            int expectedCount)
        {
            if (expectedCount <= 0)
            {
                return new float[0];
            }

            if (command != null && command.lineDurations != null && command.lineDurations.Length == expectedCount)
            {
                return command.lineDurations.Select(v => Mathf.Max(0f, v)).ToArray();
            }

            if (command != null && command.lineDurations != null && command.lineDurations.Length > 0 && command.lineDurations.Length != expectedCount)
            {
                LogWarn(
                    "[cmd][seq] lineDurations count mismatch"
                    + " payload=" + command.lineDurations.Length
                    + " expected=" + expectedCount
                    + " fallback=items");
            }

            if (playbackItems == null || playbackItems.Count != expectedCount)
            {
                return new float[0];
            }

            return playbackItems
                .Select(item => item == null ? 0f : Mathf.Max(0f, item.DurationSeconds))
                .ToArray();
        }

        private float ScheduleResponseTextAction(
            string sessionId,
            int lineIndexOffset,
            bool useLineTiming,
            string[] lineTexts,
            float[] lineDurations,
            string text,
            float totalDelaySeconds,
            float legacyBaseScheduleTime,
            int matchIndex,
            string label,
            Action action)
        {
            if (action == null)
            {
                return 0f;
            }

            string normalizedSessionId = (sessionId ?? string.Empty).Trim();
            if (useLineTiming
                && !string.IsNullOrEmpty(normalizedSessionId)
                && TryComputeActionDelaySecondsInLine(lineTexts, lineDurations, matchIndex, out int lineIndex, out float lineOffsetSeconds))
            {
                ScheduleLinePlaybackAction(normalizedSessionId, lineIndex + Mathf.Max(0, lineIndexOffset), lineOffsetSeconds, label, action);
                return lineOffsetSeconds;
            }

            float delaySeconds = useLineTiming
                ? ComputeActionDelaySecondsPerLine(lineTexts, lineDurations, matchIndex)
                : ComputeActionDelaySecondsByTextPosition(text, totalDelaySeconds, matchIndex);
            float executeAt = legacyBaseScheduleTime + delaySeconds;
            _actionScheduler.Schedule(executeAt, CreateSessionGuardedAction(normalizedSessionId, label, action));
            return delaySeconds;
        }

        private void ScheduleLinePlaybackAction(string sessionId, int lineIndex, float lineOffsetSeconds, string label, Action action)
        {
            string normalizedSessionId = (sessionId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalizedSessionId) || lineIndex < 0 || action == null)
            {
                return;
            }

            float safeOffset = Mathf.Max(0f, lineOffsetSeconds);
            if (_sequenceSessions.ScheduleLineAction(
                normalizedSessionId,
                lineIndex,
                safeOffset,
                label,
                action,
                LogStaleSessionAction,
                out float executeAt))
            {
                Log(
                    "[response_text] line action scheduled"
                    + " label=" + (string.IsNullOrWhiteSpace(label) ? "(none)" : label)
                    + " session=" + normalizedSessionId
                    + " line=" + (lineIndex + 1).ToString(CultureInfo.InvariantCulture)
                    + " offset=" + safeOffset.ToString("F3", CultureInfo.InvariantCulture)
                    + " executeIn=" + Mathf.Max(0f, executeAt - Time.unscaledTime).ToString("F3", CultureInfo.InvariantCulture)
                    + " basis=playback_started");
                return;
            }

            Log(
                "[response_text] line action pending"
                + " label=" + (string.IsNullOrWhiteSpace(label) ? "(none)" : label)
                + " session=" + normalizedSessionId
                + " line=" + (lineIndex + 1).ToString(CultureInfo.InvariantCulture)
                + " offset=" + safeOffset.ToString("F3", CultureInfo.InvariantCulture)
                + " basis=playback_started");
        }

        private void RecordSequenceLinePlaybackStart(string sessionId, int lineIndex)
        {
            string normalizedSessionId = (sessionId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalizedSessionId) || lineIndex < 0)
            {
                return;
            }

            float lineStartTime = Time.unscaledTime;
            Log(
                "[response_text] line playback started"
                + " session=" + normalizedSessionId
                + " line=" + (lineIndex + 1).ToString(CultureInfo.InvariantCulture));

            IReadOnlyList<Scheduling.ScheduledLineActionInfo> scheduled =
                _sequenceSessions.RecordLineStart(
                    normalizedSessionId,
                    lineIndex,
                    lineStartTime,
                    LogStaleSessionAction);
            for (int i = 0; i < scheduled.Count; i++)
            {
                Scheduling.ScheduledLineActionInfo pending = scheduled[i];
                Log(
                    "[response_text] pending line action scheduled"
                    + " label=" + (string.IsNullOrWhiteSpace(pending.Label) ? "(none)" : pending.Label)
                    + " session=" + normalizedSessionId
                    + " line=" + (lineIndex + 1).ToString(CultureInfo.InvariantCulture)
                    + " offset=" + pending.OffsetSeconds.ToString("F3", CultureInfo.InvariantCulture)
                    + " executeIn=" + Mathf.Max(0f, pending.ExecuteAt - Time.unscaledTime).ToString("F3", CultureInfo.InvariantCulture)
                    + " basis=playback_started");
            }
        }

        private static int ResolvePlaybackStartedLineIndex(ExternalVoicePlaybackStartedEvent started)
        {
            if (started == null)
            {
                return -1;
            }

            if (started.SequencePosition > 0)
            {
                return started.SequencePosition - 1;
            }

            if (started.Index > 0)
            {
                return started.Index - 1;
            }

            return -1;
        }

        private static bool TryComputeActionDelaySecondsInLine(
            string[] lineTexts,
            float[] lineDurations,
            int matchIndex,
            out int lineIndex,
            out float lineOffsetSeconds)
        {
            lineIndex = -1;
            lineOffsetSeconds = 0f;
            if (!HasPerLineTiming(lineTexts, lineDurations))
            {
                return false;
            }

            int idx = Mathf.Max(0, matchIndex);
            int offset = 0;
            for (int i = 0; i < lineTexts.Length; i++)
            {
                string line = lineTexts[i] ?? string.Empty;
                int len = line.Length;
                int lineEnd = offset + len;
                if (idx < lineEnd || i == lineTexts.Length - 1)
                {
                    int posInLine = Mathf.Clamp(idx - offset, 0, Mathf.Max(0, len - 1));
                    float lineRatio = len > 1 ? posInLine / (float)(len - 1) : 0f;
                    lineIndex = i;
                    lineOffsetSeconds = Mathf.Max(0f, lineDurations[i]) * Mathf.Clamp01(lineRatio);
                    return true;
                }

                offset = lineEnd + 1;
            }

            return false;
        }

        private static float ComputeActionDelaySecondsPerLine(string[] lineTexts, float[] lineDurations, int matchIndex)
        {
            if (!HasPerLineTiming(lineTexts, lineDurations))
            {
                return 0f;
            }

            int idx = Mathf.Max(0, matchIndex);
            float cumulative = 0f;
            int offset = 0;
            for (int i = 0; i < lineTexts.Length; i++)
            {
                string line = lineTexts[i] ?? string.Empty;
                int len = line.Length;
                float dur = Mathf.Max(0f, lineDurations[i]);
                int lineEnd = offset + len; // exclusive。lineEnd は行間セパレータ "\n" の位置
                if (idx < lineEnd || i == lineTexts.Length - 1)
                {
                    int posInLine = Mathf.Clamp(idx - offset, 0, Mathf.Max(0, len - 1));
                    float lineRatio = len > 1 ? posInLine / (float)(len - 1) : 0f;
                    return cumulative + lineRatio * dur;
                }
                cumulative += dur;
                offset = lineEnd + 1; // +1 = セパレータ "\n" 分
            }
            return cumulative;
        }

        // fromIndex を含む行の「最後の文字」のインデックスを返す（行末発火用）。
        // 次の改行の直前。改行が無ければ末尾文字。空文字なら 0。
        private static int ComputeLineEndIndex(string text, int fromIndex)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int start = Mathf.Clamp(fromIndex, 0, text.Length - 1);
            int nl = text.IndexOf('\n', start);
            if (nl < 0)
            {
                return text.Length - 1;
            }

            return Mathf.Max(start, nl - 1);
        }

        private static int FindFirstKeywordIndex(string text, string[] keywords, StringComparison comparisonType)
        {
            if (string.IsNullOrEmpty(text) || keywords == null || keywords.Length <= 0)
            {
                return -1;
            }

            int best = -1;
            foreach (string keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                int index = text.IndexOf(keyword, comparisonType);
                if (index < 0)
                {
                    continue;
                }

                if (best < 0 || index < best)
                {
                    best = index;
                }
            }

            return best;
        }

        private int FindPoseMatchIndexFromText(string text, string poseName, string poseCategory)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return -1;
            }

            if (!string.IsNullOrWhiteSpace(poseName))
            {
                int poseIndex = text.IndexOf(poseName, StringComparison.OrdinalIgnoreCase);
                if (poseIndex >= 0)
                {
                    return poseIndex;
                }

                if (_poseNameAliasesByCanonical.TryGetValue(poseName, out List<string> aliases) && aliases != null)
                {
                    int aliasIndex = FindFirstKeywordIndex(text, aliases.ToArray(), StringComparison.OrdinalIgnoreCase);
                    if (aliasIndex >= 0)
                    {
                        return aliasIndex;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(poseCategory))
            {
                string[] aliases = ResolvePoseCategoryAliases(poseCategory);
                int aliasIndex = FindFirstKeywordIndex(text, aliases, StringComparison.Ordinal);
                if (aliasIndex >= 0)
                {
                    return aliasIndex;
                }
            }

            return -1;
        }

        private IEnumerable<string> EnumeratePoseMatchNames(PoseCategoryEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.NameAnimation))
            {
                yield break;
            }

            yield return entry.NameAnimation;

            if (_poseNameAliasesByCanonical.TryGetValue(entry.NameAnimation, out List<string> aliases) && aliases != null)
            {
                foreach (string alias in aliases)
                {
                    if (string.IsNullOrWhiteSpace(alias))
                    {
                        continue;
                    }

                    yield return alias;
                }
            }
        }


        private bool TryResolveInferredPoseCategory(string text, out string category, out string matchedRuleId)
        {
            category = null;
            matchedRuleId = null;
            if (string.IsNullOrWhiteSpace(text) || _poseCategoryInferRules == null || _poseCategoryInferRules.Count <= 0)
            {
                return false;
            }
            if (!_poseInferRulesEnabled)
            {
                return false;
            }

            PoseCategoryInferRule bestRule = null;
            int bestPriority = int.MinValue;
            int bestSpecificity = int.MinValue;

            foreach (PoseCategoryInferRule rule in _poseCategoryInferRules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.TargetCategory))
                {
                    continue;
                }

                if (rule.Enabled == false)
                {
                    continue;
                }

                if (!IsPoseCategoryEnabled(rule.TargetCategory))
                {
                    continue;
                }

                if (!_poseEntriesByCategory.ContainsKey(rule.TargetCategory))
                {
                    continue;
                }

                if (!IsInferRuleMatch(text, rule))
                {
                    continue;
                }

                int specificity =
                    (rule.RequiredAll?.Length ?? 0) * 100 +
                    (rule.RequiredAny?.Length ?? 0) * 10 +
                    (rule.ExcludeAny?.Length ?? 0);

                bool better = rule.Priority > bestPriority ||
                              (rule.Priority == bestPriority && specificity > bestSpecificity);
                if (!better)
                {
                    continue;
                }

                bestRule = rule;
                bestPriority = rule.Priority;
                bestSpecificity = specificity;
            }

            if (bestRule == null)
            {
                return false;
            }

            category = bestRule.TargetCategory;
            matchedRuleId = bestRule.RuleId;
            return true;
        }

        private bool IsInferRuleMatch(string text, PoseCategoryInferRule rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] excludeAny = rule.ExcludeAny ?? new string[0];
            foreach (string kw in excludeAny)
            {
                if (ContainsKeyword(text, kw))
                {
                    return false;
                }
            }

            string[] requiredAll = rule.RequiredAll ?? new string[0];
            foreach (string kw in requiredAll)
            {
                if (!ContainsKeyword(text, kw))
                {
                    return false;
                }
            }

            string[] requiredAny = rule.RequiredAny ?? new string[0];
            if (requiredAny.Length <= 0)
            {
                return true;
            }

            foreach (string kw in requiredAny)
            {
                if (ContainsKeyword(text, kw))
                {
                    return true;
                }
            }

            return false;
        }

        private PoseCategoryEntry TryPickPreferredPoseEntry(
            string category,
            string text,
            List<PoseCategoryEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(text) || entries == null || entries.Count <= 0)
            {
                return null;
            }
            if (!_poseRulesEnabled)
            {
                return null;
            }

            PoseCategoryEntry scoredPreferred = TryPickPoseEntryByTokenScore(category, text, entries);
            if (scoredPreferred != null)
            {
                return scoredPreferred;
            }

            if (string.Equals(category, "正常位系", StringComparison.Ordinal) &&
                ContainsAny(text, NormalMissionaryInterlockKeywords))
            {
                PoseCategoryEntry normalPreferred = PickRandomPoseEntryByNames(entries, NormalMissionaryInterlockPoseNames);
                if (normalPreferred != null)
                {
                    Log($"[pose] preferred matched category={category} reason=密着/しがみ pose={normalPreferred.NameAnimation}");
                    return normalPreferred;
                }
            }

            if (string.Equals(category, "立位系", StringComparison.Ordinal) &&
                text.IndexOf("壁", StringComparison.Ordinal) >= 0)
            {
                PoseCategoryEntry standingPreferred = PickRandomPoseEntryByNames(entries, StandingWallPreferredPoseNames);
                if (standingPreferred != null)
                {
                    Log($"[pose] preferred matched category={category} reason=壁 pose={standingPreferred.NameAnimation}");
                    return standingPreferred;
                }
            }

            return null;
        }

        private PoseCategoryEntry TryPickPoseEntryByTokenScore(string category, string text, List<PoseCategoryEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(text) || entries == null || entries.Count <= 0)
            {
                return null;
            }

            if (!TryFindBestScoreMatchForCategory(category, text, entries, out PoseScoreMatch bestMatch))
            {
                return null;
            }

            PoseCategoryEntry selected;
            lock (_random)
            {
                selected = bestMatch.Candidates[_random.Next(bestMatch.Candidates.Count)];
            }

            string level = bestMatch.Score >= _poseForceThreshold ? "force" : "prefer";
            Log($"[pose] scored-{level} matched category={category} rule={bestMatch.Rule.RuleId} score={bestMatch.Score} longest={bestMatch.LongestMatch} pose={selected?.NameAnimation}");
            return selected;
        }

        private bool TryPickPoseFromGlobalTokenScore(string text, out string category, out PoseCategoryEntry selected, out PoseScoreMatch match)
        {
            category = null;
            selected = null;
            match = null;

            if (string.IsNullOrWhiteSpace(text) || _poseKeywordScoreRules == null || _poseKeywordScoreRules.Count <= 0)
            {
                return false;
            }

            var candidateCategories = _poseKeywordScoreRules
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Category))
                .Select(r => r.Category)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (candidateCategories.Length <= 0)
            {
                return false;
            }

            PoseScoreMatch bestMatch = null;
            foreach (string candidateCategory in candidateCategories)
            {
                if (!_poseEntriesByCategory.TryGetValue(candidateCategory, out var entries) || entries == null || entries.Count <= 0)
                {
                    continue;
                }

                if (!TryFindBestScoreMatchForCategory(candidateCategory, text, entries, out PoseScoreMatch current))
                {
                    continue;
                }

                if (IsBetterScoreMatch(current, bestMatch))
                {
                    bestMatch = current;
                }
            }

            if (bestMatch == null || bestMatch.Candidates == null || bestMatch.Candidates.Count <= 0)
            {
                return false;
            }

            lock (_random)
            {
                selected = bestMatch.Candidates[_random.Next(bestMatch.Candidates.Count)];
            }

            category = bestMatch.Category;
            match = bestMatch;
            return selected != null;
        }

        private bool TryFindBestScoreMatchForCategory(string category, string text, List<PoseCategoryEntry> entries, out PoseScoreMatch bestMatch)
        {
            bestMatch = null;
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(text) || entries == null || entries.Count <= 0)
            {
                return false;
            }
            if (!_poseRulesEnabled)
            {
                return false;
            }

            if (!IsPoseCategoryEnabled(category))
            {
                return false;
            }

            if (_poseKeywordScoreRules == null || _poseKeywordScoreRules.Count <= 0)
            {
                return false;
            }

            foreach (var rule in _poseKeywordScoreRules)
            {
                if (rule == null || rule.Tokens == null || rule.Tokens.Length <= 0 || rule.PoseNames == null || rule.PoseNames.Length <= 0)
                {
                    continue;
                }

                if (rule.Enabled == false)
                {
                    continue;
                }

                if (!IsScoreRuleCategoryMatch(category, rule.Category))
                {
                    continue;
                }

                var candidates = FindPoseEntriesByNames(entries, rule.PoseNames);
                if (candidates.Count <= 0)
                {
                    continue;
                }

                int score = _poseScoreBase;
                int matchedTokenCount = 0;
                int longestMatch = 0;

                foreach (var token in rule.Tokens)
                {
                    if (token == null || string.IsNullOrWhiteSpace(token.Keyword))
                    {
                        continue;
                    }

                    if (!ContainsKeyword(text, token.Keyword))
                    {
                        continue;
                    }

                    matchedTokenCount++;
                    score += token.Score;
                    if (token.Keyword.Length > longestMatch)
                    {
                        longestMatch = token.Keyword.Length;
                    }
                }

                if (matchedTokenCount <= 0 || score < _poseAdoptThreshold)
                {
                    continue;
                }

                var currentMatch = new PoseScoreMatch
                {
                    Category = category,
                    Rule = rule,
                    Candidates = candidates,
                    Score = score,
                    LongestMatch = longestMatch,
                    Priority = rule.Priority,
                    MatchedTokenCount = matchedTokenCount
                };

                if (IsBetterScoreMatch(currentMatch, bestMatch))
                {
                    bestMatch = currentMatch;
                }
            }

            return bestMatch != null;
        }

        private static bool IsBetterScoreMatch(PoseScoreMatch current, PoseScoreMatch best)
        {
            if (current == null)
            {
                return false;
            }

            if (best == null)
            {
                return true;
            }

            return current.Score > best.Score ||
                   (current.Score == best.Score && current.LongestMatch > best.LongestMatch) ||
                   (current.Score == best.Score && current.LongestMatch == best.LongestMatch && current.Priority > best.Priority) ||
                   (current.Score == best.Score && current.LongestMatch == best.LongestMatch && current.Priority == best.Priority && current.MatchedTokenCount > best.MatchedTokenCount);
        }

        private static bool IsScoreRuleCategoryMatch(string selectedCategory, string ruleCategory)
        {
            if (string.IsNullOrWhiteSpace(selectedCategory) || string.IsNullOrWhiteSpace(ruleCategory))
            {
                return false;
            }

            if (string.Equals(selectedCategory, ruleCategory, StringComparison.Ordinal))
            {
                return true;
            }


            return false;
        }

        private bool IsPoseCategoryEnabled(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return false;
            }

            if (_poseCategoryEnabled.TryGetValue(category, out bool enabled))
            {
                return enabled;
            }

            return true;
        }

        private static bool ContainsKeyword(string text, string keyword)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(keyword))
            {
                return false;
            }

            bool asciiOnly = keyword.All(c => c <= sbyte.MaxValue);
            StringComparison comparison = asciiOnly ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return text.IndexOf(keyword, comparison) >= 0;
        }

        private PoseCategoryEntry PickRandomPoseEntryByNames(List<PoseCategoryEntry> entries, string[] preferredNames)
        {
            if (entries == null || entries.Count <= 0 || preferredNames == null || preferredNames.Length <= 0)
            {
                return null;
            }

            var matched = FindPoseEntriesByNames(entries, preferredNames);

            if (matched.Count <= 0)
            {
                return null;
            }

            lock (_random)
            {
                return matched[_random.Next(matched.Count)];
            }
        }

        private static List<PoseCategoryEntry> FindPoseEntriesByNames(List<PoseCategoryEntry> entries, string[] preferredNames)
        {
            if (entries == null || entries.Count <= 0 || preferredNames == null || preferredNames.Length <= 0)
            {
                return new List<PoseCategoryEntry>();
            }

            var exact = entries
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.NameAnimation))
                .Where(e => preferredNames.Any(name =>
                    !string.IsNullOrWhiteSpace(name) &&
                    string.Equals(e.NameAnimation, name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (exact.Count > 0)
            {
                return exact;
            }

            return entries
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.NameAnimation))
                .Where(e => preferredNames.Any(name =>
                    !string.IsNullOrWhiteSpace(name) &&
                    e.NameAnimation.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
        }


        private bool TrySelectVideoFileNameFromText(string text, out string selectedFileName, out int httpPort, out string reason)
        {
            selectedFileName = null;
            reason = "unknown";
            PluginSettings settings = Settings;
            httpPort = settings != null ? settings.BlankMapAddHttpPort : DefaultBlankMapAddHttpPort;

            if (settings == null)
            {
                reason = "settings unavailable";
                return false;
            }

            if (!settings.EnableVideoPlaybackByResponseText)
            {
                reason = "video response-text playback disabled";
                return false;
            }

            string[] triggerKeywords = SplitKeywords(settings.VideoPlaybackTriggerKeywords);
            if (triggerKeywords.Length <= 0)
            {
                triggerKeywords = new[] { "流す" };
            }

            if (!ContainsAny(text, triggerKeywords))
            {
                reason = "trigger keyword not found";
                return false;
            }

            if (!TryLoadBlankMapAddFolderInfo(settings, out string folderPath, out int resolvedPort, out string loadReason))
            {
                httpPort = resolvedPort;
                reason = loadReason;
                return false;
            }

            httpPort = resolvedPort;

            HashSet<string> allowedExt = BuildVideoExtensionSet(settings.VideoFileExtensions);
            string[] allFiles;
            try
            {
                allFiles = Directory.GetFiles(folderPath);
            }
            catch (Exception ex)
            {
                reason = "folder scan failed: " + ex.Message;
                return false;
            }

            List<VideoTrackGroup> trackGroups = BuildVideoTrackGroups(allFiles, allowedExt);
            if (trackGroups.Count <= 0)
            {
                reason = "no video track groups";
                return false;
            }

            if (!TryFindVideoTrackGroupAfterTrigger(
                text,
                triggerKeywords,
                trackGroups,
                out VideoTrackGroup matchedGroup,
                out int lineNumber,
                out string matchedTrigger,
                out string matchedLineTail))
            {
                reason = "no track group after trigger line";
                return false;
            }

            List<string> candidates = matchedGroup.FileNames;
            if (candidates == null || candidates.Count <= 0)
            {
                reason = "matched track group has no files: " + matchedGroup.CanonicalName;
                return false;
            }

            lock (_random)
            {
                selectedFileName = candidates[_random.Next(candidates.Count)];
            }

            reason = "matched group='" + matchedGroup.CanonicalName + "' variants=" + candidates.Count
                + " line=" + lineNumber + " trigger='" + matchedTrigger + "'";
            Log("[video] track group matched canonical='" + matchedGroup.CanonicalName
                + "' variants=" + candidates.Count
                + " selected='" + selectedFileName + "'"
                + " line=" + lineNumber
                + " trigger='" + matchedTrigger + "'"
                + " tail='" + TrimPreview(matchedLineTail, 80) + "'");
            return true;
        }

        private static List<VideoTrackGroup> BuildVideoTrackGroups(string[] allFiles, HashSet<string> allowedExt)
        {
            var groups = new Dictionary<string, VideoTrackGroup>(StringComparer.Ordinal);
            if (allFiles == null || allFiles.Length <= 0)
            {
                return new List<VideoTrackGroup>();
            }

            for (int i = 0; i < allFiles.Length; i++)
            {
                string path = allFiles[i];
                string ext = Path.GetExtension(path) ?? string.Empty;
                if (allowedExt != null && allowedExt.Count > 0 && !allowedExt.Contains(ext))
                {
                    continue;
                }

                string fileName = Path.GetFileName(path);
                string baseName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(baseName))
                {
                    continue;
                }

                string canonicalName = StripVideoVariantSuffix(baseName);
                string normalizedName = NormalizeVideoLookupText(canonicalName);
                if (string.IsNullOrWhiteSpace(normalizedName))
                {
                    continue;
                }

                if (!groups.TryGetValue(normalizedName, out VideoTrackGroup group))
                {
                    group = new VideoTrackGroup
                    {
                        CanonicalName = canonicalName,
                        NormalizedName = normalizedName
                    };
                    groups[normalizedName] = group;
                }

                if (!group.FileNames.Any(x => string.Equals(x, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    group.FileNames.Add(fileName);
                }
            }

            foreach (VideoTrackGroup group in groups.Values)
            {
                group.FileNames.Sort(StringComparer.OrdinalIgnoreCase);
            }

            return groups.Values
                .OrderByDescending(x => x.NormalizedName.Length)
                .ThenBy(x => x.CanonicalName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool TryFindVideoTrackGroupAfterTrigger(
            string text,
            string[] triggerKeywords,
            List<VideoTrackGroup> trackGroups,
            out VideoTrackGroup matchedGroup,
            out int lineNumber,
            out string matchedTrigger,
            out string matchedLineTail)
        {
            matchedGroup = null;
            lineNumber = -1;
            matchedTrigger = string.Empty;
            matchedLineTail = string.Empty;

            if (string.IsNullOrWhiteSpace(text) || triggerKeywords == null || triggerKeywords.Length <= 0
                || trackGroups == null || trackGroups.Count <= 0)
            {
                return false;
            }

            string[] lines = SplitVideoResponseLines(text);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                List<VideoTriggerHit> hits = FindVideoTriggerHits(line, triggerKeywords);
                if (hits.Count <= 0)
                {
                    continue;
                }

                for (int h = 0; h < hits.Count; h++)
                {
                    VideoTriggerHit hit = hits[h];
                    if (hit == null || string.IsNullOrWhiteSpace(hit.Keyword))
                    {
                        continue;
                    }

                    int tailStart = hit.Index + hit.Keyword.Length;
                    if (tailStart < 0 || tailStart > line.Length)
                    {
                        continue;
                    }

                    string tail = line.Substring(tailStart);
                    string normalizedTail = NormalizeVideoLookupText(tail);
                    if (string.IsNullOrWhiteSpace(normalizedTail))
                    {
                        continue;
                    }

                    VideoTrackGroup group = FindBestVideoTrackGroupInTail(normalizedTail, trackGroups);
                    if (group == null)
                    {
                        continue;
                    }

                    matchedGroup = group;
                    lineNumber = i + 1;
                    matchedTrigger = hit.Keyword;
                    matchedLineTail = tail.Trim();
                    return true;
                }
            }

            return false;
        }

        private static string[] SplitVideoResponseLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new string[0];
            }

            return text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None);
        }

        private static List<VideoTriggerHit> FindVideoTriggerHits(string line, string[] triggerKeywords)
        {
            var hits = new List<VideoTriggerHit>();
            if (string.IsNullOrWhiteSpace(line) || triggerKeywords == null)
            {
                return hits;
            }

            for (int i = 0; i < triggerKeywords.Length; i++)
            {
                string keyword = triggerKeywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                int start = 0;
                while (start < line.Length)
                {
                    int index = line.IndexOf(keyword, start, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        break;
                    }

                    hits.Add(new VideoTriggerHit
                    {
                        Index = index,
                        Keyword = keyword
                    });

                    start = index + Math.Max(1, keyword.Length);
                }
            }

            return hits
                .OrderBy(x => x.Index)
                .ThenByDescending(x => x.Keyword == null ? 0 : x.Keyword.Length)
                .ToList();
        }

        private static VideoTrackGroup FindBestVideoTrackGroupInTail(string normalizedTail, List<VideoTrackGroup> trackGroups)
        {
            if (string.IsNullOrWhiteSpace(normalizedTail) || trackGroups == null || trackGroups.Count <= 0)
            {
                return null;
            }

            VideoTrackGroup bestGroup = null;
            int bestLength = -1;
            int bestIndex = int.MaxValue;
            for (int i = 0; i < trackGroups.Count; i++)
            {
                VideoTrackGroup group = trackGroups[i];
                if (group == null || string.IsNullOrWhiteSpace(group.NormalizedName))
                {
                    continue;
                }

                int index = normalizedTail.IndexOf(group.NormalizedName, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                int length = group.NormalizedName.Length;
                if (length > bestLength || (length == bestLength && index < bestIndex))
                {
                    bestGroup = group;
                    bestLength = length;
                    bestIndex = index;
                }
            }

            return bestGroup;
        }

        private static string StripVideoVariantSuffix(string baseName)
        {
            string name = (baseName ?? string.Empty).Trim();
            if (name.Length <= 0)
            {
                return string.Empty;
            }

            int close = name.Length - 1;
            while (close >= 0 && char.IsWhiteSpace(name[close]))
            {
                close--;
            }

            if (close <= 0)
            {
                return name;
            }

            char closeChar = name[close];
            char openChar;
            if (closeChar == ')')
            {
                openChar = '(';
            }
            else if (closeChar == '）')
            {
                openChar = '（';
            }
            else
            {
                return name;
            }

            int open = name.LastIndexOf(openChar, close);
            if (open < 0)
            {
                return name;
            }

            string inner = name.Substring(open + 1, close - open - 1).Trim();
            if (inner.Length <= 0)
            {
                return name;
            }

            for (int i = 0; i < inner.Length; i++)
            {
                if (!char.IsDigit(inner[i]))
                {
                    return name;
                }
            }

            return name.Substring(0, open).TrimEnd();
        }

        private static string NormalizeVideoLookupText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Normalize(NormalizationForm.FormKC);
            var sb = new StringBuilder(normalized.Length);
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        private bool TryLoadBlankMapAddFolderInfo(PluginSettings settings, out string folderPath, out int httpPort, out string reason)
        {
            folderPath = string.Empty;
            reason = "unknown";
            httpPort = settings != null ? settings.BlankMapAddHttpPort : DefaultBlankMapAddHttpPort;

            if (settings == null)
            {
                reason = "settings unavailable";
                return false;
            }

            string configuredPath = string.IsNullOrWhiteSpace(settings.BlankMapAddSettingsRelativePath)
                ? "..\\MainGameBlankMapAdd\\MapAddSettings.json"
                : settings.BlankMapAddSettingsRelativePath.Trim();

            string settingsPath;
            try
            {
                settingsPath = Path.IsPathRooted(configuredPath)
                    ? Path.GetFullPath(configuredPath)
                    : Path.GetFullPath(Path.Combine(PluginDir ?? string.Empty, configuredPath));
            }
            catch (Exception ex)
            {
                reason = "invalid blank map settings path: " + ex.Message;
                return false;
            }

            if (!File.Exists(settingsPath))
            {
                reason = "blank map settings not found: " + settingsPath;
                return false;
            }

            BlankMapAddSettingsSnapshot snapshot;
            try
            {
                string json = File.ReadAllText(settingsPath, Encoding.UTF8);
                var serializer = new DataContractJsonSerializer(typeof(BlankMapAddSettingsSnapshot));
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                using (var ms = new MemoryStream(bytes))
                {
                    snapshot = serializer.ReadObject(ms) as BlankMapAddSettingsSnapshot;
                }
            }
            catch (Exception ex)
            {
                reason = "blank map settings parse failed: " + ex.Message;
                return false;
            }

            if (snapshot == null)
            {
                reason = "blank map settings parse returned null";
                return false;
            }

            if (snapshot.HttpEnabled.HasValue && !snapshot.HttpEnabled.Value)
            {
                reason = "blank map http disabled";
                return false;
            }

            if (snapshot.HttpPort.HasValue && snapshot.HttpPort.Value > 0 && snapshot.HttpPort.Value <= 65535)
            {
                httpPort = snapshot.HttpPort.Value;
            }

            if (httpPort <= 0 || httpPort > 65535)
            {
                httpPort = DefaultBlankMapAddHttpPort;
            }

            string rawFolder = (snapshot.FolderPlayPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawFolder))
            {
                reason = "FolderPlayPath is empty";
                return false;
            }

            try
            {
                folderPath = Path.IsPathRooted(rawFolder)
                    ? Path.GetFullPath(rawFolder)
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(settingsPath) ?? string.Empty, rawFolder));
            }
            catch (Exception ex)
            {
                reason = "folder path invalid: " + ex.Message;
                return false;
            }

            if (!Directory.Exists(folderPath))
            {
                reason = "folder not found: " + folderPath;
                return false;
            }

            reason = "ok";
            return true;
        }

        private void PostVideoPlayByFileName(string fileName, int httpPort)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                LogWarn("[video] filename is empty");
                return;
            }

            PluginSettings settings = Settings;
            string endpointPath = settings != null ? settings.VideoPlayEndpointPath : "/videoroom/play";
            if (string.IsNullOrWhiteSpace(endpointPath))
            {
                endpointPath = "/videoroom/play";
            }

            endpointPath = endpointPath.Trim();
            if (!endpointPath.StartsWith("/", StringComparison.Ordinal))
            {
                endpointPath = "/" + endpointPath;
            }

            int port = httpPort;
            if (port <= 0 || port > 65535)
            {
                port = settings != null ? settings.BlankMapAddHttpPort : DefaultBlankMapAddHttpPort;
            }
            if (port <= 0 || port > 65535)
            {
                port = DefaultBlankMapAddHttpPort;
            }

            string url = "http://127.0.0.1:" + port + endpointPath;
            string payload = "{\"filename\":\"" + EscapeJsonValue(fileName) + "\"}";
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.Timeout = 1500;
                request.ReadWriteTimeout = 1500;

                byte[] body = Encoding.UTF8.GetBytes(payload);
                request.ContentLength = body.Length;
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(body, 0, body.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    string responseText = reader.ReadToEnd();
                    Log($"[video] play sent filename='{fileName}' status={(int)response.StatusCode} port={port}");
                    if (!string.IsNullOrWhiteSpace(responseText))
                    {
                        Log("[video] play response: " + TrimPreview(responseText, 120));
                    }
                }
            }
            catch (WebException webEx)
            {
                string detail = webEx.Message;
                try
                {
                    if (webEx.Response != null)
                    {
                        using (var stream = webEx.Response.GetResponseStream())
                        using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                        {
                            string body = reader.ReadToEnd();
                            if (!string.IsNullOrWhiteSpace(body))
                            {
                                detail += " body=" + TrimPreview(body, 120);
                            }
                        }
                    }
                }
                catch { }
                LogWarn("[video] play request failed: " + detail + " url=" + url);
            }
            catch (Exception ex)
            {
                LogWarn("[video] play request error: " + ex.Message + " url=" + url);
            }
        }

        private static string[] SplitFileNameIntoWords(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
                return new string[0];

            char[] splitChars = { ' ', '　', '-', '_', '(', ')', '（', '）', '[', ']', '【', '】',
                '.', '·', '·', '/', '\\', '&', '+', '=', '#', '@', '~', '～' };
            return baseName.Split(splitChars, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] BuildVideoMatchTokens(string text, string[] triggerKeywords)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return tokens.ToArray();
            }

            string[] keywords = triggerKeywords ?? new string[0];
            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                int start = 0;
                while (true)
                {
                    int idx = text.IndexOf(keyword, start, StringComparison.Ordinal);
                    if (idx < 0)
                    {
                        break;
                    }

                    int beforeStart = Math.Max(0, idx - 32);
                    string before = text.Substring(beforeStart, idx - beforeStart);
                    string beforeToken = ExtractTailVideoToken(before);
                    if (!string.IsNullOrWhiteSpace(beforeToken))
                    {
                        tokens.Add(beforeToken);
                    }

                    int afterStart = idx + keyword.Length;
                    int afterLength = Math.Min(32, Math.Max(0, text.Length - afterStart));
                    if (afterLength > 0)
                    {
                        string after = text.Substring(afterStart, afterLength);
                        string afterToken = ExtractHeadVideoToken(after);
                        if (!string.IsNullOrWhiteSpace(afterToken))
                        {
                            tokens.Add(afterToken);
                        }
                    }

                    start = idx + keyword.Length;
                }
            }

            return tokens
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string ExtractTailVideoToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string tail = text.Trim();
            char[] separators = { ' ', '　', '\t', '\r', '\n', '、', '。', '！', '!', '？', '?', ',', '，', '・', '」', '』', '"', '\'' };
            int cut = tail.LastIndexOfAny(separators);
            if (cut >= 0 && cut < tail.Length - 1)
            {
                tail = tail.Substring(cut + 1);
            }

            tail = tail.Trim();
            if (string.IsNullOrWhiteSpace(tail))
            {
                return string.Empty;
            }

            string[] suffixes =
            {
                "を", "で", "に", "へ", "は", "が", "と", "ね", "よ",
                "動画", "ビデオ", "再生", "して", "する"
            };
            bool changed = true;
            while (changed && !string.IsNullOrWhiteSpace(tail))
            {
                changed = false;
                for (int i = 0; i < suffixes.Length; i++)
                {
                    string suffix = suffixes[i];
                    if (tail.Length > suffix.Length && tail.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        tail = tail.Substring(0, tail.Length - suffix.Length).Trim();
                        changed = true;
                    }
                }
            }

            return tail.Length >= 2 ? tail : string.Empty;
        }

        private static string ExtractHeadVideoToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string head = text.Trim();
            if (string.IsNullOrWhiteSpace(head))
            {
                return string.Empty;
            }

            string[] prefixes =
            {
                "を", "で", "に", "へ", "は", "が", "と", "ね", "よ",
                "動画", "ビデオ", "再生", "して", "する"
            };
            bool changed = true;
            while (changed && !string.IsNullOrWhiteSpace(head))
            {
                changed = false;
                for (int i = 0; i < prefixes.Length; i++)
                {
                    string prefix = prefixes[i];
                    if (head.Length > prefix.Length && head.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        head = head.Substring(prefix.Length).TrimStart();
                        changed = true;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(head))
            {
                return string.Empty;
            }

            char[] separators = { ' ', '　', '\t', '\r', '\n', '、', '。', '！', '!', '？', '?', ',', '，', '・', '」', '』', '"', '\'' };
            int cut = head.IndexOfAny(separators);
            if (cut > 0)
            {
                head = head.Substring(0, cut);
            }
            else if (cut == 0)
            {
                return string.Empty;
            }

            head = head.Trim();
            return head.Length >= 2 ? head : string.Empty;
        }

        private static HashSet<string> BuildVideoExtensionSet(string csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] parts = SplitKeywords(csv);
            for (int i = 0; i < parts.Length; i++)
            {
                string ext = parts[i];
                if (string.IsNullOrWhiteSpace(ext))
                {
                    continue;
                }

                ext = ext.Trim();
                if (!ext.StartsWith(".", StringComparison.Ordinal))
                {
                    ext = "." + ext;
                }
                set.Add(ext);
            }

            if (set.Count <= 0)
            {
                for (int i = 0; i < DefaultVideoExtensions.Length; i++)
                {
                    set.Add(DefaultVideoExtensions[i]);
                }
            }

            return set;
        }

        private static string TrimPreview(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            if (maxLength <= 0 || text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength);
        }

        private static string EscapeJsonValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        // ParseCoordFromText は廃止。呼び出し元 HandleResponseTextCommand に統合済み。

        private List<TimedClothesItem> ParseTimedClothesFromText(string text)
        {
            var results = new List<TimedClothesItem>();
            if (string.IsNullOrEmpty(text))
            {
                return results;
            }

            string[] removeAllKeywords = SplitKeywords(_cfgRemoveAllKeywords?.Value ?? "全裸になるね,全部脱ぐね,全部脱いじゃう");
            int removeAllIndex = FindFirstKeywordIndex(text, removeAllKeywords, StringComparison.Ordinal);
            if (removeAllIndex >= 0)
            {
                for (int i = 0; i < 8; i++)
                {
                    results.Add(new TimedClothesItem
                    {
                        Item = new ClothesItem { kind = i, state = 3 },
                        MatchIndex = removeAllIndex,
                        PartKeyword = "all",
                        ActionKeyword = "remove_all"
                    });
                }

                return results
                    .OrderBy(x => x.MatchIndex)
                    .ThenBy(x => x.Item.kind)
                    .ToList();
            }

            string[] putOnAllKeywords = SplitKeywords(_cfgPutOnAllKeywords?.Value ?? "全部着るね");
            int putOnAllIndex = FindFirstKeywordIndex(text, putOnAllKeywords, StringComparison.Ordinal);
            if (putOnAllIndex >= 0)
            {
                for (int i = 0; i < 8; i++)
                {
                    results.Add(new TimedClothesItem
                    {
                        Item = new ClothesItem { kind = i, state = 0 },
                        MatchIndex = putOnAllIndex,
                        PartKeyword = "all",
                        ActionKeyword = "put_on_all"
                    });
                }

                return results
                    .OrderBy(x => x.MatchIndex)
                    .ThenBy(x => x.Item.kind)
                    .ToList();
            }

            string[][] partKeywords =
            {
                SplitKeywords(_cfgTopKeywords?.Value ?? "上着,ジャケット,トップス"),
                SplitKeywords(_cfgBottomKeywords?.Value ?? "スカート,ホットパンツ,ミニスカ,ボトムス,ズボン,パンツ"),
                SplitKeywords(_cfgBraKeywords?.Value ?? "ブラ"),
                SplitKeywords(_cfgShortsKeywords?.Value ?? "パンティー,パンティ"),
                SplitKeywords(_cfgGlovesKeywords?.Value ?? "グローブ,手袋"),
                SplitKeywords(_cfgPanthoseKeywords?.Value ?? "ガーターベルト,パンスト,ガーター"),
                SplitKeywords(_cfgSocksKeywords?.Value ?? "ストッキング,ニーハイ,靴下"),
                SplitKeywords(_cfgShoesKeywords?.Value ?? "ハイヒール,スニーカー,サンダル,ヒール,靴"),
            };

            string[] removeKeywords = SplitKeywords(_cfgRemoveKeywords?.Value ?? "脱ぐね,脱いじゃう");
            string[] shiftKeywords = SplitKeywords(_cfgShiftKeywords?.Value ?? "ずらすね,半脱ぎにするね");
            string[] shiftSecondKeywords = SplitKeywords(_cfgShiftSecondKeywords?.Value ?? "下げるね");
            string[] putOnKeywords = SplitKeywords(_cfgPutOnKeywords?.Value ?? "着るね,付けるね");

            var actionGroups = new[]
            {
                Tuple.Create(removeKeywords, 3),
                Tuple.Create(shiftKeywords, -1),
                Tuple.Create(shiftSecondKeywords, -2),
                Tuple.Create(putOnKeywords, 0),
            };

            foreach (TextLineSpan lineSpan in SplitTextLinesWithOffsets(text))
            {
                string line = lineSpan?.Line ?? string.Empty;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var lineMatches = new List<TimedClothesItem>();
                foreach (Tuple<string[], int> actionGroup in actionGroups)
                {
                    string[] actionKeywords = actionGroup.Item1;
                    int state = actionGroup.Item2;
                    foreach (string actionKeyword in actionKeywords)
                    {
                        if (string.IsNullOrWhiteSpace(actionKeyword))
                        {
                            continue;
                        }

                        int searchIndex = 0;
                        while (searchIndex < line.Length)
                        {
                            int actionPos = line.IndexOf(actionKeyword, searchIndex, StringComparison.Ordinal);
                            if (actionPos < 0)
                            {
                                break;
                            }

                            searchIndex = actionPos + Math.Max(1, actionKeyword.Length);
                            int actionAbs = lineSpan.StartIndex + actionPos;

                            for (int kind = 0; kind < partKeywords.Length; kind++)
                            {
                                if (!TryFindNearestKeywordBeforeIndex(line, partKeywords[kind], actionPos, out string partKeyword, out _))
                                {
                                    continue;
                                }

                                lineMatches.Add(new TimedClothesItem
                                {
                                    Item = new ClothesItem { kind = kind, state = state },
                                    MatchIndex = actionAbs,
                                    PartKeyword = partKeyword,
                                    ActionKeyword = actionKeyword
                                });
                            }
                        }
                    }
                }

                // A clothing part is fixed by its first action on this line. Later
                // actions may see its earlier name, but must not change that state.
                var fixedKinds = new HashSet<int>();
                foreach (TimedClothesItem match in lineMatches.OrderBy(x => x.MatchIndex).ThenBy(x => x.Item.kind))
                {
                    if (fixedKinds.Add(match.Item.kind))
                    {
                        results.Add(match);
                    }
                }
            }

            return results
                .OrderBy(x => x.MatchIndex)
                .ThenBy(x => x.Item.kind)
                .ToList();
        }

        private bool TryParseTimedGlassesStateFromText(
            string text,
            out int glassesState,
            out int matchIndex,
            out string partKeyword,
            out string actionKeyword)
        {
            glassesState = -1;
            matchIndex = -1;
            partKeyword = null;
            actionKeyword = null;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string[] glassesKeywords = SplitKeywords(_cfgGlassesKeywords?.Value ?? "メガネ,眼鏡,めがね");
            if (glassesKeywords.Length <= 0)
            {
                return false;
            }

            string[] removeKeywords = SplitKeywords(_cfgRemoveKeywords?.Value ?? "脱ぐね,脱いじゃう");
            string[] putOnKeywords = SplitKeywords(_cfgPutOnKeywords?.Value ?? "着るね,付けるね");
            var actionGroups = new[]
            {
                Tuple.Create(removeKeywords, 0),
                Tuple.Create(putOnKeywords, 1)
            };

            int bestIndex = int.MaxValue;
            foreach (TextLineSpan lineSpan in SplitTextLinesWithOffsets(text))
            {
                string line = lineSpan?.Line ?? string.Empty;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                foreach (Tuple<string[], int> actionGroup in actionGroups)
                {
                    string[] actionKeywords = actionGroup.Item1;
                    int state = actionGroup.Item2;
                    foreach (string keyword in actionKeywords)
                    {
                        if (string.IsNullOrWhiteSpace(keyword))
                        {
                            continue;
                        }

                        int searchIndex = 0;
                        while (searchIndex < line.Length)
                        {
                            int actionPos = line.IndexOf(keyword, searchIndex, StringComparison.Ordinal);
                            if (actionPos < 0)
                            {
                                break;
                            }

                            searchIndex = actionPos + Math.Max(1, keyword.Length);
                            if (!TryFindNearestKeywordBeforeIndex(line, glassesKeywords, actionPos, out string foundPartKeyword, out _))
                            {
                                continue;
                            }

                            int actionAbs = lineSpan.StartIndex + actionPos;
                            if (actionAbs >= bestIndex)
                            {
                                continue;
                            }

                            bestIndex = actionAbs;
                            glassesState = state;
                            matchIndex = actionAbs;
                            partKeyword = foundPartKeyword;
                            actionKeyword = keyword;
                        }
                    }
                }
            }

            return glassesState >= 0;
        }

        private static bool TryFindNearestKeywordBeforeIndex(
            string line,
            string[] keywords,
            int beforeIndex,
            out string foundKeyword,
            out int foundPos)
        {
            foundKeyword = null;
            foundPos = -1;
            if (string.IsNullOrEmpty(line) || keywords == null || keywords.Length <= 0 || beforeIndex <= 0)
            {
                return false;
            }

            foreach (string keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                int pos = FindLastIndexBefore(line, keyword, beforeIndex, StringComparison.Ordinal);
                if (pos < 0)
                {
                    continue;
                }

                if (pos + keyword.Length > beforeIndex)
                {
                    continue;
                }

                if (pos > foundPos || (pos == foundPos && (foundKeyword == null || keyword.Length > foundKeyword.Length)))
                {
                    foundKeyword = keyword;
                    foundPos = pos;
                }
            }

            return foundPos >= 0;
        }

        private static string[] SplitKeywords(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new string[0];
            string[] parts = csv.Split(',');
            var list = new List<string>();
            foreach (string p in parts)
            {
                string t = p.Trim();
                if (!string.IsNullOrEmpty(t)) list.Add(t);
            }
            return list.ToArray();
        }

        private static bool ContainsAny(string text, string[] keywords)
        {
            foreach (string kw in keywords)
                if (text.IndexOf(kw, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private List<CameraPresetTriggerHit> FindCameraPresetTriggerHitsFromText(string text)
        {
            return FindCameraPresetTriggerHitsFromTextInternal(text, isSubCamera: false);
        }

        private List<CameraPresetTriggerHit> FindSubCameraPresetTriggerHitsFromText(string text)
        {
            return FindCameraPresetTriggerHitsFromTextInternal(text, isSubCamera: true);
        }

        private List<CameraPresetTriggerHit> FindCameraPresetTriggerHitsFromTextInternal(string text, bool isSubCamera)
        {
            var hits = new List<CameraPresetTriggerHit>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return hits;
            }

            string[] triggerKeywords = SplitKeywords((_cfgCameraTriggerKeywords != null ? _cfgCameraTriggerKeywords.Value : Settings?.CameraTriggerKeywords) ?? "カメラにして,カメラ切り替えて,視点にして,視点切り替えて,アングルにして,で見せて");
            if (triggerKeywords == null || triggerKeywords.Length <= 0 || !ContainsAny(text, triggerKeywords))
            {
                return hits;
            }

            string[] presetNames;
            string presetReason;
            bool ok = isSubCamera
                ? TryGetSubCameraPresetNamesExternal(out presetNames, out presetReason)
                : TryGetCameraPresetNamesExternal(out presetNames, out presetReason);
            if (!ok || presetNames == null || presetNames.Length <= 0)
            {
                LogWarn("[response_text] " + (isSubCamera ? "subcamera" : "camera") + " preset names unavailable reason=" + (string.IsNullOrWhiteSpace(presetReason) ? "unknown" : presetReason));
                return hits;
            }

            string[] lines = SplitVideoResponseLines(text);
            int globalOffset = 0;
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(line))
                {
                    globalOffset += line.Length + 1;
                    continue;
                }

                List<VideoTriggerHit> triggerHits = FindVideoTriggerHits(line, triggerKeywords);
                if (triggerHits == null || triggerHits.Count <= 0)
                {
                    globalOffset += line.Length + 1;
                    continue;
                }

                foreach (VideoTriggerHit triggerHit in triggerHits.OrderBy(x => x.Index))
                {
                    if (triggerHit == null)
                    {
                        continue;
                    }

                    string prefix = line.Substring(0, Mathf.Clamp(triggerHit.Index, 0, line.Length));
                    string matchedPreset = FindBestCameraPresetNameInPrefix(prefix, presetNames, out int presetIndexInPrefix);
                    if (string.IsNullOrWhiteSpace(matchedPreset))
                    {
                        continue;
                    }

                    hits.Add(new CameraPresetTriggerHit
                    {
                        LineIndex = lineIndex,
                        MatchIndex = globalOffset + Mathf.Max(0, presetIndexInPrefix),
                        TriggerKeyword = triggerHit.Keyword,
                        PresetName = matchedPreset
                    });
                }

                globalOffset += line.Length + 1;
            }

            return hits;
        }

        private static string FindBestCameraPresetNameInPrefix(string prefix, string[] presetNames, out int presetIndex)
        {
            presetIndex = -1;
            if (string.IsNullOrWhiteSpace(prefix) || presetNames == null || presetNames.Length <= 0)
            {
                return null;
            }

            string bestName = null;
            int bestIndex = -1;
            int bestTokenLength = -1;
            int bestPresetLength = -1;
            foreach (CameraPresetAliasEntry aliasEntry in EnumerateCameraPresetAliasEntries(presetNames))
            {
                if (aliasEntry == null || string.IsNullOrWhiteSpace(aliasEntry.Alias) || string.IsNullOrWhiteSpace(aliasEntry.PresetName))
                {
                    continue;
                }

                int found = prefix.LastIndexOf(aliasEntry.Alias, StringComparison.Ordinal);
                if (found < 0)
                {
                    continue;
                }

                if (found > bestIndex
                    || (found == bestIndex && aliasEntry.Alias.Length > bestTokenLength)
                    || (found == bestIndex && aliasEntry.Alias.Length == bestTokenLength && aliasEntry.PresetName.Length > bestPresetLength)
                    || (found == bestIndex && bestName == null))
                {
                    bestIndex = found;
                    bestName = aliasEntry.PresetName;
                    bestTokenLength = aliasEntry.Alias.Length;
                    bestPresetLength = aliasEntry.PresetName.Length;
                }
            }

            presetIndex = bestIndex;
            return bestName;
        }

        private static IEnumerable<CameraPresetAliasEntry> EnumerateCameraPresetAliasEntries(string[] presetNames)
        {
            if (presetNames == null || presetNames.Length <= 0)
            {
                yield break;
            }

            var yielded = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < presetNames.Length; i++)
            {
                string presetName = presetNames[i];
                if (string.IsNullOrWhiteSpace(presetName))
                {
                    continue;
                }

                foreach (string alias in SplitCameraPresetAliases(presetName))
                {
                    string key = presetName + "\n" + alias;
                    if (!yielded.Add(key))
                    {
                        continue;
                    }

                    yield return new CameraPresetAliasEntry
                    {
                        PresetName = presetName,
                        Alias = alias
                    };
                }
            }
        }

        private static IEnumerable<string> SplitCameraPresetAliases(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName))
            {
                yield break;
            }

            string trimmedPresetName = presetName.Trim();
            if (trimmedPresetName.Length <= 0)
            {
                yield break;
            }

            yield return trimmedPresetName;

            string[] parts = trimmedPresetName.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string alias = parts[i] != null ? parts[i].Trim() : string.Empty;
                if (alias.Length <= 0)
                {
                    continue;
                }

                yield return alias;
            }
        }

        private sealed class CameraPresetAliasEntry
        {
            public string PresetName;
            public string Alias;
        }

        private static bool TryGetCameraPresetNamesExternal(out string[] presetNames, out string reason)
        {
            presetNames = new string[0];
            reason = string.Empty;
            try
            {
                Type apiType = Type.GetType("MainGameCameraControl.MainGameCameraControlApi, MainGameCameraControl", throwOnError: false);
                if (apiType == null)
                {
                    reason = "api_type_not_found";
                    return false;
                }

                var method = apiType.GetMethod("TryGetPresetNames", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method == null)
                {
                    reason = "api_method_not_found";
                    return false;
                }

                object[] args = { null, null };
                object result = method.Invoke(null, args);
                presetNames = args[0] as string[] ?? new string[0];
                reason = args[1] as string ?? string.Empty;
                return result is bool && (bool)result;
            }
            catch (Exception ex)
            {
                reason = "exception:" + ex.Message;
                presetNames = new string[0];
                return false;
            }
        }

        // ----------------------------------------------------------------
        // pose コマンド: nameAnimation で体位を切り替える
        // ----------------------------------------------------------------

        private void HandleSemanticPoseCommand(ExternalVoiceFaceCommand command)
        {
            string prompt = command != null ? (command.posePrompt ?? string.Empty).Trim() : string.Empty;
            AppendSemanticPoseDiagnostic("received prompt_len=" + prompt.Length + " capture=" + (command != null ? command.captureAfterApply : 0));
            if (prompt.Length == 0)
            {
                AppendSemanticPoseDiagnostic("rejected reason=empty_prompt");
                LogWarn("[cmd][semantic-pose] posePrompt is empty");
                return;
            }

            try
            {
                Type bridgeType = Type.GetType("MainGameSemanticPoseBridge.Plugin, MainGameSemanticPoseBridge", false);
                MethodInfo method = bridgeType != null
                    ? bridgeType.GetMethod(
                        "TryRequestPose",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(string), typeof(int), typeof(string).MakeByRefType() },
                        null)
                    : null;
                if (method == null)
                {
                    AppendSemanticPoseDiagnostic("rejected reason=bridge_or_method_not_found type_found=" + (bridgeType != null));
                    LogWarn("[cmd][semantic-pose] MainGameSemanticPoseBridge is not loaded");
                    return;
                }

                int captureCount = command.captureCount > 0 ? command.captureCount : (command.captureAfterApply > 0 ? 1 : 0);
                object[] invokeArgs = { prompt, Mathf.Clamp(captureCount, 0, 10), string.Empty };
                object invokeResult = method.Invoke(null, invokeArgs);
                bool accepted = invokeResult is bool && (bool)invokeResult;
                string reason = invokeArgs[2] as string ?? string.Empty;
                AppendSemanticPoseDiagnostic("invoke accepted=" + accepted + " reason=" + reason);
                if (accepted)
                {
                    Log("[cmd][semantic-pose] accepted prompt=" + prompt + " reason=" + reason);
                }
                else
                {
                    LogWarn("[cmd][semantic-pose] rejected prompt=" + prompt + " reason=" + reason);
                }
            }
            catch (Exception ex)
            {
                AppendSemanticPoseDiagnostic("invoke_exception " + ex);
                LogWarn("[cmd][semantic-pose] failed: " + ex.Message);
            }
        }

        private readonly External.NamedPoseTextResolver _namedPoseTextResolver =
            new External.NamedPoseTextResolver(Log, LogWarn);

        private bool TryExtractNamedPoseFromText(string text, out string customName, out int matchIndex)
        {
            return _namedPoseTextResolver.TryResolve(text, _cfgNamedPoseTriggerSuffix?.Value ?? "のポーズになるね",
                Settings != null && Settings.VerboseLog, out customName, out matchIndex);
        }

        private void HandleNamedPoseCommand(string customName, bool captureAfterApply)
        {
            HandleNamedPoseCommand(customName, captureAfterApply ? 1 : 0);
        }

        private void HandleNamedPoseCommand(string customName, int captureCount)
        {
            try
            {
                Type bridgeType = Type.GetType("MainGameSemanticPoseBridge.Plugin, MainGameSemanticPoseBridge", false);
                MethodInfo method = bridgeType != null
                    ? bridgeType.GetMethod(
                        "TryApplyNamedPoseByName",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(string), typeof(int), typeof(string).MakeByRefType() },
                        null)
                    : null;
                if (method == null)
                {
                    LogWarn("[named-pose] MainGameSemanticPoseBridge.TryApplyNamedPoseByName is unavailable");
                    return;
                }

                object[] args = { customName, Mathf.Clamp(captureCount, 0, 10), string.Empty };
                object result = method.Invoke(null, args);
                bool applied = result is bool && (bool)result;
                string reason = args[2] as string ?? string.Empty;
                if (applied)
                    Log($"[named-pose] applied name='{customName}' reason={reason}");
                else
                    LogWarn($"[named-pose] failed name='{customName}' reason={reason}");
            }
            catch (Exception ex)
            {
                LogWarn("[named-pose] invoke failed: " + ex.Message);
            }
        }

        private void HandleDiscordPoseCommand(ExternalVoiceFaceCommand command)
        {
            if (command == null) return;
            string responseText = (command.responseText ?? string.Empty).Trim();
            string posePrompt = (command.posePrompt ?? string.Empty).Trim();
            int captureCount = command.captureCount > 0 ? command.captureCount : (command.captureAfterApply > 0 ? 1 : 0);
            Log($"[discord-pose][diagnostic] received prefer_named={command.preferNamedPoseOverPrompt} capture={command.captureAfterApply} response_len={responseText.Length} prompt_len={posePrompt.Length}");

            if (command.preferNamedPoseOverPrompt > 0
                && TryExtractNamedPoseFromText(responseText, out string customName, out int matchIndex))
            {
                Log($"[discord-pose] named pose preferred name='{customName}' pos={matchIndex}; semantic pose suppressed");
                HandleNamedPoseCommand(customName, captureCount);
                return;
            }

            if (_poseChangeEnabled
                && _poseSimpleModeEnabled
                && TryPickPoseFromTextSimple(
                    responseText,
                    out string voiceFacePoseName,
                    out int voiceFacePoseMode,
                    out string voiceFacePoseCategory,
                    out int voiceFacePoseMatchIndex,
                    randomizeCategoryPose: false,
                    logMatch: false))
            {
                Log($"[discord-pose] VoiceFace body position matched category='{voiceFacePoseCategory}' pose='{voiceFacePoseName}' mode={voiceFacePoseMode} pos={voiceFacePoseMatchIndex}; semantic pose suppressed");
                return;
            }

            if (posePrompt.Length > 0)
            {
                Log("[discord-pose] named pose not found; semantic pose fallback");
                HandleSemanticPoseCommand(new ExternalVoiceFaceCommand
                {
                    type = "semantic_pose",
                    posePrompt = posePrompt,
                    captureAfterApply = captureCount > 0 ? 1 : 0,
                    captureCount = captureCount
                });
                return;
            }

            Log("[discord-pose] no named pose or pose prompt; no pose action");
        }

        private static void AppendSemanticPoseDiagnostic(string message)
        {
            if (!RuntimeLoggingEnabled) return;
            try
            {
                string directory = Path.Combine(BepInEx.Paths.PluginPath, "canon_plugins", "MainGameVoiceFaceEventBridge");
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, "semantic_pose_diagnostic.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }

        private void HandlePoseCommand(ExternalVoiceFaceCommand command)
        {
            string name = (command.poseName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
            {
                LogWarn("[pose] poseName is empty");
                return;
            }

            HSceneProc proc = CurrentProc;
            if (proc == null)
            {
                LogWarn("[pose] HSceneProc not available");
                return;
            }

            var lists = LstUseAnimInfoField?.GetValue(proc) as List<HSceneProc.AnimationListInfo>[];
            if (lists == null)
            {
                LogWarn("[pose] lstUseAnimInfo not found");
                return;
            }

            bool filterMode = command.poseMode >= 0 && System.Enum.IsDefined(typeof(HFlag.EMode), command.poseMode);
            HFlag.EMode targetMode = filterMode ? (HFlag.EMode)command.poseMode : HFlag.EMode.none;

            int bestScore = int.MinValue;
            HSceneProc.AnimationListInfo best = null;

            for (int i = 0; i < lists.Length; i++)
            {
                var list = lists[i];
                if (list == null) continue;
                for (int j = 0; j < list.Count; j++)
                {
                    var c = list[j];
                    if (c == null) continue;
                    if (filterMode && c.mode != targetMode) continue;

                    int score = 0;
                    if (filterMode) score += 500;

                    if (string.Equals(c.nameAnimation, name, StringComparison.OrdinalIgnoreCase))
                        score += 1000;
                    else if (!string.IsNullOrWhiteSpace(c.nameAnimation) &&
                             c.nameAnimation.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 400;
                    else
                        continue;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
            }

            if (best == null)
            {
                LogWarn($"[pose] not found: name={name} mode={(filterMode ? command.poseMode.ToString() : "any")}");
                return;
            }

            DumpPoseDiagnostics("PRE", proc, best, name);

            PoseContinuationState continuation = CapturePoseContinuation(proc, best);
            proc.flags.selectAnimationListInfo = best;
            proc.flags.click = continuation != null && continuation.TargetHoushi && continuation.ShouldContinue
                ? HFlag.ClickKind.insert
                : HFlag.ClickKind.actionChange;

            if (continuation != null && (continuation.ShouldContinue || continuation.ShouldRestoreAuto))
            {
                SchedulePoseContinuation(continuation);
            }

            Log($"[pose] → id={best.id} mode={best.mode} name={best.nameAnimation} click={proc.flags.click} continue={(continuation != null && continuation.ShouldContinue)}");

            DumpPoseDiagnostics("POST", proc, best, name);

            string targetName = best.nameAnimation ?? string.Empty;
            int targetId = best.id;
            HFlag.EMode targetModeForCheck = best.mode;
            _actionScheduler.Schedule(Time.unscaledTime + 0.1f, () =>
            {
                DumpPoseDiagnostics("POST+0.1s", CurrentProc, null, targetName, targetId, targetModeForCheck);
            });
            _actionScheduler.Schedule(Time.unscaledTime + 0.5f, () =>
            {
                DumpPoseDiagnostics("POST+0.5s", CurrentProc, null, targetName, targetId, targetModeForCheck);
            });
            _actionScheduler.Schedule(Time.unscaledTime + 2.0f, () =>
            {
                DumpPoseDiagnostics("POST+2.0s", CurrentProc, null, targetName, targetId, targetModeForCheck);
            });
        }

        private void DumpPoseDiagnostics(string tag, HSceneProc proc, HSceneProc.AnimationListInfo best, string requestedName, int targetId = -1, HFlag.EMode targetMode = HFlag.EMode.none)
        {
            try
            {
                if (proc == null)
                {
                    Log($"[pose-diag {tag}] proc=null request='{requestedName}'");
                    return;
                }

                var flags = proc.flags;
                string nowName = "?", nowModeStr = "?", nowId = "?";
                string flagsModeStr = "?", clickStr = "?";
                string voiceWait = "?", selectIsNull = "?";
                if (flags != null)
                {
                    flagsModeStr = flags.mode.ToString();
                    clickStr = flags.click.ToString();
                    voiceWait = flags.voiceWait.ToString();
                    selectIsNull = (flags.selectAnimationListInfo == null) ? "null" : ("set:" + (flags.selectAnimationListInfo.nameAnimation ?? "?"));
                    var now = flags.nowAnimationInfo;
                    if (now == null)
                    {
                        nowName = "null"; nowModeStr = "null"; nowId = "null";
                    }
                    else
                    {
                        nowName = now.nameAnimation ?? "?";
                        nowModeStr = now.mode.ToString();
                        nowId = now.id.ToString();
                    }
                }
                else
                {
                    Log($"[pose-diag {tag}] proc.flags=null request='{requestedName}'");
                    return;
                }

                int lstProcCount = -1;
                try
                {
                    var lp = LstProcField?.GetValue(proc) as IList;
                    if (lp != null) lstProcCount = lp.Count;
                }
                catch { }

                bool changeTaii = false;
                bool bChangePoint = false;
                try { var v = ChangeTaiiField?.GetValue(proc); if (v is bool) changeTaii = (bool)v; } catch { }
                try { var v = BChangePointField?.GetValue(proc); if (v is bool) bChangePoint = (bool)v; } catch { }

                string spriteStr = "?";
                try
                {
                    var spr = SpriteField?.GetValue(proc) as HSprite;
                    if (spr == null) spriteStr = "null";
                    else spriteStr = $"isFade={spr.isFade} fadeKind={spr.GetFadeKindProc()}";
                }
                catch (Exception ex) { spriteStr = "ex:" + ex.GetType().Name; }

                string sceneStr = "?";
                try
                {
                    sceneStr = $"IsFadeNow={Manager.Scene.IsFadeNow} IsOverlap={Manager.Scene.IsOverlap}";
                }
                catch (Exception ex) { sceneStr = "ex:" + ex.GetType().Name; }

                string categorysStr = "?";
                try
                {
                    var cats = CategorysField?.GetValue(proc) as IEnumerable;
                    if (cats != null)
                    {
                        var sb = new StringBuilder();
                        sb.Append('[');
                        bool first = true;
                        foreach (var c in cats)
                        {
                            if (!first) sb.Append(',');
                            sb.Append(c);
                            first = false;
                        }
                        sb.Append(']');
                        categorysStr = sb.ToString();
                    }
                    else categorysStr = "null";
                }
                catch (Exception ex) { categorysStr = "ex:" + ex.GetType().Name; }

                string bestStr;
                if (best != null)
                {
                    var sb = new StringBuilder();
                    sb.Append("id=").Append(best.id);
                    sb.Append(" mode=").Append(best.mode);
                    sb.Append(" name=").Append(best.nameAnimation ?? "?");
                    sb.Append(" cats=[");
                    if (best.lstCategory != null)
                    {
                        bool first = true;
                        foreach (var c in best.lstCategory)
                        {
                            if (!first) sb.Append(',');
                            sb.Append(c != null ? c.category.ToString() : "null");
                            first = false;
                        }
                    }
                    sb.Append(']');
                    bestStr = sb.ToString();
                }
                else
                {
                    bestStr = $"deferred target='{requestedName}' id={targetId} mode={targetMode}";
                }

                string reachedTarget = "?";
                if (flags?.nowAnimationInfo != null && best == null && targetId >= 0)
                {
                    bool nameOk = !string.IsNullOrEmpty(requestedName) && string.Equals(flags.nowAnimationInfo.nameAnimation, requestedName, StringComparison.OrdinalIgnoreCase);
                    bool idOk = flags.nowAnimationInfo.id == targetId && flags.nowAnimationInfo.mode == targetMode;
                    reachedTarget = (nameOk || idOk).ToString();
                }

                Log($"[pose-diag {tag}] request='{requestedName}' best=({bestStr}) "
                    + $"now=(name={nowName} mode={nowModeStr} id={nowId}) "
                    + $"flags.mode={flagsModeStr} click={clickStr} voiceWait={voiceWait} select={selectIsNull} "
                    + $"lstProc={lstProcCount} changeTaii={changeTaii} bChangePoint={bChangePoint} "
                    + $"sprite=({spriteStr}) scene=({sceneStr}) categorys={categorysStr} "
                    + (reachedTarget != "?" ? $"reachedTarget={reachedTarget}" : ""));
            }
            catch (Exception ex)
            {
                LogWarn($"[pose-diag {tag}] dump failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void HandleCameraPresetCommand(ExternalVoiceFaceCommand command)
        {
            string presetName = (command.cameraPresetName ?? string.Empty).Trim();
            int presetIndex = command.cameraPresetIndex;
            bool hasName = !string.IsNullOrEmpty(presetName);
            bool hasIndex = presetIndex >= 0;
            if (!hasName && !hasIndex)
            {
                LogWarn("[camera_preset] cameraPresetName / cameraPresetIndex is empty");
                return;
            }

            string reason;
            bool ok;
            if (hasName)
            {
                ok = TryLoadCameraPresetByNameExternal(presetName, out reason);
                if (ok)
                    Log($"[camera_preset] apply name='{presetName}'");
                else
                    LogWarn($"[camera_preset] apply failed name='{presetName}' reason={reason}");
                return;
            }

            ok = TryLoadCameraPresetByIndexExternal(presetIndex, out reason);
            if (ok)
                Log($"[camera_preset] apply index={presetIndex}");
            else
                LogWarn($"[camera_preset] apply failed index={presetIndex} reason={reason}");
        }

        private static bool TryLoadCameraPresetByNameExternal(string presetName, out string reason)
        {
            object[] args = { presetName, null };
            return InvokeMainGameCameraControlApi("TryLoadPresetByName", args, out reason);
        }

        private static bool TryLoadCameraPresetByIndexExternal(int presetIndex, out string reason)
        {
            object[] args = { presetIndex, null };
            return InvokeMainGameCameraControlApi("TryLoadPresetByIndex", args, out reason);
        }

        private static bool InvokeMainGameCameraControlApi(string methodName, object[] args, out string reason)
        {
            reason = string.Empty;
            try
            {
                Type apiType = Type.GetType("MainGameCameraControl.MainGameCameraControlApi, MainGameCameraControl", throwOnError: false);
                if (apiType == null)
                {
                    reason = "api_type_not_found";
                    return false;
                }

                var method = apiType.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method == null)
                {
                    reason = "api_method_not_found";
                    return false;
                }

                object result = method.Invoke(null, args);
                reason = args != null && args.Length >= 2 ? args[1] as string ?? string.Empty : string.Empty;
                return result is bool && (bool)result;
            }
            catch (Exception ex)
            {
                reason = "exception:" + ex.Message;
                return false;
            }
        }

        private static bool TryLoadSubCameraPresetByNameExternal(string presetName, out string reason)
        {
            object[] args = { presetName, null };
            return InvokeSubCameraDisplayProbeApi("TryLoadPresetByName", args, out reason);
        }

        private static bool TryGetSubCameraPresetNamesExternal(out string[] presetNames, out string reason)
        {
            presetNames = new string[0];
            reason = string.Empty;
            try
            {
                Type apiType = Type.GetType("MainGameSubCameraDisplayProbe.MainGameSubCameraDisplayProbeApi, MainGameSubCameraDisplayProbe", throwOnError: false);
                if (apiType == null)
                {
                    reason = "api_type_not_found";
                    return false;
                }

                var method = apiType.GetMethod("TryGetPresetNames", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method == null)
                {
                    reason = "api_method_not_found";
                    return false;
                }

                object[] args = { null, null };
                object result = method.Invoke(null, args);
                presetNames = args[0] as string[] ?? new string[0];
                reason = args[1] as string ?? string.Empty;
                return result is bool && (bool)result;
            }
            catch (Exception ex)
            {
                reason = "exception:" + ex.Message;
                presetNames = new string[0];
                return false;
            }
        }

        private static bool InvokeSubCameraDisplayProbeApi(string methodName, object[] args, out string reason)
        {
            reason = string.Empty;
            try
            {
                Type apiType = Type.GetType("MainGameSubCameraDisplayProbe.MainGameSubCameraDisplayProbeApi, MainGameSubCameraDisplayProbe", throwOnError: false);
                if (apiType == null)
                {
                    reason = "api_type_not_found";
                    return false;
                }

                var method = apiType.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method == null)
                {
                    reason = "api_method_not_found";
                    return false;
                }

                object result = method.Invoke(null, args);
                reason = args != null && args.Length >= 2 ? args[1] as string ?? string.Empty : string.Empty;
                return result is bool && (bool)result;
            }
            catch (Exception ex)
            {
                reason = "exception:" + ex.Message;
                return false;
            }
        }

        private PoseContinuationState CapturePoseContinuation(HSceneProc proc, HSceneProc.AnimationListInfo target)
        {
            if (proc == null || proc.flags == null || target == null)
            {
                return null;
            }

            HFlag.EMode previousMode = proc.flags.mode;
            bool previousSonyu = IsSonyuPoseMode(previousMode);
            bool previousHoushi = IsHoushiPoseMode(previousMode);
            bool targetSonyu = IsSonyuPoseMode(target.mode);
            bool targetHoushi = IsHoushiPoseMode(target.mode);
            if ((!previousSonyu || !targetSonyu) && (!previousHoushi || !targetHoushi))
            {
                return null;
            }

            HActionBase currentAction = ResolveCurrentHAction(proc);
            bool wasAuto = CapturePoseAutoEnabled(currentAction, previousSonyu, previousHoushi);

            string stateName = (proc.flags.nowAnimStateName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(stateName) && !wasAuto)
            {
                return null;
            }

            bool wasLoop = false;
            bool wasStrongLoop = false;
            bool wasAnal = false;
            bool shouldContinue = false;

            if (previousSonyu)
            {
                shouldContinue = IsSonyuContinuationState(stateName);
                wasLoop = IsSonyuLoopState(stateName);
                wasStrongLoop = IsStrongLoopState(stateName);
                wasAnal = stateName.StartsWith("A_", StringComparison.Ordinal);
            }
            else if (previousHoushi)
            {
                shouldContinue = IsHoushiContinuationState(stateName);
                wasLoop = IsHoushiLoopState(stateName);
                wasStrongLoop = IsStrongLoopState(stateName);
            }

            if (!shouldContinue && !wasAuto)
            {
                return null;
            }

            return new PoseContinuationState
            {
                ShouldContinue = shouldContinue,
                ShouldRestoreAuto = wasAuto,
                PreviousMode = previousMode,
                TargetMode = target.mode,
                TargetId = target.id,
                TargetName = target.nameAnimation ?? string.Empty,
                PreviousStateName = stateName,
                TargetSonyu = targetSonyu,
                TargetHoushi = targetHoushi,
                WasAnal = wasAnal,
                WasLoop = wasLoop,
                WasStrongLoop = wasStrongLoop,
                SpeedCalc = Mathf.Clamp01(proc.flags.speedCalc),
                SpeedHoushi = proc.flags.speedHoushi,
                VoiceSpeedMotion = proc.flags.voice != null && proc.flags.voice.speedMotion,
                WasAuto = wasAuto
            };
        }

        private void SchedulePoseContinuation(PoseContinuationState continuation)
        {
            if (continuation == null || (!continuation.ShouldContinue && !continuation.ShouldRestoreAuto))
            {
                return;
            }

            _actionScheduler.Schedule(Time.unscaledTime + 0.12f, () =>
            {
                ApplyPoseContinuation(continuation);
            });

            Log($"[pose-continue] scheduled prevMode={continuation.PreviousMode} prevState={continuation.PreviousStateName} targetMode={continuation.TargetMode} target='{continuation.TargetName}' speedCalc={continuation.SpeedCalc:F2} auto={continuation.WasAuto}");
        }

        private void ApplyPoseContinuation(PoseContinuationState continuation)
        {
            if (continuation == null || (!continuation.ShouldContinue && !continuation.ShouldRestoreAuto))
            {
                return;
            }

            HSceneProc proc = CurrentProc ?? FindCurrentProc();
            if (!IsPoseContinuationTargetReady(proc, continuation))
            {
                RetryPoseContinuation(continuation, "target-not-ready");
                return;
            }

            HActionBase action = ResolveCurrentHAction(proc);
            if (action == null)
            {
                RetryPoseContinuation(continuation, "action-not-found");
                return;
            }

            if (!continuation.ShouldContinue)
            {
                RestorePoseAutoState(action, continuation);
                return;
            }

            if (continuation.TargetSonyu)
            {
                ApplySonyuPoseContinuation(proc, action, continuation);
                return;
            }

            if (continuation.TargetHoushi)
            {
                ApplyHoushiPoseContinuation(proc, action, continuation);
            }
        }

        private void RetryPoseContinuation(PoseContinuationState continuation, string reason)
        {
            continuation.Attempts++;
            if (continuation.Attempts >= 16)
            {
                LogWarn($"[pose-continue] give up reason={reason} target='{continuation.TargetName}' attempts={continuation.Attempts}");
                return;
            }

            _actionScheduler.Schedule(Time.unscaledTime + 0.08f, () =>
            {
                ApplyPoseContinuation(continuation);
            });
        }

        private static bool IsPoseContinuationTargetReady(HSceneProc proc, PoseContinuationState continuation)
        {
            if (proc == null || proc.flags == null || continuation == null)
            {
                return false;
            }

            HSceneProc.AnimationListInfo info = proc.flags.nowAnimationInfo;
            if (info == null)
            {
                return false;
            }

            if (info.mode != continuation.TargetMode)
            {
                return false;
            }

            if (info.id == continuation.TargetId)
            {
                return true;
            }

            return string.Equals(info.nameAnimation, continuation.TargetName, StringComparison.OrdinalIgnoreCase);
        }

        private static HActionBase ResolveCurrentHAction(HSceneProc proc)
        {
            if (proc == null || proc.flags == null || LstProcField == null)
            {
                return null;
            }

            try
            {
                IList actions = LstProcField.GetValue(proc) as IList;
                int mode = (int)proc.flags.mode;
                if (actions == null || mode < 0 || mode >= actions.Count)
                {
                    return null;
                }

                return actions[mode] as HActionBase;
            }
            catch
            {
                return null;
            }
        }

        private void ApplySonyuPoseContinuation(HSceneProc proc, HActionBase action, PoseContinuationState continuation)
        {
            bool useAnal = continuation.WasAnal && proc.flags.isAnalInsertOK;
            string stateName = BuildSonyuContinuationStateName(continuation, useAnal);
            RestorePoseContinuationSpeed(proc, continuation, minLoopSpeed: continuation.WasLoop ? 0.35f : 0f);
            proc.flags.finish = HFlag.FinishKind.none;
            proc.flags.voiceWait = false;
            proc.flags.isAnalPlay = useAnal;
            if (useAnal)
            {
                proc.flags.SetInsertAnal();
                proc.flags.SetInsertAnalVoiceCondition();
            }
            else
            {
                proc.flags.SetInsertKokan();
                proc.flags.SetInsertKokanVoiceCondition();
            }

            proc.flags.click = HFlag.ClickKind.none;
            action.SetPlay(stateName);
            RestorePoseAutoState(action, continuation);
            Log($"[pose-continue] applied sonyu state={stateName} target='{continuation.TargetName}' anal={useAnal} speedCalc={proc.flags.speedCalc:F2}");
        }

        private void ApplyHoushiPoseContinuation(HSceneProc proc, HActionBase action, PoseContinuationState continuation)
        {
            string stateName = continuation.WasStrongLoop ? "SLoop" : "WLoop";
            RestorePoseContinuationSpeed(proc, continuation, minLoopSpeed: 0.35f);
            proc.flags.finish = HFlag.FinishKind.none;
            proc.flags.voiceWait = false;
            proc.flags.speedHoushi = continuation.SpeedHoushi;
            proc.flags.SetHoushiPlay();
            proc.flags.click = HFlag.ClickKind.none;
            action.SetPlay(stateName);
            RestorePoseAutoState(action, continuation);
            Log($"[pose-continue] applied houshi state={stateName} target='{continuation.TargetName}' speedCalc={proc.flags.speedCalc:F2}");
        }

        private static bool CapturePoseAutoEnabled(HActionBase action, bool sonyuMode, bool houshiMode)
        {
            if (action == null)
            {
                return false;
            }

            try
            {
                if (sonyuMode)
                {
                    FieldInfo isAutoField = action.GetType().GetField("isAuto", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (isAutoField != null && isAutoField.FieldType == typeof(bool))
                    {
                        return (bool)isAutoField.GetValue(action);
                    }
                }

                if (houshiMode)
                {
                    FieldInfo autoStartField = action.GetType().GetField("autoStart", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (autoStartField != null && autoStartField.FieldType == typeof(bool))
                    {
                        return (bool)autoStartField.GetValue(action);
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private void RestorePoseAutoState(HActionBase action, PoseContinuationState continuation)
        {
            if (action == null || continuation == null || !continuation.ShouldRestoreAuto || !continuation.WasAuto)
            {
                return;
            }

            try
            {
                if (continuation.TargetSonyu)
                {
                    FieldInfo isAutoField = action.GetType().GetField("isAuto", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (isAutoField != null && isAutoField.FieldType == typeof(bool))
                    {
                        isAutoField.SetValue(action, true);
                        Log($"[pose-auto] restored sonyu auto target='{continuation.TargetName}'");
                    }

                    return;
                }

                if (continuation.TargetHoushi)
                {
                    FieldInfo autoStartField = action.GetType().GetField("autoStart", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (autoStartField != null && autoStartField.FieldType == typeof(bool))
                    {
                        autoStartField.SetValue(action, true);
                        Log($"[pose-auto] restored houshi auto target='{continuation.TargetName}'");
                    }
                }
            }
            catch (Exception ex)
            {
                LogWarn($"[pose-auto] restore failed target='{continuation.TargetName}' message={ex.Message}");
            }
        }

        private static void RestorePoseContinuationSpeed(HSceneProc proc, PoseContinuationState continuation, float minLoopSpeed)
        {
            if (proc == null || proc.flags == null || continuation == null)
            {
                return;
            }

            float speedCalc = Mathf.Clamp01(Mathf.Max(continuation.SpeedCalc, minLoopSpeed));
            proc.flags.speedCalc = speedCalc;
            proc.flags.speedMaxBody = 1f;
            proc.flags.speed = ResolvePoseContinuationSpeed(proc.flags, speedCalc);
            if (proc.flags.voice != null)
            {
                proc.flags.voice.speedMotion = continuation.VoiceSpeedMotion || speedCalc >= 0.6f;
            }
        }

        private static float ResolvePoseContinuationSpeed(HFlag flags, float speedCalc)
        {
            if (flags == null)
            {
                return speedCalc;
            }

            AnimationCurve curve = IsHoushiPoseMode(flags.mode) ? flags.speedHoushiCurve : flags.speedSonyuCurve;
            return curve != null ? curve.Evaluate(speedCalc) : speedCalc;
        }

        private static string BuildSonyuContinuationStateName(PoseContinuationState continuation, bool useAnal)
        {
            string prefix = useAnal ? "A_" : string.Empty;
            if (!continuation.WasLoop)
            {
                return prefix + "InsertIdle";
            }

            return prefix + (continuation.WasStrongLoop ? "SLoop" : "WLoop");
        }

        private static bool IsSonyuPoseMode(HFlag.EMode mode)
        {
            return mode == HFlag.EMode.sonyu || mode == HFlag.EMode.sonyu3P || mode == HFlag.EMode.sonyu3PMMF;
        }

        private static bool IsHoushiPoseMode(HFlag.EMode mode)
        {
            return mode == HFlag.EMode.houshi || mode == HFlag.EMode.houshi3P || mode == HFlag.EMode.houshi3PMMF;
        }

        private static bool IsSonyuContinuationState(string stateName)
        {
            return IsSonyuLoopState(stateName)
                || string.Equals(stateName, "Insert", StringComparison.Ordinal)
                || string.Equals(stateName, "A_Insert", StringComparison.Ordinal)
                || string.Equals(stateName, "InsertIdle", StringComparison.Ordinal)
                || string.Equals(stateName, "A_InsertIdle", StringComparison.Ordinal);
        }

        private static bool IsSonyuLoopState(string stateName)
        {
            return string.Equals(stateName, "WLoop", StringComparison.Ordinal)
                || string.Equals(stateName, "SLoop", StringComparison.Ordinal)
                || string.Equals(stateName, "A_WLoop", StringComparison.Ordinal)
                || string.Equals(stateName, "A_SLoop", StringComparison.Ordinal);
        }

        private static bool IsHoushiContinuationState(string stateName)
        {
            return IsHoushiLoopState(stateName);
        }

        private static bool IsHoushiLoopState(string stateName)
        {
            return string.Equals(stateName, "WLoop", StringComparison.Ordinal)
                || string.Equals(stateName, "SLoop", StringComparison.Ordinal);
        }

        private static bool IsStrongLoopState(string stateName)
        {
            return string.Equals(stateName, "SLoop", StringComparison.Ordinal)
                || string.Equals(stateName, "A_SLoop", StringComparison.Ordinal);
        }

        // ----------------------------------------------------------------
        // clothes コマンド: 各部位の着衣状態を変える
        // ----------------------------------------------------------------

        private void HandleClothesCommand(ExternalVoiceFaceCommand command)
        {
            if (command.clothesItems == null || command.clothesItems.Length == 0)
            {
                LogWarn("[clothes] clothesItems が空です");
                return;
            }

            ChaControl female = null;
            HSceneProc proc = FindCurrentProc();
            int main = command.ResolveMain(Settings?.TargetMainIndex ?? 0);
            if (proc != null)
            {
                int mainForResolve = ClampMainIndex(proc, main);
                female = ResolveFemale(proc, mainForResolve);
            }
            if (female == null)
            {
                female = CanonPlugins.Shared.IndependentActorFilter.FindHActors()
                    .FirstOrDefault(c => c != null && c.sex == 1 && c.visibleAll);
            }
            if (female == null)
            {
                LogWarn("[clothes] ChaControl が見つかりません");
                return;
            }

            bool anyApplied = false;
            foreach (var item in command.clothesItems)
            {
                if (item.kind < 0 || item.kind > 8) continue;
                try
                {
                    if (item.state == -2)
                    {
                        if (!ApplyShiftClothesState(female, item.kind, secondStage: true))
                        {
                            continue;
                        }
                    }
                    else if (item.state == -1)
                    {
                        if (!ApplyShiftClothesState(female, item.kind, secondStage: false))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        female.SetClothesState(item.kind, (byte)item.state);
                        Log($"[clothes] SetClothesState kind={item.kind} state={item.state}");
                    }
                    anyApplied = true;
                }
                catch (Exception ex)
                {
                    LogWarn($"[clothes] 失敗 kind={item.kind}: {ex.Message}");
                }
            }

            if (anyApplied)
                TryScheduleMorphBridgeClothesReapply("voiceface clothes", main);
        }

        private static bool TryScheduleMorphBridgeClothesReapply(string reason, int femaleIndex)
        {
            try
            {
                Type apiType = Type.GetType("MainGameCharacterMorphBridge.CharacterMorphBridgeApi, MainGameCharacterMorphBridge", throwOnError: false);
                if (apiType == null)
                    return false;

                MethodInfo method = apiType.GetMethod(
                    "ScheduleClothesChangeReapply",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(int) },
                    null);
                if (method == null)
                    return TryScheduleMorphBridgeReapply(reason, femaleIndex);

                object result = method.Invoke(null, new object[] { reason, femaleIndex });
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        private void HandleGlassesToggle(int main, bool show)
        {
            ChaControl female = ResolveFemaleForMain(main);
            if (female == null)
            {
                LogWarn("[accessory][glasses] ChaControl が見つかりません");
                return;
            }

            int[] slots = FindGlassesAccessorySlots(female);
            if (slots.Length == 0)
            {
                LogWarn("[accessory][glasses] glasses slot not found (parentKey=a_n_megane)");
                return;
            }

            int changed = 0;
            int already = 0;
            int failed = 0;

            foreach (int slot in slots)
            {
                try
                {
                    bool before = TryGetAccessoryShowState(female, slot, out bool b) ? b : false;
                    female.SetAccessoryState(slot, show);
                    bool after = TryGetAccessoryShowState(female, slot, out bool a) ? a : show;

                    if (before == show)
                    {
                        already++;
                    }
                    else if (after == show)
                    {
                        changed++;
                    }
                    else
                    {
                        failed++;
                    }

                    Log($"[accessory][glasses] slot={slot} show={(show ? 1 : 0)} before={(before ? 1 : 0)} after={(after ? 1 : 0)}");
                }
                catch (Exception ex)
                {
                    failed++;
                    LogWarn($"[accessory][glasses] failed slot={slot} show={(show ? 1 : 0)} message={ex.Message}");
                }
            }

            Log($"[accessory][glasses] apply show={(show ? 1 : 0)} slots={slots.Length} changed={changed} already={already} failed={failed}");
        }

        private ChaControl ResolveFemaleForMain(int main)
        {
            HSceneProc proc = FindCurrentProc();
            if (proc != null)
            {
                int mainForResolve = ClampMainIndex(proc, main);
                ChaControl female = ResolveFemale(proc, mainForResolve);
                if (female != null)
                {
                    return female;
                }
            }

            return CanonPlugins.Shared.IndependentActorFilter.FindHActors()
                .FirstOrDefault(c => c != null && c.sex == 1 && c.visibleAll);
        }

        private static int[] FindGlassesAccessorySlots(ChaControl female)
        {
            var parts = female?.nowCoordinate?.accessory?.parts;
            if (parts == null || parts.Length == 0)
            {
                return new int[0];
            }

            var slots = new List<int>(4);
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part == null || part.type == 120)
                {
                    continue;
                }

                string parentKey = (part.parentKey ?? string.Empty).Trim();
                if (parentKey.Equals("a_n_megane", StringComparison.OrdinalIgnoreCase) ||
                    parentKey.IndexOf("megane", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    slots.Add(i);
                }
            }

            return slots.ToArray();
        }

        private static bool TryGetAccessoryShowState(ChaControl female, int slot, out bool show)
        {
            show = false;
            try
            {
                bool[] states = female?.fileStatus?.showAccessory;
                if (states == null || slot < 0 || slot >= states.Length)
                {
                    return false;
                }

                show = states[slot];
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ApplyShiftClothesState(ChaControl female, int kind, bool secondStage)
        {
            byte currentState = 0;
            try
            {
                if (female?.fileStatus?.clothesState != null && kind >= 0 && kind < female.fileStatus.clothesState.Length)
                {
                    currentState = female.fileStatus.clothesState[kind];
                }
            }
            catch
            {
                currentState = 0;
            }

            bool hasState1 = female != null && female.IsClothesStateType(kind, 1);
            bool hasState2 = female != null && female.IsClothesStateType(kind, 2);
            if (secondStage && !hasState2)
            {
                Log($"[clothes] ShiftSecond ignored kind={kind} current={currentState} reason=state2_unavailable");
                return false;
            }
            if (!hasState1 && !hasState2)
            {
                Log($"[clothes] Shift ignored kind={kind} current={currentState} reason=shift_state_unavailable");
                return false;
            }

            byte targetState = secondStage
                ? (byte)2
                : (hasState1 ? (byte)1 : (byte)2);
            female.SetClothesState(kind, targetState, next: false);
            Log($"[clothes] Shift stage={(secondStage ? 2 : 1)} kind={kind} current={currentState} target={targetState}");
            return true;
        }

        // ----------------------------------------------------------------
        // coord コマンド: 衣装名で検索して着替える
        // ----------------------------------------------------------------

        private static readonly string[] CoordTypeNames = { "plain", "swim", "pajamas", "bathing" };
        private static readonly string[] CoordTypeJapaneseNames = { "私服", "水着", "お泊り", "風呂" };

        private static IEnumerable<string> GetCoordSearchNames(int index, string slotName)
        {
            if (!string.IsNullOrWhiteSpace(slotName)) yield return slotName;
            if (index < CoordTypeJapaneseNames.Length) yield return CoordTypeJapaneseNames[index];
        }

        private void HandleCoordCommand(ExternalVoiceFaceCommand command)
        {
            string coordName = (command.coordName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(coordName))
            {
                LogWarn("[coord] coordName が空です");
                return;
            }

            // ChaControl を取得: HScene → フォールバック
            ChaControl female = null;
            HSceneProc proc = FindCurrentProc();
            if (proc != null)
            {
                int resolvedMain = ClampMainIndex(proc, command.ResolveMain(Settings?.TargetMainIndex ?? 0));
                female = ResolveFemale(proc, resolvedMain);
            }
            if (female == null)
            {
                female = CanonPlugins.Shared.IndependentActorFilter.FindHActors()
                    .FirstOrDefault(c => c != null && c.sex == 1 && c.visibleAll);
            }
            if (female == null)
            {
                LogWarn("[coord] ChaControl が見つかりません");
                return;
            }

            int main = command.ResolveMain(Settings?.TargetMainIndex ?? 0);
            int? beforeCoord = TryGetCurrentCoordinateIndex(female);
            Log($"[coord] request name='{coordName}' main={main} before={(beforeCoord.HasValue ? beforeCoord.Value.ToString() : "unknown")}");

            string matchedSlotName;
            bool exactMatch;
            int coordIndex = TryFindCoordIndexByName(female, coordName, out matchedSlotName, out exactMatch);
            if (coordIndex < 0)
            {
                LogWarn($"[coord] '{coordName}' に一致するコーデが見つかりません");
                LogWarn($"[coord] slots: {BuildCoordSlotSummary(female)}");
                return;
            }

            try
            {
                Log($"[coord] match name='{coordName}' slot={coordIndex} slotName='{matchedSlotName}' match={(exactMatch ? "exact" : "partial")}");
                female.ChangeCoordinateTypeAndReload((ChaFileDefine.CoordinateType)coordIndex, true);
                int? immediateCoord = TryGetCurrentCoordinateIndex(female);
                Log($"[coord] apply immediate target={coordIndex} current={(immediateCoord.HasValue ? immediateCoord.Value.ToString() : "unknown")}");
                TryScheduleMorphBridgeReapply("voiceface coordinate immediate", main);
                ScheduleCoordResultCheck(female, coordName, coordIndex, main);
            }
            catch (Exception ex)
            {
                LogWarn($"[coord] ChangeCoordinateTypeAndReload 失敗: {ex.Message}");
            }
        }

        private static int TryFindCoordIndexByName(ChaControl female, string name, out string matchedSlotName, out bool exactMatch)
        {
            matchedSlotName = string.Empty;
            exactMatch = false;
            string nameLower = name.ToLowerInvariant();
            var coordSlots = female.chaFile?.coordinate;
            if (coordSlots == null) return -1;

            if (string.Equals(name, "ランダム", StringComparison.Ordinal))
            {
                int current = TryGetCurrentCoordinateIndex(female) ?? -1;
                int available = coordSlots.Length - (current >= 0 && current < coordSlots.Length ? 1 : 0);
                if (available <= 0) return -1;
                int chosen = UnityEngine.Random.Range(0, available);
                if (current >= 0 && current < coordSlots.Length && chosen >= current) chosen++;
                matchedSlotName = GetCoordSlotName(chosen, null, null);
                exactMatch = true;
                return chosen;
            }

            for (int i = 0; i < Math.Min(coordSlots.Length, CoordTypeJapaneseNames.Length); i++)
            {
                if (string.Equals(name, CoordTypeJapaneseNames[i], StringComparison.Ordinal))
                {
                    matchedSlotName = CoordTypeNames[i];
                    exactMatch = true;
                    return i;
                }
            }

            // MoreOutfitsController のリフレクション準備
            MonoBehaviour moCtrl = null;
            MethodInfo moGetName = null;
            try
            {
                moCtrl = female.gameObject.GetComponents<MonoBehaviour>()
                    .FirstOrDefault(c => c.GetType().Name == "MoreOutfitsController");
                if (moCtrl != null)
                    moGetName = moCtrl.GetType().GetMethod("GetCoodinateName",
                        BindingFlags.Public | BindingFlags.Instance);
            }
            catch { }

            // 完全一致を優先、次に部分一致
            int partialMatch = -1;
            string partialSlotName = string.Empty;
            for (int i = 0; i < coordSlots.Length; i++)
            {
                string rawSlotName = GetCoordSlotName(i, moCtrl, moGetName);
                string slotName = rawSlotName.ToLowerInvariant();
                if (slotName == nameLower)
                {
                    matchedSlotName = rawSlotName;
                    exactMatch = true;
                    return i;
                }
                if (partialMatch < 0 && (slotName.Contains(nameLower) || nameLower.Contains(slotName)))
                {
                    partialMatch = i;
                    partialSlotName = rawSlotName;
                }
            }

            matchedSlotName = partialSlotName;
            return partialMatch;
        }

        private void ScheduleCoordResultCheck(ChaControl female, string coordName, int targetIndex, int femaleIndex)
        {
            if (female == null)
            {
                return;
            }

            _actionScheduler.Schedule(Time.unscaledTime + 0.8f, () =>
            {
                int? current = TryGetCurrentCoordinateIndex(female);
                string result;
                if (!current.HasValue)
                {
                    result = "unknown";
                }
                else if (current.Value == targetIndex)
                {
                    result = "ok";
                }
                else
                {
                    result = "overwritten_or_failed";
                }

                Log($"[coord] post-check name='{coordName}' target={targetIndex} current={(current.HasValue ? current.Value.ToString() : "unknown")} result={result}");
                TryScheduleMorphBridgeReapply("voiceface coordinate post-check", femaleIndex);
            });
        }

        private static bool TryScheduleMorphBridgeReapply(string reason, int femaleIndex)
        {
            try
            {
                Type apiType = Type.GetType("MainGameCharacterMorphBridge.CharacterMorphBridgeApi, MainGameCharacterMorphBridge", throwOnError: false);
                if (apiType == null)
                    return false;

                MethodInfo method = apiType.GetMethod(
                    "ScheduleReapplyCurrentMorphState",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(int) },
                    null);
                if (method == null)
                    return false;

                object result = method.Invoke(null, new object[] { reason, femaleIndex });
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        private static int? TryGetCurrentCoordinateIndex(ChaControl female)
        {
            if (female == null)
            {
                return null;
            }

            try
            {
                object fileStatus = female.fileStatus;
                int value;
                if (TryReadIntMember(fileStatus, "coordinateType", out value)) return value;
                if (TryReadIntMember(fileStatus, "nowCoordinateType", out value)) return value;
                if (TryReadIntMember(fileStatus, "coordType", out value)) return value;
            }
            catch
            {
            }

            try
            {
                int value;
                if (TryReadIntMember(female, "coordinateType", out value)) return value;
                if (TryReadIntMember(female, "nowCoordinateType", out value)) return value;
                if (TryReadIntMember(female, "coordType", out value)) return value;
            }
            catch
            {
            }

            return null;
        }

        private static bool TryReadIntMember(object target, string memberName, out int value)
        {
            value = 0;
            if (target == null || string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            try
            {
                Type type = target.GetType();
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                PropertyInfo prop = type.GetProperty(memberName, flags);
                if (prop != null)
                {
                    object v = prop.GetValue(target, null);
                    if (v is int)
                    {
                        value = (int)v;
                        return true;
                    }
                    if (v is byte)
                    {
                        value = (byte)v;
                        return true;
                    }
                }

                FieldInfo field = type.GetField(memberName, flags);
                if (field != null)
                {
                    object v = field.GetValue(target);
                    if (v is int)
                    {
                        value = (int)v;
                        return true;
                    }
                    if (v is byte)
                    {
                        value = (byte)v;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static string BuildCoordSlotSummary(ChaControl female)
        {
            try
            {
                var coordSlots = female?.chaFile?.coordinate;
                if (coordSlots == null || coordSlots.Length == 0)
                {
                    return "(empty)";
                }

                MonoBehaviour moCtrl = null;
                MethodInfo moGetName = null;
                try
                {
                    moCtrl = female.gameObject.GetComponents<MonoBehaviour>()
                        .FirstOrDefault(c => c.GetType().Name == "MoreOutfitsController");
                    if (moCtrl != null)
                    {
                        moGetName = moCtrl.GetType().GetMethod("GetCoodinateName",
                            BindingFlags.Public | BindingFlags.Instance);
                    }
                }
                catch
                {
                }

                int max = Math.Min(coordSlots.Length, 20);
                var list = new List<string>(max);
                for (int i = 0; i < max; i++)
                {
                    string n = GetCoordSlotName(i, moCtrl, moGetName);
                    list.Add(i + ":" + n);
                }

                if (coordSlots.Length > max)
                {
                    list.Add("...");
                }

                return string.Join(", ", list.ToArray());
            }
            catch
            {
                return "(error)";
            }
        }

        private static string GetCoordSlotName(int index, MonoBehaviour moCtrl, MethodInfo moGetName)
        {
            if (index < CoordTypeNames.Length) return CoordTypeNames[index];
            if (moCtrl != null && moGetName != null)
            {
                try { return (moGetName.Invoke(moCtrl, new object[] { index }) as string) ?? index.ToString(); }
                catch { }
            }
            return index.ToString();
        }

        private void BeginExternalVoiceGuardCore(HSceneProc proc, ChaControl female, int main)
        {
            var s = Settings;
            if (s != null)
            {
                float until = Time.unscaledTime + s.ExternalPlayPreBlockSeconds;
                _voiceGuardState.ExtendBlockUntil(until);
            }

            TryStopKissAction(proc, main, "external-play-start");

            if (s == null || !s.StopGameVoiceBeforeExternalPlay)
            {
                return;
            }

            TryStopGameVoice(proc, female, main);
        }

        private void TryStopKissAction(HSceneProc proc, int main, string reason)
        {
            try
            {
                HandCtrl hand = ResolveHand(proc, main);
                if (hand == null)
                {
                    return;
                }

                if (!hand.IsKissAction())
                {
                    return;
                }

                bool result = hand.ForceFinish();
                Log(
                    "[kiss-block] force-finish active kiss"
                    + " main=" + main
                    + " result=" + result
                    + " reason=" + reason);
            }
            catch (Exception ex)
            {
                LogWarn("[kiss-block] force-finish failed main=" + main + " reason=" + reason + " ex=" + ex.Message);
            }
        }

        private static HandCtrl ResolveHand(HSceneProc proc, int main)
        {
            if (proc == null)
            {
                return null;
            }

            if (main <= 0)
            {
                return proc.hand;
            }

            return proc.hand1 ?? proc.hand;
        }

        private void TryStopGameVoice(HSceneProc proc, ChaControl female, int main)
        {
            try
            {
                int targets = 0;
                int stoppedTargets = 0;
                int residualBefore = 0;
                int residualAfter = 0;

                if (proc != null && proc.flags != null && proc.flags.transVoiceMouth != null)
                {
                    for (int i = 0; i < proc.flags.transVoiceMouth.Length; i++)
                    {
                        Transform voiceTrans = proc.flags.transVoiceMouth[i];
                        if (voiceTrans == null)
                        {
                            continue;
                        }

                        targets++;
                        bool wasPlaying = Manager.Voice.IsPlay(voiceTrans);
                        if (wasPlaying)
                        {
                            residualBefore++;
                        }

                        int voiceNo = -1;
                        if (proc.flags.lstHeroine != null && i >= 0 && i < proc.flags.lstHeroine.Count && proc.flags.lstHeroine[i] != null)
                        {
                            voiceNo = proc.flags.lstHeroine[i].voiceNo;
                        }

                        if (voiceNo >= 0)
                        {
                            Manager.Voice.Stop(voiceNo, voiceTrans);
                        }

                        Manager.Voice.Stop(voiceTrans);

                        if (Manager.Voice.IsPlay(voiceTrans))
                        {
                            if (voiceNo >= 0)
                            {
                                Manager.Voice.Stop(voiceNo, voiceTrans);
                            }
                            Manager.Voice.Stop(voiceTrans);
                        }

                        if (!Manager.Voice.IsPlay(voiceTrans))
                        {
                            stoppedTargets++;
                        }
                        else
                        {
                            residualAfter++;
                        }
                    }
                }

                if (female != null)
                {
                    Manager.Voice.Stop(female.transform);
                    ClearLipSyncSource(female);
                }

                if (proc != null && proc.voice != null)
                {
                    // 捕獲した値がアストラル由来(=AstralModeが毎フレーム立てている true)かを記録。
                    // 復元時に、アストラルを抜けた後の stale な true 復元を防ぐ判定に使う。
                    if (!_voiceGuardState.ProcStopOverridden)
                        _voiceGuardState.CaptureProcStop(proc.voice.isVoicePrcoStop, IsAstralModeActive());

                    proc.voice.isVoicePrcoStop = true;
                }

                if (residualAfter > 0)
                {
                    Manager.Voice.StopAll(false);
                }

                bool anyPlayingAfter = Manager.Voice.IsPlay();
                Log(
                    "[guard] force-stop game voice before external play"
                    + " main=" + main
                    + " targets=" + targets
                    + " residualBefore=" + residualBefore
                    + " stoppedTargets=" + stoppedTargets
                    + " residualAfter=" + residualAfter
                    + " anyPlayingAfter=" + anyPlayingAfter);
            }
            catch (Exception ex)
            {
                LogWarn("[guard] stop game voice failed: " + ex.Message);
            }
        }

        private static void ClearLipSyncSource(ChaControl female)
        {
            if (female == null)
            {
                return;
            }

            try
            {
                female.SetLipSync(null);
            }
            catch
            {
            }
        }

        private static float ResolveExternalAudioDefaultVolume(PluginSettings settings)
        {
            if (settings == null)
            {
                return 1f;
            }

            return settings.ResolveFemalePlaybackVolume();
        }

        private void LogGuardSettings(string reason)
        {
            var s = Settings;
            if (s == null)
            {
                return;
            }

            Log(
                "[guard] settings"
                + " reason=" + reason
                + " stopBefore=" + s.StopGameVoiceBeforeExternalPlay
                + " blockWhilePlay=" + s.BlockGameVoiceWhileExternalPlaying
                + " preBlockSec=" + s.ExternalPlayPreBlockSeconds
                + " vol=" + s.PlaybackVolume
                + " femaleVol=" + s.FemalePlaybackVolume
                + " extPitch=" + s.ExternalPlaybackPitch);
        }

        private void RestoreVoiceProcStopIfNeeded()
        {
            if (!_voiceGuardState.ProcStopOverridden)
            {
                return;
            }

            HSceneProc proc = CurrentProc ?? FindCurrentProc();
            if (proc == null || proc.voice == null)
            {
                float now = Time.unscaledTime;
                if (_voiceGuardState.ShouldLogRestorePending(now))
                {
                    LogWarn(
                        "[guard] restore pending: keep override because "
                        + (proc == null ? "proc is null" : "proc.voice is null"));
                }

                return;
            }

            try
            {
                // 捕獲した true がアストラル由来で、かつ既にアストラルを抜けているなら、
                // それを復元すると現実で音声が止まったままになる(stale)。この時だけ false に矯正する。
                // それ以外はゲーム本来の値を尊重して捕獲値を復元（バグ以外は従来挙動と同一）。
                bool restoreValue = _voiceGuardState.ResolveRestoreValue(IsAstralModeActive(), out bool staleAstralTrue);

                bool before = proc.voice.isVoicePrcoStop;
                proc.voice.isVoicePrcoStop = restoreValue;
                bool after = proc.voice.isVoicePrcoStop;
                Log(
                    "[guard] restore applied"
                    + " before=" + before
                    + " after=" + after
                    + " original=" + _voiceGuardState.ProcStopOriginal
                    + " capturedAstral=" + _voiceGuardState.ProcStopCapturedAstral
                    + " staleAstralTrue=" + staleAstralTrue);
                _voiceGuardState.CompleteRestore();
            }
            catch (Exception ex)
            {
                float now = Time.unscaledTime;
                if (_voiceGuardState.ShouldLogRestorePending(now))
                {
                    LogWarn("[guard] restore failed: " + ex.Message);
                }
            }
        }

        private string NormalizeAudioPath(string incomingPath)
        {
            if (string.IsNullOrWhiteSpace(incomingPath))
            {
                return string.Empty;
            }

            try
            {
                string path = incomingPath.Trim().Trim('"');
                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(PluginDir ?? string.Empty, path);
                }

                return Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                LogWarn("normalize audio path failed: " + ex.Message);
                return string.Empty;
            }
        }

        private static string ResolveFacePresetJsonPath(PluginSettings settings)
        {
            string configuredPath = settings == null || string.IsNullOrWhiteSpace(settings.FacePresetJsonRelativePath)
                ? DefaultFacePresetJsonRelativePath
                : settings.FacePresetJsonRelativePath.Trim();

            try
            {
                if (Path.IsPathRooted(configuredPath))
                {
                    return Path.GetFullPath(configuredPath);
                }

                return Path.GetFullPath(Path.Combine(PluginDir ?? string.Empty, configuredPath));
            }
            catch
            {
                return configuredPath;
            }
        }

        private bool TryLoadFacePresetItems(string sourcePath, out List<FacePresetJsonItem> items, out string reason)
        {
            items = new List<FacePresetJsonItem>();
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                reason = "path_empty";
                return false;
            }

            try
            {
                var info = new FileInfo(sourcePath);
                if (!info.Exists)
                {
                    reason = "file_not_found";
                    return false;
                }

                DateTime writeTime = info.LastWriteTimeUtc;
                long length = info.Length;

                lock (_facePresetCacheLock)
                {
                    bool cacheHit =
                        string.Equals(_facePresetCachePath, sourcePath, StringComparison.OrdinalIgnoreCase)
                        && _facePresetCacheWriteTimeUtc == writeTime
                        && _facePresetCacheLength == length
                        && _facePresetCache != null
                        && _facePresetCache.Count > 0;
                    if (cacheHit)
                    {
                        items = _facePresetCache.Select(CloneFacePresetItem).Where(p => p != null).ToList();
                        reason = "cache_hit";
                        return items.Count > 0;
                    }
                }

                string json = File.ReadAllText(sourcePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    reason = "json_empty";
                    return false;
                }

                FacePresetJsonFile parsed = DeserializeFacePresetJson(json);
                if (parsed == null || parsed.Presets == null)
                {
                    reason = "parse_null";
                    return false;
                }

                var normalized = new List<FacePresetJsonItem>(parsed.Presets.Count);
                for (int i = 0; i < parsed.Presets.Count; i++)
                {
                    FacePresetJsonItem normalizedItem = NormalizeFacePresetItem(parsed.Presets[i]);
                    if (normalizedItem == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(normalizedItem.Name) && string.IsNullOrWhiteSpace(normalizedItem.Id))
                    {
                        continue;
                    }

                    normalized.Add(normalizedItem);
                }

                if (normalized.Count <= 0)
                {
                    reason = "preset_empty";
                    return false;
                }

                lock (_facePresetCacheLock)
                {
                    _facePresetCachePath = sourcePath;
                    _facePresetCacheWriteTimeUtc = writeTime;
                    _facePresetCacheLength = length;
                    _facePresetCache = normalized.Select(CloneFacePresetItem).Where(p => p != null).ToList();
                }

                items = normalized.Select(CloneFacePresetItem).Where(p => p != null).ToList();
                reason = "loaded";
                return items.Count > 0;
            }
            catch (Exception ex)
            {
                reason = "load_exception:" + ex.Message;
                return false;
            }
        }

        private static FacePresetJsonFile DeserializeFacePresetJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                var serializer = new DataContractJsonSerializer(typeof(FacePresetJsonFile));
                return serializer.ReadObject(ms) as FacePresetJsonFile;
            }
        }

        private static FacePresetJsonItem NormalizeFacePresetItem(FacePresetJsonItem source)
        {
            if (source == null)
            {
                return null;
            }

            var normalized = new FacePresetJsonItem
            {
                Id = (source.Id ?? string.Empty).Trim(),
                Name = (source.Name ?? string.Empty).Trim(),
                Eyebrow = Mathf.Max(0, source.Eyebrow),
                Eye = Mathf.Max(0, source.Eye),
                Mouth = Mathf.Max(0, source.Mouth),
                EyeMin = Mathf.Clamp01(source.EyeMin),
                MouthMin = Mathf.Clamp01(source.MouthMin),
                Tears = Mathf.Clamp(source.Tears, 0, 10),
                Cheek = Mathf.Clamp01(source.Cheek)
            };

            normalized.EyeMax = Mathf.Clamp01(Mathf.Max(normalized.EyeMin, source.EyeMax));
            normalized.MouthMax = Mathf.Clamp01(Mathf.Max(normalized.MouthMin, source.MouthMax));
            return normalized;
        }

        private static FacePresetJsonItem CloneFacePresetItem(FacePresetJsonItem source)
        {
            if (source == null)
            {
                return null;
            }

            return new FacePresetJsonItem
            {
                Id = source.Id,
                Name = source.Name,
                Eyebrow = source.Eyebrow,
                Eye = source.Eye,
                Mouth = source.Mouth,
                EyeMin = source.EyeMin,
                EyeMax = source.EyeMax,
                MouthMin = source.MouthMin,
                MouthMax = source.MouthMax,
                Tears = source.Tears,
                Cheek = source.Cheek
            };
        }

        private bool TrySelectFacePresetByRoute(
            PluginSettings settings,
            string requestedName,
            string requestedId,
            bool facePresetRandom,
            out FacePresetJsonItem selected,
            out string sourcePath,
            out int poolCount,
            out int candidateCount,
            out string reason)
        {
            selected = null;
            sourcePath = ResolveFacePresetJsonPath(settings);
            poolCount = 0;
            candidateCount = 0;
            reason = string.Empty;

            List<FacePresetJsonItem> allPresets;
            string loadReason;
            if (!TryLoadFacePresetItems(sourcePath, out allPresets, out loadReason))
            {
                reason = "load_failed:" + loadReason;
                return false;
            }

            poolCount = allPresets.Count;
            string name = (requestedName ?? string.Empty).Trim();
            string id = (requestedId ?? string.Empty).Trim();

            var candidates = new List<FacePresetJsonItem>();
            if (!string.IsNullOrWhiteSpace(id))
            {
                for (int i = 0; i < allPresets.Count; i++)
                {
                    FacePresetJsonItem item = allPresets[i];
                    if (item != null && string.Equals(item.Id ?? string.Empty, id, StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add(item);
                    }
                }
            }

            if (candidates.Count <= 0 && !string.IsNullOrWhiteSpace(name))
            {
                for (int i = 0; i < allPresets.Count; i++)
                {
                    FacePresetJsonItem item = allPresets[i];
                    if (item != null && string.Equals(item.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add(item);
                    }
                }
            }

            if (candidates.Count <= 0 && facePresetRandom && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(id))
            {
                candidates.AddRange(allPresets);
            }

            candidateCount = candidates.Count;
            if (candidateCount <= 0)
            {
                reason = "candidate_not_found";
                return false;
            }

            int index = 0;
            if (facePresetRandom && candidateCount > 1)
            {
                lock (_random)
                {
                    index = _random.Next(candidateCount);
                }
            }

            selected = CloneFacePresetItem(candidates[index]);
            if (selected == null)
            {
                reason = "candidate_null";
                return false;
            }

            reason = "selected";
            return true;
        }

        private bool TryApplyFacePresetByRoute(
            PluginSettings settings,
            ChaControl female,
            string requestedName,
            string requestedId,
            bool facePresetRandom,
            out FacePresetJsonItem selected,
            out string sourcePath,
            out int poolCount,
            out int candidateCount,
            out string reason)
        {
            selected = null;
            sourcePath = ResolveFacePresetJsonPath(settings);
            poolCount = 0;
            candidateCount = 0;
            reason = string.Empty;

            if (female == null)
            {
                reason = "female_null";
                return false;
            }

            FacePresetJsonItem resolvedPreset;
            if (!TrySelectFacePresetByRoute(
                    settings,
                    requestedName,
                    requestedId,
                    facePresetRandom,
                    out resolvedPreset,
                    out sourcePath,
                    out poolCount,
                    out candidateCount,
                    out reason))
            {
                return false;
            }

            try
            {
                ApplyFacePresetToCha(female, resolvedPreset);
                selected = resolvedPreset;
                reason = "applied";
                return true;
            }
            catch (Exception ex)
            {
                selected = resolvedPreset;
                reason = "apply_exception:" + ex.Message;
                return false;
            }
        }

        private static void ApplyFacePresetToCha(ChaControl cha, FacePresetJsonItem preset)
        {
            if (cha == null || preset == null)
            {
                return;
            }

            cha.ChangeEyebrowPtn(preset.Eyebrow, true);
            cha.ChangeEyesPtn(preset.Eye, true);
            cha.ChangeMouthPtn(preset.Mouth, true);

            float eyeMin = Mathf.Clamp01(preset.EyeMin);
            float eyeMax = Mathf.Clamp01(Mathf.Max(eyeMin, preset.EyeMax));
            cha.ChangeEyesOpenMax(eyeMax);
            SetFaceCtrlMinMax(cha, "eyesCtrl", eyeMin, eyeMax);

            float mouthMin = Mathf.Clamp01(preset.MouthMin);
            float mouthMax = Mathf.Clamp01(Mathf.Max(mouthMin, preset.MouthMax));
            cha.ChangeMouthOpenMax(mouthMax);
            SetFaceCtrlMinMax(cha, "mouthCtrl", mouthMin, mouthMax);

            if (cha.mouthCtrl != null)
            {
                cha.mouthCtrl.FixedRate = mouthMin;
            }

            cha.tearsLv = (byte)Mathf.Clamp(preset.Tears, 0, 10);
            cha.ChangeHohoAkaRate(Mathf.Clamp01(preset.Cheek));
        }

        internal void OnFacePresetProbeMutatorCalled(ChaControl target, string methodName, string arg0)
        {
            var probe = _facePresetProbe;
            if (probe == null || target == null || probe.Target == null)
            {
                return;
            }

            if (!ReferenceEquals(target, probe.Target))
            {
                return;
            }

            int frame = Time.frameCount;
            if (frame > probe.ExpireFrame)
            {
                return;
            }

            string caller = ResolveFacePresetProbeCaller();
            string hookKey = methodName + "|" + arg0 + "|" + caller;
            if (probe.LastHookFrame == frame && string.Equals(probe.LastHookKey, hookKey, StringComparison.Ordinal))
            {
                return;
            }

            probe.LastHookFrame = frame;
            probe.LastHookKey = hookKey;

            if (probe.HookLogCount >= 40)
            {
                return;
            }

            probe.HookLogCount++;
            Log(
                "[cmd][face-preset][probe-call]"
                + " id=" + probe.ProbeId
                + " frame=+" + (frame - probe.StartFrame)
                + " method=" + (string.IsNullOrWhiteSpace(methodName) ? "(unknown)" : methodName)
                + " arg0=" + (string.IsNullOrWhiteSpace(arg0) ? "(empty)" : arg0)
                + " caller=" + caller);
        }

        private void StartFacePresetProbe(
            ChaControl target,
            string requestedName,
            string requestedId,
            bool requestedRandom,
            string selectedName,
            string selectedId)
        {
            if (!RuntimeLoggingEnabled) return;
            if (target == null)
            {
                _facePresetProbe = null;
                return;
            }

            FaceSnapshot baseline = CaptureFaceSnapshot(target);
            if (baseline == null)
            {
                _facePresetProbe = null;
                LogWarn("[cmd][face-preset][probe] start failed reason=snapshot_null");
                return;
            }

            int probeId = Interlocked.Increment(ref _facePresetProbeSequence);
            int startFrame = Time.frameCount;
            _facePresetProbe = new FacePresetProbeState
            {
                ProbeId = probeId,
                Target = target,
                RequestedName = requestedName ?? string.Empty,
                RequestedId = requestedId ?? string.Empty,
                RequestedRandom = requestedRandom,
                SelectedName = selectedName ?? string.Empty,
                SelectedId = selectedId ?? string.Empty,
                StartFrame = startFrame,
                ExpireFrame = startFrame + 240,
                StartTime = Time.realtimeSinceStartup,
                Baseline = baseline,
                Last = baseline,
                ChangeLogCount = 0,
                HookLogCount = 0,
                LastHookFrame = -1,
                LastHookKey = string.Empty
            };

            Log(
                "[cmd][face-preset][probe] start"
                + " id=" + probeId
                + " requestedName=" + (string.IsNullOrWhiteSpace(requestedName) ? "(empty)" : requestedName)
                + " requestedId=" + (string.IsNullOrWhiteSpace(requestedId) ? "(empty)" : requestedId)
                + " requestedRandom=" + requestedRandom
                + " selectedName=" + (string.IsNullOrWhiteSpace(selectedName) ? "(empty)" : selectedName)
                + " selectedId=" + (string.IsNullOrWhiteSpace(selectedId) ? "(empty)" : selectedId)
                + " baseline=" + FormatFaceSnapshot(baseline));
        }

        private void UpdateFacePresetProbe()
        {
            var probe = _facePresetProbe;
            if (probe == null)
            {
                return;
            }

            if (probe.Target == null)
            {
                LogWarn(
                    "[cmd][face-preset][probe] end"
                    + " id=" + probe.ProbeId
                    + " reason=target_null");
                _facePresetProbe = null;
                return;
            }

            int frame = Time.frameCount;
            int elapsedFrame = frame - probe.StartFrame;
            if (frame > probe.ExpireFrame)
            {
                FaceSnapshot finalSnapshot = CaptureFaceSnapshot(probe.Target);
                string finalDiff = DescribeFaceSnapshotDiff(probe.Baseline, finalSnapshot);
                Log(
                    "[cmd][face-preset][probe] end"
                    + " id=" + probe.ProbeId
                    + " reason=expired"
                    + " frames=" + elapsedFrame
                    + " changes=" + probe.ChangeLogCount
                    + " hookCalls=" + probe.HookLogCount
                    + " final=" + FormatFaceSnapshot(finalSnapshot)
                    + " baselineDiff=" + (string.IsNullOrWhiteSpace(finalDiff) ? "none" : finalDiff));
                _facePresetProbe = null;
                return;
            }

            FaceSnapshot current = CaptureFaceSnapshot(probe.Target);
            if (current == null)
            {
                return;
            }

            if (!AreFaceSnapshotsEqual(current, probe.Last))
            {
                probe.ChangeLogCount++;
                string delta = DescribeFaceSnapshotDiff(probe.Last, current);
                string baselineDiff = DescribeFaceSnapshotDiff(probe.Baseline, current);
                LogWarn(
                    "[cmd][face-preset][probe] changed"
                    + " id=" + probe.ProbeId
                    + " frame=+" + elapsedFrame
                    + " delta=" + (string.IsNullOrWhiteSpace(delta) ? "none" : delta)
                    + " baselineDiff=" + (string.IsNullOrWhiteSpace(baselineDiff) ? "none" : baselineDiff)
                    + " state=" + FormatFaceSnapshot(current));
                probe.Last = current;
                return;
            }

            if (elapsedFrame == 1 || elapsedFrame == 10 || elapsedFrame == 30 || elapsedFrame == 60 || elapsedFrame == 120 || elapsedFrame == 180)
            {
                Log(
                    "[cmd][face-preset][probe] stable"
                    + " id=" + probe.ProbeId
                    + " frame=+" + elapsedFrame
                    + " state=" + FormatFaceSnapshot(current));
            }
        }

        private static FaceSnapshot CaptureFaceSnapshot(ChaControl cha)
        {
            if (cha == null || cha.fileStatus == null)
            {
                return null;
            }

            float eyesMin = 0f;
            float eyesMax = cha.fileStatus.eyesOpenMax;
            TryReadFaceCtrlMinMax(cha, "eyesCtrl", ref eyesMin, ref eyesMax);

            float mouthMin = 0f;
            float mouthMax = cha.fileStatus.mouthOpenMax;
            TryReadFaceCtrlMinMax(cha, "mouthCtrl", ref mouthMin, ref mouthMax);

            return new FaceSnapshot
            {
                Eyebrow = cha.fileStatus.eyebrowPtn,
                Eyes = cha.fileStatus.eyesPtn,
                Mouth = cha.fileStatus.mouthPtn,
                EyesOpenMin = eyesMin,
                EyesOpenMax = eyesMax,
                MouthOpenMin = mouthMin,
                MouthOpenMax = mouthMax,
                MouthFixedRate = cha.mouthCtrl != null ? cha.mouthCtrl.FixedRate : float.NaN,
                Tears = cha.tearsLv,
                Cheek = cha.fileStatus.hohoAkaRate
            };
        }

        private static void TryReadFaceCtrlMinMax(ChaControl cha, string ctrlName, ref float min, ref float max)
        {
            if (cha == null || string.IsNullOrWhiteSpace(ctrlName))
            {
                return;
            }

            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Type chaType = cha.GetType();
                PropertyInfo ctrlProp = chaType.GetProperty(ctrlName, flags);
                FieldInfo ctrlField = chaType.GetField(ctrlName, flags);
                object ctrl = ctrlProp != null ? ctrlProp.GetValue(cha, null) : ctrlField?.GetValue(cha);
                if (ctrl == null)
                {
                    return;
                }

                Type ctrlType = ctrl.GetType();
                FieldInfo minField = ctrlType.GetField("OpenMin", BindingFlags.Instance | BindingFlags.Public);
                FieldInfo maxField = ctrlType.GetField("OpenMax", BindingFlags.Instance | BindingFlags.Public);
                if (minField != null)
                {
                    min = Convert.ToSingle(minField.GetValue(ctrl));
                }

                if (maxField != null)
                {
                    max = Convert.ToSingle(maxField.GetValue(ctrl));
                }
            }
            catch
            {
            }
        }

        private static bool AreFaceSnapshotsEqual(FaceSnapshot a, FaceSnapshot b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            return a.Eyebrow == b.Eyebrow
                && a.Eyes == b.Eyes
                && a.Mouth == b.Mouth
                && NearlyEqual(a.EyesOpenMin, b.EyesOpenMin)
                && NearlyEqual(a.EyesOpenMax, b.EyesOpenMax)
                && NearlyEqual(a.MouthOpenMin, b.MouthOpenMin)
                && NearlyEqual(a.MouthOpenMax, b.MouthOpenMax)
                && NearlyEqual(a.MouthFixedRate, b.MouthFixedRate)
                && a.Tears == b.Tears
                && NearlyEqual(a.Cheek, b.Cheek);
        }

        private static bool NearlyEqual(float a, float b)
        {
            if (float.IsNaN(a) && float.IsNaN(b))
            {
                return true;
            }

            return Mathf.Abs(a - b) <= 0.0005f;
        }

        private static string DescribeFaceSnapshotDiff(FaceSnapshot from, FaceSnapshot to)
        {
            if (from == null || to == null)
            {
                return "(snapshot-null)";
            }

            var parts = new List<string>(10);
            if (from.Eyebrow != to.Eyebrow) parts.Add("eyebrow:" + from.Eyebrow + "->" + to.Eyebrow);
            if (from.Eyes != to.Eyes) parts.Add("eyes:" + from.Eyes + "->" + to.Eyes);
            if (from.Mouth != to.Mouth) parts.Add("mouth:" + from.Mouth + "->" + to.Mouth);
            if (!NearlyEqual(from.EyesOpenMin, to.EyesOpenMin)) parts.Add("eyesMin:" + Round3(from.EyesOpenMin) + "->" + Round3(to.EyesOpenMin));
            if (!NearlyEqual(from.EyesOpenMax, to.EyesOpenMax)) parts.Add("eyesMax:" + Round3(from.EyesOpenMax) + "->" + Round3(to.EyesOpenMax));
            if (!NearlyEqual(from.MouthOpenMin, to.MouthOpenMin)) parts.Add("mouthMin:" + Round3(from.MouthOpenMin) + "->" + Round3(to.MouthOpenMin));
            if (!NearlyEqual(from.MouthOpenMax, to.MouthOpenMax)) parts.Add("mouthMax:" + Round3(from.MouthOpenMax) + "->" + Round3(to.MouthOpenMax));
            if (!NearlyEqual(from.MouthFixedRate, to.MouthFixedRate)) parts.Add("mouthFixed:" + Round3(from.MouthFixedRate) + "->" + Round3(to.MouthFixedRate));
            if (from.Tears != to.Tears) parts.Add("tears:" + from.Tears + "->" + to.Tears);
            if (!NearlyEqual(from.Cheek, to.Cheek)) parts.Add("cheek:" + Round3(from.Cheek) + "->" + Round3(to.Cheek));
            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : string.Empty;
        }

        private static string FormatFaceSnapshot(FaceSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "(null)";
            }

            return "eyebrow=" + snapshot.Eyebrow
                + " eyes=" + snapshot.Eyes
                + " mouth=" + snapshot.Mouth
                + " eyesMin=" + Round3(snapshot.EyesOpenMin)
                + " eyesMax=" + Round3(snapshot.EyesOpenMax)
                + " mouthMin=" + Round3(snapshot.MouthOpenMin)
                + " mouthMax=" + Round3(snapshot.MouthOpenMax)
                + " mouthFixed=" + Round3(snapshot.MouthFixedRate)
                + " tears=" + snapshot.Tears
                + " cheek=" + Round3(snapshot.Cheek);
        }

        private static string Round3(float value)
        {
            if (float.IsNaN(value))
            {
                return "NaN";
            }

            return Math.Round(value, 3, MidpointRounding.AwayFromZero).ToString("0.###");
        }

        private static string ResolveFacePresetProbeCaller()
        {
            try
            {
                var trace = new System.Diagnostics.StackTrace(2, false);
                for (int i = 0; i < trace.FrameCount; i++)
                {
                    var method = trace.GetFrame(i)?.GetMethod();
                    if (method == null)
                    {
                        continue;
                    }

                    Type declaringType = method.DeclaringType;
                    string fullName = declaringType != null ? declaringType.FullName : string.Empty;
                    string asmName = declaringType != null && declaringType.Assembly != null
                        ? declaringType.Assembly.GetName().Name
                        : string.Empty;

                    if (string.Equals(asmName, "MainGameVoiceFaceEventBridge", StringComparison.Ordinal)
                        || string.Equals(asmName, "0Harmony", StringComparison.Ordinal)
                        || string.Equals(asmName, "HarmonyXInterop", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(fullName)
                        && fullName.IndexOf("Harmony", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    return (string.IsNullOrWhiteSpace(asmName) ? "(asm?)" : asmName)
                        + ":"
                        + (string.IsNullOrWhiteSpace(fullName) ? "(type?)" : fullName)
                        + "."
                        + method.Name;
                }
            }
            catch
            {
            }

            return "(unknown)";
        }

        private static void SetFaceCtrlMinMax(ChaControl cha, string ctrlName, float min, float max)
        {
            if (cha == null || string.IsNullOrWhiteSpace(ctrlName))
            {
                return;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type chaType = cha.GetType();
            PropertyInfo ctrlProp = chaType.GetProperty(ctrlName, flags);
            FieldInfo ctrlField = chaType.GetField(ctrlName, flags);
            object ctrl = ctrlProp != null ? ctrlProp.GetValue(cha, null) : ctrlField?.GetValue(cha);
            if (ctrl == null)
            {
                return;
            }

            Type ctrlType = ctrl.GetType();
            FieldInfo minField = ctrlType.GetField("OpenMin", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo maxField = ctrlType.GetField("OpenMax", BindingFlags.Instance | BindingFlags.Public);
            if (minField != null)
            {
                minField.SetValue(ctrl, min);
            }

            if (maxField != null)
            {
                maxField.SetValue(ctrl, max);
            }
        }

        private bool TryApplyFace(HSceneProc proc, int main, ChaControl female, int face, int voiceKind, int action)
        {
            if (face < 0)
            {
                return true;
            }

            object faceCtrl = GetFaceCtrlByFemaleIndex(proc, main);
            if (faceCtrl == null)
            {
                LogWarn("[cmd] faceCtrl not found main=" + main);
                return false;
            }

            try
            {
                var setFaceMethod = AccessTools.Method(faceCtrl.GetType(), "SetFace", new[] { typeof(int), typeof(ChaControl), typeof(int), typeof(int) });
                if (setFaceMethod == null)
                {
                    LogWarn("[cmd] SetFace method not found on " + faceCtrl.GetType().FullName);
                    return false;
                }

                object resultObj = setFaceMethod.Invoke(faceCtrl, new object[] { face, female, voiceKind, action });
                bool ok = resultObj is bool && (bool)resultObj;
                if (!ok)
                {
                    LogWarn("[cmd] SetFace failed face=" + face + " main=" + main);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[cmd] SetFace exception: " + ex.Message);
                return false;
            }
        }

        private static object GetFaceCtrlByFemaleIndex(HSceneProc proc, int femaleIndex)
        {
            if (proc == null)
            {
                return null;
            }

            object face1 = Face1Field?.GetValue(proc);
            if (femaleIndex == 1 && face1 != null)
            {
                return face1;
            }

            return FaceField?.GetValue(proc);
        }

        private int ResolveFace(
            ExternalVoiceFaceCommand command,
            PluginSettings settings,
            out bool keepCurrentFaceMode)
        {
            keepCurrentFaceMode = command.ResolveKeepCurrentFace(settings.KeepCurrentFaceByDefault);
            if (keepCurrentFaceMode)
            {
                return -1;
            }

            int directFace = command.ResolveFace(-1);
            if (directFace >= 0)
            {
                return directFace;
            }

            int randomFromCommand = TrySelectRandomFace(command.faces);
            if (randomFromCommand >= 0)
            {
                return randomFromCommand;
            }

            if (settings.DefaultFace >= 0)
            {
                return settings.DefaultFace;
            }

            int randomFromSettings = TrySelectRandomFace(settings.RandomFaceCandidates);
            if (randomFromSettings >= 0)
            {
                return randomFromSettings;
            }

            return 0;
        }

        private int TrySelectRandomFace(int[] source)
        {
            if (source == null || source.Length == 0)
            {
                return -1;
            }

            int[] valid = new int[source.Length];
            int count = 0;
            for (int i = 0; i < source.Length; i++)
            {
                int face = source[i];
                if (face < 0)
                {
                    continue;
                }

                valid[count] = face;
                count++;
            }

            if (count <= 0)
            {
                return -1;
            }

            lock (_random)
            {
                return valid[_random.Next(count)];
            }
        }

        private void HandleCommand(ExternalVoiceFaceCommand command)
        {
            var settings = Settings;
            if (command == null || settings == null)
            {
                return;
            }

            if (command.IsStop())
            {
                SetKaleidoscopeThought(false);
                _thoughtMarkedForSession = false;
                _externalVoicePlayer?.Stop("external stop");
                _voiceGuardState.ClearBlock();
                RestoreVoiceProcStopIfNeeded();
                _sequenceSessions.Deactivate();
                ClearDelayedActions("stop_command");
                ResetResponseTextQueue("stop_command");
                return;
            }

            if (string.Equals(command.type, "coord", StringComparison.OrdinalIgnoreCase))
            {
                HandleCoordCommand(command);
                return;
            }

            if (string.Equals(command.type, "clothes", StringComparison.OrdinalIgnoreCase))
            {
                HandleClothesCommand(command);
                return;
            }

            if (string.Equals(command.type, "response_text", StringComparison.OrdinalIgnoreCase))
            {
                EnqueueResponseTextCommand(command);
                return;
            }

            if (string.Equals(command.type, "pose", StringComparison.OrdinalIgnoreCase))
            {
                HandlePoseCommand(command);
                return;
            }

            if (string.Equals(command.type, "discord_pose", StringComparison.OrdinalIgnoreCase))
            {
                HandleDiscordPoseCommand(command);
                return;
            }

            if (string.Equals(command.type, "semantic_pose", StringComparison.OrdinalIgnoreCase))
            {
                HandleSemanticPoseCommand(command);
                return;
            }

            if (string.Equals(command.type, "camera_preset", StringComparison.OrdinalIgnoreCase))
            {
                HandleCameraPresetCommand(command);
                return;
            }

            HSceneProc proc = FindCurrentProc();
            if (proc == null)
            {
                if (settings.IgnoreCommandOutsideHScene)
                {
                    if (settings.VerboseLog)
                    {
                        LogWarn("[cmd] dropped because H scene is inactive result=dropped_hscene_inactive");
                    }
                    return;
                }

                LogWarn("[cmd] HSceneProc not found result=error_hsceneproc_not_found");
                return;
            }

            int requestedMain = command.ResolveMain(settings.TargetMainIndex);
            int main = ClampMainIndex(proc, requestedMain);
            ChaControl female = ResolveFemale(proc, main);
            if (female == null)
            {
                LogWarn("[cmd] female not found main=" + main + " result=error_female_not_found");
                return;
            }

            bool keepCurrentFaceMode;
            int face = ResolveFace(command, settings, out keepCurrentFaceMode);
            string facePresetId = (command.ResolveFacePresetId() ?? string.Empty).Trim();
            string facePresetName = (command.ResolveFacePresetName() ?? string.Empty).Trim();
            bool facePresetRandom = command.ResolveFacePresetRandom(false);
            bool hasFacePresetRouting =
                !string.IsNullOrWhiteSpace(facePresetId)
                || !string.IsNullOrWhiteSpace(facePresetName)
                || facePresetRandom;
            string facePresetRouteLog =
                " facePresetName=" + (string.IsNullOrWhiteSpace(facePresetName) ? "(empty)" : facePresetName)
                + " facePresetId=" + (string.IsNullOrWhiteSpace(facePresetId) ? "(empty)" : facePresetId)
                + " facePresetRandom=" + facePresetRandom;
            string facePresetApplyResult = "not_requested";
            string facePresetSelectedName = string.Empty;
            string facePresetSelectedId = string.Empty;
            string facePresetSourcePath = string.Empty;
            int facePresetPoolCount = 0;
            int facePresetCandidateCount = 0;
            if (hasFacePresetRouting)
            {
                keepCurrentFaceMode = true;
                face = -1;
                Log(
                    "[cmd][face-preset] received"
                    + facePresetRouteLog
                    + " applyEnabled=" + settings.EnableFacePresetApply
                    + " result=accepted");

                if (!settings.EnableFacePresetApply)
                {
                    facePresetApplyResult = "disabled_by_config";
                    LogWarn(
                        "[cmd][face-preset] apply skipped"
                        + facePresetRouteLog
                        + " result=disabled_by_config");
                }
                else
                {
                    FacePresetJsonItem selectedPreset;
                    string presetReason;
                    bool appliedPreset = TryApplyFacePresetByRoute(
                        settings,
                        female,
                        facePresetName,
                        facePresetId,
                        facePresetRandom,
                        out selectedPreset,
                        out facePresetSourcePath,
                        out facePresetPoolCount,
                        out facePresetCandidateCount,
                        out presetReason);
                    if (appliedPreset)
                    {
                        facePresetApplyResult = "applied";
                        facePresetSelectedName = selectedPreset?.Name ?? string.Empty;
                        facePresetSelectedId = selectedPreset?.Id ?? string.Empty;
                        StartFacePresetProbe(
                            female,
                            facePresetName,
                            facePresetId,
                            facePresetRandom,
                            facePresetSelectedName,
                            facePresetSelectedId);
                        Log(
                            "[cmd][face-preset] apply"
                            + facePresetRouteLog
                            + " selectedName=" + (string.IsNullOrWhiteSpace(facePresetSelectedName) ? "(empty)" : facePresetSelectedName)
                            + " selectedId=" + (string.IsNullOrWhiteSpace(facePresetSelectedId) ? "(empty)" : facePresetSelectedId)
                            + " pool=" + facePresetPoolCount
                            + " candidates=" + facePresetCandidateCount
                            + " sourcePath=" + (string.IsNullOrWhiteSpace(facePresetSourcePath) ? "(empty)" : facePresetSourcePath)
                            + " result=applied");
                    }
                    else
                    {
                        facePresetApplyResult = "apply_failed";
                        LogWarn(
                            "[cmd][face-preset] apply failed"
                            + facePresetRouteLog
                            + " pool=" + facePresetPoolCount
                            + " candidates=" + facePresetCandidateCount
                            + " sourcePath=" + (string.IsNullOrWhiteSpace(facePresetSourcePath) ? "(empty)" : facePresetSourcePath)
                            + " reason=" + (string.IsNullOrWhiteSpace(presetReason) ? "unknown" : presetReason)
                            + " result=apply_failed");
                    }
                }
            }
            string facePresetExecutionLog =
                " facePresetApply=" + facePresetApplyResult
                + " facePresetSelectedName=" + (string.IsNullOrWhiteSpace(facePresetSelectedName) ? "(empty)" : facePresetSelectedName)
                + " facePresetSelectedId=" + (string.IsNullOrWhiteSpace(facePresetSelectedId) ? "(empty)" : facePresetSelectedId)
                + " facePresetPool=" + facePresetPoolCount
                + " facePresetCandidates=" + facePresetCandidateCount
                + " facePresetSourcePath=" + (string.IsNullOrWhiteSpace(facePresetSourcePath) ? "(empty)" : facePresetSourcePath);
            int voiceKind = command.ResolveVoiceKind(settings.DefaultVoiceKind);
            int action = command.ResolveAction(settings.DefaultAction);
            if (string.Equals(command.type, "speak_sequence", StringComparison.OrdinalIgnoreCase))
            {
                bool interruptCurrent = command.ResolveInterrupt(settings.DefaultInterruptCurrent);
                string sessionId = NormalizeSessionId(command.sessionId);
                if (interruptCurrent || !string.Equals(sessionId, _sequenceSessions.ActiveSessionId, StringComparison.Ordinal))
                    _thoughtMarkedForSession = false;
                List<ExternalVoicePlaybackItem> playbackItems = BuildSequencePlaybackItems(command);
                MarkThoughtPlaybackItems(playbackItems, command);
                if (playbackItems.Count <= 0)
                {
                    LogWarn("[cmd][seq] no playable items result=error_sequence_empty");
                    return;
                }

                if (!playbackItems[0].IsThought) BeginExternalVoiceGuard(proc, female, main);

                if (!playbackItems[0].IsThought && !keepCurrentFaceMode)
                {
                    TryApplyFace(proc, main, female, face, voiceKind, action);
                }

                bool deleteAfterPlay = command.ResolveDeleteAfterPlay(settings.DeleteAudioAfterPlayback);
                float volume = command.ResolveVolume(1f);
                float playbackPitch = command.ResolvePitch(settings.ExternalPlaybackPitch);
                if (interruptCurrent)
                {
                    SetKaleidoscopeThought(false);
                    _sequenceSessions.Activate(sessionId);
                    ClearDelayedActions("sequence_interrupt:" + sessionId);
                    ResetResponseTextQueue("sequence_interrupt:" + sessionId);
                }

                bool playedSequence = _externalVoicePlayer != null && _externalVoicePlayer.PlaySequence(
                    playbackItems,
                    sessionId,
                    female,
                    interruptCurrent,
                    deleteAfterPlay,
                    volume,
                    playbackPitch);

                if (playedSequence)
                {
                    _sequenceSessions.Activate(sessionId);
                    float totalDuration = 0f;
                    for (int i = 0; i < playbackItems.Count; i++)
                    {
                        totalDuration += Mathf.Max(0f, playbackItems[i].DurationSeconds);
                    }

                    RegisterSequenceResponseTextTriggers(command, sessionId, playbackItems, main, totalDuration);

                    Log(
                        "[cmd][seq] queued"
                        + " result=sequence_success"
                        + " session=" + sessionId
                        + " main=" + main
                        + " count=" + playbackItems.Count
                        + " totalDuration=" + totalDuration.ToString("F3")
                        + " face=" + face
                        + " keepCurrentFace=" + keepCurrentFaceMode
                        + " voiceKind=" + voiceKind
                        + " action=" + action
                        + facePresetRouteLog
                        + facePresetExecutionLog
                        + " volume=" + volume
                        + " pitch=" + playbackPitch);
                }
                else
                {
                    LogWarn(
                        "[cmd][seq] play failed"
                        + " result=sequence_failed"
                        + " session=" + sessionId
                        + " main=" + main
                        + " count=" + playbackItems.Count
                        + facePresetRouteLog
                        + facePresetExecutionLog);
                    _voiceGuardState.ClearBlock();
                    RestoreVoiceProcStopIfNeeded();
                }

                return;
            }

            if (string.Equals(command.type, "speak_sequence_append", StringComparison.OrdinalIgnoreCase))
            {
                string sessionId = NormalizeSessionId(command.sessionId);
                if (string.IsNullOrEmpty(sessionId) || !IsCurrentSession(sessionId))
                {
                    LogWarn("[cmd][seq-append] skip stale session=" + sessionId + " active=" + _sequenceSessions.ActiveSessionId);
                    return;
                }

                List<ExternalVoicePlaybackItem> playbackItems = BuildSequencePlaybackItems(command, preserveItemIndexForSequencePosition: true);
                if (playbackItems.Count <= 0)
                {
                    LogWarn("[cmd][seq-append] no playable items result=error_sequence_empty session=" + sessionId);
                    return;
                }

                MarkThoughtPlaybackItems(playbackItems, command);
                if (!playbackItems[0].IsThought) BeginExternalVoiceGuard(proc, female, main);

                bool deleteAfterPlay = command.ResolveDeleteAfterPlay(settings.DeleteAudioAfterPlayback);
                float volume = command.ResolveVolume(1f);
                float playbackPitch = command.ResolvePitch(settings.ExternalPlaybackPitch);

                bool appendedSequence = _externalVoicePlayer != null && _externalVoicePlayer.AppendSequence(
                    playbackItems,
                    sessionId,
                    female,
                    deleteAfterPlay,
                    volume,
                    playbackPitch);

                if (appendedSequence)
                {
                    float totalDuration = 0f;
                    for (int i = 0; i < playbackItems.Count; i++)
                    {
                        totalDuration += Mathf.Max(0f, playbackItems[i].DurationSeconds);
                    }

                    RegisterSequenceResponseTextTriggers(command, sessionId, playbackItems, main, totalDuration);

                    Log(
                        "[cmd][seq-append] queued"
                        + " result=sequence_append_success"
                        + " session=" + sessionId
                        + " main=" + main
                        + " count=" + playbackItems.Count
                        + " firstLine=" + playbackItems[0].SequencePosition
                        + " totalDuration=" + totalDuration.ToString("F3")
                        + " volume=" + volume
                        + " pitch=" + playbackPitch);
                }
                else
                {
                    LogWarn(
                        "[cmd][seq-append] append failed"
                        + " result=sequence_append_failed"
                        + " session=" + sessionId
                        + " active=" + _sequenceSessions.ActiveSessionId
                        + " main=" + main
                        + " count=" + playbackItems.Count);
                }

                return;
            }

            string audioPath = NormalizeAudioPath(command.ResolveAudioPath());
            if (!string.IsNullOrWhiteSpace(audioPath))
            {
                BeginExternalVoiceGuard(proc, female, main);

                if (!keepCurrentFaceMode)
                {
                    TryApplyFace(proc, main, female, face, voiceKind, action);
                }

                bool interruptCurrent = command.ResolveInterrupt(settings.DefaultInterruptCurrent);
                bool deleteAfterPlay = command.ResolveDeleteAfterPlay(settings.DeleteAudioAfterPlayback);
                float volume = command.ResolveVolume(1f);
                float playbackPitch = command.ResolvePitch(settings.ExternalPlaybackPitch);

                bool playedAudio = _externalVoicePlayer != null && _externalVoicePlayer.Play(
                    audioPath,
                    female,
                    interruptCurrent,
                    deleteAfterPlay,
                    volume,
                    playbackPitch);

                if (playedAudio)
                {
                    Log(
                        "[cmd] played audio"
                        + " result=played_audio_success"
                        + " main=" + main
                        + " face=" + face
                        + " keepCurrentFace=" + keepCurrentFaceMode
                        + " voiceKind=" + voiceKind
                        + " action=" + action
                        + facePresetRouteLog
                        + facePresetExecutionLog
                        + " volume=" + volume
                        + " pitch=" + playbackPitch
                        + " audioPath=" + audioPath);
                }
                else
                {
                    LogWarn(
                        "[cmd] audio play failed"
                        + " result=played_audio_failed"
                        + " main=" + main
                        + facePresetRouteLog
                        + facePresetExecutionLog
                        + " audioPath=" + audioPath);
                    _voiceGuardState.ClearBlock();
                    RestoreVoiceProcStopIfNeeded();
                }

                return;
            }

            if (proc.voice == null)
            {
                LogWarn("[cmd] HVoiceCtrl is not ready result=error_hvoicectrl_not_ready");
                return;
            }

            int voiceNo = ResolveVoiceNo(proc, main);
            if (voiceNo < 0)
            {
                LogWarn("[cmd] voiceNo not found main=" + main + " result=error_voice_no_not_found");
                return;
            }

            string assetBundle = command.ResolveAssetBundle(settings.DefaultAssetBundle);
            string assetName = command.ResolveAssetName(settings.DefaultAssetName);
            if (string.IsNullOrWhiteSpace(assetBundle) || string.IsNullOrWhiteSpace(assetName))
            {
                LogWarn("[cmd] assetBundle or assetName is empty result=error_asset_bundle_or_name_empty");
                return;
            }

            int eyeNeck = command.ResolveEyeNeck(settings.DefaultEyeNeck);
            float pitch = command.ResolvePitch(settings.DefaultPitch);
            float fadeTime = command.ResolveFadeTime(settings.DefaultFadeTime);

            var voiceSetting = new Illusion.Game.Utils.Voice.Setting
            {
                assetBundleName = assetBundle,
                assetName = assetName,
                no = voiceNo,
                pitch = pitch,
                fadeTime = fadeTime,
                voiceTrans = female.transform,
                settingNo = -1,
                isAsync = true,
                isPlayEndDelete = true
            };

            bool result = proc.voice.PlayVoice(
                female,
                voiceSetting,
                face,
                eyeNeck,
                voiceKind,
                action,
                main);

            if (result)
            {
                Log(
                    "[cmd] played"
                    + " result=playvoice_success"
                    + " main=" + main
                    + " voiceNo=" + voiceNo
                    + " face=" + face
                    + " keepCurrentFace=" + keepCurrentFaceMode
                    + " eyeneck=" + eyeNeck
                    + " voiceKind=" + voiceKind
                    + " action=" + action
                    + facePresetRouteLog
                    + facePresetExecutionLog
                    + " asset=" + assetName);
            }
            else
            {
                LogWarn(
                    "[cmd] PlayVoice returned false"
                    + " result=playvoice_failed"
                    + " main=" + main
                    + facePresetRouteLog
                    + facePresetExecutionLog
                    + " asset=" + assetName);
            }
        }

        private static string NormalizeSessionId(string sessionId)
        {
            string value = (sessionId ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            return "seq_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        }

        private List<ExternalVoicePlaybackItem> BuildSequencePlaybackItems(
            ExternalVoiceFaceCommand command,
            bool preserveItemIndexForSequencePosition = false)
        {
            var results = new List<ExternalVoicePlaybackItem>();
            ExternalVoiceSequenceItem[] items = command?.items ?? new ExternalVoiceSequenceItem[0];
            bool verbose = Settings != null && Settings.VerboseLog;

            if (verbose) Log("[seq-build] begin"
                + " type=" + (command == null ? "(null)" : (command.type ?? "(null)"))
                + " items=" + (command?.items == null ? -1 : command.items.Length)
                + " preserveIndex=" + preserveItemIndexForSequencePosition);

            float subtitleHoldPaddingSeconds = Settings != null
                ? Settings.SequenceSubtitleHoldPaddingSeconds
                : 0.2f;

            for (int i = 0; i < items.Length; i++)
            {
                ExternalVoiceSequenceItem item = items[i];

                if (item == null)
                {
                    LogWarn("[seq-build] skip null item i=" + i);
                    continue;
                }

                string rawResolved = item.ResolveAudioPath();
                string audioPath = NormalizeAudioPath(rawResolved);

                if (verbose) Log("[seq-build] item"
                    + " i=" + i
                    + " index=" + item.index
                    + " audioPath=" + (item.audioPath ?? "(null)")
                    + " path=" + (item.path ?? "(null)")
                    + " resolvedRaw=" + (rawResolved ?? "(null)")
                    + " normalized=" + (audioPath ?? "(null)")
                    + " exists=" + (!string.IsNullOrWhiteSpace(audioPath) && File.Exists(audioPath)));

                if (string.IsNullOrWhiteSpace(audioPath))
                {
                    LogWarn("[cmd][seq] skip item without audioPath index="
                        + (item.index > 0 ? item.index : i + 1));
                    continue;
                }

                results.Add(new ExternalVoicePlaybackItem
                {
                    Index = item.index > 0 ? item.index : i + 1,
                    Path = audioPath,
                    Subtitle = (item.ResolveSubtitle() ?? string.Empty).Trim(),
                    DurationSeconds = Mathf.Max(0f, item.durationSeconds),
                    HoldSeconds = Mathf.Max(0f, item.holdSeconds),
                    DeleteAfterPlay = item.deleteAfterPlay > 0
                });
            }

            int total = results.Count;

            if (verbose) Log("[seq-build] built"
                + " type=" + (command == null ? "(null)" : (command.type ?? "(null)"))
                + " sourceItems=" + items.Length
                + " playable=" + total);

            if (total > 0)
            {
                var fullSubtitleLines = new List<string>();
                float fullHoldSeconds = 0f;

                for (int i = 0; i < total; i++)
                {
                    ExternalVoicePlaybackItem item = results[i];

                    int sequencePosition =
                        preserveItemIndexForSequencePosition && item.Index > 0
                            ? item.Index
                            : i + 1;

                    item.Total = preserveItemIndexForSequencePosition
                        ? Mathf.Max(sequencePosition, item.Total)
                        : total;

                    item.SequencePosition = sequencePosition;

                    if (!string.IsNullOrWhiteSpace(item.Subtitle))
                    {
                        fullSubtitleLines.Add(item.Subtitle.Trim());
                    }

                    float itemHoldSeconds = item.HoldSeconds > 0f
                        ? item.HoldSeconds
                        : (item.DurationSeconds > 0f
                            ? item.DurationSeconds + subtitleHoldPaddingSeconds
                            : 0f);

                    if (itemHoldSeconds > 0f)
                    {
                        fullHoldSeconds += itemHoldSeconds;
                    }
                }

                string fullSubtitle = string.Join("\n", fullSubtitleLines.ToArray()).Trim();

                for (int i = 0; i < total; i++)
                {
                    results[i].FullSubtitle = fullSubtitle;
                    results[i].FullHoldSeconds = fullHoldSeconds;
                }
            }

            return results;
        }

        private void ClearDelayedActions(string reason)
        {
            if (_sequenceSessions == null)
            {
                _actionScheduler.Clear();
                return;
            }

            Scheduling.SequenceTransientClearResult cleared = _sequenceSessions.ClearTransientState();
            if (cleared.IsEmpty)
            {
                return;
            }

            LogWarn(
                "[delayed] cleared reason=" + reason
                + " dropped=" + cleared.DelayedActionCount
                + " pendingLine=" + cleared.PendingLineActionCount
                + " lineStarts=" + cleared.LineStartCount
                + " triggerSessions=" + cleared.TriggerSessionCount);
        }

        private bool IsCurrentSession(string sessionId)
        {
            return _sequenceSessions.IsCurrent(sessionId);
        }

        private Action CreateSessionGuardedAction(string sessionId, string label, Action action)
        {
            return _sequenceSessions.CreateGuardedAction(sessionId, label, action, LogStaleSessionAction);
        }

        private void LogStaleSessionAction(string sessionId, string label)
        {
            LogWarn(
                "[session] skip stale delayed action label=" + label
                + " session=" + sessionId
                + " active=" + _sequenceSessions.ActiveSessionId);
        }

        private void OnExternalVoicePlaybackStarted(ExternalVoicePlaybackStartedEvent started)
        {
            if (started == null)
            {
                return;
            }

            if (!IsCurrentSession(started.SessionId))
            {
                LogWarn("[subtitle-seq] skip stale subtitle session=" + started.SessionId + " active=" + _sequenceSessions.ActiveSessionId + " index=" + started.Index);
                return;
            }

            int startedLineIndex = ResolvePlaybackStartedLineIndex(started);
            if (startedLineIndex >= 0)
            {
                RecordSequenceLinePlaybackStart(started.SessionId, startedLineIndex);
            }

            PluginSettings settings = Settings;
            if (settings == null || !settings.SequenceSubtitleEnabled)
            {
                return;
            }

            string sendMode = NormalizeSequenceSubtitleSendMode(settings.SequenceSubtitleSendMode);
            bool fullTextOnce = string.Equals(sendMode, "FullTextOnce", StringComparison.Ordinal);
            if (fullTextOnce && started.SequencePosition > 1)
            {
                Log("[subtitle-seq] skip full-text duplicate session=" + started.SessionId + " index=" + started.Index + "/" + started.Total);
                return;
            }

            string rawSubtitle = fullTextOnce ? started.FullSubtitle : started.Subtitle;
            if (string.IsNullOrWhiteSpace(rawSubtitle))
            {
                return;
            }

            string displayMode = string.IsNullOrWhiteSpace(settings.SequenceSubtitleDisplayMode)
                ? "StackFemale"
                : settings.SequenceSubtitleDisplayMode.Trim();
            string subtitleText = settings.SequenceSubtitleProgressPrefixEnabled
                ? BuildSequenceSubtitleProgressText(rawSubtitle, fullTextOnce ? 1 : started.Index, started.Total)
                : rawSubtitle;
            string text = BuildSequenceSubtitleText(subtitleText, displayMode);
            string wavName = fullTextOnce
                ? "sequence_full"
                : (string.IsNullOrWhiteSpace(started.Path) ? ("line_" + started.Index.ToString(CultureInfo.InvariantCulture)) : Path.GetFileName(started.Path));
            float holdSeconds = fullTextOnce && started.FullHoldSeconds > 0f
                ? started.FullHoldSeconds
                : (started.HoldSeconds > 0f
                ? started.HoldSeconds
                : Mathf.Max(0.1f, started.DurationSeconds + settings.SequenceSubtitleHoldPaddingSeconds));

            Log("[subtitle-seq] send start mode=" + sendMode + " session=" + started.SessionId + " index=" + started.Index + "/" + started.Total + " hold=" + holdSeconds.ToString("F3") + " text=" + TrimPreview(subtitleText, 60));
            var options = new External.SubtitleEventClientOptions
            {
                Host = settings.SequenceSubtitleHost,
                Port = settings.SequenceSubtitlePort,
                EndpointPath = settings.SequenceSubtitleEndpointPath,
                Token = settings.SequenceSubtitleToken,
                VerboseLog = settings.VerboseLog
            };
            var message = new External.SubtitleEventMessage
            {
                Text = text,
                WavName = wavName,
                Backend = settings.SequenceSubtitleBackend,
                DisplayMode = displayMode,
                HoldSeconds = holdSeconds,
                SessionId = started.SessionId,
                Index = started.Index
            };
            External.SubtitleEventClient client = _subtitleEventClient;
            if (client != null && client.TrySendLocal(options, message)) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                client?.Send(options, message);
            });
        }

        private static string NormalizeSequenceSubtitleSendMode(string mode)
        {
            return External.SubtitleEventPayloadBuilder.NormalizeSendMode(mode);
        }

        private static string BuildSequenceSubtitleProgressText(string text, int index, int total)
        {
            return External.SubtitleEventPayloadBuilder.BuildProgressText(text, index, total);
        }

        private static string BuildSequenceSubtitleText(string text, string displayMode)
        {
            return External.SubtitleEventPayloadBuilder.BuildDisplayText(text, displayMode);
        }
    }
}
