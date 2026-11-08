using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;

namespace DocMe.HideIslands
{
    /// <summary>
    /// The texts of one tool in the chosen language, read from its &lt;prefix&gt;xx-XX.po files. A key comes from the chosen
    /// language, then another file of the same base language, then English, so a raw key never shows. A count picks
    /// key + ".one" for exactly 1 and key otherwise.
    /// </summary>
    public sealed class PoTable
    {
        static readonly List<PoTable> all = new List<PoTable>();

        readonly string prefix;
        Dictionary<string, PoFile> files;
        double retryAt;
        readonly HashSet<string> registered = new HashSet<string>();

        /// <param name="prefix">The file name up to the language, dot included ("EyeInstaller.").</param>
        public PoTable(string prefix)
        {
            this.prefix = prefix;
            lock (all) all.Add(this);
        }

        public string FilePrefix => prefix;

        public string Text(string key) => Find(key, DocMeLanguage.Current) ?? key;

        public string Text(string key, params object[] args)
        {
            try
            {
                return string.Format(CultureInfo.InvariantCulture, Text(key), args);
            }
            catch (FormatException)
            {
                try
                {
                    return string.Format(CultureInfo.InvariantCulture, Find(key, DocMeLanguage.English) ?? key, args);
                }
                catch (FormatException)
                {
                    return key;
                }
            }
        }

        public string Count(int n, string key, params object[] args) => Text(n == 1 ? key + ".one" : key, args);

        /// <summary>The text, or null when no file has the key (a control without a tooltip).</summary>
        public string Tip(string key) => key == null ? null : Find(key, DocMeLanguage.Current);

        /// <summary>The languages that have a file, lowercase, English first.</summary>
        public List<string> Languages()
        {
            var codes = new List<string> { DocMeLanguage.English };
            codes.AddRange(Files().Keys.Where(c => c != DocMeLanguage.English).OrderBy(c => c, StringComparer.Ordinal));
            foreach (var code in codes)
                if (registered.Add(code))
                    DocMeLanguage.Store.Register(code);
            return codes;
        }

        public PoFile PoOf(string code) => Files().TryGetValue(DocMeLanguage.Normalize(code), out var file) ? file : null;

        /// <summary>Project path of each language file, by language code.</summary>
        public Dictionary<string, string> Paths()
        {
            var paths = new Dictionary<string, string>();
            foreach (var path in FindFiles()) paths[CodeOf(path)] = path;
            return paths;
        }

        public void Invalidate()
        {
            files = null;
            retryAt = 0;
        }

        string Find(string key, string language)
        {
            var found = Files();
            language = DocMeLanguage.Normalize(language);
            string text = null;
            if (found.TryGetValue(language, out var exact))
                text = exact.Text(key);
            if (text == null)
            {
                string baseCode = DocMeLanguage.BaseOf(language);
                foreach (var pair in found)
                {
                    if (pair.Key == language || DocMeLanguage.BaseOf(pair.Key) != baseCode) continue;
                    text = pair.Value.Text(key);
                    if (text != null) break;
                }
            }
            if (text == null && found.TryGetValue(DocMeLanguage.English, out var english))
                text = english.Text(key);
            return text;
        }

        Dictionary<string, PoFile> Files()
        {
            if (files != null) return files;
            if (EditorApplication.timeSinceStartup < retryAt) return new Dictionary<string, PoFile>();
            var found = new Dictionary<string, PoFile>();
            foreach (var path in FindFiles())
            {
                var text = PoFile.Decode(SafeRead(path), out _);
                if (text == null) continue;
                found[CodeOf(path)] = PoFile.Parse(text);
            }
            if (found.Count > 0) return files = found;
            retryAt = EditorApplication.timeSinceStartup + 2;
            return found;
        }

        // Unity imports .po files as LocalizationAsset, so they are found by name, never by t:TextAsset.
        IEnumerable<string> FindFiles()
        {
            foreach (var guid in AssetDatabase.FindAssets(prefix.TrimEnd('.')))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Owns(path)) yield return path;
            }
        }

        bool Owns(string path) => path.EndsWith(".po", StringComparison.OrdinalIgnoreCase)
                                  && Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal);

        string CodeOf(string path) => DocMeLanguage.Normalize(Path.GetFileNameWithoutExtension(path).Substring(prefix.Length));

        static byte[] SafeRead(string assetPath)
        {
            try { return File.ReadAllBytes(Path.GetFullPath(assetPath)); }
            catch (Exception) { return null; }
        }

        sealed class Watcher : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                PoTable[] tables;
                lock (all) tables = all.ToArray();
                foreach (var table in tables)
                    if (imported.Concat(deleted).Concat(moved).Any(table.Owns))
                        table.Invalidate();
            }
        }
    }

    /// <summary>Hide Mesh Islands texts in the chosen language, from Editor/Localization/HideIslands.xx-XX.po.</summary>
    public static class L
    {
        public static readonly PoTable Table = new PoTable("HideIslands.");

        public static string Text(string key) => Table.Text(key);
        public static string Text(string key, params object[] args) => Table.Text(key, args);
        public static string Count(int n, string key, params object[] args) => Table.Count(n, key, args);
        public static string Tip(string key) => Table.Tip(key);
    }
}
