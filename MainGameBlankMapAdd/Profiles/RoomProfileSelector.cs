using System;

namespace MainGameBlankMapAdd.Profiles
{
    internal sealed class RoomProfileSelection
    {
        internal static readonly RoomProfileSelection None =
            new RoomProfileSelection(null, string.Empty, string.Empty);

        internal RoomProfileSelection(RoomLayoutProfile profile, string scopeName, string key)
        {
            Profile = profile;
            ScopeName = scopeName ?? string.Empty;
            Key = key ?? string.Empty;
        }

        internal RoomLayoutProfile Profile { get; }
        internal string ScopeName { get; }
        internal string Key { get; }
        internal bool HasValue => Profile != null;
        internal string KeyLogSuffix => string.IsNullOrWhiteSpace(Key) ? string.Empty : $" key={Key}";
    }

    internal static class RoomProfileSelector
    {
        internal static RoomProfileSelection SelectRoomLayout(
            RoomLayoutProfile video,
            string videoKey,
            RoomLayoutProfile folder,
            string folderKey,
            RoomLayoutProfile baseline)
        {
            return Select(video, videoKey, folder, folderKey, baseline, profile => profile.HasRoomLayout);
        }

        internal static RoomProfileSelection SelectAudioGain(
            RoomLayoutProfile video,
            string videoKey,
            RoomLayoutProfile folder,
            string folderKey)
        {
            return Select(video, videoKey, folder, folderKey, null, profile => profile.HasAudioGain);
        }

        internal static RoomProfileSelection SelectSpeedLimitBreak(
            RoomLayoutProfile video,
            string videoKey,
            RoomLayoutProfile folder,
            string folderKey,
            RoomLayoutProfile baseline)
        {
            return Select(video, videoKey, folder, folderKey, baseline, profile => profile.HasSpeedLimitBreak);
        }

        internal static RoomProfileSelection SelectBeatSync(
            RoomLayoutProfile video,
            string videoKey,
            RoomLayoutProfile folder,
            string folderKey,
            RoomLayoutProfile baseline)
        {
            return Select(video, videoKey, folder, folderKey, baseline, profile => profile.HasBeatSync);
        }

        private static RoomProfileSelection Select(
            RoomLayoutProfile video,
            string videoKey,
            RoomLayoutProfile folder,
            string folderKey,
            RoomLayoutProfile baseline,
            Func<RoomLayoutProfile, bool> includesSection)
        {
            if (video != null && includesSection(video))
                return new RoomProfileSelection(video, "video", videoKey);
            if (folder != null && includesSection(folder))
                return new RoomProfileSelection(folder, "folder", folderKey);
            if (baseline != null && includesSection(baseline))
                return new RoomProfileSelection(baseline, "folder-baseline", string.Empty);
            return RoomProfileSelection.None;
        }
    }
}
