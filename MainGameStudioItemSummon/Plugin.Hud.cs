namespace MainGameStudioItemSummon
{
    public sealed partial class Plugin
    {
        // [診断ビルド 2026-06-05] HUD(OnGUI / GUILayout.Window)を完全に撤去した。
        // 目的: IMGUI ウィンドウがタイトル画面の uGUI ボタンを塞いでいた犯人かを切り分ける。
        //   - ボタンが効く   → IMGUI ウィンドウ(OnGUI)が原因。HUD の作りを変えて戻す。
        //   - ボタンが効かない → このプラグインの IMGUI は無関係。原因は別を当たる。
        // OnGUI が無ければ Unity は IMGUI 描画を一切呼ばない（このプラグインに関して）。
    }
}
