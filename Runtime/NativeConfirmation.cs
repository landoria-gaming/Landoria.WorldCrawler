using System;

namespace Landoria.WorldCrawler.Runtime
{
    // Bridges the extra native popup constructor argument added by Valheim 1.0.
    internal static class NativeConfirmation
    {
        // Displays Valheim's Yes/No dialog and closes it before running the selected action.
        internal static void Show(string title, string message, Action<bool> answer)
        {
            if (!UnifiedPopup.IsAvailable() || UnifiedPopup.IsVisible())
            {
                throw new InvalidOperationException("Close the current game dialog before preparing a world.");
            }
            var signature = new[] { typeof(string), typeof(string), typeof(PopupButtonCallback),
                typeof(PopupButtonCallback), typeof(bool), typeof(bool) };
            var constructor = typeof(YesNoPopup).GetConstructor(signature);
            PopupButtonCallback yes = () => { UnifiedPopup.Pop(); answer(true); };
            PopupButtonCallback no = () => { UnifiedPopup.Pop(); answer(false); };
            if (constructor == null)
            {
                throw new MissingMethodException("The current native YesNoPopup constructor is unavailable.");
            }
            UnifiedPopup.Push((PopupBase)constructor.Invoke(new object[] { title, message, yes, no, false, true }));
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
