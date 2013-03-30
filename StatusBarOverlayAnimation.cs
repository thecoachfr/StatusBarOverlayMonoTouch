namespace StatusBarOverlay
{
    /// <summary>
    /// Animation that happens, when the user touches the status bar overlay
    /// </summary>
    public enum StatusBarOverlayAnimation
    {
        None, // nothing happens
        Shrink,
        // the status bar shrinks to the right side and only shows the activity indicator
        FallDown // the status bar falls down and displays more information
    }
}