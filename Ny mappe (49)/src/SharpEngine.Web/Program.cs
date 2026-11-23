using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using SharpEngine.Web;

// The page (wwwroot/index.html + sharp.js) loads the build's files and calls Boot, then calls Frame once per animation frame.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
var canvas = new JsCanvas();
WebEntry.Host = new WebHost(canvas);
WebEntry.Host.Input.OnCapture = on => { try { Js.Capture(on); } catch (Exception) { } };
canvas.Ready();
await builder.Build().RunAsync();

namespace SharpEngine.Web
{
    /// <summary>The functions sharp.js calls (DotNet.invokeMethod('SharpEngine.Web', name, ...)).</summary>
    public static class WebEntry
    {
        internal static WebHost Host = null!;

        [JSInvokable] public static void WriteFile(string path, byte[] data) => WebHost.WriteFile(path, data);
        [JSInvokable] public static string Boot() { Host.Boot(); return Host.Error ?? ""; }
        [JSInvokable] public static void Frame(double seconds) => Host.Frame(seconds);
        [JSInvokable] public static void Key(string code, bool down) => Host.Input.Key(code, down);
        [JSInvokable] public static void Mouse(int button, bool down) => Host.Input.Mouse(button, down);
        [JSInvokable] public static void MouseMove(double dx, double dy) => Host.Input.MouseMove((float)dx, (float)dy);
        [JSInvokable] public static void ReleaseAll() => Host.Input.ReleaseAll();
    }
}
