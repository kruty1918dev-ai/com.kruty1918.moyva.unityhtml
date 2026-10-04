# Remote documents

Import from Package Manager → Moyva UnityHTML → Samples → Remote documents.
Create a screen-space Canvas, stretch a child RectTransform and add
`RemoteDocumentExample`. Assign a TMP font for the language you need. Enter Play
Mode: a bundled, clearly marked draft appears without network access.

For your server, set `source.allowedOrigins` to its exact HTTPS origin and fill
the variant URL/format/locale/revision. Keep approved locale variants and their
bundled offline copies together. The example has no acceptance action: your
application owns acknowledgement receipts, optional consent and navigation.

See [the complete guide](../../Documentation~/remote-content.md) for API schema,
localization integration, safe formatting, cache and first-run flows.
