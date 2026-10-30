using Grasshopper2.Documentation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SuperHelper;

internal sealed record Gh2DocumentationPage(string Html, string[] Examples);

internal static class Gh2Documentation
{
    public static string Unescape(string value)
    {
        var databaseType = typeof(Docs).Assembly.GetReferencedAssemblies().FirstOrDefault(name => name.Name == "DocumentationPlatform");
        var type = Assembly.Load(databaseType).GetType("DocumentationPlatform.Database", throwOnError: true);
        return (string)type.GetMethod("UnescapeUrl", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { value });
    }
    // DocumentationPlatform is supplied by Rhino but omitted from the GH2 NuGet
    // reference pack. Keep that optional runtime boundary confined to this adapter.
    public static Gh2DocumentationPage Find(Guid id)
    {
        try { return FindCore(id); }
        catch (Exception exception)
        {
            Rhino.RhinoApp.WriteLine("SuperHelper GH2 documentation unavailable: " + exception.GetBaseException().Message);
            return new(null, Array.Empty<string>());
        }
    }
    private static Gh2DocumentationPage FindCore(Guid id)
    {
        var database = typeof(Docs).GetProperty("Database").GetValue(null);
        var databaseType = database.GetType();
        var spec = databaseType.GetMethod("FindSpec").Invoke(database, new object[] { id });
        if (spec == null) return new(null, Array.Empty<string>());
        var contentType = databaseType.Assembly.GetType("DocumentationPlatform.Content", throwOnError: true);
        var contents = Array.CreateInstance(contentType, 1);
        contents.SetValue(spec, 0);
        var html = (string)contentType.GetMethod("CreateFullHtml", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { database, contents });
        var examples = spec.GetType().GetProperty("Examples").GetValue(spec);
        var names = examples?.GetType().GetProperty("Values").GetValue(examples) as IEnumerable<string> ?? Enumerable.Empty<string>();
        var findFile = databaseType.GetMethod("FindFile");
        return new(html, names.Select(name => (string)findFile.Invoke(database, new object[] { name }))
            .Where(path => !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path)).ToArray());
    }
}
