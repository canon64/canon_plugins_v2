using System.Collections.Generic;
using UnityEngine;

namespace MainGirlHipHijack
{
    /// <summary>
    /// 女性ポーズプリセットの保存先と画面操作中の一時状態を所有する。
    /// プリセットの適用処理やUnityイベントは所有しない。
    /// </summary>
    internal sealed class FemalePosePresetState<TPreset, TTransitionPoint>
    {
        internal List<TPreset> Presets { get; } = new List<TPreset>();
        internal Dictionary<string, Texture2D> ThumbnailCache { get; } =
            new Dictionary<string, Texture2D>();

        internal string RootDirectory { get; set; }
        internal string ShotsDirectory { get; set; }
        internal string IndexPath { get; set; }
        internal bool IsLoaded { get; set; }
        internal bool ThumbnailDirty { get; set; }
        internal string LastClickedPresetId { get; set; }
        internal float LastClickTime { get; set; }
        internal string TransitionPresetId { get; set; }
        internal Coroutine TransitionCoroutine { get; set; }
        internal List<TTransitionPoint> ActiveTransitionPoints { get; set; }
    }
}
