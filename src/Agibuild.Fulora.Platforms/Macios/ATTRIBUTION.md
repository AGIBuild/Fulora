# Macios Interop — Attribution

This namespace contains code originally derived from the
[Avalonia.Controls.WebView](https://github.com/AvaloniaUI/Avalonia.Controls.WebView)
project, MIT-licensed by AvaloniaUI OÜ. The vendored files are listed below
with their upstream paths so future upstream patches can be re-applied.

| Local file | Upstream file | Vendored at commit |
|---|---|---|
| `Interop/Libobjc.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/Libobjc.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/NSObject.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSObject.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/NSManagedObjectBase.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSManagedObjectBase.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/NSString.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSString.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/BlockLiteral.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/BlockLiteral.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/CGRect.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/CGRect.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/Foundation/Foundation.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/Foundation.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/Foundation/NSValue.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSValue.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/Foundation/NSError.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSError.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/Foundation/NSUrl.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSUrl.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/Foundation/NSURLRequest.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSURLRequest.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/Foundation/NSMutableURLRequest.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSMutableURLRequest.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/Foundation/NSData.cs` | n/a | n/a (newly authored — see Foundation/NSData.cs SPDX header) |
| `Interop/Foundation/NSDictionary.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSDictionary.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e (modified — see file header) |
| `Interop/Foundation/NSArray.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSArray.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/Foundation/NSDate.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSDate.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/Foundation/NSNumber.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSNumber.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/Foundation/NSUUID.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSUUID.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e |
| `Interop/WebKit/WKWebKit.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/WebKit/WebKit.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e (modified — AMENDMENT #8 WebKit `dlopen` cctor + `objc_getClass` / `objc_getProtocol` forwarders) |
| `Interop/WebKit/WKWebView.cs` | n/a | n/a (newly authored — Fulora-original; stock `WKWebView` via `initWithFrame:configuration:` — upstream Avalonia subclasses `AppleView`, not vendored verbatim) |
| `Interop/WebKit/WKWebViewConfiguration.cs` | n/a | n/a (newly authored — Fulora-original; `WebsiteDataStore` / `UserContentController` as `IntPtr` per T6) |
| `Interop/WebKit/WKPreferences.cs` | n/a | n/a (newly authored — Fulora-original) |
| `Interop/WebKit/WKWebpagePreferences.cs` | n/a | n/a (newly authored — Fulora-original) |
| `Interop/WebKit/WKUserContentController.cs` | n/a | n/a (newly authored — Fulora-original) |
| `Interop/WebKit/WKUserScript.cs` | n/a | n/a (newly authored — Fulora-original) |
| `Interop/WebKit/WKWebsiteDataStore.cs` | n/a | n/a (newly authored — Fulora-original) |
| `Interop/WebKit/WKHTTPCookieStore.cs` | n/a | n/a (newly authored — Fulora-original) |
| `Interop/Foundation/NSHTTPCookie.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSHTTPCookie.cs` | 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e (modified — WebViewCookie conversions + property getters; Fulora Task 9) |
| `Interop/WebKit/WKNavigationDelegate.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/WebKit/WKNavigationDelegate.cs` | TBD |

## License

Original copyright: (c) 2026 AvaloniaUI OÜ — MIT License
Modifications: (c) 2026 Agibuild — MIT License (see repo root LICENSE).

Each vendored file carries a per-file SPDX header indicating both copyrights.
