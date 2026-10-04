using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityHTML.Runtime;
using UnityHTML.Runtime.Content;

/// <summary>Attach to a RectTransform under a Canvas. Bundled content runs with no server.</summary>
public sealed class RemoteDocumentExample : MonoBehaviour
{
    public TMP_FontAsset font;
    public string locale = "en";
    public ContentSource source = new ContentSource
    {
        id = "example-notice", defaultLanguage = "en",
        variants = new[] { new ContentVariant { language = "en", revision = "draft-1",
            bundledBody = "Example notice — draft\n\nReplace this placeholder with your approved document before publishing." } }
    };
    readonly UnityHtmlHost _host = new UnityHtmlHost();
    UnityHtmlContentBinding _field;
    bool _dirty;
    void OnEnable()
    {
        if (_field == null) { _field = gameObject.AddComponent<UnityHtmlContentBinding>(); _field.Changed += Refresh; }
        _host.NativeEventResolver = expression => expression == "OpenSource()" ? new System.Action<object, object>((_, __) => _field.OpenSource(source)) : null;
        _field.Load(source, locale, new UnityHtmlContentLoader(System.IO.Path.Combine(Application.persistentDataPath, "example-document-cache")));
    }
    void Refresh() => _dirty = true;
    void LateUpdate()
    {
        if (!_dirty || ((RectTransform)transform).rect.width < 1) return;
        _dirty = false;
        string body = _field.Document == null ? "<text>" + UnityHtmlContentReader.Escape(_field.Loading ? "Loading…" : _field.Error) + "</text>" : _field.Render();
        string html = "<view class='page' data-safe-area='all'><text class='title'>Document</text><scroll class='body'><view>" + body
            + "</view></scroll><button onClick='OpenSource()'" + (_field.CanOpenSource(source) ? "" : " disabled='true'") + "><text>Original source</text></button></view>";
        var result = _host.Mount((RectTransform)transform, new UnityHtmlDocument(html,
            ".page { width:100%; height:100%; padding:24px; background-color:#f5efdc; } .title { font-size:36px; margin-bottom:20px; } .body { flex-grow:1; min-height:60px; } text { font-size:26px; color:#243e35; white-space:normal; } .remote-paragraph { margin-bottom:20px; } button { min-height:64px; margin-top:20px; background-color:#b6c8ae; }", "Remote document example"),
            new Dictionary<string, object> { ["moyvaFont"] = font });
        if (!result.Succeeded) Debug.LogError(result.ErrorMessage);
    }
    void OnDisable() { _field?.Cancel(); _host.Unmount(); }
    void OnDestroy() { if (_field != null) _field.Changed -= Refresh; _host.Dispose(); }
}
