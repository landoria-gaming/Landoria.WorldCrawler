using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Landoria.WorldCrawler.Restoration.Persistence;
using UnityEngine;

namespace Landoria.WorldCrawler.UI
{
    // Resolves native prefab name tokens through Valheim's English localization table.
    internal sealed class EnglishPrefabNames
    {
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
        private Dictionary<string, string> _english;

        // Returns an English display name only when the game defines one.
        internal string Resolve(int hash, string prefabName)
        {
            if (_cache.TryGetValue(prefabName, out var cached))
            {
                return cached;
            }
            var prefab = ZNetScene.instance?.GetPrefab(hash);
            var token = Token(prefab);
            if (string.IsNullOrEmpty(token))
            {
                return _cache[prefabName] = null;
            }
            if (!token.StartsWith("$", StringComparison.Ordinal))
            {
                return _cache[prefabName] = token == prefabName ? null : token;
            }
            _english = _english ?? LoadEnglish();
            _english.TryGetValue(token.Substring(1), out var value);
            return _cache[prefabName] = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        // Reads the usual build-piece, item and pickable labels from prefab components.
        private static string Token(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }
            var piece = prefab.GetComponent<Piece>();
            if (piece != null && !string.IsNullOrEmpty(piece.m_name))
            {
                return piece.m_name;
            }
            var item = prefab.GetComponent<ItemDrop>() ??
                prefab.GetComponent<Pickable>()?.m_itemPrefab?.GetComponent<ItemDrop>();
            return item?.m_itemData?.m_shared?.m_name;
        }

        // Loads the built-in English column once without changing the player's language.
        private static Dictionary<string, string> LoadEnglish()
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var asset = Resources.Load<TextAsset>("localization");
            if (asset == null)
            {
                return names;
            }
            using (var reader = new StringReader(asset.text))
            {
                var rows = Rows(reader).GetEnumerator();
                if (!rows.MoveNext())
                {
                    return names;
                }
                var english = Array.FindIndex(rows.Current, value => value.Trim() == "English");
                while (english >= 0 && rows.MoveNext())
                {
                    var row = rows.Current;
                    if (row.Length > english && row.Length > 0 && row[0].Length > 0)
                    {
                        names[row[0].Trim()] = row[english];
                    }
                }
            }
            return names;
        }

        // Parses quoted CSV fields, including commas and newlines inside translations.
        private static IEnumerable<string[]> Rows(StringReader reader)
        {
            string[] row;
            while ((row = ReadRow(reader)) != null)
            {
                yield return row;
            }
        }

        // Reads one CSV row while respecting quoted commas and line breaks.
        private static string[] ReadRow(StringReader reader)
        {
            var fields = new List<string>();
            var value = new StringBuilder();
            var quoted = false;
            int next;
            while ((next = reader.Read()) != -1)
            {
                if (next == '"')
                {
                    if (quoted && reader.Peek() == '"')
                    {
                        reader.Read();
                        value.Append('"');
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (next == ',' && !quoted)
                {
                    fields.Add(value.ToString());
                    value.Clear();
                }
                else if (next == '\n' && !quoted)
                {
                    fields.Add(value.ToString());
                    return fields.ToArray();
                }
                else if (next != '\r' || quoted)
                {
                    value.Append((char)next);
                }
            }
            if (fields.Count == 0 && value.Length == 0)
            {
                return null;
            }
            fields.Add(value.ToString());
            return fields.ToArray();
        }
    }
}
