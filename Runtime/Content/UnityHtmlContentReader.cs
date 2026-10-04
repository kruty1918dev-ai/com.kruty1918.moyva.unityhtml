using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityHTML.Runtime.Content
{
    /// <summary>Small inert document reader, not a browser. Scripts, styles, handlers and remote assets never reach the host.</summary>
    public static class UnityHtmlContentReader
    {
        static readonly Regex Tags = new Regex(@"<!--[\s\S]*?-->|<![^>]*>|<(?<close>/)?(?<tag>[A-Za-z][\w:-]*)(?<attrs>(?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.CultureInvariant);
        static readonly Regex Id = new Regex(@"\bid\s*=\s*(?:""(?<id>[^""]*)""|'(?<id>[^']*)'|(?<id>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly HashSet<string> Hidden = new HashSet<string> { "head", "script", "style", "noscript", "template", "svg", "iframe", "nav", "footer" };
        static readonly HashSet<string> Boundaries = new HashSet<string> { "p", "div", "section", "article", "main", "body", "h1", "h2", "h3", "h4", "h5", "h6", "li", "br", "hr", "tr", "blockquote", "pre" };
        static readonly HashSet<string> Void = new HashSet<string> { "br", "hr", "img", "input", "meta", "link", "source", "wbr", "area", "base", "embed", "param", "col", "track" };

        public static ContentDocument Decode(ContentVariant variant, string payload, ContentOrigin origin)
        {
            if (variant == null || string.IsNullOrWhiteSpace(payload)) throw new FormatException("Document is empty.");
            string body = payload, language = variant.language, revision = variant.revision;
            var format = variant.format;
            if (format == ContentFormat.Json)
            {
                var response = JsonUtility.FromJson<ContentApiResponse>(payload);
                if (response == null || string.IsNullOrWhiteSpace(response.body) || string.IsNullOrWhiteSpace(response.language)
                    || string.IsNullOrWhiteSpace(response.revision)) throw new FormatException("API requires body, language and revision.");
                if (ContentSource.NormalizeLocale(response.language) != ContentSource.NormalizeLocale(variant.language))
                    throw new FormatException("API language does not match the approved variant.");
                if (!string.IsNullOrEmpty(variant.revision) && response.revision != variant.revision)
                    throw new FormatException("API revision does not match the configured publication.");
                format = response.format == "html" ? ContentFormat.Html : response.format == "text" ? ContentFormat.PlainText
                    : throw new FormatException("API format must be text or html.");
                body = response.body; language = response.language; revision = response.revision;
            }
            var blocks = format == ContentFormat.Html ? ReadHtml(body, variant.selector) : ReadText(body);
            if (blocks.Count == 0) throw new FormatException("Document contains no visible text in the selected region.");
            return new ContentDocument(body, language, revision, string.IsNullOrWhiteSpace(variant.sourceUrl) ? variant.url : variant.sourceUrl, origin, blocks);
        }

        static List<ContentBlock> ReadText(string body)
        {
            var blocks = new List<ContentBlock>();
            foreach (string paragraph in Regex.Split(body.Replace("\r\n", "\n"), @"\n\s*\n"))
                if (!string.IsNullOrWhiteSpace(paragraph)) blocks.Add(new ContentBlock(paragraph.Trim()));
            return blocks;
        }

        static List<ContentBlock> ReadHtml(string html, string selector)
        {
            var blocks = new List<ContentBlock>(); var buffer = new StringBuilder();
            var stack = new List<(string Tag, bool Hidden)>();
            string wanted = string.IsNullOrWhiteSpace(selector) ? (Regex.IsMatch(html, @"<body\b", RegexOptions.IgnoreCase) ? "body" : "") : selector.Trim();
            if (wanted.Length > 0 && !Regex.IsMatch(wanted, @"^(#[\w-]+|[A-Za-z][\w-]*)$")) throw new FormatException("Use one tag name or #element-id as selector.");
            int selectedDepth = -1, position = 0, hidden = 0; bool completed = false;
            string kind = "paragraph";
            void Flush()
            {
                var text = Regex.Replace(WebUtility.HtmlDecode(buffer.ToString()).Replace('\u00a0', ' '), @"\s+", " ").Trim();
                buffer.Clear(); if (text.Length > 0) blocks.Add(new ContentBlock(text, kind)); kind = "paragraph";
            }
            bool Visible() => hidden == 0 && !completed && (wanted.Length == 0 || selectedDepth >= 0);
            foreach (Match match in Tags.Matches(html))
            {
                if (Visible()) buffer.Append(html, position, match.Index - position);
                position = match.Index + match.Length;
                if (!match.Groups["tag"].Success) continue;
                string tag = match.Groups["tag"].Value.ToLowerInvariant(); bool close = match.Groups["close"].Success;
                if (Visible() && Boundaries.Contains(tag)) Flush();
                if (close)
                {
                    int index = stack.FindLastIndex(t => t.Tag == tag);
                    if (index < 0) continue;
                    if (selectedDepth >= index && selectedDepth >= 0) { Flush(); completed = true; selectedDepth = -1; }
                    for (int i = stack.Count - 1; i >= index; i--) { if (stack[i].Hidden) hidden--; stack.RemoveAt(i); }
                    continue;
                }
                if (hidden == 0 && selectedDepth < 0 && !completed && wanted.Length > 0
                    && (wanted[0] == '#' ? WebUtility.HtmlDecode(Id.Match(match.Groups["attrs"].Value).Groups["id"].Value) == wanted.Substring(1) : tag == wanted.ToLowerInvariant()))
                    selectedDepth = stack.Count;
                if (Visible()) { if (tag.Length == 2 && tag[0] == 'h' && char.IsDigit(tag[1])) kind = "heading"; else if (tag == "li") kind = "list-item"; }
                if (!Void.Contains(tag) && !match.Groups["attrs"].Value.TrimEnd().EndsWith("/"))
                { bool skip = Hidden.Contains(tag); stack.Add((tag, skip)); if (skip) hidden++; }
            }
            if (Visible()) buffer.Append(html, position, html.Length - position);
            Flush();
            if (wanted.Length > 0 && !completed) throw new FormatException("Selected HTML region is missing or unclosed; refusing partial policy text.");
            return blocks;
        }

        /// <summary>Render words safely in native markup. Custom CSS classes style updated content without rewriting it.</summary>
        public static string Render(ContentDocument document, string id = "remote-document")
        {
            if (!Regex.IsMatch(id ?? "", @"^[A-Za-z][\w-]*$")) throw new ArgumentException("Invalid region ID.");
            var html = new StringBuilder("<view id=\"" + id + "\" class=\"remote-document\">");
            foreach (var block in document.Blocks)
                html.Append("<text richText=\"false\" class=\"remote-").Append(block.Kind).Append("\">").Append(Escape(block.Text)).Append("</text>");
            return html.Append("</view>").ToString();
        }
        public static string Escape(string text) => System.Security.SecurityElement.Escape(text ?? "");
    }
}
