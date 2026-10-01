using System;
using System.Collections.Generic;
using System.Globalization;
using ReactUnity.UGUI;
using ReactUnity.UGUI.Behaviours;
using UnityEngine;
using UnityEngine.UI;
using Yoga;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// Post-layout pass that applies element-level <c>data-*</c> effects:
    /// raycast toggles, CanvasGroup controls, text shadow/outline and
    /// orientation-conditional activation. Runs after every layout pass so
    /// region-updated elements get the same treatment as fresh mounts.
    /// </summary>
    internal static class UnityHtmlElementEffects
    {
        /// <summary>Applies all element effects; returns true when a second
        /// layout pass is needed (safe-area padding is written to Yoga after
        /// the first pass computed sizes).</summary>
        internal static bool Apply(UGUIContext context, RectTransform root)
        {
            if (root == null)
                return false;

            bool layoutDirty = false;
            var orientation = UnityHtmlEnvironment.Orientation;
            var elements = root.GetComponentsInChildren<ReactElement>(true);
            for (var i = 0; i < elements.Length; i++)
            {
                UGUIComponent component = elements[i] != null ? elements[i].Component : null;
                if (component == null)
                    continue;

                ApplyOrientation(component, orientation);
                ApplyRaycast(elements[i], component);
                ApplyCanvasGroup(elements[i], component);
                ApplyTextEffects(elements[i], component);
                ApplyImageModes(elements[i], component);
                ApplyHaptic(elements[i], component);
                layoutDirty |= ApplySafeArea(component);
            }

            LinkScrollbars(root);
            return layoutDirty;
        }

        private static bool Has(UGUIComponent component, string key, out string value)
        {
            value = null;
            return component.Data.TryGetValue(key, out object raw) &&
                   (value = raw?.ToString()) != null;
        }

        // data-orientation lives on the element (the expander only removes
        // data-platform subtrees) so rotation can flip visibility without
        // rebuilding markup — active state survives region reconciliation.
        private static void ApplyOrientation(UGUIComponent component,
            UnityHtmlEnvironment.OrientationClass orientation)
        {
            if (!Has(component, "orientation", out string value))
                return;

            bool match = false;
            foreach (string token in value.Split('|', ',', ' '))
            {
                switch (token.Trim().ToLowerInvariant())
                {
                    case "landscape" when orientation == UnityHtmlEnvironment.OrientationClass.Landscape:
                    case "portrait" when orientation == UnityHtmlEnvironment.OrientationClass.Portrait:
                        match = true;
                        break;
                }
            }
            component.SetProperty("active", match);
        }

        private static void ApplyRaycast(ReactElement element, UGUIComponent component)
        {
            if (!Has(component, "raycast", out string value))
                return;

            bool enabled = !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
            foreach (Graphic graphic in element.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = enabled;
        }

        private static void ApplyCanvasGroup(ReactElement element, UGUIComponent component)
        {
            bool hasAlpha = Has(component, "alpha", out string alpha);
            bool hasInteractable = Has(component, "interactable", out string interactable);
            bool hasBlocking = Has(component, "blocks-raycasts", out string blocking);
            if (!hasAlpha && !hasInteractable && !hasBlocking)
                return;

            var group = element.GetComponent<CanvasGroup>();
            if (group == null)
                group = element.gameObject.AddComponent<CanvasGroup>();

            if (hasAlpha && float.TryParse(alpha, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float alphaValue))
                group.alpha = alphaValue;
            if (hasInteractable)
                group.interactable = !IsFalse(interactable);
            if (hasBlocking)
                group.blocksRaycasts = !IsFalse(blocking);
        }

        private static void ApplyTextEffects(ReactElement element, UGUIComponent component)
        {
            if (Has(component, "shadow", out string shadow))
                ApplyEffect<Shadow>(element, ParseXY(shadow, new Vector2(1f, -1f)),
                    ParseColorArg(shadow, 2));
            if (Has(component, "outline", out string outline))
                ApplyEffect<Outline>(element, ParseXY(outline, new Vector2(1f, -1f)),
                    ParseColorArg(outline, 2));
        }

        private static void ApplyEffect<T>(ReactElement element, Vector2 distance, Color? color)
            where T : Shadow
        {
            foreach (Graphic graphic in element.GetComponentsInChildren<Graphic>(true))
            {
                var effect = graphic.GetComponent<T>();
                if (effect == null)
                    effect = graphic.gameObject.AddComponent<T>();
                effect.effectDistance = distance;
                if (color.HasValue)
                    effect.effectColor = color.Value;
            }
        }

        // Image render modes: data-slice / data-tiled / data-fill switch
        // Image.type; data-fill-amount + data-fill-origin drive filled images
        // (cooldown sweeps, bars). Values are plain attributes — they reapply
        // idempotently on every layout pass.
        private static void ApplyImageModes(ReactElement element, UGUIComponent component)
        {
            if (Has(component, "gradient", out string gradient))
                ApplyGradient(element, gradient);

            var image = element.GetComponentInChildren<Image>(true);
            if (image == null)
                return;

            if (Has(component, "slice", out string slice) && !IsFalse(slice))
                image.type = Image.Type.Sliced;
            else if (Has(component, "tiled", out string tiled) && !IsFalse(tiled))
                image.type = Image.Type.Tiled;

            if (!Has(component, "fill", out string fill))
                return;

            image.type = Image.Type.Filled;
            switch (fill.Trim().ToLowerInvariant())
            {
                case "radial":
                case "radial360":
                    image.fillMethod = Image.FillMethod.Radial360;
                    break;
                case "radial90": image.fillMethod = Image.FillMethod.Radial90; break;
                case "radial180": image.fillMethod = Image.FillMethod.Radial180; break;
                case "vertical": image.fillMethod = Image.FillMethod.Vertical; break;
                default: image.fillMethod = Image.FillMethod.Horizontal; break;
            }

            if (Has(component, "fill-origin", out string origin) &&
                int.TryParse(origin, NumberStyles.Integer, CultureInfo.InvariantCulture, out int originValue))
                image.fillOrigin = originValue;
            if (Has(component, "fill-amount", out string amount) &&
                float.TryParse(amount, NumberStyles.Float, CultureInfo.InvariantCulture, out float amountValue))
                image.fillAmount = Mathf.Clamp01(amountValue);
            if (Has(component, "fill-clockwise", out string cw))
                image.fillClockwise = !IsFalse(cw);
        }

        // data-gradient="top,bottom" or "tl,tr,bl,br" — TMP vertex gradient.
        private static void ApplyGradient(ReactElement element, string spec)
        {
            var parts = spec.Split(',');
            if (parts.Length != 2 && parts.Length != 4)
                return;
            var colors = new Color[4];
            for (var i = 0; i < parts.Length; i++)
                if (!ColorUtility.TryParseHtmlString(parts[i].Trim(), out colors[i]))
                    return;
            var gradient = parts.Length == 2
                ? new TMPro.VertexGradient(colors[0], colors[0], colors[1], colors[1])
                : new TMPro.VertexGradient(colors[0], colors[1], colors[2], colors[3]);
            foreach (var text in element.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                text.enableVertexGradient = true;
                text.colorGradient = gradient;
            }
        }

        // <scrollbar data-for="#scrollId"> links into the target's ScrollRect.
        private static void LinkScrollbars(RectTransform root)
        {
            var elements = root.GetComponentsInChildren<ReactElement>(true);
            for (var i = 0; i < elements.Length; i++)
            {
                UGUIComponent component = elements[i] != null ? elements[i].Component : null;
                if (component == null || !Has(component, "for", out string targetId))
                    continue;
                var scrollbar = elements[i].GetComponentInChildren<Scrollbar>(true);
                if (scrollbar == null)
                    continue;

                string id = targetId.TrimStart('#');
                ScrollRect scrollRect = null;
                for (var j = 0; j < elements.Length; j++)
                {
                    UGUIComponent other = elements[j] != null ? elements[j].Component : null;
                    if (other?.Id != id)
                        continue;
                    scrollRect = elements[j].GetComponentInChildren<ScrollRect>(true);
                    break;
                }
                if (scrollRect == null)
                    continue;

                // Axis follows the target's scroll axes, not the scrollbar's
                // direction — a fresh scrollbar defaults to LeftToRight.
                if (scrollRect.vertical)
                {
                    if (scrollRect.verticalScrollbar != scrollbar)
                    {
                        scrollRect.verticalScrollbar = scrollbar;
                        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                    }
                }
                else if (scrollRect.horizontal && scrollRect.horizontalScrollbar != scrollbar)
                {
                    scrollRect.horizontalScrollbar = scrollbar;
                    scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                }
            }
        }

        // "x,y[,#color]" — color is an optional third segment.
        private static Vector2 ParseXY(string spec, Vector2 fallback)
        {
            var parts = spec.Split(',');
            if (parts.Length >= 2 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                return new Vector2(x, y);
            return fallback;
        }

        private static Color? ParseColorArg(string spec, int index)
        {
            var parts = spec.Split(',');
            if (parts.Length <= index)
                return null;
            return ColorUtility.TryParseHtmlString(parts[index].Trim(), out Color color)
                ? color
                : (Color?)null;
        }

        // data-safe-area="all|top|bottom|left|right" → Yoga padding in pixels.
        // Written directly (not through style) so orientation changes reapply
        // without reparsing markup; the host runs a second layout pass when
        // this returns true.
        private static bool ApplySafeArea(UGUIComponent component)
        {
            if (!Has(component, "safe-area", out string spec))
                return false;

            Vector4 all = UnityHtmlEnvironment.SafeAreaInsets();
            bool useAll = false, left = false, right = false, top = false, bottom = false;
            foreach (string token in spec.Split('|', ',', ' '))
            {
                switch (token.Trim().ToLowerInvariant())
                {
                    case "all": useAll = true; break;
                    case "left": left = true; break;
                    case "right": right = true; break;
                    case "top": top = true; break;
                    case "bottom": bottom = true; break;
                }
            }

            var layout = component.Layout;
            if (layout == null)
                return false;

            bool dirty = false;
            dirty |= SetPadding(layout, YogaEdge.Left, (useAll || left) ? all.x : 0f);
            dirty |= SetPadding(layout, YogaEdge.Bottom, (useAll || bottom) ? all.y : 0f);
            dirty |= SetPadding(layout, YogaEdge.Right, (useAll || right) ? all.z : 0f);
            dirty |= SetPadding(layout, YogaEdge.Top, (useAll || top) ? all.w : 0f);
            return dirty;
        }

        private static bool SetPadding(YogaNode layout, YogaEdge edge, float value)
        {
            YogaValue current;
            YogaValue desired = YogaValue.Point(value);
            switch (edge)
            {
                case YogaEdge.Left: current = layout.PaddingLeft; break;
                case YogaEdge.Right: current = layout.PaddingRight; break;
                case YogaEdge.Top: current = layout.PaddingTop; break;
                default: current = layout.PaddingBottom; break;
            }
            if (current.Unit == desired.Unit && Mathf.Approximately(current.Value, desired.Value))
                return false;
            switch (edge)
            {
                case YogaEdge.Left: layout.PaddingLeft = desired; break;
                case YogaEdge.Right: layout.PaddingRight = desired; break;
                case YogaEdge.Top: layout.PaddingTop = desired; break;
                default: layout.PaddingBottom = desired; break;
            }
            return true;
        }

        // data-haptic replays haptic feedback on the element's primary click —
        // the trigger survives reconciliation since it lives on the element GO.
        private static void ApplyHaptic(ReactElement element, UGUIComponent component)
        {
            var trigger = element.GetComponent<UnityHtmlHapticTrigger>();
            if (!Has(component, "haptic", out string level) || IsFalse(level))
            {
                if (trigger != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(trigger);
                    else UnityEngine.Object.DestroyImmediate(trigger);
                }
                return;
            }
            if (trigger == null)
                trigger = element.gameObject.AddComponent<UnityHtmlHapticTrigger>();
            trigger.Level = level;
        }

        private static bool IsFalse(string value)
            => string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
               value == "0";

        /// <summary>Explicit Selectable navigation from data-nav-* attributes
        /// plus data-nav-wrap cycling within a container.</summary>
        internal static void ApplyNavigation(RectTransform root)
        {
            if (root == null)
                return;

            var byId = new Dictionary<string, Selectable>(StringComparer.Ordinal);
            var elements = root.GetComponentsInChildren<ReactElement>(true);
            for (var i = 0; i < elements.Length; i++)
            {
                UGUIComponent component = elements[i] != null ? elements[i].Component : null;
                if (component?.Id == null)
                    continue;
                var selectable = elements[i].GetComponent<Selectable>()
                    ?? elements[i].GetComponentInChildren<Selectable>(true);
                if (selectable != null)
                    byId[component.Id] = selectable;
            }

            for (var i = 0; i < elements.Length; i++)
            {
                UGUIComponent component = elements[i] != null ? elements[i].Component : null;
                if (component == null)
                    continue;

                var selectable = elements[i].GetComponent<Selectable>();
                if (selectable != null)
                    ApplyExplicitNavigation(selectable, component, byId);

                if (Has(component, "nav-wrap", out string wrap) && !IsFalse(wrap))
                    ApplyWrap(elements[i]);
            }
        }

        private static void ApplyExplicitNavigation(
            Selectable selectable, UGUIComponent component, Dictionary<string, Selectable> byId)
        {
            Navigation navigation = selectable.navigation;
            bool touched = false;
            if (TryLink(component, "nav-up", byId, out Selectable up))
            { navigation.selectOnUp = up; touched = true; }
            if (TryLink(component, "nav-down", byId, out Selectable down))
            { navigation.selectOnDown = down; touched = true; }
            if (TryLink(component, "nav-left", byId, out Selectable left))
            { navigation.selectOnLeft = left; touched = true; }
            if (TryLink(component, "nav-right", byId, out Selectable right))
            { navigation.selectOnRight = right; touched = true; }
            if (touched)
            {
                navigation.mode = Navigation.Mode.Explicit;
                selectable.navigation = navigation;
            }
        }

        private static bool TryLink(UGUIComponent component, string key,
            Dictionary<string, Selectable> byId, out Selectable target)
        {
            target = null;
            if (!Has(component, key, out string targetId))
                return false;
            string id = targetId.TrimStart('#');
            return byId.TryGetValue(id, out target);
        }

        // Wrap-around: the first selectable's "up" reaches the last and the
        // last's "down" reaches the first, in document (hierarchy) order.
        private static void ApplyWrap(ReactElement container)
        {
            var selectables = container.GetComponentsInChildren<Selectable>(true);
            var ordered = new List<Selectable>();
            foreach (Selectable s in selectables)
                if (s != null && s.IsActive() && s.interactable &&
                    s.navigation.mode != Navigation.Mode.None)
                    ordered.Add(s);
            if (ordered.Count < 2)
                return;

            Link(ordered[0], ordered[ordered.Count - 1], up: true);
            Link(ordered[ordered.Count - 1], ordered[0], up: false);
        }

        private static void Link(Selectable from, Selectable to, bool up)
        {
            Navigation navigation = from.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            // Only the wrap pair is set — left/right stay untouched.
            if (up) navigation.selectOnUp = to; else navigation.selectOnDown = to;
            from.navigation = navigation;
        }
    }
}
