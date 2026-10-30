using Eto.Drawing;
using Eto.Forms;
using Grasshopper2.Components.Standard;
using Grasshopper2.Doc;
using Grasshopper2.Doc.Migration;
using Grasshopper2.Interop;
using Grasshopper2.UI;
using Grasshopper2.UI.Canvas;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IOPath = System.IO.Path;

namespace SuperHelper;

internal sealed class Gh2Example : IHelpExample
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly Guid HopsId = new("C69BB52C-88BA-4640-B69F-188D111029E8");
    internal const string DragFormat = "SuperHelper.GH2.Example";
    private static readonly Dictionary<string, Document> DraggedDocuments = new();
    private static readonly MethodInfo ConvertGh1 = typeof(DocumentIO).GetMethod("Convert1", BindingFlags.Static | BindingFlags.NonPublic,
        null, new[] { typeof(IGH_Document), typeof(MigrationLevel) }, null);
    private readonly string _cacheDirectory = IOPath.Combine(IOPath.GetTempPath(), "SuperHelper.GH2", Guid.NewGuid().ToString("N"));
    private readonly List<string> _downloads = new();
    private readonly List<Task<Document>> _loads = new();
    private CancellationTokenSource _cancellation;
    private Task<Document> _loading;
    private Document _document;
    private bool _disposed;
    private string _path;
    private Image _thumbnail;
    private readonly Image _icon = EtoImageUtilities.FromBitmap(Properties.Resources.SuperHelperIcon_24);
    public string Path
    {
        get => _path;
        set
        {
            if (_disposed || string.Equals(_path, value, StringComparison.Ordinal)) return;
            _path = value;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _document?.Close();
            _document = null;
            FileName = string.IsNullOrWhiteSpace(value) ? "No file" : "Loading…";
            Description = string.Empty;
            _loading = Load(value, _cancellation.Token);
            _loads.Add(_loading);
            Changed();
        }
    }
    public string FileName { get; private set; } = "No file";
    public string Author { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Image ThumbnailImage => _thumbnail;
    public Image IconImage => _icon;
    public event PropertyChangedEventHandler PropertyChanged;
    public Gh2Example(string path = null) { if (path != null) Path = path; }
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private async Task<Document> Load(string source, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        try
        {
            var file = source;
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            {
                var bytes = await Client.GetByteArrayAsync(uri, token);
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(_cacheDirectory);
                file = IOPath.Combine(_cacheDirectory, Guid.NewGuid().ToString("N") + IOPath.GetExtension(uri.AbsolutePath));
                _downloads.Add(file);
                await File.WriteAllBytesAsync(file, bytes, token);
            }
            token.ThrowIfCancellationRequested();
            return await OnUi(() =>
            {
                token.ThrowIfCancellationRequested();
                if (_disposed) return null;
                var doc = ReadDocument(file);
                if (doc == null) throw new IOException("The example could not be read.");
                _document = doc;
                FileName = IOPath.GetFileName(Uri.TryCreate(source, UriKind.Absolute, out var original) && original.Scheme is "https" or "http" ? original.AbsolutePath : source);
                Description = doc.Notes ?? string.Empty;
                if (doc.Objects.Forwards.FirstOrDefault() is { } first && first.GetCreationInfo(out var author, out _)) Author = author;
                _thumbnail?.Dispose();
                _thumbnail = DocumentIO.TryHarvestCanvasThumbnail(file);
                Changed();
                return doc;
            });
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception exception)
        {
            await OnUi(() =>
            {
                if (!_disposed && !token.IsCancellationRequested)
                {
                    FileName = "Invalid";
                    Description = exception.GetBaseException().Message;
                    Changed();
                }
                return true;
            });
            return null;
        }
    }

    internal static Document ReadDocument(string path)
    {
        if (IOPath.GetExtension(path).Equals(".gh", StringComparison.OrdinalIgnoreCase) || IOPath.GetExtension(path).Equals(".ghx", StringComparison.OrdinalIgnoreCase))
        {
            // Preview without GH2's interactive migration dialog, preserving native
            // migration of components, data and wires. Detect SDK drift explicitly.
            if (ConvertGh1 == null) throw new NotSupportedException("This GH2 SDK does not expose the expected GH1 migration entry point.");
            var old = Grasshopper1.Instance.CreateDocument(path) ?? throw new IOException("The Grasshopper 1 example could not be read.");
            try { return (Document)ConvertGh1.Invoke(null, new object[] { old, MigrationLevel.Total }); }
            finally { (old.Underlying as IDisposable)?.Dispose(); }
        }
        var io = new DocumentIO(null, trackFiles: false, reportErrors: false, retainRoot: false, resolvePlugins: false);
        if (!io.Open(path)) throw new IOException("The Grasshopper 2 example could not be read.");
        io.Document.State = DocumentState.Inert;
        return io.Document;
    }

    public async Task CopyFromArchive()
    {
        var doc = _loading == null ? null : await _loading;
        if (_disposed || doc == null) return;
        await OnUi(() => CopyWholeDocument(doc, ClipboardKind.Global));
    }

    public async Task<bool> DragFrom(Control owner)
    {
        var doc = _loading == null ? null : await _loading;
        // The card already checks MouseDown's Primary button. Querying the
        // global physical button state here rejects valid Mac/remote input and
        // falls back to inserting a copy even when the drag is cancelled.
        if (_disposed || doc == null) return false;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = Guid.NewGuid().ToString("N");
        DataObject data = null;
        EventHandler<DragEventArgs> ended = (_, _) => completion.TrySetResult(true);
        EventHandler<EventArgs> unloaded = (_, _) => completion.TrySetResult(true);
        try
        {
            await OnUi(() =>
            {
                DraggedDocuments.Add(token, doc);
                data = new DataObject();
                data.SetString(token, DragFormat);
                owner.DragEnd += ended;
                owner.UnLoad += unloaded;
                // Eto.Mac starts NSDraggingSession from the supplied image.
                // Without one it creates no dragging items and no drop occurs.
                var image = _thumbnail ?? _icon;
                owner.DoDragDrop(data, DragEffects.Copy, image, new PointF(image.Width / 2f, image.Height / 2f));
                return true;
            });
            // Eto's DoDragDrop starts an asynchronous native drag on Mac. Keep
            // both payload and token alive until DragEnd, including cancellation.
            // A cancelled drag was handled and must not fall back to inserting.
            return await completion.Task;
        }
        finally
        {
            await OnUi(() =>
            {
                owner.DragEnd -= ended;
                owner.UnLoad -= unloaded;
                DraggedDocuments.Remove(token);
                data?.Dispose();
                return true;
            });
        }
    }
    internal static bool TryGetDragged(DataObject data, out Document document)
    {
        document = null;
        return data.Contains(DragFormat) && DraggedDocuments.TryGetValue(data.GetString(DragFormat), out document);
    }

    internal static bool CopyWholeDocument(Document source, ClipboardKind clipboard)
    {
        // DocumentIO.Copy serializes selected objects only. An example is a
        // complete definition even when the saved file has no selection.
        var selection = source.Objects.Forwards.Select(obj => (Object: obj, obj.Selection)).ToArray();
        try
        {
            source.Methods.SelectAll();
            return new DocumentIO(source, false, true, false, false).Copy(clipboard);
        }
        finally
        {
            foreach (var item in selection) item.Object.Selection = item.Selection;
        }
    }

    internal static bool Insert(Document source, Grasshopper2.UI.Canvas.Canvas canvas, PointF? location = null)
    {
        if (!CopyWholeDocument(source, ClipboardKind.Local)) return false;
        var editor = Editor.Instance;
        if (canvas.Document == null) editor.Documents.Push(Document.NewActiveDocument(), null);
        var target = canvas.Document;
        var oldIds = target.Objects.Forwards.Select(obj => obj.InstanceId).ToHashSet();
        var actions = new Grasshopper2.Undo.ActionList();
        if (!target.Methods.PasteFromClipboard(ClipboardKind.Local, PasteBehaviour.Centre | PasteBehaviour.SelectNewObjects | PasteBehaviour.DeselectOldObjects, actions)) return false;
        if (location.HasValue)
        {
            var added = target.Objects.Forwards.Where(obj => !oldIds.Contains(obj.InstanceId)).ToArray();
            var bounds = added.Select(obj => obj.Attributes.Bounds).Aggregate(RectangleF.Union);
            var offset = location.Value - bounds.Center;
            foreach (var obj in added) obj.Attributes.Move(offset.X, offset.Y);
        }
        target.Undo.Do(new Grasshopper2.Undo.VerbNoun("Insert", "example"), actions);
        target.Modify();
        _ = target.Solution.Start(SolutionMode.Regular);
        canvas.Focus();
        canvas.Invalidate();
        return true;
    }

    public async Task PasteFromArchive(bool asHops)
    {
        var doc = _loading == null ? null : await _loading;
        if (_disposed || doc == null) return;
        await OnUi(() =>
        {
            var editor = Editor.Instance;
            if (editor == null) return false;
            Document hopsDocument = null;
            try
            {
                var source = doc;
                if (asHops)
                {
                    // Hops remains a GH1 component; use GH2's supported interop wrapper.
                    var hops = Grasshopper1.Instance.CreateObject(HopsId) as IGH_Component;
                    if (hops == null) { MessageBox.Show(editor, "Please install Hops in Grasshopper 1.", "SuperHelper GH2"); return false; }
                    var native = (object)hops.UnderlyingObject;
                    var location = native.GetType().GetProperty("RemoteDefinitionLocation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? throw new NotSupportedException("This Hops version has no RemoteDefinitionLocation property.");
                    location.SetValue(native, Path);
                    hopsDocument = Document.NewInertDocument();
                    hopsDocument.Objects.Add(new GH1InteropComponent(hops));
                    source = hopsDocument;
                }
                // GH2 owns id remapping, wires and undo. The user's OS clipboard is preserved.
                return Insert(source, editor.Canvas);
            }
            finally { hopsDocument?.Close(); }
        });
    }

    internal static Task<T> OnUi<T>(Func<T> action)
    {
        if (Application.Instance.IsUIThread) return Task.FromResult(action());
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Application.Instance.AsyncInvoke(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception exception) { completion.SetException(exception); }
        });
        return completion.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _document?.Close();
        _thumbnail?.Dispose();
        _icon.Dispose();
        // Wait for cancelled downloads before deleting this instance's own files.
        _ = Task.WhenAll(_loads).ContinueWith(_ => CleanupDownloads(), TaskScheduler.Default);
    }
    private void CleanupDownloads()
    {
        // Only files created by this instance are removed; never recurse into user folders.
        foreach (var file in _downloads) { try { File.Delete(file); } catch (IOException) { } }
        try { if (Directory.Exists(_cacheDirectory)) Directory.Delete(_cacheDirectory, recursive: false); } catch (IOException) { }
    }
}
