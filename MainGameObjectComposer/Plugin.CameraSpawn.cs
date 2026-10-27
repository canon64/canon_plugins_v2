using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        /// <summary>
        /// プレイヤー視点（カメラ）の前方ワールド位置を返す。
        /// VR では VR カメラ、デスクトップでは Camera.main が使われる。
        /// 取得失敗時は Vector3.zero。
        /// </summary>
        private bool TryGetCameraFrontWorldPos(float distance, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            Camera cam = ResolveActiveCamera();
            if (cam == null) return false;

            worldPos = cam.transform.position + cam.transform.forward * distance;
            return true;
        }

        private static Camera ResolveActiveCamera()
        {
            Camera cam = Camera.main;
            if (cam != null && cam.isActiveAndEnabled) return cam;

            Camera[] cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                if (cams[i] != null && cams[i].isActiveAndEnabled)
                {
                    return cams[i];
                }
            }
            return null;
        }

        /// <summary>
        /// ルート直下用: ワールド位置をルート (= __MainGameObjectComposerRoot) のローカル座標に変換。
        /// ルートはワールド原点 + 回転なしの想定だが、念のため Transform 経由で変換する。
        /// </summary>
        private Vector3 WorldToManagedLocalPosition(Vector3 worldPos, Transform parent)
        {
            if (parent == null) return worldPos;
            return parent.InverseTransformPoint(worldPos);
        }

        /// <summary>
        /// 選択中オブジェクトをカメラ前方へ移動。
        /// 親が外部 Transform (キャラボーン等) の場合も親のローカル空間へ変換する。
        /// </summary>
        private void MoveSelectedToCameraFront()
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                LogWarn("move to front failed: no selected object");
                return;
            }

            if (!TryGetCameraFrontWorldPos(_settings.DefaultSpawnDistance, out Vector3 worldPos))
            {
                LogWarn("move to front failed: no active camera");
                return;
            }

            RuntimeObjectRef runtimeRef = FindRuntimeById(selected.id);
            if (runtimeRef == null || runtimeRef.GameObject == null)
            {
                LogWarn("move to front failed: runtime object missing");
                return;
            }

            Transform tf = runtimeRef.GameObject.transform;
            Transform parent = tf.parent;
            Vector3 localPos = WorldToManagedLocalPosition(worldPos, parent);

            // 既存の SetSelectedTransform を流用すれば、Undo + Base姿勢無効化 + 保存まで一括
            SetSelectedTransform(localPos, selected.localEulerAngles, selected.localScale);
            LogInfo("moved to camera front: id=" + selected.id + " worldPos=" + worldPos);
        }
    }
}
