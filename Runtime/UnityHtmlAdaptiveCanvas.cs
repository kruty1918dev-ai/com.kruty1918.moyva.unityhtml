using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnityHTML.Runtime
{
    /// <summary>Shared, reversible orientation policy for a screen Canvas's existing design resolution.</summary>
    [ExecuteAlways, DefaultExecutionOrder(-100)]
    internal sealed class UnityHtmlAdaptiveCanvas : MonoBehaviour
    {
        readonly HashSet<UnityHtmlHost> _owners = new HashSet<UnityHtmlHost>();
        CanvasScaler _scaler;
        Canvas _canvas;
        Vector2 _authored, _applied;

        internal static UnityHtmlAdaptiveCanvas Acquire(RectTransform root,UnityHtmlHost owner)
        {
            var canvas=root.GetComponentInParent<Canvas>()?.rootCanvas;
            if(canvas == null || canvas.renderMode == RenderMode.WorldSpace) return null;
            var scaler=canvas.GetComponent<CanvasScaler>();
            if(scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return null;
            var policy=canvas.GetComponent<UnityHtmlAdaptiveCanvas>();
            if(policy == null) policy=canvas.gameObject.AddComponent<UnityHtmlAdaptiveCanvas>();
            if(policy._owners.Count == 0)
            {
                policy._canvas=canvas;policy._scaler=scaler;
                policy._authored=policy._applied=scaler.referenceResolution;
            }
            policy._owners.Add(owner);policy.enabled=true;
            policy.Apply();
#if UNITY_EDITOR
            if(!Application.isPlaying) policy.hideFlags=HideFlags.DontSaveInEditor;
#endif
            return policy;
        }

        internal void Release(UnityHtmlHost owner)
        {
            _owners.Remove(owner);
            if(_owners.Count != 0) return;
            // Respect a designer's changes while mounted; restore only our own value.
            if(_scaler != null && _scaler.referenceResolution == _applied)
                _scaler.referenceResolution=_authored;
            enabled=false;
        }

        internal void Apply()
        {
            if(_owners.Count == 0 || _scaler == null || _canvas == null
                || _scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;
            if(_scaler.referenceResolution != _applied) _authored=_scaler.referenceResolution;
            var pixels=_canvas.renderMode == RenderMode.ScreenSpaceCamera && _canvas.worldCamera != null
                ? _canvas.worldCamera.pixelRect.size : _canvas.targetDisplay > 0 ? _canvas.pixelRect.size : UnityHtmlEnvironment.ScreenSizeProvider();
            if(pixels.x <= 0 || pixels.y <= 0 || _authored.x <= 0 || _authored.y <= 0) return;
            var desired=_authored;
            if((pixels.x > pixels.y) != (_authored.x > _authored.y)) desired=new Vector2(_authored.y,_authored.x);
            _applied=desired;
            if(_scaler.referenceResolution != desired) _scaler.referenceResolution=desired;
        }
        void Update() => Apply();
    }
}
