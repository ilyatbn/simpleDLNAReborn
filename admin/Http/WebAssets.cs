using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NMaier.SimpleDlna.Admin.Http
{
  /// <summary>
  ///   Serves the admin SPA out of the assembly's embedded resources.
  /// </summary>
  /// <remarks>
  ///   Resources are embedded with an explicit LogicalName of
  ///   "wwwroot/&lt;path&gt;", which sidesteps the SDK's resource-name mangling
  ///   entirely - Vite emits hashed names like assets/index-D4f8Ab12.js that
  ///   the default naming would rewrite.
  /// </remarks>
  public sealed class WebAssets
  {
    private const string ROOT = "wwwroot/";

    private static readonly Dictionary<string, string> contentTypes =
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        {".html", "text/html; charset=utf-8"},
        {".js", "text/javascript; charset=utf-8"},
        {".mjs", "text/javascript; charset=utf-8"},
        {".css", "text/css; charset=utf-8"},
        {".json", "application/json; charset=utf-8"},
        {".svg", "image/svg+xml"},
        {".png", "image/png"},
        {".jpg", "image/jpeg"},
        {".jpeg", "image/jpeg"},
        {".gif", "image/gif"},
        {".ico", "image/x-icon"},
        {".webp", "image/webp"},
        {".woff", "font/woff"},
        {".woff2", "font/woff2"},
        {".ttf", "font/ttf"},
        {".map", "application/json; charset=utf-8"},
        {".txt", "text/plain; charset=utf-8"}
      };

    private readonly Assembly assembly;

    private readonly Dictionary<string, string> resources =
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public WebAssets()
    {
      assembly = typeof (WebAssets).Assembly;
      foreach (var name in assembly.GetManifestResourceNames()) {
        if (name.StartsWith(ROOT, StringComparison.OrdinalIgnoreCase)) {
          resources["/" + name.Substring(ROOT.Length)] = name;
        }
      }
      HasUi = resources.ContainsKey("/index.html");
    }

    /// <summary>False when the project was built with -p:SkipWebBuild=true.</summary>
    public bool HasUi { get; }

    public AdminResponse Handle(AdminRequest request)
    {
      if (!HasUi) {
        return NotBuilt();
      }
      var path = request.Path;
      if (path == "/" || path.Length == 0) {
        path = "/index.html";
      }

      string resource;
      if (resources.TryGetValue(path, out resource)) {
        return Serve(request, path, resource);
      }

      // Unknown paths without an extension are client-side routes, so the SPA
      // shell answers them. Anything with an extension is a genuine 404.
      if (!Path.HasExtension(path)) {
        return Serve(request, "/index.html", resources["/index.html"]);
      }
      return AdminResponse.Text(404, "Not found");
    }

    private AdminResponse Serve(AdminRequest request, string path,
      string resource)
    {
      byte[] body;
      using (var stream = assembly.GetManifestResourceStream(resource)) {
        if (stream == null) {
          return AdminResponse.Text(404, "Not found");
        }
        using (var buffer = new MemoryStream()) {
          stream.CopyTo(buffer);
          body = buffer.ToArray();
        }
      }

      var rv = new AdminResponse(200, ContentType(path), body);
      // Hashed asset names are immutable by construction; the shell is not.
      rv.Headers["Cache-Control"] =
        path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
          ? "public, max-age=31536000, immutable"
          : "no-cache";
      return rv;
    }

    private static string ContentType(string path)
    {
      var ext = Path.GetExtension(path);
      string rv;
      return contentTypes.TryGetValue(ext ?? string.Empty, out rv)
        ? rv
        : "application/octet-stream";
    }

    private static AdminResponse NotBuilt()
    {
      var html = new StringBuilder();
      html.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
      html.Append("<title>SimpleDLNA - web UI not built</title>");
      html.Append("<style>body{font-family:'Segoe UI',Helvetica,sans-serif;");
      html.Append("background:#22282c;color:#e8eef2;margin:0;padding:3rem;}");
      html.Append("code{background:#161e24;padding:.15em .4em;border-radius:4px}");
      html.Append("a{color:#8fd0f0}</style></head><body>");
      html.Append("<h1>Web UI not built</h1>");
      html.Append("<p>This build was produced with <code>SkipWebBuild=true</code>, ");
      html.Append("so the admin interface is not embedded in the assembly.</p>");
      html.Append("<p>The REST API is running normally at <code>/api/v1</code>.</p>");
      html.Append("<p>To build the UI, install Node.js and rebuild:</p>");
      html.Append("<pre><code>cd web &amp;&amp; npm ci &amp;&amp; npm run build\n");
      html.Append("dotnet build sdlna.sln</code></pre>");
      html.Append("</body></html>");
      var rv = new AdminResponse(503, "text/html; charset=utf-8",
        Encoding.UTF8.GetBytes(html.ToString()));
      rv.Headers["Cache-Control"] = "no-store";
      return rv;
    }
  }
}
