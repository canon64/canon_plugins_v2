using System.Collections.Generic;
using UnityEngine;
namespace MainGameBlankMapAdd {
 public sealed partial class Plugin {
  private readonly Dictionary<string, float> _marqueeStarts = new Dictionary<string, float>();
  private GUIStyle _marqueeStyle;
  private bool MarqueeButton(Rect rect, string text, string key) {
   bool clicked = GUI.Button(rect, new GUIContent("", text));
   DrawMarqueeText(rect, text, key);
   return clicked;
  }
  private void DrawMarqueeText(Rect rect, string text, string key) {
   if (Event.current.type != EventType.Repaint) return;
   if (_marqueeStyle == null) _marqueeStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false, padding = new RectOffset(0,0,0,0) };
   float available = Mathf.Max(1, rect.width - 12);
   var content = new GUIContent(text ?? "");
   float width = _marqueeStyle.CalcSize(content).x;
   float overflow = Mathf.Max(0, width - available);
   string identity = key + "\n" + text;
   if (!_marqueeStarts.TryGetValue(identity, out float start)) {
    if (_marqueeStarts.Count >= 256) _marqueeStarts.Clear();
    _marqueeStarts[identity] = start = Time.unscaledTime;
   }
   float offset = 0;
   if (overflow > 0) {
    float travel = overflow / 35f;
    float phase = (Time.unscaledTime - start) % (travel + 3f);
    offset = Mathf.Clamp((phase - 1.5f) * 35f, 0, overflow);
   }
   GUI.BeginGroup(new Rect(rect.x + 6, rect.y, available, rect.height));
   // Draw text only; button background and hit area remain fixed.
   _marqueeStyle.Draw(new Rect(overflow > 0 ? -offset : (available - width) * 0.5f, 0, Mathf.Max(width,1), rect.height), content, false, false, false, false);
   GUI.EndGroup();
  }
 }
}
