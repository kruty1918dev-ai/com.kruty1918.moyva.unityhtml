using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Scripting;

namespace UnityHTML.Runtime.Content
{
    public sealed class ContentFetchResult
    {
        public string Body, Error;
        public bool Succeeded => Error == null && Body != null;
    }
    /// <summary>Main-thread coroutine transport. Dependency injection allows offline deterministic checks.</summary>
    public interface IContentTransport
    {
        IEnumerator Fetch(ContentSource source, ContentVariant variant, CancellationToken cancellation, Action<ContentFetchResult> completed);
    }

    public sealed class UnityWebContentTransport : IContentTransport
    {
        public IEnumerator Fetch(ContentSource source, ContentVariant variant, CancellationToken cancellation, Action<ContentFetchResult> completed)
        {
            source.Validate();
            if (!source.Allows(variant.url)) { completed(new ContentFetchResult { Error = "Document URL is not an approved HTTPS origin." }); yield break; }
            using var handler = new LimitedDownload(source.maximumBytes);
            using var request = new UnityWebRequest(variant.url, "GET", handler, null);
            request.timeout = source.timeoutSeconds;
            // Cross-origin redirects must never send a request before validation. Publish a direct final URL.
            request.redirectLimit = 0;
            request.SetRequestHeader("Accept", "application/json, text/html, text/plain");
            request.SetRequestHeader("Accept-Language", variant.language);
            using var cancellationRegistration = cancellation.Register(request.Abort);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellation.IsCancellationRequested) { request.Abort(); yield break; }
                yield return null;
            }
            if (cancellation.IsCancellationRequested) yield break;
            if (request.result != UnityWebRequest.Result.Success || request.responseCode < 200 || request.responseCode >= 300 || handler.Exceeded)
                completed(new ContentFetchResult { Error = handler.Exceeded ? "Document exceeds the size limit." : "Document request failed (" + request.responseCode + ")." });
            else
            {
                string body = null, error = null;
                try { body = new UTF8Encoding(false, true).GetString(handler.Bytes); }
                catch (DecoderFallbackException) { error = "Document is not valid UTF-8."; }
                completed(new ContentFetchResult { Body = body, Error = error });
            }
        }
        sealed class LimitedDownload : DownloadHandlerScript
        {
            readonly MemoryStream _buffer = new MemoryStream(); readonly int _maximum;
            public bool Exceeded { get; private set; }
            public byte[] Bytes => _buffer.ToArray();
            public LimitedDownload(int maximum) : base(new byte[8192]) { _maximum = maximum; }
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (data == null || length <= 0) return true;
                if (_buffer.Length + length > _maximum) { Exceeded = true; return false; }
                _buffer.Write(data, 0, length); return true;
            }
            protected override void ReceiveContentLengthHeader(ulong length) { if (length > (ulong)_maximum) Exceeded = true; }
            protected override byte[] GetData() => Bytes;
        }
    }

    /// <summary>Network first, validated local cache second, bundled approved document last. No telemetry or application identifiers.</summary>
    public sealed class UnityHtmlContentLoader
    {
        readonly IContentTransport _transport; readonly string _cacheDirectory;
        public UnityHtmlContentLoader(string cacheDirectory = null, IContentTransport transport = null)
        { _transport = transport ?? new UnityWebContentTransport(); _cacheDirectory = cacheDirectory; }

        [Serializable, Preserve] sealed class Cached
        { public string payload, hash; public long savedUtcTicks; }

        public IEnumerator Load(ContentSource source, string deviceLocale, Action<ContentResult> completed, CancellationToken cancellation = default)
        {
            ContentVariant variant = null; string error = null;
            try { source.Validate(); variant = source.Select(deviceLocale); }
            catch (Exception exception) { error = exception.Message; }
            if (variant == null) { if (!cancellation.IsCancellationRequested) completed(new ContentResult { Error = error }); yield break; }
            if (cancellation.IsCancellationRequested) yield break;
            ContentFetchResult fetched = null;
            if (!string.IsNullOrWhiteSpace(variant.url))
            {
                // Validate also with injected transports so allow-list cannot be bypassed by adapters.
                if (source.Allows(variant.url))
                    yield return _transport.Fetch(source, variant, cancellation, result => fetched = result);
                else error = "Document URL is not an approved HTTPS origin.";
            }
            if (cancellation.IsCancellationRequested) yield break;
            ContentDocument document = null;
            if (fetched?.Succeeded == true)
            {
                document = Decode(source, variant, fetched.Body, ContentOrigin.Network, out error);
                if (document != null) SaveCache(source, variant, fetched.Body);
            }
            else if (fetched != null) error = fetched.Error;
            if (document == null && source.cacheMaxAgeHours > 0)
            {
                string cached = ReadCache(source, variant);
                if (cached != null) document = Decode(source, variant, cached, ContentOrigin.Cache, out _);
            }
            if (document == null && !string.IsNullOrEmpty(variant.bundledBody))
                document = Decode(source, variant, variant.bundledBody, ContentOrigin.Bundled, out _);
            if (!cancellation.IsCancellationRequested)
                completed(new ContentResult { Document = document, Error = error ?? (document == null ? "No valid document is available." : null) });
        }

        static ContentDocument Decode(ContentSource source, ContentVariant variant, string payload, ContentOrigin origin, out string error)
        {
            error = null;
            try
            {
                if (Encoding.UTF8.GetByteCount(payload) > source.maximumBytes) throw new FormatException("Document exceeds the size limit.");
                return UnityHtmlContentReader.Decode(variant, payload, origin);
            }
            catch (Exception exception) { error = exception.Message; return null; }
        }
        string CachePath(ContentSource source, ContentVariant variant) => string.IsNullOrEmpty(_cacheDirectory) ? null
            : Path.Combine(_cacheDirectory, ContentDocument.Hash(source.id + "\n" + variant.language + "\n" + variant.url + "\n" + variant.sourceUrl + "\n" + variant.revision + "\n" + variant.format + "\n" + variant.selector) + ".json");
        void SaveCache(ContentSource source, ContentVariant variant, string payload)
        {
            string path = CachePath(source, variant); if (path == null) return;
            try
            {
                Directory.CreateDirectory(_cacheDirectory);
                string json = JsonUtility.ToJson(new Cached { payload = payload, hash = ContentDocument.Hash(payload), savedUtcTicks = DateTime.UtcNow.Ticks });
                File.WriteAllText(path + ".tmp", json, Encoding.UTF8);
                // Rename in the same directory prevents interrupted writes becoming valid documents.
                if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException) { /* Cache is optional. */ }
        }
        string ReadCache(ContentSource source, ContentVariant variant)
        {
            string path = CachePath(source, variant); if (path == null) return null;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > source.maximumBytes * 6L + 2048) return null;
                var cached = JsonUtility.FromJson<Cached>(File.ReadAllText(path));
                if (cached == null || cached.payload == null || cached.hash != ContentDocument.Hash(cached.payload)) return null;
                long age = DateTime.UtcNow.Ticks - cached.savedUtcTicks;
                if (age < 0 || age > TimeSpan.FromHours(source.cacheMaxAgeHours).Ticks) return null;
                return cached.payload;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException) { return null; }
        }
        /// <summary>Remove only this reader's cache folder; choose a dedicated directory at construction.</summary>
        public void ClearCache() { if (!string.IsNullOrEmpty(_cacheDirectory) && Directory.Exists(_cacheDirectory)) Directory.Delete(_cacheDirectory, true); }
    }
}
