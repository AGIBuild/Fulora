using Foundation;
using WebKit;

namespace Spike0cIos;

[Register ("SceneDelegate")]
public class SceneDelegate : UIResponder, IUIWindowSceneDelegate {

	[Export ("window")]
	public UIWindow? Window { get; set; }

	[Export ("scene:willConnectToSession:options:")]
	public void WillConnect (UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
	{
		if (scene is UIWindowScene windowScene) {
			Window ??= new UIWindow (windowScene);

			var vc = new UIViewController ();
			vc.View!.AddSubview (new UILabel (Window!.Frame) {
				BackgroundColor = UIColor.SystemBackground,
				TextAlignment = UITextAlignment.Center,
				Text = "Spike 0c AOT ClassPair",
				AutoresizingMask = UIViewAutoresizing.All,
			});

			var webView = new WKWebView (vc.View!.Bounds, new WKWebViewConfiguration ()) {
				AutoresizingMask = UIViewAutoresizing.All,
			};
			vc.View!.AddSubview (webView);

			Window.RootViewController = vc;
			Window.MakeKeyAndVisible ();

			NavDelegateProbe.Run (webView);
		}
	}

	[Export ("sceneDidDisconnect:")]
	public void DidDisconnect (UIScene scene)
	{
	}

	[Export ("sceneDidBecomeActive:")]
	public void DidBecomeActive (UIScene scene)
	{
	}

	[Export ("sceneWillResignActive:")]
	public void WillResignActive (UIScene scene)
	{
	}

	[Export ("sceneWillEnterForeground:")]
	public void WillEnterForeground (UIScene scene)
	{
	}

	[Export ("sceneDidEnterBackground:")]
	public void DidEnterBackground (UIScene scene)
	{
	}
}
