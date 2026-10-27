# MainGameKaleidoscope

サブカメラが映す女性像を、通常版 KKS の画面全体に2D万華鏡背景として描く独立プラグインです。呼び出しキャラがサブカメラを専有している間は、そのキャラだけを万華鏡の素材に使い、メイン画面の前景から外します。壁ディスプレイの材質や動画再生は変更しません。

MainGameVrDildo のディルド模型はキャラとは別のオブジェクトなので、前景に加えて万華鏡上でも見えるようにします。サブカメラ背景を取り込む場合は、素材側に同じディルドを重ねません。

- PlaybackBar の「万華鏡」タブ先頭のボタンで手動 ON/OFF を切り替えられます。同じタブでサブカメラ背景の取り込み ON/OFF、フェード、分割数、模様の回転、素材映像の回転角度、拡大、中心、ねじれ、波、色相、彩度、明るさ、色収差、背景色、素材解像度、周辺の暗さ、思考中の音量を設定できます。
- VoiceFaceEventBridge が `｛思考中｝` を受け取ると、その行以降の音声区間で自動起動します。トリガーは VoiceFaceEventBridge の BepInEx 設定 `Kaleidoscope.ThoughtTriggerWord` で変更できます。
- 思考音声は口パクから外し、通常ゲーム音声と喘ぎ声の再生は維持します。思考音声以外の音量比は万華鏡タブで調整できます。思考中は対象音源を弱めてローパスをかけ、終了時に元へ戻します。トリガーと音源ごとの音量は BepInEx ログに記録します。

`dotnet build MainGameKaleidoscope/MainGameKaleidoscope.csproj -c Release` で DLL をビルドします。シェーダーを変更したときは Unity 2019.4.9f1 で `BuildShader.ps1` を実行してから DLL をビルドします。配布時は自プラグインの `bin/Release/net472/MainGameKaleidoscope.dll` と `Resources/kaleidoscope.bundle` を同じ相対配置でコピーし、更新した MainGameSubCameraDisplayProbe の DLL もそのプラグイン自身の出力から配置します。ゲーム再起動後に反映されます。
