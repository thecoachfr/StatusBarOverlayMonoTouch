StatusBarOverlayMonoTouch
=========================

Port of the MTStatusBarOverlay (https://github.com/myell0w/MTStatusBarOverlay) for monontouch.

Description
-----------------

This class provides a custom iOS (iPhone + iPad) status bar overlay window known from Apps like Reeder, Google Mobile App or Evernote.
It currently supports touch-handling, queuing of messages, delegation as well as three different animation modes:
 
* StatusBarOverlayAnimation.Shrink: When the user touches the overlay the overlay shrinks and only covers the battery-icon on the right side
* StatusBarOverlayAnimation.FallDown: When the user touches the overlay a detail view falls down where additional information can be displayed. You can get a history of all your displayed messages for free by enabling historyTracking!
* StatusBarOverlayAnimation.None: Nothing happens, when the user touches the overlay

StatusBarOverlay currently fully supports two different status bar styles, which also can be changed in your app (StatusBarOverlay will adopt the style and will be updated the next time you show it):

* UIStatusBarStyle.Default
* UIStatusBarStyle.BlackOpaque

Usage
------------------

You can use the custom status bar like this:

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


Known Limitations
----------------------- 
* When using UIStatusBarStyle.BlackTranslutient the overlay is black opaque
* User interaction in detail view is not possible yet



Part of that documentation, original code and most of the comments are copyright to myell0w !