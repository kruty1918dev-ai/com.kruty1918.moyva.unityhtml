using UnityEngine;

namespace UnityHTML.Runtime
{
    public readonly struct UnityHtmlDocument
    {
        public UnityHtmlDocument(string html, string css = null, string sourceName = null)
        {
            Html = StripUtf8Bom(html);
            Css = StripUtf8Bom(css);
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? "UnityHTML Document" : sourceName.Trim();
        }

        public string Html { get; }
        public string Css { get; }
        public string SourceName { get; }
        public bool HasHtml => !string.IsNullOrWhiteSpace(Html);

        public static UnityHtmlDocument FromTextAssets(TextAsset htmlAsset, TextAsset cssAsset = null, string sourceName = null)
        {
            var resolvedName = !string.IsNullOrWhiteSpace(sourceName)
                ? sourceName
                : htmlAsset != null
                    ? htmlAsset.name
                    : "Missing UnityHTML Document";

            return new UnityHtmlDocument(htmlAsset != null ? htmlAsset.text : null, cssAsset != null ? cssAsset.text : null, resolvedName);
        }

        private static string StripUtf8Bom(string value)
        {
            return !string.IsNullOrEmpty(value) && value[0] == '\uFEFF'
                ? value.Substring(1)
                : value;
        }
    }
}
