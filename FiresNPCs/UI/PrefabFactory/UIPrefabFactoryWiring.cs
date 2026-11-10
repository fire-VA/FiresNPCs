using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Handles wiring up panel references and fixing UI components.
  /// Creates transparent runtime input fields while respecting Unity's DetailPanel layout.
    /// </summary>
    public static partial class UIPrefabFactory
    {

   // Color palette - warm orange/yellow tones for text
      private static readonly Color TextColor = new Color(1f, 0.85f, 0.5f); // Warm gold/orange

        #region Wiring References

    #endregion

        #region Button Helpers

        #endregion
    }

    /// <summary>
    /// Component that handles button border highlight effect on click.
    /// </summary>
    public class ButtonBorderHighlight : MonoBehaviour
    {
        private Image _borderImage;
        private Button _button;
        private Color _normalColor = new Color(0.06f, 0.06f, 0.08f, 0.6f);
        private Color _highlightColor = new Color(0.8f, 0.5f, 0.2f, 0.9f); // Warm orange highlight to match text
        private float _highlightDuration = 0.15f;
        private float _fadeDuration = 0.25f;
   private float _timer;
        private bool _isHighlighting;

        public void Initialize(Image borderImage, Button button)
        {
     _borderImage = borderImage;
       _button = button;

            if (_button != null)
       {
        _button.onClick.AddListener(OnButtonClick);
            }
        }

        private void OnButtonClick()
  {
       _isHighlighting = true;
     _timer = 0f;
if (_borderImage != null)
   {
      _borderImage.color = _highlightColor;
       }
        }

        private void Update()
        {
            if (!_isHighlighting || _borderImage == null) return;

   _timer += Time.unscaledDeltaTime;

            if (_timer < _highlightDuration)
            {
     // Stay at highlight color
       return;
 }

      // Fade back to normal
         float fadeProgress = (_timer - _highlightDuration) / _fadeDuration;
  if (fadeProgress >= 1f)
            {
           _borderImage.color = _normalColor;
    _isHighlighting = false;
   }
            else
            {
       _borderImage.color = Color.Lerp(_highlightColor, _normalColor, fadeProgress);
       }
        }

        private void OnDestroy()
     {
      if (_button != null)
     {
         _button.onClick.RemoveListener(OnButtonClick);
     }
        }
    }
}
