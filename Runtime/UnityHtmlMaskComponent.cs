using System;
using ReactUnity.UGUI;
using UnityEngine;
using UnityEngine.UI;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// <c>&lt;mask&gt;</c> — clips children with a uGUI <see cref="Mask"/>.
    /// A transparent <see cref="Image"/> provides the stencil graphic;
    /// <c>show-graphic="true"</c> makes the mask itself drawable
    /// (e.g. a rounded avatar frame background).
    /// </summary>
    internal sealed class UnityHtmlMaskComponent : UGUIComponent
    {
        private readonly Mask _mask;

        public UnityHtmlMaskComponent(UGUIContext context) : base(context, "mask")
        {
            var graphic = AddComponent<Image>();
            graphic.color = Color.white;
            graphic.raycastTarget = true;
            _mask = AddComponent<Mask>();
            _mask.showMaskGraphic = false;
        }

        public override void SetProperty(string propertyName, object value)
        {
            if (propertyName == "show-graphic")
                _mask.showMaskGraphic = Convert.ToBoolean(value);
            else
                base.SetProperty(propertyName, value);
        }
    }

    /// <summary>
    /// <c>&lt;rectmask&gt;</c> — RectMask2D clipping without a stencil texture;
    /// <c>softness="x,y"</c> adds the soft-edge fade used by list edges.
    /// </summary>
    internal sealed class UnityHtmlRectMaskComponent : UGUIComponent
    {
        private readonly RectMask2D _mask;

        public UnityHtmlRectMaskComponent(UGUIContext context) : base(context, "rectmask")
        {
            _mask = AddComponent<RectMask2D>();
        }

        public override void SetProperty(string propertyName, object value)
        {
            if (propertyName == "softness" && value != null)
            {
                var parts = value.ToString().Split(',');
                float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x);
                float y = x;
                if (parts.Length > 1)
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out y);
                _mask.softness = new Vector2Int(Mathf.RoundToInt(x), Mathf.RoundToInt(y));
                return;
            }
            base.SetProperty(propertyName, value);
        }
    }
}
