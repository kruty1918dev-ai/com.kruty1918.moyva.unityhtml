using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// Post-parse transform pass over the markup tree. Rewrites convenience
    /// elements/attributes (<see cref="FEATURES.md"/> W1) into the canonical
    /// tag/style surface before reconciliation, so shorthands reconcile like
    /// hand-written markup — same tag, same key, same identity.
    /// </summary>
    internal static class UnityHtmlAttributeExpander
    {
        private const string FillStyle = "position:absolute;left:0;top:0;right:0;bottom:0";

        private static readonly Dictionary<string, string> AnchorStyles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["top-left"] = "position:absolute;top:0;left:0",
                ["top"] = "position:absolute;top:0;left:0;right:0;margin-left:auto;margin-right:auto",
                ["top-right"] = "position:absolute;top:0;right:0",
                ["left"] = "position:absolute;top:0;bottom:0;left:0;margin-top:auto;margin-bottom:auto",
                ["center"] = "position:absolute;top:0;bottom:0;left:0;right:0;margin:auto",
                ["right"] = "position:absolute;top:0;bottom:0;right:0;margin-top:auto;margin-bottom:auto",
                ["bottom-left"] = "position:absolute;bottom:0;left:0",
                ["bottom"] = "position:absolute;bottom:0;left:0;right:0;margin-left:auto;margin-right:auto",
                ["bottom-right"] = "position:absolute;bottom:0;right:0",
            };

        internal static void Expand(XmlElement root)
        {
            if (root == null)
                return;
            ExpandElement(root);
        }

        private static void ExpandElement(XmlElement element)
        {
            // Snapshot children: expansion may insert/remove nodes. Child
            // references stay valid through tag renames (they move with nodes).
            var children = new List<XmlElement>();
            foreach (XmlNode child in element.ChildNodes)
                if (child is XmlElement childElement)
                    children.Add(childElement);

            element = NormalizePanel(element);
            ExpandBackground(element);
            element = NormalizeSpacer(element);
            element = NormalizeDivider(element);
            element = ExpandBackdrop(element);
            if (element.ParentNode != null)
                ExpandStyleShorthands(element);

            foreach (XmlElement child in children)
                if (child.ParentNode != null)
                    ExpandElement(child);
        }

        // <panel role="dialog" bg="screen"> → <view data-motion-role="dialog" data-bg="screen">
        private static XmlElement NormalizePanel(XmlElement element)
        {
            if (element.Name != "panel")
                return element;

            XmlElement renamed = RenameTag(element, "view");
            MoveAttribute(renamed, "role", "data-motion-role");
            MoveAttribute(renamed, "bg", "data-bg");
            return renamed;
        }

        // data-bg="screen" on an element injects a fullscreen backdrop sibling
        // directly behind it. Per-backdrop options ride data-bg-* attributes.
        private static void ExpandBackground(XmlElement element)
        {
            string mode = element.GetAttribute("data-bg");
            if (string.IsNullOrEmpty(mode) || element.ParentNode == null)
                return;

            element.RemoveAttribute("data-bg");
            XmlElement backdrop = BuildBackdrop(element.OwnerDocument,
                element.GetAttribute("data-bg-src"),
                element.GetAttribute("data-bg-dim"),
                element.GetAttribute("data-bg-close"),
                element.GetAttribute("data-bg-color"));
            element.RemoveAttribute("data-bg-src");
            element.RemoveAttribute("data-bg-dim");
            element.RemoveAttribute("data-bg-close");
            element.RemoveAttribute("data-bg-color");
            element.ParentNode.InsertBefore(backdrop, element);
        }

        // <backdrop src dim close color> → absolute-filled view holding a
        // stretched image plus a dim/click-catcher layer.
        private static XmlElement ExpandBackdrop(XmlElement element)
        {
            if (element.Name != "backdrop")
                return element;

            XmlElement expanded = BuildBackdrop(element.OwnerDocument,
                element.GetAttribute("src"),
                element.GetAttribute("dim"),
                element.GetAttribute("close"),
                element.GetAttribute("color"));
            foreach (XmlAttribute attr in element.Attributes)
                if (attr.Name is "id" or "class" or "className" or "style" or "data-key")
                    expanded.SetAttribute(attr.Name, attr.Value);
            // Declared children render above the background layers.
            while (element.HasChildNodes)
                expanded.AppendChild(element.FirstChild);
            element.ParentNode?.ReplaceChild(expanded, element);
            return expanded;
        }

        private static XmlElement BuildBackdrop(
            XmlDocument document, string src, string dim, string close, string color)
        {
            XmlElement wrapper = document.CreateElement("view");
            wrapper.SetAttribute("data-backdrop", "true");
            AppendStyle(wrapper, FillStyle);

            XmlElement image = document.CreateElement("image");
            if (!string.IsNullOrEmpty(src))
                image.SetAttribute("source", src);
            AppendStyle(image, FillStyle);
            wrapper.AppendChild(image);

            float dimValue = ParseFloat(dim);
            bool closeable = string.Equals(close, "true", StringComparison.OrdinalIgnoreCase);
            if (dimValue > 0f || !string.IsNullOrEmpty(color) || closeable)
            {
                // A Graphic is required for raycasts — the catcher is an image,
                // not a view, so click-to-close works without extra components.
                XmlElement shade = document.CreateElement("image");
                AppendStyle(shade, FillStyle);
                string shadeColor = !string.IsNullOrEmpty(color)
                    ? color
                    : $"rgba(0, 0, 0, {dimValue.ToString("0.###", CultureInfo.InvariantCulture)})";
                AppendStyle(shade, $"background-color:{shadeColor}");
                if (closeable)
                    shade.SetAttribute("onClick", "Globals.ui.Back()");
                wrapper.AppendChild(shade);
            }
            return wrapper;
        }

        // <spacer size="8|8x16|flex"> → sized view
        private static XmlElement NormalizeSpacer(XmlElement element)
        {
            if (element.Name != "spacer")
                return element;

            element = RenameTag(element, "view");
            string size = element.GetAttribute("size");
            element.RemoveAttribute("size");
            if (string.Equals(size, "flex", StringComparison.OrdinalIgnoreCase))
                AppendStyle(element, "flex-grow:1");
            else if (!string.IsNullOrEmpty(size))
            {
                var parts = size.Split('x');
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float w);
                float h = parts.Length > 1 &&
                          float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                    ? parsed
                    : w;
                AppendStyle(element, $"width:{Fmt(w)}px;height:{Fmt(h)}px");
            }
            else
                AppendStyle(element, "width:8px;height:8px");
            return element;
        }

        // <divider orientation="h|v" inset="8" color="..."> → 1px view
        private static XmlElement NormalizeDivider(XmlElement element)
        {
            if (element.Name != "divider")
                return element;

            element = RenameTag(element, "view");
            bool vertical = string.Equals(
                element.GetAttribute("orientation"), "v", StringComparison.OrdinalIgnoreCase);
            string color = element.GetAttribute("color");
            string inset = element.GetAttribute("inset");
            element.RemoveAttribute("orientation");
            element.RemoveAttribute("color");
            element.RemoveAttribute("inset");

            AppendStyle(element, vertical ? "width:1px;align-self:stretch" : "height:1px;align-self:stretch");
            AppendStyle(element, $"background-color:{(string.IsNullOrEmpty(color) ? "rgba(255,255,255,0.2)" : color)}");
            if (!string.IsNullOrEmpty(inset))
                AppendStyle(element, vertical
                    ? $"margin-top:{inset}px;margin-bottom:{inset}px"
                    : $"margin-left:{inset}px;margin-right:{inset}px");
            return element;
        }

        private static void ExpandStyleShorthands(XmlElement element)
        {
            // data-platform subtrees are removed at parse time — the platform
            // never changes at runtime. data-orientation and data-safe-area
            // are left in place: they land in component.Data and are applied
            // by UnityHtmlElementEffects so they react to rotation without a
            // document rebuild.
            ExpandConditional(element, "data-platform", PlatformMatches);

            string anchor = element.GetAttribute("data-anchor");
            if (!string.IsNullOrEmpty(anchor) &&
                AnchorStyles.TryGetValue(anchor.Trim(), out string anchorStyle))
            {
                AppendStyle(element, anchorStyle);
                element.RemoveAttribute("data-anchor");
            }

            if (element.HasAttribute("data-stretch") &&
                !string.Equals(element.GetAttribute("data-stretch"), "false", StringComparison.OrdinalIgnoreCase))
            {
                AppendStyle(element, FillStyle);
                element.RemoveAttribute("data-stretch");
            }

            // data-center: absolute centering via auto margins on all edges.
            if (element.HasAttribute("data-center"))
            {
                AppendStyle(element, AnchorStyles["center"]);
                element.RemoveAttribute("data-center");
            }
        }

        private delegate bool Condition(string value);

        // Remove subtrees that can never apply on this device — the check runs
        // at parse time, so inapplicable branches never create components.
        private static void ExpandConditional(XmlElement element, string attribute, Condition matches)
        {
            string value = element.GetAttribute(attribute);
            if (string.IsNullOrEmpty(value))
                return;
            element.RemoveAttribute(attribute);
            if (!matches(value))
                element.ParentNode?.RemoveChild(element);
        }

        private static bool PlatformMatches(string value)
        {
            var platform = UnityHtmlEnvironment.Platform;
            foreach (string token in value.Split('|', ',', ' '))
            {
                switch (token.Trim().ToLowerInvariant())
                {
                    case "mobile" when platform == UnityHtmlEnvironment.PlatformClass.Mobile:
                    case "desktop" when platform == UnityHtmlEnvironment.PlatformClass.Desktop:
                    case "console" when platform == UnityHtmlEnvironment.PlatformClass.Console:
                        return true;
                }
            }
            return false;
        }

        private static void MoveAttribute(XmlElement element, string from, string to)
        {
            if (!element.HasAttribute(from))
                return;
            element.SetAttribute(to, element.GetAttribute(from));
            element.RemoveAttribute(from);
        }

        private static XmlElement RenameTag(XmlElement element, string tag)
        {
            // XmlElement.Name is read-only — rebuild the element in place.
            XmlDocument document = element.OwnerDocument;
            XmlElement renamed = document.CreateElement(tag);
            foreach (XmlAttribute attr in element.Attributes)
                renamed.SetAttribute(attr.Name, attr.Value);
            while (element.HasChildNodes)
                renamed.AppendChild(element.FirstChild);
            element.ParentNode?.ReplaceChild(renamed, element);
            return renamed;
        }

        private static void AppendStyle(XmlElement element, string style)
        {
            string existing = element.GetAttribute("style");
            element.SetAttribute("style",
                string.IsNullOrEmpty(existing) ? style : existing.TrimEnd(';') + ";" + style);
        }

        private static float ParseFloat(string value)
            => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : 0f;

        private static string Fmt(float value)
            => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
