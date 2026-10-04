using System;
using System.Threading;
using UnityEngine;

namespace UnityHTML.Runtime.Content
{
    /// <summary>Reusable UI field controller. Bind local keys to a localization module; remote prose uses approved locale variants.</summary>
    public sealed class UnityHtmlContentBinding : MonoBehaviour
    {
        public ContentDocument Document { get; private set; }
        public string Error { get; private set; }
        public bool Loading { get; private set; }
        public event Action Changed;
        CancellationTokenSource _cancel;
        Coroutine _load;
        int _generation;

        public void Load(ContentSource source, string deviceLocale, UnityHtmlContentLoader loader = null)
        {
            Cancel(); int generation = ++_generation;
            _cancel = new CancellationTokenSource();
            Document = null; Error = null; Loading = true; Changed?.Invoke();
            _load = StartCoroutine((loader ?? new UnityHtmlContentLoader()).Load(source, deviceLocale, result =>
            {
                if (generation != _generation || !isActiveAndEnabled) return;
                Document = result.Document; Error = result.Error; Loading = false; _load = null; Changed?.Invoke();
            }, _cancel.Token));
        }
        public string Render(string id = "remote-document") => Document == null ? "" : UnityHtmlContentReader.Render(Document, id);
        public bool CanOpenSource(ContentSource source) => Document != null && source.Allows(Document.SourceUrl);
        public void OpenSource(ContentSource source) { if (CanOpenSource(source)) Application.OpenURL(Document.SourceUrl); }
        public void Cancel()
        {
            ++_generation; _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null;
            if (_load != null) StopCoroutine(_load); _load = null; Loading = false;
        }
        void OnDisable() => Cancel();
        void OnDestroy() => Cancel();
    }
}
