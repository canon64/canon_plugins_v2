# UnityResourceDiagnostics

Unity上に残っている主要リソース数を専用txtへ記録する診断用BepInExプラグインです。

## 出力

- `_logs/resource_diagnostics.txt`

## 既定値

- 有効: `true`
- 詳細ログ: `false`
- 自動スナップショット間隔: `30` 秒
- 手動スナップショット: `Ctrl+F9`

## 見る対象

- `GameObject`
- `Material`
- `Texture2D`
- `RenderTexture`
- `Mesh`
- `AudioClip`

名前に `VideoMat_`、`VideoRT_`、`__BlankMapVideoRoom`、`RotationObjectStriped` などを含む疑わしい対象は個別に件数を出します。

## Mesh診断

- Meshの `GetInstanceID()` 単位で前回スナップショットとの差分を出します。
- `MeshFilter.sharedMesh`、`SkinnedMeshRenderer.sharedMesh`、`MeshCollider.sharedMesh` から owner を逆引きします。
- どの owner からも参照されていない Mesh は orphan として集計します。
- `EnableBakeMeshTrace` を `true` にすると、`SkinnedMeshRenderer.BakeMesh(Mesh)` の最初の指定回数を `_logs/bake_mesh_trace.txt` に記録します。
- `EnableBakeMeshTrace` の既定値は `false` です。
