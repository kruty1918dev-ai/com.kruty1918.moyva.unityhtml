using System;
using UnityEngine;

namespace UnityHTML.Runtime
{
    /// <summary>Container dimensions and safe insets in Canvas layout units, never raw display pixels.</summary>
    public readonly struct UnityHtmlViewport : IEquatable<UnityHtmlViewport>
    {
        public readonly Vector2 Size;
        public readonly Vector2 ScreenSize;
        public readonly Rect ScreenRect;
        public readonly Vector4 SafeInsets;
        public readonly Vector2 PixelScale;
        public bool IsValid => Finite(Size.x) && Finite(Size.y) && Size.x > .5f && Size.y > .5f;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public bool IsWide => IsValid && Size.x >= 960f && Size.x > Size.y * 1.1f;
        public bool IsLandscape => Size.x > Size.y;

        UnityHtmlViewport(Vector2 size, Vector2 screen, Rect rect, Vector4 insets)
        {
            Size = size; ScreenSize = screen; ScreenRect = rect; SafeInsets = insets;
            PixelScale = new Vector2(rect.width / Mathf.Max(.001f,size.x),rect.height / Mathf.Max(.001f,size.y));
        }

        internal static UnityHtmlViewport Capture(RectTransform root, Vector3[] corners)
        {
            if(root == null) return default;
            var size = root.rect.size;
            var screen = UnityHtmlEnvironment.ScreenSizeProvider();
            if(!Finite(size.x) || !Finite(size.y) || !Finite(screen.x) || !Finite(screen.y) || size.x <= .5f || size.y <= .5f || screen.x <= 0 || screen.y <= 0) return default;
            var window = new Rect(0,0,screen.x,screen.y);
            var canvas = root.GetComponentInParent<Canvas>()?.rootCanvas;
            var bounds = window;
            bool safeSupported = canvas == null || canvas.renderMode != RenderMode.WorldSpace && canvas.targetDisplay == 0;
            if(canvas != null && canvas.renderMode != RenderMode.WorldSpace)
            {
                if(canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera != null)
                    window = canvas.worldCamera.pixelRect;
                else if(canvas.targetDisplay > 0) window = canvas.pixelRect;
                var canvasRect = (RectTransform)canvas.transform;
                var cr = canvasRect.rect;
                if(cr.width > .5f && cr.height > .5f)
                {
                    root.GetWorldCorners(corners);
                    var min = new Vector2(float.MaxValue,float.MaxValue);
                    var max = new Vector2(float.MinValue,float.MinValue);
                    for(int i=0;i<4;i++)
                    {
                        var local=canvasRect.InverseTransformPoint(corners[i]);
                        var p=new Vector2(window.x+(local.x-cr.xMin)/cr.width*window.width,
                            window.y+(local.y-cr.yMin)/cr.height*window.height);
                        min=Vector2.Min(min,p);max=Vector2.Max(max,p);
                    }
                    bounds=Rect.MinMaxRect(min.x,min.y,max.x,max.y);
                }
            }
            var inset=Vector4.zero;
            var safe=UnityHtmlEnvironment.SafeAreaProvider();
            if(safeSupported && safe.width > 0 && safe.height > 0 && bounds.width > .5f && bounds.height > .5f)
            {
                safe=Rect.MinMaxRect(Mathf.Clamp(safe.xMin,0,screen.x),Mathf.Clamp(safe.yMin,0,screen.y),
                    Mathf.Clamp(safe.xMax,0,screen.x),Mathf.Clamp(safe.yMax,0,screen.y));
                inset=new Vector4(Mathf.Clamp(safe.xMin-bounds.xMin,0,bounds.width)*size.x/bounds.width,
                    Mathf.Clamp(safe.yMin-bounds.yMin,0,bounds.height)*size.y/bounds.height,
                    Mathf.Clamp(bounds.xMax-safe.xMax,0,bounds.width)*size.x/bounds.width,
                    Mathf.Clamp(bounds.yMax-safe.yMax,0,bounds.height)*size.y/bounds.height);
            }
            return new UnityHtmlViewport(size,screen,bounds,inset);
        }

        public bool Equals(UnityHtmlViewport other) => Size == other.Size && ScreenSize == other.ScreenSize
            && ScreenRect == other.ScreenRect && SafeInsets == other.SafeInsets && PixelScale == other.PixelScale;
        public override bool Equals(object obj) => obj is UnityHtmlViewport other && Equals(other);
        public override int GetHashCode() => Size.GetHashCode() ^ ScreenRect.GetHashCode() ^ SafeInsets.GetHashCode();
    }
}
