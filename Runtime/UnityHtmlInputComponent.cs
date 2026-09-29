using ReactUnity.UGUI;
using TMPro;
using UnityEngine;

namespace UnityHTML.Runtime
{
    internal sealed class UnityHtmlInputComponent : InputComponent
    {
        internal bool NativeLayoutConfigured;
        internal Vector2 NativeLayoutSize;
        public UnityHtmlInputComponent(string text, UGUIContext context) : base(text, context) => ConfigureText();
        public override bool Revive()
        {
            if (!base.Revive()) return false;
            ConfigureText();
            return true;
        }
        private void ConfigureText()
        {
            NativeLayoutConfigured = false;
            // TMP owns these rects and the caret's horizontal scrolling.
            TextViewport.Component.enabled = false;
            TextComponent.Component.enabled = false;
            PlaceholderComponent.Component.enabled = false;
            RectTransform viewport = TextViewport.RectTransform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = Vector2.one * 0.5f;
            viewport.offsetMin = new Vector2(10, 0);
            viewport.offsetMax = new Vector2(-10, 0);
            TextComponent.Style["textAlign"] = "center";
            TextComponent.Style["verticalAlign"] = "middle";
            PlaceholderComponent.Style["textAlign"] = "center";
            PlaceholderComponent.Style["verticalAlign"] = "middle";
            InputField.textComponent.alignment = TextAlignmentOptions.Center;
            InputField.richText = false;
        }
    }
}
