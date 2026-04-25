using Foundation;

namespace Spike0bIos;

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
				Text = "Spike 0b BlockLiteral",
				AutoresizingMask = UIViewAutoresizing.All,
			});

			Window.RootViewController = vc;
			Window.MakeKeyAndVisible ();
		}

		CookieBlockProbe.Run ();
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
