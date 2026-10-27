using MainGameUiInputCapture;
using UnityEngine;
using MainGirlHipHijack.Runtime;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private void OnBodyIkGizmoDragStateChanged(int idx, bool dragging)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;

            BodyIkOperationPointState state = _bikEff[idx];
            if (state == null)
                return;

            if (state.GizmoDragging == dragging)
                return;

            state.GizmoDragging = dragging;
            LogDebug("gizmo drag " + (dragging ? "ON" : "OFF") + " idx=" + idx);

            if (dragging)
            {
                state.HasPostDragHold = false;
                state.PostDragHoldFrames = 0;
            }
            else if (state.Proxy != null)
            {
                state.PostDragHoldPos = state.Proxy.position;
                state.PostDragHoldRot = state.Proxy.rotation;
                state.PostDragHoldFrames = BodyIkPostDragHoldFrames;
                state.HasPostDragHold = true;
                LogDebug("post-drag hold armed idx=" + idx + " frames=" + state.PostDragHoldFrames);
            }

            // ドラッグ終了時に追従オフセットを再計算して保存する。
            // これによりドラッグ後の位置を新しい追従基準として維持できる。
            if (!dragging && state.FollowBone != null && state.Proxy != null)
            {
                Quaternion offsetRot = GetFollowOffsetRotation(state.FollowBone);
                state.FollowBonePositionOffset =
                    Quaternion.Inverse(offsetRot) * (state.Proxy.position - state.FollowBone.position);
                if (IsRotationDrivenEffector(idx))
                    state.FollowBoneRotationOffset =
                        Quaternion.Inverse(state.FollowBone.rotation) * state.Proxy.rotation;
                LogDebug("follow offset updated idx=" + idx);
            }

            RecomputeGizmoDraggingState();
        }

        private void OnMaleHeadTargetGizmoDragStateChanged(bool dragging)
        {
            if (_maleSession.MaleHeadTargetGizmoDragging == dragging)
                return;

            _maleSession.MaleHeadTargetGizmoDragging = dragging;
            LogDebug("male head gizmo drag " + (dragging ? "ON" : "OFF"));
            RecomputeGizmoDraggingState();
        }

        private void RecomputeGizmoDraggingState()
        {
            bool anyDragging = false;
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                BodyIkOperationPointState state = _bikEff[i];
                if (state == null || !state.Running)
                    continue;
                if (state.GizmoDragging || (state.Gizmo != null && state.Gizmo.IsDragging))
                {
                    anyDragging = true;
                    break;
                }
            }

            if (!anyDragging && _maleSession.MaleHeadTargetGizmo != null)
            {
                if (_maleSession.MaleHeadTargetGizmoDragging || _maleSession.MaleHeadTargetGizmo.IsDragging)
                    anyDragging = true;
            }

            if (!anyDragging)
            {
                for (int i = 0; i < BIK_TOTAL; i++)
                {
                    MaleControlState state = _maleControlStates[i];
                    if (state == null)
                        continue;
                    if (state.GizmoDragging || (state.Gizmo != null && state.Gizmo.IsDragging))
                    {
                        anyDragging = true;
                        break;
                    }
                }
            }

            if (_gizmoDragging == anyDragging)
                return;

            _gizmoDragging = anyDragging;
            LogDebug("gizmo drag aggregate " + (anyDragging ? "ON" : "OFF"));
        }

        private void UpdateUiDraggingStateByMouseRelease()
        {
            if (Input.GetMouseButton(0))
                return;

            if (_windowDragging)
                SetWindowDragging(false, "mouse-release");
            if (_sliderDragging)
                SetSliderDragging(false, _sliderDraggingIndex, "mouse-release");
            if (_scrollDragging)
                SetScrollDragging(false, "mouse-release");
        }

        private void UpdateInputCaptureApiState()
        {
            if (_settings == null || !UiInputCaptureApi.IsAvailable)
                return;

            bool standaloneVisible = _settings.UiVisible && !_unifiedUiGateway.IsHosting();

            UiInputCaptureApi.SetOwnerDebug(InputCaptureOwnerKey, _settings.DetailLogEnabled);

            if (!standaloneVisible)
            {
                if (_windowDragging)
                    SetWindowDragging(false, "ui-hidden");
                if (_sliderDragging)
                    SetSliderDragging(false, _sliderDraggingIndex, "ui-hidden");
                if (_scrollDragging)
                    SetScrollDragging(false, "ui-hidden");
            }

            RecomputeGizmoDraggingState();

            // UI表示中はカーソルを常に解放維持する
            UiInputCaptureApi.SetIdleCursorUnlock(InputCaptureOwnerKey, standaloneVisible);

            UiInputCaptureApi.Sync(InputCaptureOwnerKey, InputCaptureSourceGizmo, _gizmoDragging);
            UiInputCaptureApi.Sync(InputCaptureOwnerKey, InputCaptureSourceWindow, standaloneVisible && _windowDragging);
            UiInputCaptureApi.Sync(InputCaptureOwnerKey, InputCaptureSourceSlider, standaloneVisible && _sliderDragging);
            UiInputCaptureApi.Sync(InputCaptureOwnerKey, InputCaptureSourceScroll, standaloneVisible && _scrollDragging);
        }

        private void SetWindowDragging(bool dragging, string source)
        {
            if (_windowDragging == dragging)
                return;

            _windowDragging = dragging;
            LogDebug("window drag " + (dragging ? "ON" : "OFF") + " (" + source + ")");
        }

        private void SetScrollDragging(bool dragging, string source)
        {
            if (_scrollDragging == dragging)
                return;

            _scrollDragging = dragging;
            LogDebug("scroll drag " + (dragging ? "ON" : "OFF") + " (" + source + ")");
        }

        private void SetSliderDragging(bool dragging, int idx, string source)
        {
            int normalizedIdx = dragging ? idx : -1;
            if (_sliderDragging == dragging && _sliderDraggingIndex == normalizedIdx)
                return;

            _sliderDragging = dragging;
            _sliderDraggingIndex = normalizedIdx;
            int displayIndex = normalizedIdx >= 0 ? normalizedIdx + 1 : 0;
            LogDebug("slider drag " + (dragging ? "ON" : "OFF") + " idx=" + displayIndex + " (" + source + ")");
        }

        private bool IsInputCaptureActive()
        {
            return UiInputCaptureApi.IsAvailable && UiInputCaptureApi.IsOwnerActive(InputCaptureOwnerKey);
        }

        private void ReleaseAllInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable)
                return;

            UiInputCaptureApi.SetOwnerDebug(InputCaptureOwnerKey, false);
            UiInputCaptureApi.SetIdleCursorUnlock(InputCaptureOwnerKey, false);
            int removed = UiInputCaptureApi.EndOwner(InputCaptureOwnerKey);
            LogDebug("input capture release owner removed=" + removed);
            _gizmoDragging = false;
            _windowDragging = false;
            _sliderDragging = false;
            _sliderDraggingIndex = -1;
            _scrollDragging = false;
        }
    }
}
