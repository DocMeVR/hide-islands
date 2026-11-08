using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DocMe.HideIslands.Tests
{
    public class LocalizationTests
    {
        [Test]
        public void EnglishAndJapaneseLoadWithoutProblems()
        {
            var files = KeyUse.Load(L.Table);
            Assert.That(files.Keys, Is.SupersetOf(new[] { "en-us", "ja-jp" }));
            foreach (var pair in files) CollectionAssert.IsEmpty(pair.Value.Problems, pair.Key);
        }

        [Test]
        public void EveryKeyTheCodeUsesExistsInEveryLanguage()
        {
            var used = KeyUse.Scan(KeyUse.EditorFolder(L.Table), "L", "GC");
            KeyUse.AssertDefined(used, KeyUse.Load(L.Table));
        }

        [Test]
        public void JapaneseMatchesTheCurrentEnglish() => KeyUse.AssertUpToDate(KeyUse.Load(L.Table));

        [Test]
        public void EveryEnglishKeyIsUsed()
        {
            var used = KeyUse.Scan(KeyUse.EditorFolder(L.Table), "L", "GC");
            KeyUse.AssertAllUsed(used, KeyUse.Load(L.Table)[DocMeLanguage.English]);
        }

        [Test]
        public void CountPicksTheSingularForOne()
        {
            var saved = DocMeLanguage.StoreOverride;
            try
            {
                DocMeLanguage.StoreOverride = new DocMeMemoryLanguageStore("en-us");
                Assert.AreEqual("1 part hidden.", L.Count(1, "hide.hidden", 1));
                Assert.AreEqual("3 parts hidden.", L.Count(3, "hide.hidden", 3));
                DocMeLanguage.StoreOverride = new DocMeMemoryLanguageStore("ja-jp");
                Assert.AreEqual("3 箇所を隠しました。", L.Count(3, "hide.hidden", 3));
                DocMeLanguage.StoreOverride = new DocMeMemoryLanguageStore("de-de");
                Assert.AreEqual("3 parts hidden.", L.Count(3, "hide.hidden", 3), "a language without a file falls back to English");
            }
            finally { DocMeLanguage.StoreOverride = saved; }
        }
    }

    /// <summary>Which keys the code asks for, read from the C# source: the key argument of L.Text, L.Tip, L.Count and the
    /// window's GC helper (label key and tooltip key), every string literal in it, so a key chosen by ?: counts too.</summary>
    public sealed class KeyUse
    {
        public readonly HashSet<string> Plain = new HashSet<string>();
        public readonly HashSet<string> Counted = new HashSet<string>();
        public readonly HashSet<string> Labels = new HashSet<string>();

        static readonly Regex KeyLike = new Regex(@"^[a-z][A-Za-z]*(\.[A-Za-z]+)*$");
        static readonly Regex Literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
        static readonly Regex Placeholder = new Regex(@"\{(\d+)\}");

        /// <param name="receiver">The class the calls go through ("L", "HideL").</param>
        /// <param name="label">The window helper that takes a label key and an optional tooltip key ("GC", "HGC").</param>
        public static KeyUse Scan(string folder, string receiver, string label)
        {
            var use = new KeyUse();
            var call = new Regex(@"(?<![\w.])(?:" + Regex.Escape(receiver) + @"\.(Text|Tip|Count)|(" + Regex.Escape(label) + @"))\(");
            foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                string src = File.ReadAllText(file);
                foreach (Match m in call.Matches(src))
                {
                    var args = Arguments(src, m.Index + m.Length);
                    if (m.Groups[1].Success)
                    {
                        int at = m.Groups[1].Value == "Count" ? 1 : 0;
                        if (args.Count <= at) continue;
                        foreach (var key in Keys(args[at])) (m.Groups[1].Value == "Count" ? use.Counted : use.Plain).Add(key);
                    }
                    else
                    {
                        if (args.Count > 0) foreach (var key in Keys(args[0])) use.Labels.Add(key);
                        if (args.Count > 1) foreach (var key in Keys(args[1])) use.Plain.Add(key);
                    }
                }
            }
            return use;
        }

        static IEnumerable<string> Keys(string argument)
            => Literal.Matches(argument).Cast<Match>().Select(m => m.Groups[1].Value).Where(k => KeyLike.IsMatch(k));

        static List<string> Arguments(string src, int start)
        {
            var args = new List<string>();
            int depth = 0, from = start;
            bool inString = false;
            for (int i = start; i < src.Length; i++)
            {
                char c = src[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '(' || c == '[' || c == '{') depth++;
                else if ((c == ')' || c == ']' || c == '}') && depth > 0) depth--;
                else if (c == ',' && depth == 0) { args.Add(src.Substring(from, i - from)); from = i + 1; }
                else if (c == ')' && depth == 0) { args.Add(src.Substring(from, i - from)); break; }
            }
            return args;
        }

        public static Dictionary<string, PoFile> Load(PoTable table)
        {
            var files = new Dictionary<string, PoFile>();
            foreach (var pair in table.Paths())
            {
                var text = PoFile.Decode(File.ReadAllBytes(Path.GetFullPath(pair.Value)), out var error);
                Assert.IsNull(error, pair.Value + ": " + error);
                files[pair.Key] = PoFile.Parse(text);
            }
            return files;
        }

        /// <summary>The package's Editor folder: the language files sit in its Localization subfolder.</summary>
        public static string EditorFolder(PoTable table)
            => Path.GetFullPath(Path.GetDirectoryName(Path.GetDirectoryName(table.Paths()[DocMeLanguage.English])));

        public IEnumerable<string> Required()
        {
            foreach (var k in Plain) yield return k;
            foreach (var k in Labels) yield return k;
            foreach (var k in Counted) { yield return k; yield return k + ".one"; }
        }

        public static void AssertDefined(KeyUse use, Dictionary<string, PoFile> files)
        {
            var missing = new List<string>();
            foreach (var pair in files)
                foreach (var key in use.Required())
                    if (pair.Value.Text(key) == null) missing.Add(pair.Key + ": " + key);
            CollectionAssert.IsEmpty(missing, "keys the code uses but a language file lacks");
        }

        public static void AssertUpToDate(Dictionary<string, PoFile> files)
        {
            var english = files[DocMeLanguage.English];
            var problems = new List<string>();
            foreach (var pair in files)
            {
                if (pair.Key == DocMeLanguage.English) continue;
                foreach (var e in english.Entries.Where(e => !e.obsolete))
                {
                    var t = pair.Value.Find(e.key);
                    if (t == null || t.obsolete) { problems.Add(pair.Key + ": missing " + e.key); continue; }
                    if (t.fuzzy) problems.Add(pair.Key + ": fuzzy " + e.key);
                    if (t.english != e.text) problems.Add(pair.Key + ": " + e.key + " was made from \"" + t.english + "\", the English is now \"" + e.text + "\"");
                    var a = Placeholder.Matches(e.text).Cast<Match>().Select(m => m.Value).OrderBy(s => s);
                    var b = Placeholder.Matches(t.text).Cast<Match>().Select(m => m.Value).OrderBy(s => s);
                    if (!a.SequenceEqual(b)) problems.Add(pair.Key + ": " + e.key + " has other placeholders than the English");
                }
            }
            CollectionAssert.IsEmpty(problems);
        }

        public static void AssertAllUsed(KeyUse use, PoFile english, params KeyUse[] alsoUsedBy)
        {
            var used = new HashSet<string>(use.Required());
            foreach (var k in use.Labels) used.Add(k + ".tip");
            foreach (var other in alsoUsedBy)
            {
                used.UnionWith(other.Required());
                foreach (var k in other.Labels) used.Add(k + ".tip");
            }
            var unused = english.Entries.Where(e => !e.obsolete && !used.Contains(e.key)).Select(e => e.key).ToList();
            CollectionAssert.IsEmpty(unused, "English keys no code asks for");
        }
    }
}
