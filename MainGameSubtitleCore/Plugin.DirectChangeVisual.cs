using UnityEngine;
using UnityEngine.UI;

namespace MainGameSubtitleCore
{
    internal sealed partial class Plugin
    {
        private DirectChangeButtonVisual _directChangeButtonVisual;

        private void UpdateDirectChangeVisual()
        {
            if (_directChangeButton == null) return;
            Text label = _directChangeButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = DirectChangeLabel();
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 10;
                label.resizeTextMaxSize = System.Math.Max(10, label.fontSize);
            }
            Color onColor = ParseColor(_settings?.DirectChangeOnColor,
                new Color32(0xD3, 0x2F, 0x2F, 0xFF), "DirectChangeOnColor");
            _directChangeButtonVisual?.Apply(_directChangeEnabled, onColor);
        }
    }

    // Capture each new button's own skin so OFF restores both cloned and fallback UI.
    internal sealed class DirectChangeButtonVisual
    {
        private readonly Button _button;
        private readonly Graphic _background;
        private readonly Image _image;
        private readonly Color _backgroundColor;
        private readonly Material _material;
        private readonly Sprite _sprite;
        private readonly Sprite _overrideSprite;
        private readonly Image.Type _imageType;
        private readonly Selectable.Transition _transition;
        private readonly ColorBlock _colors;
        private readonly Text[] _labels;
        private readonly Color[] _labelColors;

        internal DirectChangeButtonVisual(Button button)
        {
            _button = button;
            _background = button.targetGraphic;
            _image = _background as Image;
            _transition = button.transition;
            _colors = button.colors;
            if (_background != null)
            {
                _backgroundColor = _background.color;
                _material = _background.material;
            }
            if (_image != null)
            {
                _sprite = _image.sprite;
                _overrideSprite = _image.overrideSprite;
                _imageType = _image.type;
            }
            _labels = button.GetComponentsInChildren<Text>(true);
            _labelColors = new Color[_labels.Length];
            for (int i = 0; i < _labels.Length; i++) _labelColors[i] = _labels[i].color;
        }

        internal void Apply(bool enabled, Color onColor)
        {
            if (_button == null) return;
            if (_background != null)
            {
                _background.color = enabled ? Color.white : _backgroundColor;
                _background.material = enabled ? null : _material;
            }
            if (_image != null)
            {
                // A pre-coloured game sprite would multiply red into a dark/blue tint.
                _image.sprite = enabled ? null : _sprite;
                _image.overrideSprite = enabled ? null : _overrideSprite;
                _image.type = enabled ? Image.Type.Simple : _imageType;
            }
            _button.transition = enabled ? Selectable.Transition.ColorTint : _transition;
            ColorBlock colors = _colors;
            if (enabled)
            {
                colors.normalColor = onColor;
                colors.selectedColor = onColor;
                colors.highlightedColor = Color.Lerp(onColor, Color.white, 0.12f);
                colors.pressedColor = new Color(onColor.r * 0.82f, onColor.g * 0.82f, onColor.b * 0.82f, onColor.a);
                colors.disabledColor = onColor;
                colors.colorMultiplier = 1f;
            }
            _button.colors = colors;
            for (int i = 0; i < _labels.Length; i++)
                if (_labels[i] != null) _labels[i].color = enabled ? Color.white : _labelColors[i];
        }
    }
}
