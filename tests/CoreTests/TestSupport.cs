using System;
using System.IO;

namespace Landoria.WorldCrawler.Tests
{
    // Provides dependency-free assertions and isolated test directories.
    internal static class TestSupport
    {
        // Reports a clear failure when a test invariant does not hold.
        internal static void Check(bool condition, string message)
        {
            if (!condition) { throw new InvalidOperationException(message); }
        }

        // Verifies an operation fails with the expected error category.
        internal static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }

        // Gives one test an isolated directory and removes only that verified directory.
        internal static void InDirectory(Action<string> test)
        {
            var root = Path.Combine(Path.GetTempPath(), "WorldCrawler-CoreTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { test(root); }
            finally
            {
                var full = Path.GetFullPath(root);
                var parent = Path.GetDirectoryName(full);
                var expected = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
                if (parent == expected && Path.GetFileName(full).StartsWith("WorldCrawler-CoreTests-", StringComparison.Ordinal))
                {
                    Directory.Delete(full, true);
                }
            }
        }
    }
}
