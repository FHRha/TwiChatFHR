using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.WinForms;

namespace TwitchChatCore.Views;

public class PreviewWebView : NativeControlHost
{
    private WebView2? _webView;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _webView = new WebView2();
            _webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 30, 27, 75);
            
            // Initialization has to happen on the main thread
            _webView.EnsureCoreWebView2Async().ContinueWith(t => 
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                {
                    if (App.LocalServer != null && _webView != null && _webView.CoreWebView2 != null)
                    {
                        _webView.Source = new Uri(App.LocalServer.BaseUrl + "/index.html");
                    }
                });
            });

            return new PlatformHandle(_webView.Handle, "HWND");
        }

        return base.CreateNativeControlCore(parent);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _webView?.Dispose();
            _webView = null;
        }
        base.DestroyNativeControlCore(control);
    }
}
