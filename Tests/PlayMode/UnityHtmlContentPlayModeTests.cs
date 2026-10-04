using System;
using System.Collections;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityHTML.Runtime;
using UnityHTML.Runtime.Content;

namespace UnityHTML.Tests
{
    public sealed class UnityHtmlContentPlayModeTests
    {
        GameObject _root;
        [UnityTearDown] public IEnumerator Cleanup() { if (_root != null) UnityEngine.Object.Destroy(_root); yield return null; }
        sealed class SlowTransport : IContentTransport
        {
            public int Completed, Cancelled;
            public IEnumerator Fetch(ContentSource source, ContentVariant variant, CancellationToken token, Action<ContentFetchResult> done)
            {
                for (int i = 0; i < 3; i++) { if (token.IsCancellationRequested) { Cancelled++; yield break; } yield return null; }
                if (token.IsCancellationRequested) { Cancelled++; yield break; }
                Completed++; done(new ContentFetchResult { Body = variant.url.EndsWith("first") ? "First" : "Latest" });
            }
        }
        static ContentSource Source(string path) => new ContentSource { allowedOrigins = new[] { "https://publisher.org" },
            variants = new[] { new ContentVariant { language = "en", url = "https://publisher.org/" + path, revision = "r1" } } };

        [UnityTest] public IEnumerator NewRequestOwnsTheFieldAndCancelledRequestCannotReplaceIt()
        {
            _root = new GameObject("Content ownership"); var field = _root.AddComponent<UnityHtmlContentBinding>();
            var transport = new SlowTransport(); var loader = new UnityHtmlContentLoader(transport: transport);
            int notifications = 0; field.Changed += () => notifications++;
            field.Load(Source("first"), "en", loader); yield return null;
            field.Load(Source("latest"), "en", loader);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual("Latest", field.Document.PlainText); Assert.AreEqual(1, transport.Completed);
            Assert.AreEqual(3, notifications, "Two loading states and one completed state, no stale callback.");
        }
        [UnityTest] public IEnumerator DisablingAReaderCancelsWithoutPublishingOldText()
        {
            _root = new GameObject("Disabled content"); var field = _root.AddComponent<UnityHtmlContentBinding>();
            var transport = new SlowTransport(); field.Load(Source("first"), "en", new UnityHtmlContentLoader(transport: transport));
            yield return null; field.enabled = false;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsNull(field.Document); Assert.IsFalse(field.Loading); Assert.AreEqual(0, transport.Completed);
        }
        [UnityTest] public IEnumerator RenderedSourceCannotInjectButtonsOrTmpRichText()
        {
            _root = new GameObject("Safe content", typeof(RectTransform), typeof(Canvas));
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(600,800);
            using var host = new UnityHtmlHost(); host.NativeEventResolver = _ => null;
            var variant = Source("first").variants[0];
            string body = "<button>literal</button> <color=red>original words</color>";
            var document = UnityHtmlContentReader.Decode(variant, body, ContentOrigin.Bundled);
            var result = host.Mount((RectTransform)_root.transform, new UnityHtmlDocument(UnityHtmlContentReader.Render(document), "text { font-size:28px; }"));
            Assert.IsTrue(result.Succeeded, result.ErrorMessage); yield return null;
            Assert.AreEqual(0, _root.GetComponentsInChildren<UnityEngine.UI.Button>().Length);
            var text = _root.GetComponentsInChildren<TMP_Text>().Single();
            Assert.IsFalse(text.richText); Assert.AreEqual(body, text.text);
        }
    }
}
