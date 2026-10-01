using System;
using System.Reflection;
using UnityEngine;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Bridges the extra notification argument introduced by the new Valheim version.
    internal static class HudNotification
    {
        private static MethodInfo _method;
        private static bool _latest;

        // Resolves the exact notification signature once before displaying status.
        public static void Initialize()
        {
            _latest = SupportedGameVersions.IsCurrent(GameContext.GameVersion);
            var signature = _latest ? new[] { typeof(MessageHud.MessageType), typeof(string),
                typeof(int), typeof(Sprite), typeof(bool) } : new[] { typeof(MessageHud.MessageType),
                typeof(string), typeof(int), typeof(Sprite) };
            _method = typeof(Player).GetMethod("Message", signature)
                ?? throw new MissingMethodException("Unsupported player notification API.");
        }

        // Displays a center-screen message through the running game's verified signature.
        public static void Show(string text)
        {
            var player = Player.m_localPlayer;
            if (player == null || _method == null)
            {
                return;
            }
            var args = _latest ? new object[] { MessageHud.MessageType.Center, text, 0, null, false }
                : new object[] { MessageHud.MessageType.Center, text, 0, null };
            _method.Invoke(player, args);
        }
    }
}
