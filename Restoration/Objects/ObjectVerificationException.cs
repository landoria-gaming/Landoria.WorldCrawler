using System;

namespace Landoria.WorldCrawler.Restoration.Objects
{
    // Identifies one imported object that failed its post-import checks.
    internal sealed class ObjectVerificationException : InvalidOperationException
    {
        // Keeps the exact verification failure available for the warning log.
        internal ObjectVerificationException(string message) : base(message)
        {
        }
    }
}
