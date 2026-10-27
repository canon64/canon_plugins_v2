using System.Runtime.Serialization;
using System.Collections.Generic;

namespace MainGameVMDPlay
{
    [DataContract]
    internal sealed class VMDPlaySettings
    {
        [DataMember] public string LastActorCardDirectory = "";
        [DataMember] public bool   UiVisible        = true;
        [DataMember] public string VmdDirectory     = "";     // 空なら pluginDir/vmd
        [DataMember] public bool   AutoLoadLast     = false;  // HScene開始時に前回VMDを自動ロード
        [DataMember] public string LastVmdAbsPath   = "";
        [DataMember] public bool   AutoPlayOnLoad   = true;
        [DataMember] public float  UiWindowX        = 80f;
        [DataMember] public float  UiWindowY        = 80f;
        [DataMember] public float  UiWindowW        = 480f;
        [DataMember] public float  UiWindowH        = 520f;
        [DataMember] public bool   VerboseLog       = false;
        [DataMember] public bool   UseIKDataInVMD   = true;
        [DataMember] public bool   SmoothArmTwist   = true;
        [DataMember] public bool   UseBoneTransform = true;

        [DataMember] public Dictionary<string, float> Adjustments = new Dictionary<string, float>();
        [DataMember] public string CameraVmdPath = "";
        [DataMember] public Dictionary<string, List<string>> MyLists = new Dictionary<string, List<string>>();
        [DataMember] public string SelectedMyList = "";
        [DataMember] public bool MyListLoop;
        [DataMember] public float AudioVolume = 1f;
        [DataMember] public bool VideoOnWalls = true;
        [OnDeserializing]
        private void SetNewFieldDefaults(StreamingContext context) { MyLists = new Dictionary<string, List<string>>(); AudioVolume = 1f; VideoOnWalls = true; Adjustments = new Dictionary<string, float>(); }

        internal VMDPlaySettings SuspensionCopy()
        {
            var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(VMDPlaySettings));
            using (var stream = new System.IO.MemoryStream()) {
                serializer.WriteObject(stream, this); stream.Position = 0;
                return (VMDPlaySettings)serializer.ReadObject(stream);
            }
        }

        internal VMDPlaySettings PresetCopy()
        {
            var copy = (VMDPlaySettings)MemberwiseClone();
            copy.Adjustments = new Dictionary<string, float>();
            foreach (var pair in Adjustments) if (!pair.Key.StartsWith("body.")) copy.Adjustments[pair.Key] = pair.Value;
            copy.MyLists = new Dictionary<string, List<string>>();
            copy.SelectedMyList = "";
            copy.MyListLoop = false;
            copy.LastActorCardDirectory = "";
            return copy;
        }

        // ── Music ────────────────────────────────────────────────────────
        [DataMember] public string LastAudioAbsPath = "";
        [DataMember] public bool   AudioAutoLoad    = true;   // VMDロード時に前回Audioも自動ロード
        [DataMember] public bool   AudioAutoPlay    = true;   // VMDと同時再生
        [DataMember] public bool   AudioLoop        = false;
    }
}
