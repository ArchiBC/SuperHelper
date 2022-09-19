using Grasshopper;
using Grasshopper.GUI.HTML;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Linq;
using System.IO;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.ObjectModel;

namespace SuperHelper
{
    public abstract class MenuReplacer : GH_DocumentObject
    {
        private static readonly string _location = Path.Combine(Folders.SettingsFolder, "urls.json");
        private static readonly string _locationEx = Path.Combine(Folders.SettingsFolder, "urlex.json");

        internal static Dictionary<string, string> UrlDict = new Dictionary<string, string>();
        internal static Dictionary<string, string[]> UrlExDict = new Dictionary<string, string[]>();


        internal static SuperHelperControl _control = new SuperHelperControl();

        protected MenuReplacer(IGH_InstanceDescription tag) : base(tag)
        {
        }

        internal static void SaveUrlDictToJson()
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            File.WriteAllText(_location, ser.Serialize(UrlDict));
        }

        internal static void SaveUrlExToJson()
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            File.WriteAllText(_locationEx, ser.Serialize(UrlExDict));
        }

        public static bool Init()
        {
            new HighLightConduit().Enabled = true;

            //Read from json.
            try
            {
                if (File.Exists(_location))
                {
                    string jsonStr = File.ReadAllText(_location);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    UrlDict = ser.Deserialize<Dictionary<string, string>>(jsonStr);
                }
                else
                {
                    UrlDict = new Dictionary<string, string>();
                }

                if (File.Exists(_locationEx))
                {
                    string jsonStr = File.ReadAllText(_locationEx);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    UrlExDict = ser.Deserialize<Dictionary<string, string[]>>(jsonStr);
                }
                else
                {
                    UrlExDict = new Dictionary<string, string[]>();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

            Instances.ActiveCanvas.DocumentObjectMouseDown += ActiveCanvas_DocumentObjectMouseDown;

            return ExchangeMethod(
                typeof(GH_DocumentObject).GetRuntimeMethods().Where(m => m.Name.Contains("Menu_ObjectHelpClick")).First(),
                typeof(MenuReplacer).GetRuntimeMethods().Where(m => m.Name.Contains(nameof(Menu_ObjectHelpClickNew))).First()
                );
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
                    int* tar = (int*)targetMethod.MethodHandle.Value.ToPointer() + 2;
                    int* inj = (int*)injectMethod.MethodHandle.Value.ToPointer() + 2;
                    var relay = *tar;
                    *tar = *inj;
                    *inj = relay;
                }
                else
                {
                    long* tar = (long*)targetMethod.MethodHandle.Value.ToPointer() + 1;
                    long* inj = (long*)injectMethod.MethodHandle.Value.ToPointer() + 1;
                    var relay = *tar;
                    *tar = *inj;
                    *inj = relay;
                }
            }
            return true;
        }

        private void Menu_ObjectHelpClickNew(object sender, EventArgs e)
        {
            SuperHelperAssemblyPriority.Show();
            SetOneObject(this);
        }

        private static void SetOneObject(GH_DocumentObject obj)
        {
            Task.Run(() =>
            {
                HighLightConduit.HighLightObject = null;
                foreach (var view in Rhino.RhinoDoc.ActiveDoc.Views)
                {
                    view.Redraw();
                }

                _control.Dispatcher.Invoke(() =>
                {
                    _control.DataContext = null;

                    try
                    {
                        _control.DataContext = obj;
                    }
                    catch
                    {

                    }


                    string html = (string)typeof(GH_DocumentObject).GetRuntimeMethods().Where(m => m.Name.Contains("HtmlHelp_Source")).First().Invoke(obj, new object[0]);

                    if (html == null || html.Length == 0)
                    {
                        html = "We're sorry. Help is not yet available for this object";
                    }

                    if (html.ToUpperInvariant().StartsWith("GOTO:"))
                    {
                        _control.oldUrl.AllowNavigation = true;
                        _control.oldUrl.Navigate(html.Substring(5));
                    }
                    else
                    {
                        _control.oldUrl.Navigate("about:blank");
                        _control.oldUrl.Document.OpenNew(false);

                        _control.oldUrl.Document.Write(html);
                        _control.oldUrl.Refresh();
                    }

                    if (obj != null)
                    {
                        if (UrlDict.TryGetValue(obj.ComponentGuid.ToString(), out string url))
                        {
                            _control.UrlTextBox.Text = url;

                            if (_control.myWeb.Source == null)
                            {
                                _control.myWeb.Source = new Uri(url);
                            }
                        }
                        else
                        {
                            _control.UrlTextBox.Text = "";
                        }

                        if (UrlExDict.TryGetValue(obj.ComponentGuid.ToString(), out string[] urls))
                        {
                            _control.ExampleList.ItemsSource = new ObservableCollection<HelpExample>(urls.Select(u => new HelpExample(u)));
                        }
                        else
                        {
                            _control.ExampleList.ItemsSource = new ObservableCollection<HelpExample>();
                        }
                    }
                });
            });
        }

        internal static void ActiveCanvas_DocumentObjectMouseDown(object sender, Grasshopper.GUI.GH_CanvasObjectMouseDownEventArgs e)
        {
            if (_control.AutoTarget)
            {
                var obj = (GH_DocumentObject)e.Object.Object.Attributes.GetTopLevel?.DocObject;
                SetOneObject(obj);
            }
        }
    }
}