using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MainGameHScenePoseInjector
{
    /// <summary>
    /// 自動変更のUI。
    ///
    /// 既定では一括の数個しか出さない。詳細は畳んでおく。
    /// 細かく設定できることと、画面がごちゃつかないことを両立させるため、
    /// 「普段使う物」と「詰めたい時だけ触る物」を段で分けている。
    /// </summary>
    public sealed partial class Plugin
    {
        private static readonly string[] OrderLabels = { "順送り", "逆戻り", "ランダム" };

        private bool _autoDetailOpen;
        private bool _autoPoseFilterOpen;
        private Vector2 _autoPresetScroll;
        private Vector2 _autoEbScroll;
        private Vector2 _autoEyeScroll;
        private Vector2 _autoMouthScroll;
        private string _autoEbAddId = string.Empty;
        private string _autoEyeAddId = string.Empty;
        private string _autoMouthAddId = string.Empty;

        private void DrawAutoCycleSection()
        {
            AutoCycleSettings a = Auto;
            if (a == null) return;

            GUILayout.Space(4f);
            GUILayout.BeginVertical(GUI.skin.box);

            // ── 1段目: 入り口 ────────────────────────────────
            GUILayout.BeginHorizontal();
            bool on = GUILayout.Toggle(a.Enabled, "  自動変更", GUILayout.Width(110f));
            if (on != a.Enabled)
            {
                SetAutoCycleEnabled(on);
                SaveSettings();
                SetStatus(on ? "自動変更を開始" : "自動変更を停止");
            }

            GUILayout.Space(12f);
            bool bulk = GUILayout.Toggle(a.BulkMode, "  一括設定", GUILayout.Width(100f));
            if (bulk != a.BulkMode) { a.BulkMode = bulk; SaveSettings(); }

            GUILayout.FlexibleSpace();
            bool openDetail = GUILayout.Toggle(_autoDetailOpen, _autoDetailOpen ? "詳細を閉じる" : "詳細設定",
                GUI.skin.button, GUILayout.Width(110f), GUILayout.Height(22f));
            _autoDetailOpen = openDetail;
            GUILayout.EndHorizontal();

            // ── 2段目: 一括 ──────────────────────────────────
            if (a.BulkMode)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("順序", GUILayout.Width(36f));
                int order = GUILayout.Toolbar(Mathf.Clamp(a.BulkOrder, 0, 2), OrderLabels, GUILayout.Width(210f));
                if (order != a.BulkOrder) { a.BulkOrder = order; SaveSettings(); }

                GUILayout.Space(10f);
                DrawSecondsField("保持", ref a.BulkHoldSeconds, 0.2f, 60f);
                DrawSecondsField("遷移", ref a.BulkTransitionSeconds, 0f, 5f);

                bool blend = GUILayout.Toggle(a.BulkBlend, "  表情を補間", GUILayout.Width(110f));
                if (blend != a.BulkBlend) { a.BulkBlend = blend; SaveSettings(); }

                if (GUILayout.Button("個別へ流し込む", GUILayout.Width(120f), GUILayout.Height(20f)))
                {
                    a.PushBulkToChannels();
                    SaveSettings();
                    SetStatus("一括の値を個別設定へ反映しました");
                }
                GUILayout.EndHorizontal();

                GUILayout.Label("一括中は全系統がこの順序と秒数で動きます。個別の値は消えません。",
                    GUILayout.Height(16f));
            }

            // ── 3段目: 対象の入切 ────────────────────────────
            GUILayout.BeginHorizontal();
            bool poseOn = GUILayout.Toggle(a.PoseEnabled, "  ポーズを変える", GUILayout.Width(140f));
            if (poseOn != a.PoseEnabled) { a.PoseEnabled = poseOn; SaveSettings(); }

            bool expOn = GUILayout.Toggle(a.ExpressionEnabled, "  表情を変える", GUILayout.Width(130f));
            if (expOn != a.ExpressionEnabled) { a.ExpressionEnabled = expOn; SaveSettings(); }

            GUILayout.Space(10f);
            GUILayout.Label("表情の単位", GUILayout.Width(70f));
            int src = GUILayout.Toolbar(Mathf.Clamp(a.ExpressionSource, 0, 1),
                new[] { "プリセット", "眉/目/口 別" }, GUILayout.Width(190f));
            if (src != a.ExpressionSource) { a.ExpressionSource = src; SaveSettings(); }

            GUILayout.FlexibleSpace();
            bool filterOpen = GUILayout.Toggle(_autoPoseFilterOpen,
                _autoPoseFilterOpen ? "対象を閉じる" : "対象を選ぶ",
                GUI.skin.button, GUILayout.Width(110f), GUILayout.Height(22f));
            _autoPoseFilterOpen = filterOpen;
            GUILayout.EndHorizontal();

            // 頬赤と涙は値なので、選択肢ではなくスライダーで直接触れるようにする。
            // 隠さず常に出す。
            DrawEffectControls(a);

            if (_autoPoseFilterOpen) DrawAutoTargetPicker(a);
            if (_autoDetailOpen) DrawAutoDetail(a);

            GUILayout.EndVertical();
        }

        /// <summary>回す対象の絞り込み。ポーズはカテゴリ、表情はプリセット名で選ぶ。</summary>
        private void DrawAutoTargetPicker(AutoCycleSettings a)
        {
            GUILayout.Space(2f);
            GUILayout.BeginHorizontal();

            // ポーズのカテゴリ
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(260f));
            if (a.PoseCategories == null) a.PoseCategories = new List<string>();
            GUILayout.BeginHorizontal();
            GUILayout.Label("ポーズの対象カテゴリ");
            if (GUILayout.Button("全選択", GUILayout.Width(48f), GUILayout.Height(18f)))
            {
                a.PoseCategories.Clear();
                foreach (string c in GetSelectablePoseCategories()) a.PoseCategories.Add(c);
                SaveSettings();
            }
            if (GUILayout.Button("全解除", GUILayout.Width(48f), GUILayout.Height(18f)))
            {
                a.PoseCategories.Clear();
                SaveSettings();
            }
            GUILayout.EndHorizontal();
            // ボタン風トグルは既定スキンだとONの状態がほぼ見えず、
            // 押せていないように見えるため、他の一覧と同じチェックボックスに揃える。
            GUILayout.BeginHorizontal();
            foreach (string cat in GetSelectablePoseCategories())
            {
                bool sel = a.PoseCategories.Contains(cat);
                bool now = GUILayout.Toggle(sel, cat, GUILayout.Width(62f));
                if (now == sel) continue;
                if (now) a.PoseCategories.Add(cat);
                else a.PoseCategories.Remove(cat);
                SaveSettings();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("対象 " + ResolvePoseCandidates(a).Count + " 件");
            GUILayout.EndVertical();

            // 表情プリセット
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(260f));
            if (a.PresetWhitelist == null) a.PresetWhitelist = new List<string>();
            GUILayout.BeginHorizontal();
            GUILayout.Label("対象の表情プリセット");
            if (GUILayout.Button("全選択", GUILayout.Width(48f), GUILayout.Height(18f)))
            {
                a.PresetWhitelist.Clear();
                if (_settings != null && _settings.ExpressionPresets != null)
                    foreach (ExpressionPreset ep in _settings.ExpressionPresets)
                        if (ep != null) a.PresetWhitelist.Add(ep.Name ?? "");
                SaveSettings();
            }
            if (GUILayout.Button("全解除", GUILayout.Width(48f), GUILayout.Height(18f)))
            {
                a.PresetWhitelist.Clear();
                SaveSettings();
            }
            GUILayout.EndHorizontal();
            _autoPresetScroll = GUILayout.BeginScrollView(_autoPresetScroll, GUILayout.Height(80f));
            List<ExpressionPreset> presets = _settings != null && _settings.ExpressionPresets != null
                ? _settings.ExpressionPresets
                : new List<ExpressionPreset>();
            foreach (ExpressionPreset p in presets)
            {
                if (p == null) continue;
                string name = p.Name ?? "";
                bool sel = a.PresetWhitelist.Contains(name);
                bool now = GUILayout.Toggle(sel, name);
                if (now == sel) continue;
                if (now) a.PresetWhitelist.Add(name);
                else a.PresetWhitelist.Remove(name);
                SaveSettings();
            }
            GUILayout.EndScrollView();
            GUILayout.Label("対象 " + ResolvePresetCandidates(a).Count + " 件");
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            // 系統別モードの時だけ、使うパターンを選ばせる。
            // プリセット単位で回している時には関係ないので出さない。
            if ((ExpressionCycleSource)a.ExpressionSource == ExpressionCycleSource.PerChannel)
            {
                GUILayout.BeginHorizontal();
                DrawPatternPicker("眉", GetEyebrowList(), a.Eyebrow, ref _autoEbScroll, ref _autoEbAddId);
                DrawPatternPicker("目", GetEyesList(), a.Eyes, ref _autoEyeScroll, ref _autoEyeAddId);
                DrawPatternPicker("口", GetMouthList(), a.Mouth, ref _autoMouthScroll, ref _autoMouthAddId);
                GUILayout.EndHorizontal();

            }
        }

        /// <summary>
        /// 値の系統(頬赤・涙)の行。
        /// 左のスライダーで今の値を直接決められる。自動をONにすると
        /// 最小〜最大の範囲を段階数で刻んで回す。
        /// </summary>
        private void DrawValueChannelRow(
            string label, ChannelCycleSettings ch, int hardMin, int hardMax, string unit,
            Action<int> applyNow, Action release)
        {
            if (ch == null) return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90f));

            // 直接指定
            GUILayout.Label("値", GUILayout.Width(20f));
            int cur = Mathf.Clamp(ch.ManualValue, hardMin, hardMax);
            int next = Mathf.RoundToInt(GUILayout.HorizontalSlider(cur, hardMin, hardMax, GUILayout.Width(110f)));
            GUILayout.Label(next + unit, GUILayout.Width(38f));
            if (next != ch.ManualValue)
            {
                ch.ManualValue = next;
                applyNow(next);
                SaveSettings();
            }
            if (GUILayout.Button("解除", GUILayout.Width(44f), GUILayout.Height(18f)))
            {
                release();
                SetStatus(label + " の維持を解除");
            }

            GUILayout.Space(8f);

            // 自動送り
            bool en = GUILayout.Toggle(ch.Enabled, "  自動", GUILayout.Width(56f));
            if (en != ch.Enabled) { ch.Enabled = en; SaveSettings(); }

            if (ch.Enabled)
            {
                int o = GUILayout.Toolbar(Mathf.Clamp(ch.Order, 0, 2), OrderLabels, GUILayout.Width(180f));
                if (o != ch.Order) { ch.Order = o; SaveSettings(); }

                GUILayout.Label("最小", GUILayout.Width(30f));
                int mn = Mathf.RoundToInt(GUILayout.HorizontalSlider(ch.MinValue, hardMin, hardMax, GUILayout.Width(60f)));
                GUILayout.Label(mn.ToString(), GUILayout.Width(28f));
                if (mn != ch.MinValue) { ch.MinValue = mn; SaveSettings(); }

                GUILayout.Label("最大", GUILayout.Width(30f));
                int mx = Mathf.RoundToInt(GUILayout.HorizontalSlider(ch.MaxValue, hardMin, hardMax, GUILayout.Width(60f)));
                GUILayout.Label(mx.ToString(), GUILayout.Width(28f));
                if (mx != ch.MaxValue) { ch.MaxValue = mx; SaveSettings(); }

                GUILayout.Label("段階", GUILayout.Width(30f));
                int st = Mathf.RoundToInt(GUILayout.HorizontalSlider(ch.StepCount, 2, 10, GUILayout.Width(50f)));
                GUILayout.Label(st.ToString(), GUILayout.Width(22f));
                if (st != ch.StepCount) { ch.StepCount = st; SaveSettings(); }

                DrawSecondsField("保持", ref ch.HoldSeconds, 0.2f, 60f);
            }

            GUILayout.EndHorizontal();
        }

        /// <summary>系統ごとに「回すパターン」を選ぶ。無選択なら全部。</summary>
        private void DrawPatternPicker(string title, List<PatternEntry> list, ChannelCycleSettings ch, ref Vector2 scroll, ref string addIdBuffer)
        {
            if (ch == null) return;
            if (ch.Whitelist == null) ch.Whitelist = new List<int>();

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(172f));

            GUILayout.BeginHorizontal();
            GUILayout.Label(title, GUILayout.Width(28f));
            // 全選択してから要らないものを外す使い方のためのボタン。
            if (GUILayout.Button("全選択", GUILayout.Width(48f), GUILayout.Height(18f)))
            {
                ch.Whitelist.Clear();
                if (list != null)
                    foreach (PatternEntry e in list)
                        if (e != null) ch.Whitelist.Add(e.Id);
                SaveSettings();
            }
            if (GUILayout.Button("全解除", GUILayout.Width(48f), GUILayout.Height(18f)))
            {
                ch.Whitelist.Clear();
                SaveSettings();
            }
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(96f));
            if (list != null)
            {
                foreach (PatternEntry e in list)
                {
                    if (e == null) continue;
                    bool sel = ch.Whitelist.Contains(e.Id);
                    bool now = GUILayout.Toggle(sel, e.Id + ": " + (e.Name ?? ""));
                    if (now == sel) continue;
                    if (now) ch.Whitelist.Add(e.Id);
                    else ch.Whitelist.Remove(e.Id);
                    SaveSettings();
                }
            }
            GUILayout.EndScrollView();

            // MODが足した表情はゲーム内蔵の表に載らないため、一覧に出てこない。
            // 番号が分かっていれば直接足せるようにしておく。
            GUILayout.BeginHorizontal();
            GUILayout.Label("番号", GUILayout.Width(32f));
            addIdBuffer = GUILayout.TextField(addIdBuffer ?? string.Empty, GUILayout.Width(46f));
            if (GUILayout.Button("追加", GUILayout.Width(40f), GUILayout.Height(18f)))
            {
                int addId;
                if (int.TryParse((addIdBuffer ?? string.Empty).Trim(), out addId))
                {
                    if (!ch.Whitelist.Contains(addId)) ch.Whitelist.Add(addId);
                    addIdBuffer = string.Empty;
                    SaveSettings();
                    SetStatus(title + " に番号 " + addId + " を追加");
                }
            }
            GUILayout.EndHorizontal();

            // 一覧に無い番号(MOD等)も選択中なら見せる。外せないと消せなくなるため。
            foreach (int id in new List<int>(ch.Whitelist))
            {
                bool known = list != null && list.Any(e => e != null && e.Id == id);
                if (known) continue;
                if (!GUILayout.Toggle(true, id + ": (一覧外)"))
                {
                    ch.Whitelist.Remove(id);
                    SaveSettings();
                }
            }

            int n = ch.Whitelist.Count > 0 ? ch.Whitelist.Count
                : (list != null ? list.Count : 0);
            GUILayout.Label("回す " + n + " 件");

            GUILayout.EndVertical();
        }

        /// <summary>個別設定。一括モード中は効かないので、その旨を出したうえで触らせる。</summary>
        private void DrawAutoDetail(AutoCycleSettings a)
        {
            GUILayout.Space(2f);
            if (a.BulkMode)
                GUILayout.Label("※ 一括設定がONの間、下の個別値は使われません（保存はされます）");

            GUILayout.BeginVertical(GUI.skin.box);

            // ポーズ
            GUILayout.BeginHorizontal();
            GUILayout.Label("ポーズ", GUILayout.Width(60f));
            int po = GUILayout.Toolbar(Mathf.Clamp(a.PoseOrder, 0, 2), OrderLabels, GUILayout.Width(210f));
            if (po != a.PoseOrder) { a.PoseOrder = po; SaveSettings(); }
            DrawSecondsField("保持", ref a.PoseHoldSeconds, 0.2f, 60f);
            DrawSecondsField("遷移", ref a.PoseTransitionSeconds, 0f, 5f);
            GUILayout.EndHorizontal();
            GUILayout.Label("   遷移秒数は同じアニメーター内の切替でのみ効きます。別ポーズ集を跨ぐ時は瞬時切替です。");

            // 表情プリセット
            GUILayout.BeginHorizontal();
            GUILayout.Label("表情ﾌﾟﾘｾｯﾄ", GUILayout.Width(60f));
            int pr = GUILayout.Toolbar(Mathf.Clamp(a.PresetOrder, 0, 2), OrderLabels, GUILayout.Width(210f));
            if (pr != a.PresetOrder) { a.PresetOrder = pr; SaveSettings(); }
            DrawSecondsField("保持", ref a.PresetHoldSeconds, 0.2f, 60f);
            bool pb = GUILayout.Toggle(a.PresetBlend, "  補間", GUILayout.Width(70f));
            if (pb != a.PresetBlend) { a.PresetBlend = pb; SaveSettings(); }
            GUILayout.EndHorizontal();

            DrawChannelRow("眉", a.Eyebrow);
            DrawChannelRow("目", a.Eyes);
            DrawChannelRow("口", a.Mouth);
            DrawChannelRow("頬赤", a.HohoAka);
            DrawChannelRow("涙(標準)", a.Tears);
            DrawChannelRow("顔の汁", a.FaceSiru);
            DrawChannelRow("精液: 胸・腹上", a.FrontUpSiru);
            DrawChannelRow("精液: 腹下", a.FrontDownSiru);
            DrawChannelRow("精液: 背中", a.BackUpSiru);
            DrawChannelRow("精液: 腰・尻", a.BackDownSiru);
            bool modEnabled = ModController(SelectedEffectCharacter()) != null;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && modEnabled;
            DrawChannelRow("涙(MOD)", a.ModTears);
            DrawChannelRow("よだれ", a.ModDrool);
            DrawChannelRow("唾液", a.ModSaliva);
            DrawChannelRow("汗(MOD)", a.ModSweat);
            GUI.enabled = previousEnabled;

            GUILayout.EndVertical();
        }

        private void DrawChannelRow(string label, ChannelCycleSettings ch)
        {
            if (ch == null) return;
            GUILayout.BeginHorizontal();

            bool en = GUILayout.Toggle(ch.Enabled, "  " + label, GUILayout.Width(60f));
            if (en != ch.Enabled) { ch.Enabled = en; SaveSettings(); }

            int o = GUILayout.Toolbar(Mathf.Clamp(ch.Order, 0, 2), OrderLabels, GUILayout.Width(210f));
            if (o != ch.Order) { ch.Order = o; SaveSettings(); }

            DrawSecondsField("保持", ref ch.HoldSeconds, 0.2f, 60f);

            bool b = GUILayout.Toggle(ch.Blend, "  補間", GUILayout.Width(70f));
            if (b != ch.Blend) { ch.Blend = b; SaveSettings(); }

            GUILayout.EndHorizontal();
        }

        /// <summary>秒数入力。スライダーと数値の両方を出す(粗く決めて細かく詰められるように)。</summary>
        private void DrawSecondsField(string label, ref float value, float min, float max)
        {
            GUILayout.Label(label, GUILayout.Width(32f));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(90f));
            string text = GUILayout.TextField(v.ToString("0.##"), GUILayout.Width(44f));
            float parsed;
            if (float.TryParse(text, out parsed)) v = Mathf.Clamp(parsed, min, max);
            GUILayout.Label("秒", GUILayout.Width(20f));
            if (!Mathf.Approximately(v, value))
            {
                value = v;
                SaveSettings();
            }
        }
    }
}
