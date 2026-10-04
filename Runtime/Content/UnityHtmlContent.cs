using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;

namespace UnityHTML.Runtime.Content
{
    public enum ContentFormat { PlainText, Html, Json }
    public enum ContentOrigin { Network, Cache, Bundled }

    /// <summary>Publisher-approved version. Locale fallback selects a version; it never invents a translation.</summary>
    [Serializable, Preserve] public sealed class ContentVariant
    {
        public string language = "en", url = "", revision = "", bundledBody = "";
        /// <summary>Readable website URL when the fetch URL is an API. Uses the same approved origins.</summary>
        public string sourceUrl = "";
        public ContentFormat format;
        /// <summary>HTML element ID (#policy) or tag (article, main, body). Empty selects body if present.</summary>
        public string selector = "";
    }

    [Serializable, Preserve] public sealed class ContentSource
    {
        public string id = "document", defaultLanguage = "en";
        public ContentVariant[] variants = Array.Empty<ContentVariant>();
        // Origins include scheme and optional port, e.g. https://publisher.org.
        public string[] allowedOrigins = Array.Empty<string>();
        public int maximumBytes = 262144, timeoutSeconds = 12, cacheMaxAgeHours = 168;

        public ContentVariant Select(string deviceLocale)
        {
            string wanted = NormalizeLocale(deviceLocale);
            foreach (var variant in variants ?? Array.Empty<ContentVariant>())
                if (variant != null && NormalizeLocale(variant.language) == wanted) return variant;
            string neutral = wanted.Split('-')[0];
            foreach (var variant in variants ?? Array.Empty<ContentVariant>())
                if (variant != null && NormalizeLocale(variant.language) == neutral) return variant;
            foreach (var variant in variants ?? Array.Empty<ContentVariant>())
                if (variant != null && NormalizeLocale(variant.language) == NormalizeLocale(defaultLanguage)) return variant;
            throw new InvalidOperationException("No approved default-language document is configured.");
        }
        public static string NormalizeLocale(string locale) => (locale ?? "").Trim().Replace('_', '-').ToLowerInvariant();
        public bool Allows(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || !string.IsNullOrEmpty(uri.UserInfo) || uri.IsLoopback || uri.HostNameType != UriHostNameType.Dns) return false;
            foreach (string origin in allowedOrigins ?? Array.Empty<string>())
                if (Uri.TryCreate(origin, UriKind.Absolute, out var trusted)
                    && string.Equals(uri.GetLeftPart(UriPartial.Authority), trusted.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 120 || maximumBytes < 256 || maximumBytes > 2097152
                || timeoutSeconds < 1 || timeoutSeconds > 60 || cacheMaxAgeHours < 0 || cacheMaxAgeHours > 8760)
                throw new ArgumentException("Invalid document limits or identifier.");
        }
    }

    public readonly struct ContentBlock
    {
        public ContentBlock(string text, string kind = "paragraph") { Text = text; Kind = kind; }
        public string Text { get; }
        public string Kind { get; }
    }

    /// <summary>Original publisher body is retained. Only presentation markup is replaced.</summary>
    public sealed class ContentDocument
    {
        internal ContentDocument(string body, string language, string revision, string sourceUrl,
            ContentOrigin origin, IReadOnlyList<ContentBlock> blocks)
        { OriginalBody = body; Language = language; Revision = revision; SourceUrl = sourceUrl;
            Origin = origin; Blocks = blocks; OriginalBodyHash = Hash(body);
            // Website timestamps, styles and navigation must not repeatedly prompt for the same policy words.
            ContentHash = Hash(language + "\n" + revision + "\n" + PlainText); }
        public string OriginalBody { get; }
        public string Language { get; }
        public string Revision { get; }
        public string SourceUrl { get; }
        public string ContentHash { get; }
        public string OriginalBodyHash { get; }
        public ContentOrigin Origin { get; }
        public IReadOnlyList<ContentBlock> Blocks { get; }
        public string PlainText => string.Join("\n\n", System.Linq.Enumerable.Select(Blocks, b => b.Text));
        public bool IsLanguageFallback(string requested) => ContentSource.NormalizeLocale(Language) != ContentSource.NormalizeLocale(requested)
            && ContentSource.NormalizeLocale(Language) != ContentSource.NormalizeLocale(requested).Split('-')[0];
        public static string Hash(string value)
        { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))).Replace("-", "").ToLowerInvariant(); }
    }

    public sealed class ContentResult
    {
        public ContentDocument Document { get; internal set; }
        public string Error { get; internal set; }
        public bool Succeeded => Document != null;
    }

    /// <summary>API response schema. An API gateway can map other providers to this small stable contract.</summary>
    [Serializable, Preserve] public sealed class ContentApiResponse
    {
        public string body, language, revision;
        public string format = "text";
    }
}
