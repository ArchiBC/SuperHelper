using Grasshopper;
using Grasshopper.Kernel;
using Rhino;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace SuperHelper;

public abstract class MenuReplacer : GH_DocumentObject
{
    private static readonly MethodInfo HtmlHelpInfo = typeof(GH_DocumentObject)
        .GetRuntimeMethods().First(method => method.Name.Contains("HtmlHelp_Source"));
    private static readonly string ExampleUrlLocation = Path.Combine(Folders.SettingsFolder, "urlex.json");
    private static readonly HighLightConduit HighlightConduit = new() { Enabled = true };
    private static GH_DocumentObject _lastAutoTarget;
    private static bool _initialized;

    internal static Dictionary<string, string[]> UrlExDict { get => HelperHost.Current.Examples.Items; private set => HelperHost.Current.Examples.Items = value; }
    internal static SuperHelperControl Control { get; } = new();

    public static DateTime LastDownloadURLTime
    {
        get => Instances.Settings.GetValue(nameof(LastDownloadURLTime), DateTime.MinValue);
        set => Instances.Settings.SetValue(nameof(LastDownloadURLTime), value);
    }

    protected MenuReplacer(IGH_InstanceDescription tag) : base(tag)
    {
    }

    internal static void SaveUrlExToJson() =>
        HelperHost.Current.Examples.Save();

    public static bool Init()
    {
        if (_initialized)
        {
            return true;
        }

        _initialized = true;
        ReadUrlConfiguration();

        Instances.ActiveCanvas.DocumentObjectMouseDown += ActiveCanvas_DocumentObjectMouseDown;
        Instances.ActiveCanvas.CanvasPostPaintObjects += ActiveCanvas_PostPaintObjects;
        Instances.ActiveCanvas.DocumentChanged += ActiveCanvas_DocumentChanged;

        // The method-descriptor layout used by the legacy hook is not an
        // ARM64 contract. macOS uses the explicit help command instead.
        return Rhino.Runtime.HostUtils.RunningOnOSX || ExchangeMethod(
            typeof(GH_DocumentObject).GetRuntimeMethods().First(method => method.Name.Contains("Menu_ObjectHelpClick")),
            typeof(MenuReplacer).GetRuntimeMethods().First(method => method.Name.Contains(nameof(Menu_ObjectHelpClickNew))));
    }

    private static void ReadUrlConfiguration()
    {
        try
        {
            WebHelpMappings.Initialize();
#if !DEBUG
            var now = DateTime.Now;
            if (now - LastDownloadURLTime > TimeSpan.FromDays(1))
            {
                LastDownloadURLTime = now;
                using var client = new System.Net.Http.HttpClient();
                try
                {
                    var bytes = client.GetByteArrayAsync("https://gitee.com/ArchiTed1998/SuperHelper/raw/master/urls.json").Result;
                    WebHelpMappings.Merge100GhJson(Encoding.UTF8.GetString(bytes));
                }
                catch
                {
                }

                try
                {
                    var bytes = client.GetByteArrayAsync("https://gitee.com/ArchiTed1998/SuperHelper/raw/master/urlex.json").Result;
                    UrlExDict = JsonSerializer.Deserialize<Dictionary<string, string[]>>(Encoding.UTF8.GetString(bytes)) ?? new();
                }
                catch
                {
                }
            }
#endif

            if (File.Exists(ExampleUrlLocation))
            {
                var local = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(ExampleUrlLocation));
                if (local != null)
                {
                    foreach (var pair in local)
                    {
                        if (UrlExDict.TryGetValue(pair.Key, out var existing))
                        {
                            UrlExDict[pair.Key] = existing.Concat(pair.Value).Distinct().ToArray();
                        }
                        else
                        {
                            UrlExDict[pair.Key] = pair.Value;
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Eto.Forms.MessageBox.Show(exception.Message, "SuperHelper", Eto.Forms.MessageBoxType.Error);
        }
    }

    private static void ActiveCanvas_DocumentChanged(
        Grasshopper.GUI.Canvas.GH_Canvas sender,
        Grasshopper.GUI.Canvas.GH_CanvasDocumentChangedEventArgs e)
    {
        _lastAutoTarget = null;
        Control.ClearObject();
        if (e.OldDocument != null)
        {
            e.OldDocument.SolutionEnd -= DocumentSolutionEnd;
        }
        if (e.NewDocument != null)
        {
            e.NewDocument.SolutionEnd -= DocumentSolutionEnd;
            e.NewDocument.SolutionEnd += DocumentSolutionEnd;
        }
    }

    private static void DocumentSolutionEnd(object sender, GH_SolutionEventArgs e)
    {
        if (Control.SelectedTabIndex == 3 && Control.CurrentObject != null)
        {
            RhinoApp.InvokeOnUiThread((Action)Control.RefreshObjectData);
        }
    }

    private static bool ExchangeMethod(MethodInfo targetMethod, MethodInfo injectMethod)
    {
        if (targetMethod == null || injectMethod == null)
        {
            return false;
        }

        RuntimeHelpers.PrepareMethod(targetMethod.MethodHandle);
        RuntimeHelpers.PrepareMethod(injectMethod.MethodHandle);
        unsafe
        {
            if (IntPtr.Size == 4)
            {
                var target = (int*)targetMethod.MethodHandle.Value.ToPointer() + 2;
                var inject = (int*)injectMethod.MethodHandle.Value.ToPointer() + 2;
                (*target, *inject) = (*inject, *target);
            }
            else
            {
                var target = (long*)targetMethod.MethodHandle.Value.ToPointer() + 1;
                var inject = (long*)injectMethod.MethodHandle.Value.ToPointer() + 1;
                (*target, *inject) = (*inject, *target);
            }
        }
        return true;
    }

    private void Menu_ObjectHelpClickNew(object sender, EventArgs e)
    {
        SuperHelperAssemblyPriority.Show();
        SetOneObject(this);
    }

    internal static void ShowSelectedObjectHelp()
    {
        var obj = Instances.ActiveCanvas?.Document?.SelectedObjects()
            .OfType<GH_DocumentObject>().LastOrDefault() ?? Control.CurrentObject?.NativeObject as GH_DocumentObject;
        if (obj != null)
        {
            SuperHelperAssemblyPriority.Show();
            SetOneObject(obj);
        }
    }

    private static void SetOneObject(GH_DocumentObject obj)
    {
        if (obj == null)
        {
            return;
        }

        RhinoApp.InvokeOnUiThread((Action)(() =>
        {
            HighLightConduit.HighLightObject = null;
            RhinoDoc.ActiveDoc?.Views.Redraw();

            var html = HtmlHelpInfo.Invoke(obj, Array.Empty<object>()) as string;
            html = string.IsNullOrWhiteSpace(html)
                ? "We're sorry. Help is not yet available for this object"
                : html;

            var model = Grasshopper1Host.Describe(obj);
            var webUrl = WebHelpMappings.Resolve(model, html);

            UrlExDict.TryGetValue(obj.ComponentGuid.ToString(), out var examples);
            Control.SetObject(model, html, webUrl, examples ?? Array.Empty<string>());
        }));
    }

    internal static void RefreshCurrentObject()
    {
        if (Control.CurrentObject != null)
        {
            SetOneObject(Control.CurrentObject.NativeObject as GH_DocumentObject);
        }
    }

    internal static void ActiveCanvas_DocumentObjectMouseDown(
        object sender,
        Grasshopper.GUI.GH_CanvasObjectMouseDownEventArgs e)
    {
        if (!Control.AutoTarget)
        {
            return;
        }

        // GH_RelevantObjectData already resolves input/output grips and nested
        // attributes to the owning top-level component. Going through
        // Object.Attributes.GetTopLevel is not reliable for every GH object.
        var obj = e.Object.TopLevelObject as GH_DocumentObject
            ?? e.Object.Object as GH_DocumentObject;
        _lastAutoTarget = obj;
        SetOneObject(obj);
    }

    private static void ActiveCanvas_PostPaintObjects(Grasshopper.GUI.Canvas.GH_Canvas canvas)
    {
        if (!Control.AutoTarget || canvas?.Document == null)
        {
            return;
        }

        // DocumentObjectMouseDown gives immediate feedback. This lightweight
        // selection check covers box selection, keyboard selection commands,
        // undo/redo and restored document selections, none of which raise the
        // object mouse-down event. It does no page work while the target is
        // unchanged, so normal canvas repaints remain cheap.
        var selected = canvas.Document.SelectedObjects();
        if (selected == null || selected.Count == 0)
        {
            _lastAutoTarget = null;
            return;
        }

        if (Control.CurrentObject?.NativeObject is GH_DocumentObject current && selected.Contains(current))
        {
            _lastAutoTarget = current;
            return;
        }

        var obj = selected.OfType<GH_DocumentObject>().LastOrDefault();
        if (obj == null || ReferenceEquals(obj, _lastAutoTarget))
        {
            return;
        }

        _lastAutoTarget = obj;
        SetOneObject(obj);
    }
}
