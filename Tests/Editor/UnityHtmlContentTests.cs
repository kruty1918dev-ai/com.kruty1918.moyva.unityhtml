using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityHTML.Runtime.Content;

namespace UnityHTML.Tests
{
    public sealed class UnityHtmlContentTests
    {
        string _cache;
        [SetUp] public void Setup() { _cache = Path.Combine(Path.GetTempPath(), "unityhtml-content-" + Guid.NewGuid()); }
        [TearDown] public void Cleanup() { if (Directory.Exists(_cache)) Directory.Delete(_cache, true); }
        sealed class Transport : IContentTransport
        {
            public string Body, Error; public int Calls;
            public IEnumerator Fetch(ContentSource source, ContentVariant variant, CancellationToken cancellation, Action<ContentFetchResult> done)
            { Calls++; done(new ContentFetchResult { Body = Body, Error = Error }); yield break; }
        }
        static void Run(IEnumerator routine)
        { while (routine.MoveNext()) if (routine.Current is IEnumerator nested) Run(nested); }
        static ContentSource Source(ContentFormat format = ContentFormat.PlainText) => new ContentSource
        {
            id = "policy", defaultLanguage = "en", allowedOrigins = new[] { "https://publisher.org" },
            variants = new[] { new ContentVariant { language = "en", url = "https://publisher.org/privacy", revision = "r1", format = format } }
        };
        [Test] public void LocaleUsesExactThenNeutralThenApprovedDefault()
        {
            var source = Source(); source.variants = new[] { source.variants[0], new ContentVariant { language = "de" }, new ContentVariant { language = "de-AT" } };
            Assert.AreEqual("de-AT", source.Select("de_AT").language);
            Assert.AreEqual("de", source.Select("de-CH").language);
            Assert.AreEqual("en", source.Select("fr").language);
        }
        [TestCase("http://publisher.org/privacy")]
        [TestCase("https://publisher.org.evil.org/privacy")]
        [TestCase("https://user:secret@publisher.org/privacy")]
        [TestCase("https://127.0.0.1/privacy")]
        [TestCase("https://publisher.org:444/privacy")]
        public void UntrustedRequestNeverReachesTransport(string url)
        {
            var source = Source(); source.variants[0].url = url;
            var transport = new Transport { Body = "wrong" }; ContentResult result = null;
            Run(new UnityHtmlContentLoader(_cache, transport).Load(source, "en", r => result = r));
            Assert.AreEqual(0, transport.Calls); Assert.IsFalse(result.Succeeded);
        }
        [Test] public void HtmlSelectionKeepsWordsAndRemovesExecutableMarkup()
        {
            string body = "<!doctype html><html><head><title>Site</title></head><body><nav>Ignore</nav><article id='privacy'><h1>Privacy &amp; care</h1><p>We <strong>store</strong> local data.</p><script>Globals.attack()</script><ul><li>Delete any time.</li></ul></article><footer>Ignore</footer></body></html>";
            var variant = Source(ContentFormat.Html).variants[0]; variant.selector = "#privacy";
            var doc = UnityHtmlContentReader.Decode(variant, body, ContentOrigin.Network);
            Assert.AreEqual("Privacy & care\n\nWe store local data.\n\nDelete any time.", doc.PlainText);
            Assert.AreEqual(body, doc.OriginalBody); Assert.AreEqual("heading", doc.Blocks[0].Kind);
            string rendered = UnityHtmlContentReader.Render(doc);
            Assert.IsFalse(rendered.Contains("Globals.attack")); Assert.IsFalse(rendered.Contains("<script"));
            Assert.IsTrue(rendered.Contains("Privacy &amp; care"));
        }
        [Test] public void MissingOrTruncatedSelectionIsAnErrorInsteadOfPartialPolicy()
        {
            var variant = Source(ContentFormat.Html).variants[0]; variant.selector = "#policy";
            Assert.Throws<FormatException>(() => UnityHtmlContentReader.Decode(variant, "<article>Other</article>", ContentOrigin.Network));
            Assert.Throws<FormatException>(() => UnityHtmlContentReader.Decode(variant, "<article id='policy'><p>Truncated", ContentOrigin.Network));
        }
        [Test] public void WebsitePresentationChangesDoNotChangeAcknowledgedPolicyWords()
        {
            var variant = Source(ContentFormat.Html).variants[0]; variant.selector = "article";
            var first = UnityHtmlContentReader.Decode(variant, "<article><p>Same words.</p></article><footer>Yesterday</footer>", ContentOrigin.Network);
            var next = UnityHtmlContentReader.Decode(variant, "<article class='new-style'><p><strong>Same</strong> words.</p></article><footer>Today</footer>", ContentOrigin.Network);
            Assert.AreEqual(first.ContentHash, next.ContentHash); Assert.AreNotEqual(first.OriginalBodyHash, next.OriginalBodyHash);
        }
        [Test] public void PlainTextCannotInjectNativeControlsOrTmpFormatting()
        {
            var doc = UnityHtmlContentReader.Decode(Source().variants[0], "<button onClick='evil'>Words</button>", ContentOrigin.Bundled);
            string rendered = UnityHtmlContentReader.Render(doc);
            Assert.IsFalse(rendered.Contains("<button")); Assert.IsTrue(rendered.Contains("richText=\"false\""));
        }
        [TestCase("de", "r1")]
        [TestCase("en", "r2")]
        public void ApiCannotSilentlyChangeApprovedLanguageOrRevision(string lang, string revision)
        {
            var variant = Source(ContentFormat.Json).variants[0];
            Assert.Throws<FormatException>(() => UnityHtmlContentReader.Decode(variant, "{\"body\":\"Policy\",\"language\":\"" + lang + "\",\"revision\":\"" + revision + "\"}", ContentOrigin.Network));
        }
        [Test] public void ApiBodyIsVerbatimAndHtmlIsRenderedInertly()
        {
            var doc = UnityHtmlContentReader.Decode(Source(ContentFormat.Json).variants[0], "{\"body\":\"<p>Keep words.</p>\",\"language\":\"en\",\"revision\":\"r1\",\"format\":\"html\"}", ContentOrigin.Network);
            Assert.AreEqual("Keep words.", doc.PlainText); Assert.AreEqual("<p>Keep words.</p>", doc.OriginalBody);
        }
        [Test] public void NetworkRefreshChangesHashAndOfflineUsesLastValidCopy()
        {
            var source = Source(); var transport = new Transport { Body = "First version." };
            var loader = new UnityHtmlContentLoader(_cache, transport); ContentResult first = null, next = null, offline = null;
            Run(loader.Load(source, "en", r => first = r)); transport.Body = "Updated words.";
            Run(loader.Load(source, "en", r => next = r));
            Assert.AreNotEqual(first.Document.ContentHash, next.Document.ContentHash);
            transport.Body = null; transport.Error = "offline"; Run(loader.Load(source, "en", r => offline = r));
            Assert.AreEqual(ContentOrigin.Cache, offline.Document.Origin); Assert.AreEqual(next.Document.ContentHash, offline.Document.ContentHash);
            Assert.AreEqual("offline", offline.Error);
        }
        [Test] public void InvalidUpdateDoesNotReplaceGoodCacheAndRevisionSeparatesCaches()
        {
            var source = Source(ContentFormat.Html); source.variants[0].selector = "article";
            var transport = new Transport { Body = "<article><p>Good.</p></article>" };
            var loader = new UnityHtmlContentLoader(_cache, transport); ContentResult result = null;
            Run(loader.Load(source, "en", r => result = r)); transport.Body = "<article>Incomplete";
            Run(loader.Load(source, "en", r => result = r)); Assert.AreEqual("Good.", result.Document.PlainText);
            source.variants[0].revision = "r2"; transport.Error = "offline";
            Run(loader.Load(source, "en", r => result = r)); Assert.IsFalse(result.Succeeded);
        }
        [Test] public void CorruptCacheCannotBecomeAValidDocument()
        {
            var source = Source(); var transport = new Transport { Body = "Approved" }; var loader = new UnityHtmlContentLoader(_cache, transport);
            Run(loader.Load(source, "en", _ => { })); File.WriteAllText(Directory.GetFiles(_cache).Single(), "{\"payload\":\"Forged\",\"hash\":\"wrong\",\"savedUtcTicks\":1}");
            transport.Error = "offline"; ContentResult result = null;
            Run(loader.Load(source, "en", r => result = r)); Assert.IsFalse(result.Succeeded);
        }
        [Test] public void OversizedResponseFallsBackToBundledVersion()
        {
            var source = Source(); source.maximumBytes = 256; source.variants[0].bundledBody = "Bundled policy";
            var transport = new Transport { Body = new string('x', 257) }; ContentResult result = null;
            Run(new UnityHtmlContentLoader(_cache, transport).Load(source, "en", r => result = r));
            Assert.AreEqual(ContentOrigin.Bundled, result.Document.Origin); Assert.IsNotEmpty(result.Error);
        }
        [Test] public void CancellationDoesNotPublishOrCacheAReceipt()
        {
            var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var transport = new Transport { Body = "Policy" }; bool called = false;
            Run(new UnityHtmlContentLoader(_cache, transport).Load(Source(), "en", _ => called = true, cancellation.Token));
            Assert.IsFalse(called); Assert.AreEqual(0, transport.Calls); Assert.IsFalse(Directory.Exists(_cache)); cancellation.Dispose();
        }
        [Test] public void SourceLinkCanPointToTheWebsiteWhileBodyComesFromAnApi()
        {
            var variant = Source(ContentFormat.Json).variants[0]; variant.sourceUrl = "https://publisher.org/policy-readable";
            var doc = UnityHtmlContentReader.Decode(variant, "{\"body\":\"Exact words\",\"language\":\"en\",\"revision\":\"r1\"}", ContentOrigin.Network);
            Assert.AreEqual(variant.sourceUrl, doc.SourceUrl); Assert.AreEqual("Exact words", doc.OriginalBody);
        }
        [Test] public void ExpiredCopyDoesNotAcknowledgeAnOldDocument()
        {
            var source = Source(); var transport = new Transport { Body = "Approved" }; var loader = new UnityHtmlContentLoader(_cache, transport);
            Run(loader.Load(source, "en", _ => { })); var file = Directory.GetFiles(_cache).Single();
            string json = File.ReadAllText(file);
            json = System.Text.RegularExpressions.Regex.Replace(json, @"\""savedUtcTicks\"":\d+", "\"savedUtcTicks\":" + DateTime.UtcNow.AddDays(-10).Ticks); File.WriteAllText(file, json);
            transport.Error = "offline"; ContentResult result = null; Run(loader.Load(source, "en", r => result = r));
            Assert.IsFalse(result.Succeeded);
        }
    }
}
