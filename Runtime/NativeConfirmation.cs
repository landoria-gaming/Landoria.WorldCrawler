using System;

namespace Landoria.WorldCrawler.Runtime
{
    // Displays simple native notices without presenting workflow choices.
    internal static class NativeConfirmation
    {
        // Displays an immediate working notice and returns an idempotent close action.
        internal static Action ShowWorking(string message)
        {
            if (!UnifiedPopup.IsAvailable() || UnifiedPopup.IsVisible())
            {
                throw new InvalidOperationException("Close the current game dialog before preparing a world.");
            }
            var open = true;
            Action close = () =>
            {
                if (!open)
                {
                    return;
                }
                open = false;
                UnifiedPopup.Pop();
            };
            PopupButtonCallback button = () => close();
            UnifiedPopup.Push(new WarningPopup("World Crawler", message, button, false));
            return close;
        }

        // Displays short success or failure feedback using the game's existing OK dialog.
        internal static void Report(string message)
        {
            if (UnifiedPopup.IsAvailable() && !UnifiedPopup.IsVisible())
            {
                UnifiedPopup.Push(new WarningPopup("World Crawler", message, UnifiedPopup.Pop, false));
            }
        }
    }
}
