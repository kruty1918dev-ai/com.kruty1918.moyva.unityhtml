using UnityEngine;
using UnityEngine.UI;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// Applies the host's wheel-sensitivity multiplier to a plain ScrollRect.
    /// Captures the rect's own scrollSensitivity once as the baseline so repeated
    /// applications scale from a fixed value instead of compounding. Used for
    /// controls that cannot carry MoyvaSmoothScrollRect (e.g. select dropdowns).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoyvaScrollSensitivity : MonoBehaviour
    {
        private ScrollRect _rect;
        private float _baseSensitivity = -1f;

        public void Apply(float wheelSensitivity)
        {
            if (_rect == null)
                _rect = GetComponent<ScrollRect>();
            if (_rect == null)
                return;
            if (_baseSensitivity < 0f)
                _baseSensitivity = _rect.scrollSensitivity;
            _rect.scrollSensitivity = _baseSensitivity
                * UnityHtmlScrollSettings.ClampWheelSensitivity(wheelSensitivity);
        }
    }
}
