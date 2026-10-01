using UnityEngine;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// Re-applies environment-conditional effects (data-orientation,
    /// data-safe-area) when the host rect changes — i.e. on rotation or
    /// resize — without rebuilding the document.
    /// </summary>
    internal sealed class UnityHtmlEnvironmentWatcher : MonoBehaviour
    {
        private UnityHtmlHost _host;
        private UnityHtmlEnvironment.OrientationClass _orientation;
        private Vector4 _safeArea;

        internal void Bind(UnityHtmlHost host)
        {
            _host = host;
            _orientation = UnityHtmlEnvironment.Orientation;
            _safeArea = UnityHtmlEnvironment.SafeAreaInsets();
        }

        private void OnRectTransformDimensionsChange()
        {
            var orientation = UnityHtmlEnvironment.Orientation;
            var safeArea = UnityHtmlEnvironment.SafeAreaInsets();
            if (orientation == _orientation && safeArea == _safeArea)
                return;
            _orientation = orientation;
            _safeArea = safeArea;
            _host?.RefreshEnvironment();
        }
    }
}
