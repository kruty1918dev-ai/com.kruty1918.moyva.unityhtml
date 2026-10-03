using UnityEngine;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// Re-applies environment-conditional effects (data-orientation,
    /// data-safe-area) when the host rect changes — i.e. on rotation or
    /// resize — without rebuilding the document.
    /// </summary>
    [ExecuteAlways, DefaultExecutionOrder(1000)]
    internal sealed class UnityHtmlEnvironmentWatcher : MonoBehaviour
    {
        private UnityHtmlHost _host;
        private UnityHtmlEnvironment.OrientationClass _orientation;
        private Vector4 _safeArea;
        readonly Vector3[] _corners = new Vector3[4];
        UnityHtmlViewport _viewport;
        RectTransform _root;
        bool _dirty;

        internal void Bind(UnityHtmlHost host)
        {
            _host = host;
            enabled = host != null;
            _root = (RectTransform)transform;
            _orientation = UnityHtmlEnvironment.Orientation;
            _safeArea = UnityHtmlEnvironment.SafeAreaInsets();
            _viewport = UnityHtmlViewport.Capture(_root,_corners);
            _dirty = true;
        }

        private void OnRectTransformDimensionsChange() => _dirty = true;

        // Insets can change without a rect callback (system bars, foldables,
        // split-screen). Poll a small value snapshot; traverse DOM only on change.
        internal void Tick()
        {
            if(_host == null || _root == null) return;
            var orientation = UnityHtmlEnvironment.Orientation;
            var safeArea = UnityHtmlEnvironment.SafeAreaInsets();
            var viewport = UnityHtmlViewport.Capture(_root,_corners);
            if(!viewport.IsValid) return; // Android's first frame/minimized window
            if (!_dirty && orientation == _orientation && safeArea == _safeArea && viewport.Equals(_viewport))
                return;
            _dirty = false;
            _orientation = orientation;
            _safeArea = safeArea;
            _viewport = viewport;
            _host?.RefreshEnvironment();
        }
        void LateUpdate() => Tick();
        void OnDestroy() => _host = null;
    }
}
