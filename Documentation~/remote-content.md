# Remote documents and first-run disclosures

UnityHTML can fetch a publisher's document and render its words inside a native,
scrollable game UI. Use it for privacy notices, help, release notes and service
information that must stay in step with a website or API. It is a document reader,
not a web browser: no website JavaScript, images, iframes, styles or event
handlers are executed or mounted.

## Quick start

Install UnityHTML as described in the [README](../README.md), including DOTween
and the pinned ReactUnity dependencies. Import the **Remote documents** sample
from Package Manager. Its bundled notice works without a server. Attach
`RemoteDocumentExample` to a Canvas child with a `RectTransform` and set its
optional font. Replace the placeholder with your own publisher-approved source.

```csharp
using UnityHTML.Runtime.Content;

var source = new ContentSource {
    id = "privacy", defaultLanguage = "en",
    allowedOrigins = new[] { "https://your-publisher.org" },
    variants = new[] {
        new ContentVariant {
            language = "en", url = "https://your-publisher.org/privacy",
            revision = "2026-10-04", format = ContentFormat.Html,
            selector = "#privacy-content",
            bundledBody = "<article id='privacy-content'><p>Your approved policy.</p></article>"
        }
    }
};
var field = gameObject.AddComponent<UnityHtmlContentBinding>();
field.Changed += () => {
    // Refresh your existing host or update its document region after dispatch.
    // Show loading/error text in the surrounding UI, using your localization module.
    Debug.Log(field.Loading ? "Loading" : field.Document?.Revision ?? field.Error);
};
field.Load(source, localization.CurrentLanguageId,
    new UnityHtmlContentLoader(System.IO.Path.Combine(Application.persistentDataPath, "document-cache")));
// In your renderer: field.Render("privacy-body")
// In an explicit user action: field.OpenSource(source)
```

An enabled binding owns one request. `Load` cancels the previous request and clears
the old document; late results cannot replace the new selection. `OnDisable` and
`OnDestroy` cancel it. Use `Cancel()` on close. Unity networking, binding operations
and cancellation must run on the Unity main thread. Core `Load` is a coroutine;
an injected `IContentTransport` can supply another approved source or deterministic
test responses. No analytics, sign-in or consent platform dependency is added.

## Source and API contract

| Field | Meaning |
| --- | --- |
| `id` | Stable app-owned identifier; cache names are SHA-256, never this raw string. |
| `defaultLanguage` | Required approved fallback locale. |
| `variants` | Publisher-owned language, URL, revision, format, optional bundled body and selector. `sourceUrl` optionally links an API document to its readable website. |
| `allowedOrigins` | Exact HTTPS origins, including port. A subdomain is a separate origin. |
| `maximumBytes` | UTF-8 response cap; default 256 KiB, maximum 2 MiB. |
| `timeoutSeconds` | Default 12 seconds; valid range 1–60. |
| `cacheMaxAgeHours` | Default 168; zero disables cache fallback; maximum 8760. |

`PlainText` preserves paragraph words and line breaks. `Html` extracts one tag
(`article`, `main`, `body`) or element ID (`#privacy-content`); if unspecified,
it chooses `body` when present. HTML entities become visible characters. Heading,
paragraph and list-item boundaries become native text blocks. Scripts, navigation,
footer and non-text media are omitted. Missing or unclosed selected regions fail
instead of displaying an incomplete policy.

For an API set `format = ContentFormat.Json`. Return UTF-8 JSON:

```json
{
  "body": "<article><h1>Privacy</h1><p>Your exact approved words.</p></article>",
  "language": "en",
  "revision": "2026-10-04",
  "format": "html"
}
```

`body`, `language` and `revision` are required. Body format is `text` or `html`.
Language must match the selected variant; when a variant pins a revision, a
different API revision is rejected. Leave revision empty only for non-legal
live content where the API owns version discovery. Arbitrary JSON schemas should
be mapped to this contract by a publisher-controlled API gateway.

Use a stable document-only HTML region or the API for legal content. This small
reader does not reproduce a browser's CSS visibility rules or HTML5 error recovery;
verify your published document's extracted text before release. PDF, JavaScript
apps, authenticated pages and arbitrary CSS selectors are unsupported.

## Automatic language selection and updates

Pass the current locale from your localization service; on first run that service
can resolve the device language. Matching is exact BCP-47 locale, then neutral
language, then configured default. For example `de-AT` selects an approved
`de-AT`, otherwise `de`, otherwise `en`. `IsLanguageFallback(requested)` identifies
when the source differs; display its language to the player. To change language,
call `Load` again from your localization module's change event.

These are approved translations, not automatic machine translations of legal
prose. Local UI keys (loading, retry, source link, summary toggle) use your normal
localization module. If you need machine translation for non-legal help, publish
and label that translated content as a separate API variant. Never silently
change the meaning of a privacy notice or present an unreviewed translation as
the authoritative policy.

Each `Load` re-fetches the configured URL. Changed paragraphs and headings receive
the same native CSS classes automatically. `OriginalBody` retains the publisher's
body; `PlainText` exposes extracted visible text. `ContentHash` covers language,
revision and extracted policy words, so website navigation/style changes do not
cause repeated acknowledgement. `OriginalBodyHash` separately hashes the original
body. The UI may reconcile the document in place; the
application decides whether a changed policy requires renewed acknowledgement.
There is no background polling or hidden network request while the field is closed.

## Presentation without changing the words

`UnityHtmlContentReader.Render(document, regionId)` escapes untrusted text and
disables TextMeshPro rich text. Website tags and callbacks never become controls.
Use local CSS for typography, spacing, color and responsive layout:

```css
.remote-document { width: 100%; }
.remote-heading { font-size: 36px; margin-bottom: 18px; }
.remote-paragraph, .remote-list-item { font-size: 28px; margin-bottom: 24px; }
```

Put the region inside a `<scroll>` with a finite height and reserve space for
actions outside it. Author a short summary separately, label it **Summary**,
and provide **Full text** and **Original source** actions. Do not generate a legal
summary by silently truncating or rewriting the source. Your application can
process `Blocks` for non-legal presentation; retain the original document and
label transformed content rather than replacing its authoritative body/hash.

## Offline, network and cache behavior

Loading tries network, then a valid cache, then a bundled document for the selected
locale. `ContentOrigin` identifies the source. `ContentResult.Error` can accompany
a successful fallback; show that the copy is saved/bundled rather than implying
it was just verified online. If none is valid, keep acknowledgement disabled and
offer retry/exit. Cache fallback is age limited. Bundled content has no automatic
expiry: publishers must bundle an appropriate version or omit it when offline
acceptance would be inappropriate.

Requests allow only approved HTTPS origins, forbid URL credentials and loopback,
disable redirects, bound received bytes before decoding and reject malformed
UTF-8. Configure the final URL directly. Normal certificate validation stays on.
HTTP failures and oversized or invalid payloads never overwrite the last valid
cache. Cache entries are keyed by source, locale, fetch/source URLs, revision, format and selector,
written by atomic replacement, hash checked, age checked and parsed again before
use. Hashes detect accidental cache damage; they are not signatures against a
compromised server or rooted device. Use publisher-controlled TLS endpoints.

Choose a **dedicated** cache folder. `ClearCache()` deletes that folder only; wire
it to your app's local-data deletion workflow. Do not use the root persistent-data
folder. Fetching a document exposes the network address to its server; disclose
server logging appropriately. No player identifiers, analytics events, tokens,
request bodies or authentication headers are added by this reader. Unity's native
cookie engine (and the browser on WebGL) can manage cookies independently; use a
dedicated document origin without cookies and verify the endpoint. The reader
does not clear the engine's shared cookie storage or interfere with other services.
[Unity header behavior](https://docs.unity.com/ja-jp/engine/6000.5/script-reference/unityengine/networking/unitywebrequest/setrequestheader).

## First-run policy acknowledgement

The reader deliberately does not decide legal bases or turn SDKs on. An application
may show a policy before entering gameplay and store a local receipt containing
revision, content hash, language and whether it was a draft. Store it only after
an explicit action and a successful save. Refusal, back, timeout or navigation
must not count as acceptance. A draft receipt must never acknowledge the later
real publication. Keep optional analytics/advertising consent separate and
revocable, without blocking gameplay when declined.

Google Play requires an accessible privacy policy in the listing and the app;
its prominent disclosure/consent requirements cannot be replaced by a generic
policy acknowledgement. EDPB guidance requires genuine choice where consent
is the legal basis. Review your exact processing, audience and jurisdictions
before publishing; this UI mechanism does not establish worldwide legal compliance.

- [Google Play User Data](https://support.google.com/googleplay/android-developer/answer/10144311?hl=en)
- [EDPB consent guidelines](https://www.edpb.europa.eu/documents/guideline/guidelines-052020-on-consent-under-regulation-2016679_en)

## Validation

`UnityHtmlContentTests` exercises locale fallback, origin rejection, inert HTML,
missing/truncated selection, API language/revision, update hashes, cache recovery,
corrupt cache, size caps and cancellation. Importable example and game integration
are checked in Editor; verify your actual production HTTPS endpoint and player
platform before shipping. No Android/iOS performance or legal certification is
implied by Editor results.
