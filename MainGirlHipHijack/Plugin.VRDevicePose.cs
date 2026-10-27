using System.Collections.Generic;
using MainGirlHipHijack.Adapters;
using MainGirlHipHijack.Services;
using UnityEngine;

namespace MainGirlHipHijack
{
    /// <summary>
    /// SteamVRデバイス姿勢取得の共通レイヤー。
    /// トラッカー追従・男性HMD追従から使う。
    /// </summary>
    internal sealed class RetiredVrDevicePoseCompatibility
    {
        private readonly SteamVrPoseProvider _steamVrPoseProvider =
            new SteamVrPoseProvider(new SteamVrCoordinateConverter());

        // ── デバイス列挙 ─────────────────────────────────────────────────────

        /// <summary>
        /// 接続中のGenericTracker一覧を返す（serial, index）。
        /// </summary>
        internal List<(string serial, uint index)> GetConnectedTrackers()
        {
            return _steamVrPoseProvider.GetConnectedTrackers();
        }

        /// <summary>
        /// HMDのデバイスインデックスを返す。未接続なら uint.MaxValue。
        /// </summary>
        internal uint GetHMDDeviceIndex()
        {
            return _steamVrPoseProvider.GetHmdDeviceIndex();
        }

        /// <summary>
        /// シリアルからデバイスインデックスを解決。未検出なら uint.MaxValue。
        /// </summary>
        internal uint ResolveDeviceIndex(string serial)
        {
            return _steamVrPoseProvider.ResolveDeviceIndex(serial);
        }

        // ── キャッシュ更新（定期スキャン）────────────────────────────────────

        private void TickVRDeviceScan()
        {
            _steamVrPoseProvider.TickDeviceScan(Time.unscaledTime);
        }

        // ── ポーズ取得 ──────────────────────────────────────────────────────

        /// <summary>
        /// 最新のトラッキングポーズをバッファに取得する。
        /// 毎フレーム1回呼ぶこと。
        /// </summary>
        private void FetchVRPoses()
        {
            _steamVrPoseProvider.FetchPoses();
        }

        /// <summary>
        /// 指定デバイスのワールド座標とワールド回転を返す。
        /// トラッキングが有効でない場合はfalse。
        /// </summary>
        internal bool TryGetDevicePose(uint deviceIndex, out Vector3 worldPos, out Quaternion worldRot)
        {
            return _steamVrPoseProvider.TryGetDevicePose(deviceIndex, out worldPos, out worldRot);
        }

    }
}
