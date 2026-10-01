using System;
using UnityEngine;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// Platform/orientation/safe-area facts used by the attribute expander and
    /// post-mount effects. Providers are swappable so tests can simulate any
    /// device without platform-dependent code paths.
    /// </summary>
    internal static class UnityHtmlEnvironment
    {
        public enum PlatformClass { Desktop, Mobile, Console }
        public enum OrientationClass { Landscape, Portrait }

        internal static Func<PlatformClass> PlatformProvider = DefaultPlatform;
        internal static Func<OrientationClass> OrientationProvider = DefaultOrientation;
        internal static Func<Rect> SafeAreaProvider = () => Screen.safeArea;
        internal static Func<Vector2> ScreenSizeProvider =
            () => new Vector2(Screen.width, Screen.height);

        public static PlatformClass Platform => PlatformProvider();
        public static OrientationClass Orientation => OrientationProvider();

        /// <summary>Safe-area insets in pixels: x=left, y=bottom, z=right, w=top.</summary>
        public static Vector4 SafeAreaInsets()
        {
            Rect safe = SafeAreaProvider();
            Vector2 screen = ScreenSizeProvider();
            if (screen.x <= 0f || screen.y <= 0f || safe.width <= 0f || safe.height <= 0f)
                return Vector4.zero;
            return new Vector4(
                Mathf.Max(0f, safe.xMin),
                Mathf.Max(0f, safe.yMin),
                Mathf.Max(0f, screen.x - safe.xMax),
                Mathf.Max(0f, screen.y - safe.yMax));
        }

        internal static void ResetOverrides()
        {
            PlatformProvider = DefaultPlatform;
            OrientationProvider = DefaultOrientation;
            SafeAreaProvider = () => Screen.safeArea;
            ScreenSizeProvider = () => new Vector2(Screen.width, Screen.height);
        }

        private static PlatformClass DefaultPlatform()
        {
            if (SystemInfo.deviceType == DeviceType.Console)
                return PlatformClass.Console;
            return Application.isMobilePlatform ? PlatformClass.Mobile : PlatformClass.Desktop;
        }

        private static OrientationClass DefaultOrientation()
        {
            switch (Screen.orientation)
            {
                case ScreenOrientation.Portrait:
                case ScreenOrientation.PortraitUpsideDown:
                    return OrientationClass.Portrait;
                case ScreenOrientation.LandscapeLeft:
                case ScreenOrientation.LandscapeRight:
                    return OrientationClass.Landscape;
                default:
                    return Screen.width >= Screen.height
                        ? OrientationClass.Landscape
                        : OrientationClass.Portrait;
            }
        }
    }
}
