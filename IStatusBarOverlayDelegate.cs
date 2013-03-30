using System;
using System.Collections;
using System.Collections.Generic;
using MonoTouch.UIKit;

namespace StatusBarOverlay
{
    public interface IStatusBarOverlayDelegate
    {
        // is called, when a gesture on the overlay is recognized
        void StatusBarOverlayDidRecognizeGesture(UIGestureRecognizer gestureRecognizer);
        // is called when the status bar overlay gets hidden
        void StatusBarOverlayDidHide();
        // is called, when the status bar overlay changed it's displayed message from one message to another
        void StatusBarOverlayDidSwitchFromOldMessage(String oldMessage, String newMessage);
        // is called when an immediate message gets posted and therefore messages in the queue get lost
        // it tells the delegate the lost messages and the delegate can then enqueue the messages again
        void StatusBarOverlayDidClearMessageQueue(IList<IDictionary> messageQueue);
    }
}