StatusBarOverlayMonoTouch
=========================

Port of the MTStatusBarOverlay (https://github.com/myell0w/MTStatusBarOverlay) for monontouch.

Just add :
				StatusBarOverlay.StatusBarOverlay overlay = StatusBarOverlay.StatusBarOverlay.SharedInstance;

				overlay.Animation = StatusBarOverlayAnimation.Shrink;  // MTStatusBarOverlayAnimationShrink
				overlay.DetailViewMode = DetailViewMode.History;         // enable automatic history-tracking and show in detail-view
				overlay.Progress = 0.0;
				overlay.PostMessage(@"Following @myell0w on Twitter…");
				overlay.Progress = 0.4;
				// ...
				overlay.PostMessage(@"Following myell0w on Github…",false);
				overlay.Progress = 0.8;
				// ...
				overlay.PostImmediateFinishMessage(@"Following was a good idea!",2,true);
				overlay.Progress = 1.0;

To your code.