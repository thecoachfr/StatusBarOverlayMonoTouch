using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MonoTouch.CoreGraphics;
using MonoTouch.Foundation;
using MonoTouch.ObjCRuntime;
using MonoTouch.SystemConfiguration;
using MonoTouch.UIKit;

namespace StatusBarOverlay
{
    ///This class provides an overlay over the iOS Status Bar that can display information
    ///and perform an animation when you touch it:
    ///
    ///it can either shrink and only overlap the battery-icon (like in Reeder) or it can display
    ///a detail-view that shows additional information. You can show a history of all the previous
    ///messages for free by setting historyEnabled to true
    public class StatusBarOverlay : UIWindow
    {
        // keys used in the dictionary-representation of a status message
        private const string StatusBarOverlayMessageKey = "MessageText";
        private const string StatusBarOverlayMessageTypeKey = "MessageType";
        private const string StatusBarOverlayDurationKey = "MessageDuration";
        private const string StatusBarOverlayAnimationKey = "MessageAnimation";
        private const string StatusBarOverlayImmediateKey = "MessageImmediate";

        // keys used for saving state to NSUserDefaults
        private const string StatusBarOverlayStateShrinked = "kMTStatusBarOverlayStateShrinked";

        // the view that holds all the components of the overlay (except for the detailView)
        public UIView BackgroundView { get; set; }
        // the detailView is shown when animation is set to "FallDown"
        public UIView DetailView { get; set; }
        // the frame of the status bar when animation is set to "Shrink" and it is shrinked
        public RectangleF SmallFrame { get; set; }
        // the label that holds the finished-indicator (either a checkmark, or a error-sign per default)
        public UILabel FinishedLabel { get; set; }
        // if this flag is set to YES, neither activityIndicator nor finishedLabel are shown
        public bool HidesActivity { get; set; }
        // the image used when the Status Bar Style is Default
        public UIImage DefaultStatusBarImage { get; set; }
        // the image used when the Status Bar Style is Default and the Overlay is shrinked
        public UIImage DefaultStatusBarImageShrinked { get; set; }
        // all messages that were displayed since the last finish-call
        public List<string> MessageHistory { get; private set; }
        // the last message that was visible
        public String LastPostedMessage { get; set; }
        // determines if immediate messages in the queue get removed or stay in the queue, when a new immediate message gets posted
        public bool CanRemoveImmediateMessagesFromQueue { get; set; }

        public IStatusBarOverlayDelegate Delegate { get; set; }

        public UIActivityIndicatorView ActivityIndicator { get; set; }
        public UIImageView StatusBarBackgroundImageView { get; set; }
        public UILabel StatusLabel1 { get; set; }
        public UILabel StatusLabel2 { get; set; }
        public UILabel HiddenStatusLabel { get; set; }
        public UIImageView ProgressView { get; set; }
        public RectangleF OldBackgroundViewFrame { get; set; }
        // overwrite property for read-write-access
        public bool HideInProgress { get; set; }
        public bool Active { get; set; }
        public UITextView DetailTextView { get; set; }
        public IList<IDictionary> MessageQueue { get; set; }
        // overwrite property for read-write-access
        public UITableView HistoryTableView { get; set; }
        public bool ForcedToHide { get; set; }

        private static StatusBarOverlay _sharedInstance;

        public static StatusBarOverlay SharedInstance
        {
            get { return _sharedInstance ?? (_sharedInstance = new StatusBarOverlay()); }
        }

        // Text that is displayed in the finished-Label when the finish was successful
        private const string FinishedText = @"✓";
        private const float FinishedFontSize = 22f;

        // Text that is displayed when an error occured
        private const string ErrorText = @"✗";
        private const float ErrorFontSize = 19f;
        private const float ProgressViewAlpha = 0.4f;
        private static readonly UIColor ProgressViewBackgroundColor = new UIColor(0.0f, 0.0f, 0.0f, 1.0f);

        ///////////////////////////////////////////////////////
        // Light Theme (for UIStatusBarStyleDefault)
        ///////////////////////////////////////////////////////

        private readonly UIColor _lightThemeTextColor = UIColor.Black;
        private readonly UIColor _lightThemeErrorMessageTextColor = UIColor.Black;
        // blackColor] // [UIColor colorWithRed:0.494898f green:0.330281f blue:0.314146f alpha:1.0f]
        private readonly UIColor _lightThemeFinishedMessageTextColor = UIColor.Black;
        // blackColor] // [UIColor colorWithRed:0.389487f green:0.484694f blue:0.38121f alpha:1.0f]
        private readonly UIColor _lightThemeShadowColor = UIColor.White;
        private readonly UIColor _lightThemeErrorMessageShadowColor = UIColor.White;
        private readonly UIColor _lightThemeFinishedMessageShadowColor = UIColor.White;

        private const UIActivityIndicatorViewStyle LightThemeActivityIndicatorViewStyle =
            UIActivityIndicatorViewStyle.Gray;

        private readonly UIColor _lightThemeDetailViewBackgroundColor = UIColor.Black;
        private readonly UIColor _lightThemeDetailViewBorderColor = UIColor.DarkGray;
        private readonly UIColor _lightThemeHistoryTextColor = new UIColor(0.749f, 0.749f, 0.749f, 1.0f);

        ///////////////////////////////////////////////////////
        // Dark Theme (for UIStatusBarStyleBlackOpaque)
        ///////////////////////////////////////////////////////

        private readonly UIColor _darkThemeTextColor = new UIColor(0.749f, 0.749f, 0.749f, 1.0f);
        private readonly UIColor _darkThemeErrorMessageTextColor = new UIColor(0.749f, 0.749f, 0.749f, 1.0f);
        // [UIColor colorWithRed:0.918367f green:0.48385f blue:0.423895f alpha:1.0f]
        private readonly UIColor _darkThemeFinishedMessageTextColor = new UIColor(0.749f, 0.749f, 0.749f, 1.0f);
        // [UIColor colorWithRed:0.681767f green:0.918367f blue:0.726814f alpha:1.0f]

        private const UIActivityIndicatorViewStyle DarkThemeActivityIndicatorViewStyle =
            UIActivityIndicatorViewStyle.White;

        private readonly UIColor _darkThemeDetailViewBackgroundColor = new UIColor(0.3f, 0.3f, 0.3f, 1.0f);
        private readonly UIColor _darkThemeDetailViewBorderColor = UIColor.White;
        private readonly UIColor _darkThemeHistoryTextColor = UIColor.White;

        ///////////////////////////////////////////////////////
        // Animations
        ///////////////////////////////////////////////////////

        // minimum time that a message is shown, when messages are queued
        private const float MinimumMessageVisibleTime = 0.4f;

        // duration of the animation to show next status message in seconds
        private const float NextStatusAnimationDuration = 0.6f;

        // duration the statusBarOverlay takes to appear when it was hidden
        private const float AppearAnimationDuration = 0.5f;

        // animation duration of animation mode shrink
        private const float AnimationDurationShrink = 0.3f;

        // animation duration of animation mode fallDown
        private const float AnimationDurationFallDown = 0.4f;

        // animation duration of change of progressView-size
        private const float UpdateProgressViewDuration = 0.2f;

        // delay after that the status bar gets visible again after rotation
        private readonly double _rotationAppearDelay =
            UIApplication.SharedApplication.StatusBarOrientationAnimationDuration;

        // the height of the status bar
        private const float StatusBarHeight = 20f;
        // Size of the text in the status labels
        private const float StatusLabelSize = 12f;

        // macro for checking if we are on the iPad
        private static readonly bool IsIPad = false; //UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad;

        private readonly static float DetailViewWidth = (IsIPad ? 400f : 280f);

        // default-width of the small-mode
        private const float WidthSmall = 26f;
        private const float HistoryTableRowHeight = 25f;
        private const float MaxHistoryTableRowCount = 5;

        private const float DetailViewAlpha = 0.9f;
        // default frame of detail view when it is hidden
        private readonly RectangleF _defaultDetailViewFrame = new RectangleF((ScreenWidth - DetailViewWidth)/2,
                                                                              -(HistoryTableRowHeight*
                                                                                MaxHistoryTableRowCount +
                                                                                StatusBarHeight), DetailViewWidth,
                                                                              HistoryTableRowHeight*
                                                                              MaxHistoryTableRowCount +
                                                                              StatusBarHeight);

        // width of the screen in portrait-orientation
        private static readonly float ScreenWidth = UIScreen.MainScreen.Bounds.Size.Width;
        // height of the screen in portrait-orientation
        private static readonly float ScreenHeight = UIScreen.MainScreen.Bounds.Size.Height;


        private readonly bool _isIPhoneEmulationMode = (!IsIPad &&
                                                        Math.Max(
                                                            UIApplication.SharedApplication.StatusBarFrame.Size.Width,
                                                            UIApplication.SharedApplication.StatusBarFrame.Size.Height) >
                                                        480f);

        public StatusBarOverlay(RectangleF frame)
            : base(frame)
        {
            Initialization();
        }

        public StatusBarOverlay()
        {
            Initialization();
        }

        private void Initialization()
        {
            
            // - (id)initWithFrame(CGRect)frame {
            // if ((this = [super initWithFrame:frame])) {
            var statusBarFrame = UIApplication.SharedApplication.StatusBarFrame;

            var newSize = new SizeF
                {
                    Height = (statusBarFrame.Size.Height == 2*StatusBarHeight)
                                 ? StatusBarHeight
                                 : statusBarFrame.Size.Height,
                    Width = _isIPhoneEmulationMode ? 320f : statusBarFrame.Size.Width
                };
            // only use height of 20px even is status bar is doubled
            // if we are on the iPad but in iPhone-Mode (non-universal-app) correct the width
            statusBarFrame.Size = newSize;

            // Place the window on the correct level and position
            WindowLevel = LevelStatusBar + 1f;
            Frame = statusBarFrame;
            Alpha = 0f;
            Hidden = false;

            // Default Small size: just show Activity Indicator
            SmallFrame = new RectangleF(statusBarFrame.Size.Width - WidthSmall, 0f, WidthSmall,
                                        statusBarFrame.Size.Height);

            // Default-values
            _animation = StatusBarOverlayAnimation.None;
            Active = false;
            HidesActivity = false;
            ForcedToHide = false;

            // the detail view that is shown when the user touches the status bar in animation mode "FallDown"
            DetailView = new UIView(_defaultDetailViewFrame)
                {
                    BackgroundColor = UIColor.Black,
                    Alpha = DetailViewAlpha,
                    AutoresizingMask = UIViewAutoresizing.FlexibleLeftMargin | UIViewAutoresizing.FlexibleRightMargin
                };
            _detailViewMode = DetailViewMode.Custom;

            // add rounded corners to detail-view
            DetailView.Layer.MasksToBounds = true;
            DetailView.Layer.CornerRadius = 10f;
            DetailView.Layer.BorderWidth = 2.5f;
            // add shadow
            /*detailView_.layer.ShadowColor = [UIColor blackColor].CGColor;
         detailView_.layer.shadowOpacity = 1.0f;
         detailView_.layer.shadowRadius = 6.0f;
         detailView_.layer.shadowOffset = CGSizeMake(0, 3);*/

            // Detail Text label
            DetailTextView = new UITextView(new RectangleF(0, StatusBarHeight,
                                                           _defaultDetailViewFrame.Size.Width,
                                                           _defaultDetailViewFrame.Size.Height - StatusBarHeight))
                {
                    BackgroundColor = UIColor.Clear,
                    UserInteractionEnabled = false,
                    Hidden = DetailViewMode != DetailViewMode.DetailText
                };
            AddSubview(DetailTextView);

            // Message History
            MessageHistory = new List<string>();
            HistoryTableView = new UITableView(new RectangleF(0, StatusBarHeight,
                                                              _defaultDetailViewFrame.Size.Width,
                                                              _defaultDetailViewFrame.Size.Height - StatusBarHeight))
                {
                    DataSource = new DataSource(this),
                    Delegate = null,
                    RowHeight = HistoryTableRowHeight,
                    SeparatorStyle = UITableViewCellSeparatorStyle.None,
                    BackgroundColor = UIColor.Clear,
                    Opaque = false,
                    Hidden = DetailViewMode != DetailViewMode.History,
                    BackgroundView = null
                };

            // make table view-background transparent
            DetailView.AddSubview(HistoryTableView);
            AddSubview(DetailView);

            // Create view that stores all the content
            var backgroundFrame = BackgroundViewFrameForStatusBarInterfaceOrientation();
            BackgroundView = new UIView(backgroundFrame)
                {
                    ClipsToBounds = true,
                    AutoresizingMask = UIViewAutoresizing.FlexibleWidth
                };
            OldBackgroundViewFrame = BackgroundView.Frame;

            // Add gesture recognizers
            var tapGestureRecognizer = new UITapGestureRecognizer(ContentViewClicked);
            //UISwipeGestureRecognizer *upGestureRecognizer = [[[UISwipeGestureRecognizer alloc] initWithTarget:self action:@selector(contentViewSwipedUp:)] autorelease];
            //UISwipeGestureRecognizer *downGestureRecognizer = [[[UISwipeGestureRecognizer alloc] initWithTarget:self action:@selector(contentViewSwipedDown:)] autorelease];

            //upGestureRecognizer.direction = UISwipeGestureRecognizerDirectionUp;
            //downGestureRecognizer.direction = UISwipeGestureRecognizerDirectionDown;

            BackgroundView.AddGestureRecognizer(tapGestureRecognizer);

            //[detailView_ addGestureRecognizer:upGestureRecognizer];
            //[self addGestureRecognizer:downGestureRecognizer];

            // Images used as background when status bar style is Default
            DefaultStatusBarImage = UIImage.LoadFromData(MtStatusBarBackgroundImageData(false));
            DefaultStatusBarImageShrinked = UIImage.LoadFromData(MtStatusBarBackgroundImageData(true));

            // Background-Image of the Content View
            StatusBarBackgroundImageView = new UIImageView(BackgroundView.Frame)
                {
                    AutoresizingMask = UIViewAutoresizing.FlexibleWidth |
                                       UIViewAutoresizing.FlexibleHeight
                };
            AddSubviewToBackgroundView(StatusBarBackgroundImageView);

            // Activity Indicator
            ActivityIndicator =
                new UIActivityIndicatorView(new RectangleF(6f, 3f, BackgroundView.Frame.Size.Height - 6f,
                                                           BackgroundView.Frame.Size.Height - 6f))
                    {
                        ActivityIndicatorViewStyle = UIActivityIndicatorViewStyle.Gray,
                        HidesWhenStopped = true,
                        //AutoresizingMask = UIViewAutoresizing.All,
                        Transform = CGAffineTransform.MakeScale(0.75f, 0.75f)
                    };

            /* iOS 5 doesn't correctly resize the activityIndicator. Bug?
            if (ActivityIndicator.RespondsToSelector(new Selector("setColor:")))
            {
                ActivityIndicator.Layer.SetValueForKey(new NSNumber(0.10F), new NSString(@"transform.scale"));
            }*/

            AddSubviewToBackgroundView(ActivityIndicator);

            // Finished-Label
            FinishedLabel =
                new UILabel(new RectangleF(4f, 1f, BackgroundView.Frame.Size.Height,
                                           BackgroundView.Frame.Size.Height - 1f))
                    {
                        ShadowOffset = new SizeF(0f, 1f),
                        BackgroundColor = UIColor.Clear,
                        Hidden = true,
                        Text = FinishedText,
                        TextAlignment = UITextAlignment.Center,
                        Font = UIFont.FromName(
                            @"HelveticaNeue-Bold",
                            FinishedFontSize),
                        AdjustsFontSizeToFitWidth = true
                    };

            AddSubviewToBackgroundView(FinishedLabel);

            // Status Label 1 is first visible
            StatusLabel1 =
                new UILabel(new RectangleF(30f, 0f, BackgroundView.Frame.Size.Width - 60f,
                                           BackgroundView.Frame.Size.Height - 1f))
                    {
                        BackgroundColor = UIColor.Clear,
                        ShadowOffset = new SizeF(0f, 1f),
                        Font = UIFont.BoldSystemFontOfSize(
                            StatusLabelSize),
                        TextAlignment = UITextAlignment.Center,
                        Lines = 1,
                        LineBreakMode = UILineBreakMode.TailTruncation,
                        AutoresizingMask = UIViewAutoresizing.FlexibleWidth
                    };

            AddSubviewToBackgroundView(StatusLabel1);

            // Status Label 2 is hidden
            StatusLabel2 =
                new UILabel(new RectangleF(30f, BackgroundView.Frame.Size.Height, BackgroundView.Frame.Size.Width - 60f,
                                           BackgroundView.Frame.Size.Height - 1f))
                    {
                        ShadowOffset = new SizeF(0f, 1f),
                        BackgroundColor = UIColor.Clear,
                        Font = UIFont.BoldSystemFontOfSize(
                            StatusLabelSize),
                        TextAlignment = UITextAlignment.Center,
                        Lines = 1,
                        LineBreakMode = UILineBreakMode.TailTruncation,
                        AutoresizingMask = UIViewAutoresizing.FlexibleWidth
                    };

            AddSubviewToBackgroundView(StatusLabel2);

            // the hidden status label at the beginning
            HiddenStatusLabel = StatusLabel2;

            _progress = 1.0;
            ProgressView = new UIImageView(StatusBarBackgroundImageView.Frame)
                {
                    Opaque = false,
                    Hidden = true,
                    Alpha = ProgressViewAlpha
                };
            AddSubviewToBackgroundView(ProgressView);

            MessageQueue = new List<IDictionary>();
            CanRemoveImmediateMessagesFromQueue = true;

            AddSubview(BackgroundView);

            // listen for changes of status bar frame
            NSNotificationCenter.DefaultCenter.AddObserver(this, new Selector("didChangeStatusBarFrame:"),
                                                           UIApplication.WillChangeStatusBarFrameNotification, null);
            NSNotificationCenter.DefaultCenter.AddObserver(this, new Selector("applicationDidBecomeActive:"),
                                                           UIApplication.DidBecomeActiveNotification, null);
            NSNotificationCenter.DefaultCenter.AddObserver(this, new Selector("applicationWillResignActive:"),
                                                           UIApplication.WillResignActiveNotification, null);
            // initial rotation, fixes the issue with a wrong bar appearance in landscape only mode
            RotateToStatusBarFrame(null);
        }

        public void ContentViewClicked(UIGestureRecognizer gestureRecognizer)
        {
            if (gestureRecognizer.State != UIGestureRecognizerState.Ended) return;
            // if we are currently in a special state, restore to normal
            // and ignore current set animation in that case
            if (Shrinked)
            {
                SetShrinked(false, true);
            }
            else if (!DetailViewHidden)
            {
                SetDetailViewHidden(true, true);
            }
            else
            {
                // normal case/status, do what's specified in animation-state
                switch (Animation)
                {
                    case StatusBarOverlayAnimation.Shrink:
                        SetShrinked(!Shrinked, true);
                        break;
                    case StatusBarOverlayAnimation.FallDown:
                        // detailView currently visible -> hide it
                        SetDetailViewHidden(!DetailViewHidden, true);
                        break;
                    case StatusBarOverlayAnimation.None:
                        // ignore
                        break;
                }
            }

            if (Delegate != null /*.RespondsToSelector(new Selector("statusBarOverlayDidRecognizeGesture:")*/)
            {
                Delegate.StatusBarOverlayDidRecognizeGesture(gestureRecognizer);
            }
        }

        private static RectangleF BackgroundViewFrameForStatusBarInterfaceOrientation()
        {
            var interfaceOrientation = UIApplication.SharedApplication.StatusBarOrientation;

            return ((UIInterfaceOrientation.LandscapeLeft == interfaceOrientation) ||
                    (UIInterfaceOrientation.LandscapeRight == interfaceOrientation))
                       ? new RectangleF(0, 0, ScreenHeight, StatusBarHeight)
                       : new RectangleF(0, 0, ScreenWidth, StatusBarHeight);

        }

        private NSData MtStatusBarBackgroundImageData(bool shrinked)
        {
            return NSData.FromArray(MtStatusBarBackgroundImageArray(shrinked));
            //  MtStatusBarBackgroundImageLength(shrinked), false);
            // return [NSData dataWithBytesNoCopy:MTStatusBarBackgroundImageArray(shrinked) length:MTStatusBarBackgroundImageLength(shrinked) freeWhenDone:NO];
        }

        [Action("rotateToStatusBarFrame:")]
        public void RotateToStatusBarFrame(NSValue statusBarFrameValue)
        {
            // current interface orientation
            var orientation = UIApplication.SharedApplication.StatusBarOrientation;
            // is the statusBar visible before rotation?
            var visibleBeforeTransformation = !ReallyHidden;
            // store a flag, if the StatusBar is currently shrinked
            var shrinkedBeforeTransformation = Shrinked;

            // hide and then unhide after rotation
            if (visibleBeforeTransformation)
            {
                SetHidden(true, true);
                SetDetailViewHidden(true, false);
            }

            const float pi = (float) Math.PI;
            switch (orientation)
            {
                case UIInterfaceOrientation.Portrait:
                    Transform = CGAffineTransform.MakeIdentity(); // .Identity;
                    Frame = new RectangleF(0f, 0f, ScreenWidth, StatusBarHeight);
                    SmallFrame = new RectangleF(Frame.Size.Width - WidthSmall, 0.0f, WidthSmall, Frame.Size.Height);
                    break;
                case UIInterfaceOrientation.LandscapeRight:
                    Transform = CGAffineTransform.MakeRotation(pi*(90f)/180.0f);
                    Frame = new RectangleF(ScreenWidth - StatusBarHeight, 0, StatusBarHeight, ScreenHeight);
                    SmallFrame = new RectangleF(ScreenHeight - WidthSmall, 0, WidthSmall, StatusBarHeight);
                    break;
                case UIInterfaceOrientation.LandscapeLeft:
                    Transform = CGAffineTransform.MakeRotation(pi*(-90f)/180.0f);
                    Frame = new RectangleF(0f, 0f, StatusBarHeight, ScreenHeight);
                    SmallFrame = new RectangleF(ScreenHeight - WidthSmall, 0f, WidthSmall, StatusBarHeight);
                    break;
                case UIInterfaceOrientation.PortraitUpsideDown:
                    Transform = CGAffineTransform.MakeRotation(pi);
                    Frame = new RectangleF(0f, ScreenHeight - StatusBarHeight, ScreenWidth, StatusBarHeight);
                    SmallFrame = new RectangleF(Frame.Size.Width - WidthSmall, 0f, WidthSmall, Frame.Size.Height);
                    break;
            }

            BackgroundView.Frame = BackgroundViewFrameForStatusBarInterfaceOrientation();

            // if the statusBar is currently shrinked, update the frames for the new rotation state
            if (shrinkedBeforeTransformation)
            {
                // the oldBackgroundViewFrame is the frame of the whole StatusBar
                OldBackgroundViewFrame = new RectangleF(0f, 0f, UIInterfaceOrientation.Portrait == orientation || UIInterfaceOrientation.PortraitUpsideDown == orientation
                                                                    ? ScreenWidth
                                                                    : ScreenHeight, StatusBarHeight);
                // the backgroundView gets the newly computed smallFrame
                BackgroundView.Frame = SmallFrame;
            }

            // make visible after given time
            if (visibleBeforeTransformation)
            {
                // TODO:
                // somehow this doesn't work anymore since rotation-method was changed from
                // DeviceDidRotate-Notification to StatusBarFrameChanged-Notification
                // therefore iplemented it with a UIView-Animation instead
                //[self performSelector:@selector(setHiddenUsingAlpha:) withObject:[NSNumber numberWithBool:NO] afterDelay:kRotationAppearDelay];
                Animate(AppearAnimationDuration, _rotationAppearDelay, UIViewAnimationOptions.CurveEaseInOut,
                        () => SetHiddenUsingAlpha(false), null);

            }
        }

        ////////////////////////////////////////////////////////////////////////
        //  Status Bar Appearance
        ////////////////////////////////////////////////////////////////////////

        private void AddSubviewToBackgroundView(UIView view)
        {
            view.UserInteractionEnabled = false;
            BackgroundView.AddSubview(view);
        }

        private void AddSubviewToBackgroundView(UIView view, int index)
        {
            view.UserInteractionEnabled = false;
            BackgroundView.InsertSubview(view, index);
        }

        ////////////////////////////////////////////////////////////////////////
        //  Save/Restore current State
        ////////////////////////////////////////////////////////////////////////

        private void SaveState()
        {
            SaveStateSynchronized(true);
        }

        private void SaveStateSynchronized(bool synchronizeAtEnd)
        {
            // TODO: save more state
            NSUserDefaults.StandardUserDefaults.SetBool(
                Shrinked,
                StatusBarOverlayStateShrinked);

            if (synchronizeAtEnd)
            {
                NSUserDefaults.StandardUserDefaults.Synchronize();
            }
        }

        private void RestoreState()
        {
            // restore shrinked-state
            SetShrinked(NSUserDefaults.StandardUserDefaults.BoolForKey(StatusBarOverlayStateShrinked), false);
        }

        ////////////////////////////////////////////////////////////////////////
        //  Message Posting
        ////////////////////////////////////////////////////////////////////////

        public void PostMessage(String message, bool animated = true, int duration = 0)
        {
            PostMessage(message, MessageType.Activity, TimeSpan.FromSeconds(duration), animated, false);
        }

        public void PostImmediateMessage(String message, bool animated = true, int duration = 0)
        {
            PostImmediateMessage(message, MessageType.Activity, TimeSpan.FromSeconds(duration), animated);
        }

        public void PostFinishMessage(String message, int duration = 0, bool animated = true)
        {
            PostMessage(message, MessageType.Finish, TimeSpan.FromSeconds(duration), animated, false);
        }

        public void PostImmediateFinishMessage(String message, int duration, bool animated)
        {
            PostImmediateMessage(message, MessageType.Finish, TimeSpan.FromSeconds(duration), animated);
        }

        public void PostErrorMessage(String message, int duration, bool animated = true)
        {
            PostMessage(message, MessageType.Error, TimeSpan.FromSeconds(duration), animated, false);
        }

        public void PostImmediateErrorMessage(String message, int duration, bool animated)
        {
            PostImmediateMessage(message, MessageType.Error, TimeSpan.FromSeconds(duration), animated);
        }

        private void PostMessageDictionary(IDictionary messageDictionary)
        {
            PostMessage((string) messageDictionary[StatusBarOverlayMessageKey],
                        (MessageType) (int) messageDictionary[StatusBarOverlayMessageTypeKey],
                        TimeSpan.FromSeconds((double) messageDictionary[StatusBarOverlayDurationKey]),
                        (bool) messageDictionary[StatusBarOverlayAnimationKey],
                        (bool) messageDictionary[StatusBarOverlayImmediateKey]);
        }

        private void PostMessage(String message, MessageType messageType, TimeSpan duration, bool animated,
                                 bool immediate)
        {
            InvokeOnMainThread(() =>
                {
                    // don't add to queue when message is empty
                    if (message.Length == 0)
                    {
                        return;
                    }

                    IDictionary messageDictionaryRepresentation = new Hashtable();
                    messageDictionaryRepresentation.Add(StatusBarOverlayMessageKey, message);
                    messageDictionaryRepresentation.Add(StatusBarOverlayMessageTypeKey, messageType);
                    messageDictionaryRepresentation.Add(StatusBarOverlayDurationKey, duration);
                    messageDictionaryRepresentation.Add(StatusBarOverlayAnimationKey, animated);
                    messageDictionaryRepresentation.Add(StatusBarOverlayImmediateKey, immediate);
                    lock (MessageQueue)
                    {
                        MessageQueue.Insert(0, messageDictionaryRepresentation);
                    }

                    // if the overlay is currently not active, begin with showing of messages
                    if (!Active)
                    {
                        ShowNextMessage();
                    }
                });
        }

        private void PostImmediateMessage(String message, MessageType messageType, TimeSpan duration, bool animated)
        {
            lock (MessageQueue)
            {
                IList<IDictionary> clearedMessages = new List<IDictionary>();

                foreach (var messageDictionary in MessageQueue)
                {
                    if (messageDictionary != MessageQueue.Last() &&
                        (CanRemoveImmediateMessagesFromQueue ||
                         ((bool) messageDictionary[StatusBarOverlayImmediateKey]) == false))
                    {
                        clearedMessages.Add(messageDictionary);
                    }
                }

                // TODO :  MessageQueue.Remove(clearedMessages);
                foreach (var clearedMessage in clearedMessages)
                {
                    MessageQueue.Remove(clearedMessage);
                }

                // call delegate
                if (Delegate != null)
                {
                    Delegate.StatusBarOverlayDidClearMessageQueue(clearedMessages);
                }
            }

            PostMessage(message, messageType, duration, animated, true);
        }

        ////////////////////////////////////////////////////////////////////////
        //  Showing Next Message
        ////////////////////////////////////////////////////////////////////////

        [Action("showNextMessage:")]
        public void ShowNextMessage()
        {
            if (ForcedToHide)
            {
                return;
            }

            // if there is no next message to show overlay is not active anymore
            lock (MessageQueue)
            {
                if (MessageQueue.Count < 1)
                {
                    Active = false;
                    return;
                }
            }

            // there is a next message, overlay is active
            Active = true;

            IDictionary nextMessageDictionary;

            // read out next message
            lock (MessageQueue)
            {
                nextMessageDictionary = MessageQueue.Last();
            }

            var message = (string) nextMessageDictionary[StatusBarOverlayMessageKey];
            var messageType = (MessageType) (int) nextMessageDictionary[StatusBarOverlayMessageTypeKey];
            var duration = (TimeSpan) nextMessageDictionary[StatusBarOverlayDurationKey];
            var animated = (bool) nextMessageDictionary[StatusBarOverlayAnimationKey];

            // don't show anything if status bar is hidden (queue gets cleared)
            if (UIApplication.SharedApplication.StatusBarHidden)
            {
                lock (MessageQueue)
                {
                    MessageQueue.Clear();
                }
                Active = false;
                return;
            }

            // don't duplicate animation if already displaying with text
            if (!ReallyHidden && VisibleStatusLabel().Text == message)
            {
                // remove unneccesary message
                lock (MessageQueue)
                {
                    if (MessageQueue.Count > 0)
                        MessageQueue.RemoveAt(MessageQueue.Count - 1); // removeLastObject];
                }

                // show the next message w/o delay
                ShowNextMessage();
                return;
            }

            // cancel previous hide- and clear requests
            CancelPreviousPerformRequest(this, new Selector("hide:"), null);
            CancelPreviousPerformRequest(this, new Selector("clearHistory:"), null);

            // update UI depending on current status bar style
            var statusBarStyle = UIApplication.SharedApplication.StatusBarStyle;
            SetStatusBarBackgroundForStyle(statusBarStyle);
            SetColorSchemeForStatusBarStyle(statusBarStyle, messageType);
            UpdateUiForMessageType(messageType, duration);

            // if status bar is currently hidden, show it unless it is forced to hide
            if (ReallyHidden)
            {
                // clear currently visible status label
                VisibleStatusLabel().Text = @"";

                // show status bar overlay with animation
                Animate(Shrinked ? 0 : AppearAnimationDuration, () => SetHidden(false, true));
            }

            if (animated)
            {
                // set text of currently not visible label to new text
                HiddenStatusLabel.Text = message;
                // update progressView to only cover displayed text
                UpdateProgressViewSizeForLabel(HiddenStatusLabel);

                // position hidden status label under visible status label
                HiddenStatusLabel.Frame = new RectangleF(HiddenStatusLabel.Frame.Location.X, //.Origin.X,
                                                         StatusBarHeight,
                                                         HiddenStatusLabel.Frame.Size.Width,
                                                         HiddenStatusLabel.Frame.Size.Height);

                HiddenStatusLabel.Hidden = false;

                // animate hidden label into user view and visible status label out of view
                Animate(NextStatusAnimationDuration,
                        0,
                        UIViewAnimationOptions.CurveEaseInOut | UIViewAnimationOptions.AllowUserInteraction,
                        () =>
                            {
                                // move both status labels up
                                StatusLabel1.Frame = new RectangleF(StatusLabel1.Frame.Location.X, //.Origin.X,
                                                                    StatusLabel1.Frame.Location.Y /*.Origin.Y*/-
                                                                    StatusBarHeight,
                                                                    StatusLabel1.Frame.Size.Width,
                                                                    StatusLabel1.Frame.Size.Height);
                                StatusLabel2.Frame = new RectangleF(StatusLabel2.Frame.Location.X, //Origin.X,
                                                                    StatusLabel2.Frame.Location.Y /* Origin.Y */-
                                                                    StatusBarHeight,
                                                                    StatusLabel2.Frame.Size.Width,
                                                                    StatusLabel2.Frame.Size.Height);
                            },
                        () =>
                            {
                                // add old message to history
                                AddMessageToHistory(VisibleStatusLabel().Text);

                                // after animation, set new hidden status label indicator
                                HiddenStatusLabel = HiddenStatusLabel == StatusLabel1 ? StatusLabel2 : StatusLabel1;

                                // remove the message from the queue
                                lock (MessageQueue)
                                {
                                    if (MessageQueue.Count > 0)
                                        MessageQueue.RemoveAt(MessageQueue.Count - 1); // removeLastObject];
                                }

                                // inform delegate about message-switch
                                CallDelegateWithNewMessage(message);

                                // show the next message
                                PerformSelector(new Selector("showNextMessage:"), null, MinimumMessageVisibleTime);
                            });
            } // w/o animation just save old text and set new one
            else
            {
                // add old message to history
                AddMessageToHistory(VisibleStatusLabel().Text);
                // set new text
                VisibleStatusLabel().Text = message;
                // update progressView to only cover displayed text
                UpdateProgressViewSizeForLabel(VisibleStatusLabel());

                // remove the message from the queue
                lock (MessageQueue)
                {
                    if (MessageQueue.Count > 0)
                        MessageQueue.RemoveAt(MessageQueue.Count - 1); // removeLastObject];
                }

                CallDelegateWithNewMessage(message);

                // show next message
                PerformSelector(new Selector("showNextMessage:"), null, MinimumMessageVisibleTime);
            }

            LastPostedMessage = message;
        }

        [Action("hide:")]
        public void Hide()
        {
            ActivityIndicator.StopAnimating();

            StatusLabel1.Text = @"";
            StatusLabel2.Text = @"";

            HideInProgress = false;
            // cancel previous hide- and clear requests
            CancelPreviousPerformRequest(this, new Selector("hide:"), null);

            // hide detailView
            SetDetailViewHidden(true, true);

            // hide status bar overlay with animation
            Animate(Shrinked ? 0 : AppearAnimationDuration,
                    0,
                    UIViewAnimationOptions.AllowUserInteraction,
                    () => SetHidden(true, true), () =>
                        {
                            if (Delegate != null)
                            {
                                Delegate.StatusBarOverlayDidHide();
                            }
                        });
        }

        private void HideTemporary()
        {
            ForcedToHide = true;

            // hide status bar overlay with animation
            Animate(Shrinked ? 0 : AppearAnimationDuration, () => SetHidden(true, true));
        }

        // this shows the status bar overlay, if there is text to show
        public void Show()
        {
            ForcedToHide = false;

            if (!ReallyHidden) return;
            if (VisibleStatusLabel().Text.Length > 0)
            {
                // show status bar overlay with animation
                Animate(Shrinked ? 0 : AppearAnimationDuration, () => SetHidden(false, true));
            }

            ShowNextMessage();
        }

        ////////////////////////////////////////////////////////////////////////
        //  Rotation
        ////////////////////////////////////////////////////////////////////////

        [Action("didChangeStatusBarFrame:")]
        public void DidChangeStatusBarFrame(NSNotification notification)
        {
            var statusBarFrameValue = notification.UserInfo[UIApplication.StatusBarFrameUserInfoKey];
            // TODO: react on changes of status bar height (e.g. incoming call, tethering, ...)
            // NSLog(@"Status bar frame changed: %@", NSStringFromCGRect([statusBarFrameValue CGRectValue]));
            PerformSelector(new Selector("rotateToStatusBarFrame:"), statusBarFrameValue, 0);           
        }

        ////////////////////////////////////////////////////////////////////////
        //  Setter/Getter
        ////////////////////////////////////////////////////////////////////////

        private double _progress;

        // the current progress
        public double Progress
        {
            get { return _progress; }
            set
            {
                // bound progress to 0.0 - 1.0
                var progress = Math.Max(0.0, Math.Min(value, 1.0));

                // do not decrease progress if it is no reset
                if (progress == 0.0 || progress > _progress)
                {
                    _progress = progress;
                }

                // update UI on main thread
                //InvokeOnMainThread(() => UpdateProgressViewSizeForLabel(VisibleStatusLabel()));
                UpdateProgressViewSizeForLabel(VisibleStatusLabel());
            }
        }

        private string _detailText;
        // the text displayed in the detailView (alternative to history)
        public String DetailText
        {
            get { return _detailText; }
            set
            {
                if (_detailText == value) return;
                _detailText = (string) value.Clone();

                // update text in label
                DetailTextView.Text = value;
                // update height of detailText-View
                UpdateDetailTextViewHeight();

                // update height of detailView
                SetDetailViewHidden(DetailViewHidden, true);
            }
        }

        private DetailViewMode _detailViewMode;
        // the mode of the detailView
        public DetailViewMode DetailViewMode
        {
            get { return _detailViewMode; }
            set
            {
                _detailViewMode = value;
                // update UI
                HistoryTableView.Hidden = value != DetailViewMode.History;
                DetailTextView.Hidden = value != DetailViewMode.DetailText;
            }
        }

        private StatusBarOverlayAnimation _animation;

        // the current active animation
        public StatusBarOverlayAnimation Animation
        {
            get { return _animation; }
            set
            {
                _animation = value;
                // update appearance according to new animation-mode
                // if new animation mode is shrink or none, the detailView mustn't be visible
                if (value == StatusBarOverlayAnimation.Shrink ||
                    value == StatusBarOverlayAnimation.None)
                {
                    // detailView currently visible -> hide it
                    if (!DetailViewHidden)
                    {
                        SetDetailViewHidden(true, true);
                    }
                }

                // if new animation mode is fallDown, the overlay must be extended
                if (value != StatusBarOverlayAnimation.FallDown) return;
                if (Shrinked)
                {
                    SetShrinked(false, true);
                }
            }
        }

        // detect if status bar is currently shrinked
        public bool Shrinked
        {
            get { return BackgroundView.Frame.Size.Width == SmallFrame.Size.Width; }
        }

        private void SetShrinked(bool shrinked, bool animated)
        {
            Animate(animated ? AnimationDurationShrink : 0, () =>
                {
                    // shrink the overlay
                    if (shrinked)
                    {
                        OldBackgroundViewFrame = BackgroundView.Frame;
                        BackgroundView.Frame = SmallFrame;

                        StatusLabel1.Hidden = true;
                        StatusLabel2.Hidden = true;
                    }
                        // expand the overlay
                    else
                    {
                        BackgroundView.Frame = OldBackgroundViewFrame;

                        StatusLabel1.Hidden = false;
                        StatusLabel2.Hidden = false;

                        if (ActivityIndicator.RespondsToSelector(new Selector("setColor:")))
                        {
                            var frame = StatusLabel1.Frame;
                            frame.Size = new SizeF(BackgroundView.Frame.Size.Width - 60f, frame.Size.Height);
                            StatusLabel1.Frame = frame;

                            frame = StatusLabel2.Frame;
                            frame.Size = new SizeF(BackgroundView.Frame.Size.Width - 60f, frame.Size.Height);

                            StatusLabel2.Frame = frame;
                        }
                    }

                    // update status bar background
                    SetStatusBarBackgroundForStyle(UIApplication.SharedApplication.StatusBarStyle);
                });
        }

        // detect if detailView is currently hidden
        public bool DetailViewHidden
        {
            get
            {
                return DetailView.Hidden || DetailView.Alpha == 0f ||
                       DetailView.Frame.Location.Y /* Origin.Y */+ DetailView.Frame.Size.Height < StatusBarHeight;
            }
        }

        private void SetDetailViewHidden(bool hidden, bool animated)
        {
            // hide detail view
            if (hidden)
            {
                Animate(animated ? AnimationDurationFallDown : 0,
                        0,
                        UIViewAnimationOptions.CurveEaseOut, () =>
                            {
                                DetailView.Frame = new RectangleF(DetailView.Frame.Location.X, //.Origin.X,
                                                                  -DetailView.Frame.Size.Height,
                                                                  DetailView.Frame.Size.Width,
                                                                  DetailView.Frame.Size.Height);
                            }, null);
            }
                // show detail view
            else
            {
                Animate(animated ? AnimationDurationFallDown : 0,
                        0,
                        UIViewAnimationOptions.CurveEaseIn, () =>
                            {
                                float y = 0;

                                // if history is enabled let the detailView "grow" with
                                // the number of messages in the history up until the set maximum
                                if (DetailViewMode == DetailViewMode.History)
                                {
                                    y =
                                        -(MaxHistoryTableRowCount -
                                          Math.Min(MessageHistory.Count, MaxHistoryTableRowCount))*
                                        HistoryTableRowHeight;

                                    HistoryTableView.Frame = new RectangleF(
                                        HistoryTableView.Frame.Location.X /* origin.x*/,
                                        StatusBarHeight - y,
                                        HistoryTableView.Frame.Size.Width,
                                        HistoryTableView.Frame.Size.Height);
                                }

                                if (DetailViewMode == DetailViewMode.DetailText)
                                {
                                    DetailView.Frame = new RectangleF(DetailView.Frame.Location.X /* .Origin.x*/, y,
                                                                      DetailView.Frame.Size.Width,
                                                                      DetailTextView.Frame.Size.Height +
                                                                      StatusBarHeight);
                                }
                                else
                                {
                                    DetailView.Frame = new RectangleF(DetailView.Frame.Location.X /* origin.x*/, y,
                                                                      DetailView.Frame.Size.Width,
                                                                      DetailView.Frame.Size.Height);
                                }
                            }, null);
            }

        }

        private UILabel VisibleStatusLabel()
        {
            return HiddenStatusLabel == StatusLabel1 ? StatusLabel2 : StatusLabel1;
        }

        //////////////////////////////////////////////////////////////////////// 
        //  UITableViewDataSource
        ////////////////////////////////////////////////////////////////////////

        public class DataSource : UITableViewDataSource
        {

            private readonly StatusBarOverlay _overlay;

            public DataSource(StatusBarOverlay statusBarOverlay)
            {
                _overlay = statusBarOverlay;
            }

            public override int RowsInSection(UITableView tableView, int section)
            {
                return _overlay.MessageHistory.Count;
            }

            public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
            {
                const string cellId = @"MTStatusBarOverlayHistoryCellID";

                // step 1: is there a reusable cell?
                var cell = tableView.DequeueReusableCell(cellId);

                // step 2: no? -> create new cell
                if (cell == null)
                {
                    cell = new UITableViewCell(UITableViewCellStyle.Value1, cellId);

                    cell.TextLabel.Font = UIFont.BoldSystemFontOfSize(10);
                    cell.TextLabel.TextColor = UIApplication.SharedApplication.StatusBarStyle ==
                                               UIStatusBarStyle.Default
                                                   ? _overlay._lightThemeHistoryTextColor
                                                   : _overlay._darkThemeHistoryTextColor;

                    cell.DetailTextLabel.Font = UIFont.BoldSystemFontOfSize(12);
                    cell.DetailTextLabel.TextColor = UIApplication.SharedApplication.StatusBarStyle ==
                                                     UIStatusBarStyle.Default
                                                         ? _overlay._lightThemeHistoryTextColor
                                                         : _overlay._darkThemeHistoryTextColor;
                }

                // step 3: set up cell value
                cell.TextLabel.Text = _overlay.MessageHistory[indexPath.Row];
                cell.DetailTextLabel.Text = FinishedText;

                return cell;
            }

        }

        ////////////////////////////////////////////////////////////////////////
        //  Gesture Recognizer
        ////////////////////////////////////////////////////////////////////////

        public void ContentViewSwipedUp(UIGestureRecognizer gestureRecognizer)
        {
            if (gestureRecognizer.State != UIGestureRecognizerState.Ended) return;
            SetDetailViewHidden(true, true);
            if (Delegate != null)
            {
                Delegate.StatusBarOverlayDidRecognizeGesture(gestureRecognizer);
            }
        }

        public void ContentViewSwipedDown(UIGestureRecognizer gestureRecognizer)
        {
            if (gestureRecognizer.State != UIGestureRecognizerState.Ended) return;
            SetDetailViewHidden(false, true);
            if (Delegate != null)
            {
                Delegate.StatusBarOverlayDidRecognizeGesture(gestureRecognizer);
            }
        }

        ////////////////////////////////////////////////////////////////////////
        //  UIApplication Notifications
        ////////////////////////////////////////////////////////////////////////

        [Action("applicationWillResignActive:")]
        public void ApplicationWillResignActive(NetworkReachability.Notification notifaction)
        {
            // We hide temporary when the application resigns active s.t the overlay
            // doesn't overlay the Notification Center. Let's hope this helps AppStore 
            // Approval ...
            HideTemporary();
        }

        [Action("applicationDidBecomeActive:")]
        public void ApplicationDidBecomeActive(NetworkReachability.Notification notifaction)
        {
            Show();
        }

        ////////////////////////////////////////////////////////////////////////
        //  Private Methods
        ////////////////////////////////////////////////////////////////////////

        public void SetStatusBarBackgroundForStyle(UIStatusBarStyle style)
        {
            // gray status bar?
            // on iPad the Default Status Bar Style is black too
            if (style == UIStatusBarStyle.Default && !IsIPad && !_isIPhoneEmulationMode)
            {
                // choose image depending on size
                StatusBarBackgroundImageView.Image = Shrinked
                                                         ? DefaultStatusBarImageShrinked.StretchableImage(2, 0)
                                                         : DefaultStatusBarImage.StretchableImage(2, 0);
                StatusBarBackgroundImageView.BackgroundColor = UIColor.Clear;
            }
                // black status bar? -> no image
            else
            {
                StatusBarBackgroundImageView.Image = null;
                StatusBarBackgroundImageView.BackgroundColor = UIColor.Black;
            }
        }

        public void SetColorSchemeForStatusBarStyle(UIStatusBarStyle style, MessageType messageType)
        {
            // gray status bar?
            // on iPad the Default Status Bar Style is black too
            if (style == UIStatusBarStyle.Default && !IsIPad && !_isIPhoneEmulationMode)
            {
                // set color of labels depending on messageType
                switch (messageType)
                {
                    case MessageType.Finish:
                        StatusLabel1.TextColor = _lightThemeFinishedMessageTextColor;
                        StatusLabel2.TextColor = _lightThemeFinishedMessageTextColor;
                        FinishedLabel.TextColor = _lightThemeFinishedMessageTextColor;
                        StatusLabel1.ShadowColor = _lightThemeFinishedMessageShadowColor;
                        StatusLabel2.ShadowColor = _lightThemeFinishedMessageShadowColor;
                        FinishedLabel.ShadowColor = _lightThemeFinishedMessageShadowColor;
                        break;
                    case MessageType.Error:
                        StatusLabel1.TextColor = _lightThemeErrorMessageTextColor;
                        StatusLabel2.TextColor = _lightThemeErrorMessageTextColor;
                        FinishedLabel.TextColor = _lightThemeErrorMessageTextColor;
                        StatusLabel1.ShadowColor = _lightThemeErrorMessageShadowColor;
                        StatusLabel2.ShadowColor = _lightThemeErrorMessageShadowColor;
                        FinishedLabel.ShadowColor = _lightThemeErrorMessageShadowColor;
                        break;
                    default:
                        StatusLabel1.TextColor = _lightThemeTextColor;
                        StatusLabel2.TextColor = _lightThemeTextColor;
                        FinishedLabel.TextColor = _lightThemeTextColor;
                        StatusLabel1.ShadowColor = _lightThemeShadowColor;
                        StatusLabel2.ShadowColor = _lightThemeShadowColor;
                        FinishedLabel.ShadowColor = _lightThemeShadowColor;
                        break;
                }

                ActivityIndicator.ActivityIndicatorViewStyle = LightThemeActivityIndicatorViewStyle;

                if (ActivityIndicator.RespondsToSelector(new Selector("setColor:")))
                {
                    ActivityIndicator.Color = _lightThemeTextColor;
                }

                DetailView.BackgroundColor = _lightThemeDetailViewBackgroundColor;
                DetailView.Layer.BorderColor = _lightThemeDetailViewBorderColor.CGColor;
                HistoryTableView.SeparatorColor = _lightThemeDetailViewBorderColor;
                DetailTextView.TextColor = _lightThemeHistoryTextColor;

                ProgressView.BackgroundColor = UIColor.Clear; //) clearColor];
                ProgressView.Image = DefaultStatusBarImageShrinked.StretchableImage(2, 0);
            }
            else
            {
                // set color of labels depending on messageType
                switch (messageType)
                {
                    case MessageType.Finish:
                        StatusLabel1.TextColor = _darkThemeFinishedMessageTextColor;
                        StatusLabel2.TextColor = _darkThemeFinishedMessageTextColor;
                        FinishedLabel.TextColor = _darkThemeFinishedMessageTextColor;
                        break;
                    case MessageType.Error:
                        StatusLabel1.TextColor = _darkThemeErrorMessageTextColor;
                        StatusLabel2.TextColor = _darkThemeErrorMessageTextColor;
                        FinishedLabel.TextColor = _darkThemeErrorMessageTextColor;
                        break;
                    default:
                        StatusLabel1.TextColor = _darkThemeTextColor;
                        StatusLabel2.TextColor = _darkThemeTextColor;
                        FinishedLabel.TextColor = _darkThemeTextColor;
                        break;
                }
                /*
                StatusLabel1.ShadowColor = null;
                StatusLabel2.ShadowColor = null;
                FinishedLabel.ShadowColor = null;
*/
                ActivityIndicator.ActivityIndicatorViewStyle = DarkThemeActivityIndicatorViewStyle;

                if (ActivityIndicator.RespondsToSelector(new Selector("setColor:")))
                {
                    ActivityIndicator.Color = null;
                }

                DetailView.BackgroundColor = _darkThemeDetailViewBackgroundColor;
                DetailView.Layer.BorderColor = _darkThemeDetailViewBorderColor.CGColor;
                HistoryTableView.SeparatorColor = _darkThemeDetailViewBorderColor;
                DetailTextView.TextColor = _darkThemeHistoryTextColor;

                ProgressView.BackgroundColor = ProgressViewBackgroundColor;
                ProgressView.Image = null;
            }
        }

        public void UpdateUiForMessageType(MessageType messageType, TimeSpan duration)
        {
            // set properties depending on message-type
            switch (messageType)
            {
                case MessageType.Activity:
                    // will not call hide after delay
                    HideInProgress = false;
                    // show activity indicator, hide finished-label
                    FinishedLabel.Hidden = true;
                    ActivityIndicator.Hidden = HidesActivity;

                    // start activity indicator
                    if (!HidesActivity)
                    {
                        ActivityIndicator.StartAnimating();
                    }
                    break;
                case MessageType.Finish:
                    // will call hide after delay
                    HideInProgress = true;
                    // show finished-label, hide acitvity indicator
                    FinishedLabel.Hidden = HidesActivity;
                    ActivityIndicator.Hidden = true;

                    // stop activity indicator
                    ActivityIndicator.StopAnimating();

                    // update font and text
                    FinishedLabel.Font = UIFont.FromName(@"HelveticaNeue-Bold", FinishedFontSize);
                    FinishedLabel.Text = FinishedText;
                    Progress = 1.0;
                    break;
                case MessageType.Error:
                    // will call hide after delay
                    HideInProgress = true;
                    // show finished-label, hide activity indicator
                    FinishedLabel.Hidden = HidesActivity;
                    ActivityIndicator.Hidden = true;

                    // stop activity indicator
                    ActivityIndicator.StopAnimating();

                    // update font and text
                    FinishedLabel.Font = UIFont.BoldSystemFontOfSize(ErrorFontSize);
                    FinishedLabel.Text = ErrorText;
                    Progress = 1.0;
                    break;
            }

            // if a duration is specified, hide after given duration
            if (duration <= TimeSpan.FromSeconds(0)) return;
            // hide after duration
            PerformSelector(new Selector("hide:"), null, duration.TotalSeconds);
            // clear history after duration
            PerformSelector(new Selector("clearHistory:"), null, duration.TotalSeconds);
        }

        public void CallDelegateWithNewMessage(String newMessage)
        {
            if (Delegate == null) return;
            String oldMessage = null;
            if (MessageHistory.Count > 0)
            {
                oldMessage = MessageHistory.Last();
            }
            Delegate.StatusBarOverlayDidSwitchFromOldMessage(oldMessage, newMessage);
        }

        public void UpdateDetailTextViewHeight()
        {
            var f = DetailTextView.Frame;
            f.Size = new SizeF(f.Size.Width, DetailTextView.ContentSize.Height);
            DetailTextView.Frame = f;
        }

        public void UpdateProgressViewSizeForLabel(UILabel label)
        {
            if (Progress < 1)
            {
                var size = label.SizeThatFits(label.Frame.Size);
                var width = size.Width*(float) (1 - Progress);
                var x = label.Center.X + size.Width/2f - width;

                // if we werent able to determine a size, do falsething
                if (size.Width == 0f)
                {
                    return;
                }

                // progressView always covers only the visible portion of the text
                // it "shrinks" to the right with increased progress to reveal more
                // text under it
                ProgressView.Hidden = false;
                //[UIView animateWithDuration:progress > 0.0 ? kUpdateProgressViewDuration : 0.0
                //                 animations:^{
                ProgressView.Frame = new RectangleF(x, ProgressView.Frame.Location.Y, //origin.y,
                                                    BackgroundView.Frame.Size.Width - x, ProgressView.Frame.Size.Height);
            }
            else
            {
                ProgressView.Hidden = true;
            }
        }

        ////////////////////////////////////////////////////////////////////////
        // 
        //  History Tracking
        ////////////////////////////////////////////////////////////////////////

        // DEPRECATED: enable/disable history-tracking of messages
        private bool HistoryEnabled
        {
            get { return DetailViewMode == DetailViewMode.History; }
        }

        public void SetHistoryEnabled(bool historyEnabled)
        {
            DetailViewMode = historyEnabled ? DetailViewMode.History : DetailViewMode.Custom;
            HistoryTableView.Hidden = !historyEnabled;
        }

        public void AddMessageToHistory(String message)
        {
            if (message == null || message.Trim().Length <= 0) return;
            //&& [message stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceCharacterSet]].Length > 0) {
            // add message to history-array
            MessageHistory.Add(message);

            if (!HistoryEnabled) return;
            var newHistoryMessageIndexPath = NSIndexPath.FromRowSection(MessageHistory.Count - 1, 0);
            SetDetailViewHidden(DetailViewHidden, true);

            // update history table-view
            HistoryTableView.InsertRows(new[] {newHistoryMessageIndexPath},
                                        UITableViewRowAnimation.Fade);
            HistoryTableView.ScrollToRow(newHistoryMessageIndexPath, UITableViewScrollPosition.Top, true);
        }

        [Action("clearHistory:")]
        public void ClearHistory()
        {
            MessageHistory.Clear();
            HistoryTableView.ReloadData();
        }

        ////////////////////////////////////////////////////////////////////////
        // 
        //  Custom Hide Methods
        ////////////////////////////////////////////////////////////////////////

        // used for performSelector:withObject
        public void SetHiddenUsingAlpha(bool hidden)
        {
            SetHidden(hidden, true);
        }

        public void SetHidden(bool hidden, bool useAlpha)
        {
            if (useAlpha)
            {
                Alpha = hidden ? 0f : 1f;
            }
            else
            {
                Hidden = hidden;
            }
        }

        // read out hidden-state using alpha-value and hidden-property
        public bool ReallyHidden
        {
            get { return Alpha == 0f || Hidden; }
        }

        ////////////////////////////////////////////////////////////////////////
        // 
        //  Encoded images
        ////////////////////////////////////////////////////////////////////////

        private readonly byte[] _silverBasePng =
            {
                0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d,
                0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x14,
                0x08, 0x02, 0x00, 0x00, 0x00, 0xca, 0x87, 0x60, 0x8c, 0x00, 0x00, 0x00,
                0x66, 0x49, 0x44, 0x41, 0x54, 0x18, 0x19, 0x63, 0xc8, 0xca, 0xc9, 0xbd,
                0x7c, 0xe3, 0xf6, 0xfb, 0x6f, 0x3f, 0x81, 0x24, 0x90, 0xcd, 0x78, 0xf5,
                0xe6, 0x5d, 0x09, 0x19, 0x29, 0x06, 0x30, 0x78, 0xf1, 0xe4, 0x19, 0xe3,
                0xeb, 0xcf, 0xdf, 0x20, 0x1c, 0x08, 0xc9, 0xf2, 0xef, 0xef, 0x3f, 0x54,
                0xfe, 0xbf, 0xbf, 0x28, 0xfc, 0xbf, 0x7f, 0x51, 0xf9, 0x7f, 0xd0, 0xf8,
                0xff, 0xd0, 0xf8, 0x7f, 0xff, 0xe0, 0x57, 0xff, 0x07, 0xcd, 0xfc, 0x3f,
                0x68, 0xea, 0xff, 0xa1, 0xf1, 0xff, 0xfe, 0xfd, 0x83, 0xe2, 0x9e, 0x7f,
                0xe8, 0xfa, 0xd1, 0xed, 0xff, 0x87, 0xe6, 0x1f, 0x0c, 0xff, 0xa1, 0xca,
                0xcf, 0x9f, 0x37, 0x1f, 0xd9, 0x7c, 0x00, 0x5e, 0xf4, 0x44, 0x69, 0xf0,
                0x03, 0xee, 0x97, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4e, 0x44, 0xae,
                0x42, 0x60, 0x82
            };

        private const int SilverBasePngLen = 159;

        private readonly byte[] _silverBaseShrinkedPng =
            {
                0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d,
                0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x14,
                0x08, 0x02, 0x00, 0x00, 0x00, 0xca, 0x87, 0x60, 0x8c, 0x00, 0x00, 0x00,
                0x19, 0x74, 0x45, 0x58, 0x74, 0x53, 0x6f, 0x66, 0x74, 0x77, 0x61, 0x72,
                0x65, 0x00, 0x41, 0x64, 0x6f, 0x62, 0x65, 0x20, 0x49, 0x6d, 0x61, 0x67,
                0x65, 0x52, 0x65, 0x61, 0x64, 0x79, 0x71, 0xc9, 0x65, 0x3c, 0x00, 0x00,
                0x03, 0x22, 0x69, 0x54, 0x58, 0x74, 0x58, 0x4d, 0x4c, 0x3a, 0x63, 0x6f,
                0x6d, 0x2e, 0x61, 0x64, 0x6f, 0x62, 0x65, 0x2e, 0x78, 0x6d, 0x70, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x3c, 0x3f, 0x78, 0x70, 0x61, 0x63, 0x6b, 0x65,
                0x74, 0x20, 0x62, 0x65, 0x67, 0x69, 0x6e, 0x3d, 0x22, 0xef, 0xbb, 0xbf,
                0x22, 0x20, 0x69, 0x64, 0x3d, 0x22, 0x57, 0x35, 0x4d, 0x30, 0x4d, 0x70,
                0x43, 0x65, 0x68, 0x69, 0x48, 0x7a, 0x72, 0x65, 0x53, 0x7a, 0x4e, 0x54,
                0x63, 0x7a, 0x6b, 0x63, 0x39, 0x64, 0x22, 0x3f, 0x3e, 0x20, 0x3c, 0x78,
                0x3a, 0x78, 0x6d, 0x70, 0x6d, 0x65, 0x74, 0x61, 0x20, 0x78, 0x6d, 0x6c,
                0x6e, 0x73, 0x3a, 0x78, 0x3d, 0x22, 0x61, 0x64, 0x6f, 0x62, 0x65, 0x3a,
                0x6e, 0x73, 0x3a, 0x6d, 0x65, 0x74, 0x61, 0x2f, 0x22, 0x20, 0x78, 0x3a,
                0x78, 0x6d, 0x70, 0x74, 0x6b, 0x3d, 0x22, 0x41, 0x64, 0x6f, 0x62, 0x65,
                0x20, 0x58, 0x4d, 0x50, 0x20, 0x43, 0x6f, 0x72, 0x65, 0x20, 0x35, 0x2e,
                0x30, 0x2d, 0x63, 0x30, 0x36, 0x30, 0x20, 0x36, 0x31, 0x2e, 0x31, 0x33,
                0x34, 0x37, 0x37, 0x37, 0x2c, 0x20, 0x32, 0x30, 0x31, 0x30, 0x2f, 0x30,
                0x32, 0x2f, 0x31, 0x32, 0x2d, 0x31, 0x37, 0x3a, 0x33, 0x32, 0x3a, 0x30,
                0x30, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x22, 0x3e, 0x20,
                0x3c, 0x72, 0x64, 0x66, 0x3a, 0x52, 0x44, 0x46, 0x20, 0x78, 0x6d, 0x6c,
                0x6e, 0x73, 0x3a, 0x72, 0x64, 0x66, 0x3d, 0x22, 0x68, 0x74, 0x74, 0x70,
                0x3a, 0x2f, 0x2f, 0x77, 0x77, 0x77, 0x2e, 0x77, 0x33, 0x2e, 0x6f, 0x72,
                0x67, 0x2f, 0x31, 0x39, 0x39, 0x39, 0x2f, 0x30, 0x32, 0x2f, 0x32, 0x32,
                0x2d, 0x72, 0x64, 0x66, 0x2d, 0x73, 0x79, 0x6e, 0x74, 0x61, 0x78, 0x2d,
                0x6e, 0x73, 0x23, 0x22, 0x3e, 0x20, 0x3c, 0x72, 0x64, 0x66, 0x3a, 0x44,
                0x65, 0x73, 0x63, 0x72, 0x69, 0x70, 0x74, 0x69, 0x6f, 0x6e, 0x20, 0x72,
                0x64, 0x66, 0x3a, 0x61, 0x62, 0x6f, 0x75, 0x74, 0x3d, 0x22, 0x22, 0x20,
                0x78, 0x6d, 0x6c, 0x6e, 0x73, 0x3a, 0x78, 0x6d, 0x70, 0x3d, 0x22, 0x68,
                0x74, 0x74, 0x70, 0x3a, 0x2f, 0x2f, 0x6e, 0x73, 0x2e, 0x61, 0x64, 0x6f,
                0x62, 0x65, 0x2e, 0x63, 0x6f, 0x6d, 0x2f, 0x78, 0x61, 0x70, 0x2f, 0x31,
                0x2e, 0x30, 0x2f, 0x22, 0x20, 0x78, 0x6d, 0x6c, 0x6e, 0x73, 0x3a, 0x78,
                0x6d, 0x70, 0x4d, 0x4d, 0x3d, 0x22, 0x68, 0x74, 0x74, 0x70, 0x3a, 0x2f,
                0x2f, 0x6e, 0x73, 0x2e, 0x61, 0x64, 0x6f, 0x62, 0x65, 0x2e, 0x63, 0x6f,
                0x6d, 0x2f, 0x78, 0x61, 0x70, 0x2f, 0x31, 0x2e, 0x30, 0x2f, 0x6d, 0x6d,
                0x2f, 0x22, 0x20, 0x78, 0x6d, 0x6c, 0x6e, 0x73, 0x3a, 0x73, 0x74, 0x52,
                0x65, 0x66, 0x3d, 0x22, 0x68, 0x74, 0x74, 0x70, 0x3a, 0x2f, 0x2f, 0x6e,
                0x73, 0x2e, 0x61, 0x64, 0x6f, 0x62, 0x65, 0x2e, 0x63, 0x6f, 0x6d, 0x2f,
                0x78, 0x61, 0x70, 0x2f, 0x31, 0x2e, 0x30, 0x2f, 0x73, 0x54, 0x79, 0x70,
                0x65, 0x2f, 0x52, 0x65, 0x73, 0x6f, 0x75, 0x72, 0x63, 0x65, 0x52, 0x65,
                0x66, 0x23, 0x22, 0x20, 0x78, 0x6d, 0x70, 0x3a, 0x43, 0x72, 0x65, 0x61,
                0x74, 0x6f, 0x72, 0x54, 0x6f, 0x6f, 0x6c, 0x3d, 0x22, 0x41, 0x64, 0x6f,
                0x62, 0x65, 0x20, 0x50, 0x68, 0x6f, 0x74, 0x6f, 0x73, 0x68, 0x6f, 0x70,
                0x20, 0x43, 0x53, 0x35, 0x20, 0x4d, 0x61, 0x63, 0x69, 0x6e, 0x74, 0x6f,
                0x73, 0x68, 0x22, 0x20, 0x78, 0x6d, 0x70, 0x4d, 0x4d, 0x3a, 0x49, 0x6e,
                0x73, 0x74, 0x61, 0x6e, 0x63, 0x65, 0x49, 0x44, 0x3d, 0x22, 0x78, 0x6d,
                0x70, 0x2e, 0x69, 0x69, 0x64, 0x3a, 0x31, 0x37, 0x37, 0x36, 0x31, 0x30,
                0x43, 0x42, 0x32, 0x33, 0x33, 0x34, 0x31, 0x31, 0x45, 0x30, 0x38, 0x45,
                0x42, 0x44, 0x43, 0x42, 0x33, 0x39, 0x37, 0x38, 0x33, 0x31, 0x39, 0x45,
                0x45, 0x35, 0x22, 0x20, 0x78, 0x6d, 0x70, 0x4d, 0x4d, 0x3a, 0x44, 0x6f,
                0x63, 0x75, 0x6d, 0x65, 0x6e, 0x74, 0x49, 0x44, 0x3d, 0x22, 0x78, 0x6d,
                0x70, 0x2e, 0x64, 0x69, 0x64, 0x3a, 0x31, 0x37, 0x37, 0x36, 0x31, 0x30,
                0x43, 0x43, 0x32, 0x33, 0x33, 0x34, 0x31, 0x31, 0x45, 0x30, 0x38, 0x45,
                0x42, 0x44, 0x43, 0x42, 0x33, 0x39, 0x37, 0x38, 0x33, 0x31, 0x39, 0x45,
                0x45, 0x35, 0x22, 0x3e, 0x20, 0x3c, 0x78, 0x6d, 0x70, 0x4d, 0x4d, 0x3a,
                0x44, 0x65, 0x72, 0x69, 0x76, 0x65, 0x64, 0x46, 0x72, 0x6f, 0x6d, 0x20,
                0x73, 0x74, 0x52, 0x65, 0x66, 0x3a, 0x69, 0x6e, 0x73, 0x74, 0x61, 0x6e,
                0x63, 0x65, 0x49, 0x44, 0x3d, 0x22, 0x78, 0x6d, 0x70, 0x2e, 0x69, 0x69,
                0x64, 0x3a, 0x31, 0x37, 0x37, 0x36, 0x31, 0x30, 0x43, 0x39, 0x32, 0x33,
                0x33, 0x34, 0x31, 0x31, 0x45, 0x30, 0x38, 0x45, 0x42, 0x44, 0x43, 0x42,
                0x33, 0x39, 0x37, 0x38, 0x33, 0x31, 0x39, 0x45, 0x45, 0x35, 0x22, 0x20,
                0x73, 0x74, 0x52, 0x65, 0x66, 0x3a, 0x64, 0x6f, 0x63, 0x75, 0x6d, 0x65,
                0x6e, 0x74, 0x49, 0x44, 0x3d, 0x22, 0x78, 0x6d, 0x70, 0x2e, 0x64, 0x69,
                0x64, 0x3a, 0x31, 0x37, 0x37, 0x36, 0x31, 0x30, 0x43, 0x41, 0x32, 0x33,
                0x33, 0x34, 0x31, 0x31, 0x45, 0x30, 0x38, 0x45, 0x42, 0x44, 0x43, 0x42,
                0x33, 0x39, 0x37, 0x38, 0x33, 0x31, 0x39, 0x45, 0x45, 0x35, 0x22, 0x2f,
                0x3e, 0x20, 0x3c, 0x2f, 0x72, 0x64, 0x66, 0x3a, 0x44, 0x65, 0x73, 0x63,
                0x72, 0x69, 0x70, 0x74, 0x69, 0x6f, 0x6e, 0x3e, 0x20, 0x3c, 0x2f, 0x72,
                0x64, 0x66, 0x3a, 0x52, 0x44, 0x46, 0x3e, 0x20, 0x3c, 0x2f, 0x78, 0x3a,
                0x78, 0x6d, 0x70, 0x6d, 0x65, 0x74, 0x61, 0x3e, 0x20, 0x3c, 0x3f, 0x78,
                0x70, 0x61, 0x63, 0x6b, 0x65, 0x74, 0x20, 0x65, 0x6e, 0x64, 0x3d, 0x22,
                0x72, 0x22, 0x3f, 0x3e, 0x6a, 0xe1, 0x0d, 0xf2, 0x00, 0x00, 0x00, 0x65,
                0x49, 0x44, 0x41, 0x54, 0x78, 0xda, 0x62, 0x7c, 0xff, 0xed, 0x27, 0x03,
                0x18, 0x3c, 0x79, 0xf4, 0x68, 0xfa, 0x94, 0x49, 0x8c, 0x6f, 0xbf, 0x7e,
                0x67, 0x80, 0x81, 0x17, 0x4f, 0x9e, 0xb1, 0xfc, 0xfb, 0xf7, 0x1f, 0xce,
                0x17, 0x93, 0x92, 0x64, 0xf9, 0xf7, 0xf7, 0x1f, 0x03, 0x12, 0x00, 0xca,
                0xff, 0x45, 0xe1, 0xff, 0xfd, 0x8b, 0xca, 0xff, 0x83, 0xc6, 0xff, 0x87,
                0xc6, 0xff, 0xfb, 0x07, 0xbf, 0xfa, 0x3f, 0x68, 0xe6, 0xff, 0x41, 0x53,
                0xff, 0xef, 0x0f, 0xba, 0xfd, 0x7f, 0xf0, 0xba, 0x0f, 0xdd, 0xfc, 0xbf,
                0xff, 0xd0, 0xfc, 0x83, 0xe1, 0x3f, 0x54, 0xfe, 0xfc, 0x79, 0xf3, 0x91,
                0xf9, 0x00, 0x01, 0x06, 0x00, 0xfe, 0x10, 0x41, 0xb0, 0x8a, 0x17, 0x69,
                0xf0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60,
                0x82
            };

        private const int SilverBaseShrinkedPngLen = 1009;

        private readonly byte[] _silverBase_2XPng =
            {
                0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d,
                0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x0a, 0x00, 0x00, 0x00, 0x28,
                0x08, 0x02, 0x00, 0x00, 0x00, 0x48, 0x34, 0xfc, 0xd7, 0x00, 0x00, 0x00,
                0x09, 0x70, 0x48, 0x59, 0x73, 0x00, 0x00, 0x16, 0x25, 0x00, 0x00, 0x16,
                0x25, 0x01, 0x49, 0x52, 0x24, 0xf0, 0x00, 0x00, 0x00, 0xb9, 0x49, 0x44,
                0x41, 0x54, 0x38, 0x11, 0x63, 0xcc, 0xca, 0xc9, 0x15, 0x11, 0x15, 0xf5,
                0xf6, 0xf3, 0x57, 0x54, 0x52, 0x66, 0x65, 0x65, 0x65, 0x60, 0x60, 0xf8,
                0xf5, 0xeb, 0xe7, 0x83, 0xfb, 0xf7, 0xb7, 0x6e, 0xda, 0xf8, 0xe6, 0xf5,
                0x6b, 0x66, 0x4f, 0x6f, 0x9f, 0xdc, 0xc2, 0x62, 0xa0, 0x0a, 0x26, 0x26,
                0x26, 0xa0, 0x1c, 0x10, 0x30, 0x33, 0x33, 0x0b, 0x0b, 0x8b, 0x18, 0x99,
                0x98, 0x5c, 0xb9, 0x74, 0x89, 0xf1, 0xd4, 0x85, 0x4b, 0x2a, 0xaa, 0x6a,
                0x10, 0x09, 0x34, 0xf2, 0xda, 0x95, 0x2b, 0x8c, 0xaf, 0x3e, 0x7e, 0x01,
                0x2a, 0x47, 0x93, 0x80, 0x70, 0x7f, 0xfd, 0xfa, 0xc5, 0x02, 0x34, 0xf3,
                0xff, 0xff, 0xff, 0x58, 0xa5, 0x81, 0x4e, 0x61, 0xc1, 0x25, 0x07, 0xd1,
                0x40, 0xa1, 0xf4, 0x3f, 0x1c, 0x16, 0xc3, 0x0c, 0xff, 0xf7, 0x0f, 0xab,
                0xbb, 0x60, 0xd2, 0xf8, 0x75, 0x13, 0x34, 0x1c, 0xbb, 0xa7, 0xe1, 0x86,
                0xe3, 0xb5, 0x9b, 0xa0, 0xe1, 0x83, 0x57, 0xf7, 0x3f, 0xbc, 0xfe, 0x1e,
                0xc2, 0x1e, 0xc3, 0x9f, 0x1c, 0xfe, 0xff, 0xc7, 0x1b, 0x25, 0xff, 0x29,
                0x0b, 0x16, 0x02, 0x76, 0xe3, 0x4f, 0xc8, 0x84, 0xc2, 0x9c, 0x80, 0xe1,
                0xb4, 0x94, 0x3e, 0x77, 0xf6, 0x1c, 0xbe, 0x2c, 0x78, 0xfe, 0xfc, 0x79,
                0x3c, 0xd2, 0x00, 0x60, 0xe2, 0x89, 0x27, 0x38, 0x98, 0xa8, 0xf6, 0x00,
                0x00, 0x00, 0x00, 0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60, 0x82
            };

        private const int SilverBase_2XPngLen = 263;

        private readonly byte[] _silverBaseShrinked_2XPng =
            {
                0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d,
                0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x0a, 0x00, 0x00, 0x00, 0x28,
                0x08, 0x02, 0x00, 0x00, 0x00, 0x48, 0x34, 0xfc, 0xd7, 0x00, 0x00, 0x00,
                0x19, 0x74, 0x45, 0x58, 0x74, 0x53, 0x6f, 0x66, 0x74, 0x77, 0x61, 0x72,
                0x65, 0x00, 0x41, 0x64, 0x6f, 0x62, 0x65, 0x20, 0x49, 0x6d, 0x61, 0x67,
                0x65, 0x52, 0x65, 0x61, 0x64, 0x79, 0x71, 0xc9, 0x65, 0x3c, 0x00, 0x00,
                0x03, 0x22, 0x69, 0x54, 0x58, 0x74, 0x58, 0x4d, 0x4c, 0x3a, 0x63, 0x6f,
                0x6d, 0x2e, 0x61, 0x64, 0x6f, 0x62, 0x65, 0x2e, 0x78, 0x6d, 0x70, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x3c, 0x3f, 0x78, 0x70, 0x61, 0x63, 0x6b, 0x65,
                0x74, 0x20, 0x62, 0x65, 0x67, 0x69, 0x6e, 0x3d, 0x22, 0xef, 0xbb, 0xbf,
                0x22, 0x20, 0x69, 0x64, 0x3d, 0x22, 0x57, 0x35, 0x4d, 0x30, 0x4d, 0x70,
                0x43, 0x65, 0x68, 0x69, 0x48, 0x7a, 0x72, 0x65, 0x53, 0x7a, 0x4e, 0x54,
                0x63, 0x7a, 0x6b, 0x63, 0x39, 0x64, 0x22, 0x3f, 0x3e, 0x20, 0x3c, 0x78,
                0x3a, 0x78, 0x6d, 0x70, 0x6d, 0x65, 0x74, 0x61, 0x20, 0x78, 0x6d, 0x6c,
                0x6e, 0x73, 0x3a, 0x78, 0x3d, 0x22, 0x61, 0x64, 0x6f, 0x62, 0x65, 0x3a,
                0x6e, 0x73, 0x3a, 0x6d, 0x65, 0x74, 0x61, 0x2f, 0x22, 0x20, 0x78, 0x3a,
                0x78, 0x6d, 0x70, 0x74, 0x6b, 0x3d, 0x22, 0x41, 0x64, 0x6f, 0x62, 0x65,
                0x20, 0x58, 0x4d, 0x50, 0x20, 0x43, 0x6f, 0x72, 0x65, 0x20, 0x35, 0x2e,
                0x30, 0x2d, 0x63, 0x30, 0x36, 0x30, 0x20, 0x36, 0x31, 0x2e, 0x31, 0x33,
                0x34, 0x37, 0x37, 0x37, 0x2c, 0x20, 0x32, 0x30, 0x31, 0x30, 0x2f, 0x30,
                0x32, 0x2f, 0x31, 0x32, 0x2d, 0x31, 0x37, 0x3a, 0x33, 0x32, 0x3a, 0x30,
                0x30, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x22, 0x3e, 0x20,
                0x3c, 0x72, 0x64, 0x66, 0x3a, 0x52, 0x44, 0x46, 0x20, 0x78, 0x6d, 0x6c,
                0x6e, 0x73, 0x3a, 0x72, 0x64, 0x66, 0x3d, 0x22, 0x68, 0x74, 0x74, 0x70,
                0x3a, 0x2f, 0x2f, 0x77, 0x77, 0x77, 0x2e, 0x77, 0x33, 0x2e, 0x6f, 0x72,
                0x67, 0x2f, 0x31, 0x39, 0x39, 0x39, 0x2f, 0x30, 0x32, 0x2f, 0x32, 0x32,
                0x2d, 0x72, 0x64, 0x66, 0x2d, 0x73, 0x79, 0x6e, 0x74, 0x61, 0x78, 0x2d,
                0x6e, 0x73, 0x23, 0x22, 0x3e, 0x20, 0x3c, 0x72, 0x64, 0x66, 0x3a, 0x44,
                0x65, 0x73, 0x63, 0x72, 0x69, 0x70, 0x74, 0x69, 0x6f, 0x6e, 0x20, 0x72,
                0x64, 0x66, 0x3a, 0x61, 0x62, 0x6f, 0x75, 0x74, 0x3d, 0x22, 0x22, 0x20,
                0x78, 0x6d, 0x6c, 0x6e, 0x73, 0x3a, 0x78, 0x6d, 0x70, 0x3d, 0x22, 0x68,
                0x74, 0x74, 0x70, 0x3a, 0x2f, 0x2f, 0x6e, 0x73, 0x2e, 0x61, 0x64, 0x6f,
                0x62, 0x65, 0x2e, 0x63, 0x6f, 0x6d, 0x2f, 0x78, 0x61, 0x70, 0x2f, 0x31,
                0x2e, 0x30, 0x2f, 0x22, 0x20, 0x78, 0x6d, 0x6c, 0x6e, 0x73, 0x3a, 0x78,
                0x6d, 0x70, 0x4d, 0x4d, 0x3d, 0x22, 0x68, 0x74, 0x74, 0x70, 0x3a, 0x2f,
                0x2f, 0x6e, 0x73, 0x2e, 0x61, 0x64, 0x6f, 0x62, 0x65, 0x2e, 0x63, 0x6f,
                0x6d, 0x2f, 0x78, 0x61, 0x70, 0x2f, 0x31, 0x2e, 0x30, 0x2f, 0x6d, 0x6d,
                0x2f, 0x22, 0x20, 0x78, 0x6d, 0x6c, 0x6e, 0x73, 0x3a, 0x73, 0x74, 0x52,
                0x65, 0x66, 0x3d, 0x22, 0x68, 0x74, 0x74, 0x70, 0x3a, 0x2f, 0x2f, 0x6e,
                0x73, 0x2e, 0x61, 0x64, 0x6f, 0x62, 0x65, 0x2e, 0x63, 0x6f, 0x6d, 0x2f,
                0x78, 0x61, 0x70, 0x2f, 0x31, 0x2e, 0x30, 0x2f, 0x73, 0x54, 0x79, 0x70,
                0x65, 0x2f, 0x52, 0x65, 0x73, 0x6f, 0x75, 0x72, 0x63, 0x65, 0x52, 0x65,
                0x66, 0x23, 0x22, 0x20, 0x78, 0x6d, 0x70, 0x3a, 0x43, 0x72, 0x65, 0x61,
                0x74, 0x6f, 0x72, 0x54, 0x6f, 0x6f, 0x6c, 0x3d, 0x22, 0x41, 0x64, 0x6f,
                0x62, 0x65, 0x20, 0x50, 0x68, 0x6f, 0x74, 0x6f, 0x73, 0x68, 0x6f, 0x70,
                0x20, 0x43, 0x53, 0x35, 0x20, 0x4d, 0x61, 0x63, 0x69, 0x6e, 0x74, 0x6f,
                0x73, 0x68, 0x22, 0x20, 0x78, 0x6d, 0x70, 0x4d, 0x4d, 0x3a, 0x49, 0x6e,
                0x73, 0x74, 0x61, 0x6e, 0x63, 0x65, 0x49, 0x44, 0x3d, 0x22, 0x78, 0x6d,
                0x70, 0x2e, 0x69, 0x69, 0x64, 0x3a, 0x42, 0x30, 0x37, 0x30, 0x30, 0x45,
                0x33, 0x38, 0x32, 0x33, 0x33, 0x34, 0x31, 0x31, 0x45, 0x30, 0x38, 0x45,
                0x42, 0x44, 0x43, 0x42, 0x33, 0x39, 0x37, 0x38, 0x33, 0x31, 0x39, 0x45,
                0x45, 0x35, 0x22, 0x20, 0x78, 0x6d, 0x70, 0x4d, 0x4d, 0x3a, 0x44, 0x6f,
                0x63, 0x75, 0x6d, 0x65, 0x6e, 0x74, 0x49, 0x44, 0x3d, 0x22, 0x78, 0x6d,
                0x70, 0x2e, 0x64, 0x69, 0x64, 0x3a, 0x42, 0x30, 0x37, 0x30, 0x30, 0x45,
                0x33, 0x39, 0x32, 0x33, 0x33, 0x34, 0x31, 0x31, 0x45, 0x30, 0x38, 0x45,
                0x42, 0x44, 0x43, 0x42, 0x33, 0x39, 0x37, 0x38, 0x33, 0x31, 0x39, 0x45,
                0x45, 0x35, 0x22, 0x3e, 0x20, 0x3c, 0x78, 0x6d, 0x70, 0x4d, 0x4d, 0x3a,
                0x44, 0x65, 0x72, 0x69, 0x76, 0x65, 0x64, 0x46, 0x72, 0x6f, 0x6d, 0x20,
                0x73, 0x74, 0x52, 0x65, 0x66, 0x3a, 0x69, 0x6e, 0x73, 0x74, 0x61, 0x6e,
                0x63, 0x65, 0x49, 0x44, 0x3d, 0x22, 0x78, 0x6d, 0x70, 0x2e, 0x69, 0x69,
                0x64, 0x3a, 0x31, 0x37, 0x37, 0x36, 0x31, 0x30, 0x43, 0x44, 0x32, 0x33,
                0x33, 0x34, 0x31, 0x31, 0x45, 0x30, 0x38, 0x45, 0x42, 0x44, 0x43, 0x42,
                0x33, 0x39, 0x37, 0x38, 0x33, 0x31, 0x39, 0x45, 0x45, 0x35, 0x22, 0x20,
                0x73, 0x74, 0x52, 0x65, 0x66, 0x3a, 0x64, 0x6f, 0x63, 0x75, 0x6d, 0x65,
                0x6e, 0x74, 0x49, 0x44, 0x3d, 0x22, 0x78, 0x6d, 0x70, 0x2e, 0x64, 0x69,
                0x64, 0x3a, 0x31, 0x37, 0x37, 0x36, 0x31, 0x30, 0x43, 0x45, 0x32, 0x33,
                0x33, 0x34, 0x31, 0x31, 0x45, 0x30, 0x38, 0x45, 0x42, 0x44, 0x43, 0x42,
                0x33, 0x39, 0x37, 0x38, 0x33, 0x31, 0x39, 0x45, 0x45, 0x35, 0x22, 0x2f,
                0x3e, 0x20, 0x3c, 0x2f, 0x72, 0x64, 0x66, 0x3a, 0x44, 0x65, 0x73, 0x63,
                0x72, 0x69, 0x70, 0x74, 0x69, 0x6f, 0x6e, 0x3e, 0x20, 0x3c, 0x2f, 0x72,
                0x64, 0x66, 0x3a, 0x52, 0x44, 0x46, 0x3e, 0x20, 0x3c, 0x2f, 0x78, 0x3a,
                0x78, 0x6d, 0x70, 0x6d, 0x65, 0x74, 0x61, 0x3e, 0x20, 0x3c, 0x3f, 0x78,
                0x70, 0x61, 0x63, 0x6b, 0x65, 0x74, 0x20, 0x65, 0x6e, 0x64, 0x3d, 0x22,
                0x72, 0x22, 0x3f, 0x3e, 0xff, 0x12, 0x6f, 0xeb, 0x00, 0x00, 0x00, 0x7a,
                0x49, 0x44, 0x41, 0x54, 0x78, 0xda, 0x62, 0x7c, 0xff, 0xed, 0x27, 0x03,
                0x2a, 0xf8, 0xf5, 0xeb, 0xe7, 0x83, 0xfb, 0xf7, 0xb7, 0x6e, 0xda, 0xf8,
                0xe6, 0xf5, 0x6b, 0xc6, 0x77, 0x5f, 0x7f, 0x30, 0x60, 0x03, 0x40, 0x45,
                0x53, 0x27, 0x4c, 0xc0, 0x29, 0x0d, 0x04, 0xd7, 0xae, 0x5c, 0x61, 0x7c,
                0xfb, 0xe5, 0x3b, 0x2e, 0xe9, 0x5f, 0xbf, 0x7e, 0xb1, 0xfc, 0xff, 0xff,
                0x1f, 0x97, 0x34, 0x2b, 0x2b, 0x2b, 0x3e, 0x69, 0x20, 0xa0, 0x50, 0xfa,
                0x1f, 0x01, 0xdd, 0xff, 0xfe, 0xd1, 0xd2, 0x70, 0x02, 0x2e, 0xff, 0x47,
                0x3b, 0x97, 0x0f, 0xa8, 0xee, 0x7f, 0xff, 0x87, 0xa9, 0xc7, 0xfe, 0x53,
                0x12, 0xa1, 0xff, 0x29, 0x0b, 0x16, 0xda, 0xba, 0x7c, 0xe0, 0xa4, 0xcf,
                0x9d, 0x3d, 0x87, 0x4f, 0xfa, 0xfc, 0xf9, 0xf3, 0x78, 0xa4, 0x01, 0x02,
                0x0c, 0x00, 0x3d, 0x65, 0x8a, 0xd2, 0x20, 0x85, 0xdc, 0x18, 0x00, 0x00,
                0x00, 0x00, 0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60, 0x82
            };

        private const int SilverBaseShrinked_2XPngLen = 1030;


        private byte[] MtStatusBarBackgroundImageArray(bool shrinked)
        {
            if (shrinked)
            {
                return UIScreen.MainScreen.Scale == 1.0 ? _silverBaseShrinkedPng : _silverBaseShrinked_2XPng;
            }
            return UIScreen.MainScreen.Scale == 1.0 ? _silverBasePng : _silverBase_2XPng;
        }

        private int MtStatusBarBackgroundImageLength(bool shrinked)
        {
            if (shrinked)
            {
                return UIScreen.MainScreen.Scale == 1.0 ? SilverBaseShrinkedPngLen : SilverBaseShrinked_2XPngLen;
            }
            return UIScreen.MainScreen.Scale == 1.0 ? SilverBasePngLen : SilverBase_2XPngLen;
        }

    }
}
