using System;
using System.IO;
using HarmonyLib;
using MainGameBlankMapAdd.Compatibility;
using UnityEngine;
namespace MainGameBlankMapAdd {
 public sealed partial class Plugin {
  private Texture2D _poseAllRoomThumbnail;
  private Sprite _poseAllRoomSprite;
  [HarmonyPrefix]
  [HarmonyPatch(typeof(FreeHScene), "SetMapSprite")]
  private static bool PoseAllRoomPreviewPrefix(FreeHScene __instance, MapInfo.Param _mapInfo) {
   var plugin = PluginCompatibilityResolver.ResolveLoadedPlugin();
   if (plugin?._settings == null || _mapInfo == null || _mapInfo.No != plugin._settings.AddedMapNo) return true;
   var texture = plugin.GetPoseAllRoomThumbnail();
   if (texture == null) return true;
   if (plugin._poseAllRoomSprite == null) {
    plugin._poseAllRoomSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
    plugin._poseAllRoomSprite.hideFlags = HideFlags.HideAndDontSave;
   }
   foreach (string suffix in new[] { "Normal", "Masturbation", "Lesbian", "3P" }) {
    var image = AccessTools.Field(typeof(FreeHScene), "mapImage" + suffix)?.GetValue(__instance) as UnityEngine.UI.Image;
    if (image != null) image.sprite = plugin._poseAllRoomSprite;
    var label = AccessTools.Field(typeof(FreeHScene), "mapName" + suffix)?.GetValue(__instance);
    if (label != null) AccessTools.Property(label.GetType(), "text")?.SetValue(label, _mapInfo.DisplayName, null);
   }
   return false;
  }
  private bool _thumbnailLoadFailed;
  [HarmonyPrefix]
  [HarmonyPatch(typeof(MapThumbnailInfo.Param), "GetThumbnail")]
  private static bool PoseAllRoomThumbnailPrefix(MapThumbnailInfo.Param __instance, ref Texture2D __result) {
   var plugin = PluginCompatibilityResolver.ResolveLoadedPlugin();
   if (plugin?._settings == null || __instance.ID != plugin._settings.AddedThumbnailID) return true;
   var texture = plugin.GetPoseAllRoomThumbnail();
   if (texture == null) return true;
   __result = texture;
   return false;
  }
  private Texture2D GetPoseAllRoomThumbnail() {
   if (_poseAllRoomThumbnail != null || _thumbnailLoadFailed) return _poseAllRoomThumbnail;
   Texture2D texture = null;
   try {
    using (var stream = typeof(Plugin).Assembly.GetManifestResourceStream("MainGameBlankMapAdd.PoseAllRoom.png"))
    using (var buffer = new MemoryStream()) {
     if (stream == null) throw new FileNotFoundException("PoseAllRoom thumbnail resource");
     stream.CopyTo(buffer);
     texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
     if (!ImageConversion.LoadImage(texture, buffer.ToArray(), true)) throw new InvalidDataException("PoseAllRoom PNG");
     texture.name = "PoseAllRoom thumbnail";
     texture.wrapMode = TextureWrapMode.Clamp;
     texture.filterMode = FilterMode.Bilinear;
     texture.hideFlags = HideFlags.HideAndDontSave;
     _poseAllRoomThumbnail = texture;
    }
   } catch (Exception ex) {
    if (texture != null) Destroy(texture);
    _thumbnailLoadFailed = true; LogError("PoseAllRoom thumbnail: " + ex.Message);
   }
   return _poseAllRoomThumbnail;
  }
 }
}
