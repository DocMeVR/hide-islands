using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DocMe.HideIslands
{
    /// <summary>Where the chosen language lives. Codes are lowercase ("ja-jp"); empty means none chosen yet.</summary>
    public interface IDocMeLanguageStore
    {
        string Language { get; set; }

        /// <summary>Tells the store a language there is a file for, so a shared picker can offer it.</summary>
        void Register(string code);
    }

    public sealed class DocMeMemoryLanguageStore : IDocMeLanguageStore
    {
        public readonly List<string> Registered = new List<string>();

        public DocMeMemoryLanguageStore(string language = "") => Language = language;

        public string Language { get; set; }

        public void Register(string code)
        {
            if (!Registered.Contains(code))
                Registered.Add(code);
        }
    }

    /// <summary>
    /// The language of the Eye Installer and Hide Mesh Islands windows: shared with NDMF (Modular Avatar, Avatar
    /// Optimizer and AvatarDoc use it) when NDMF is installed, otherwise its own per user setting. Until the user picks
    /// one it follows the system language.
    /// </summary>
    public static class DocMeLanguage
    {
        public const string English = "en-us";
        public const string PrefsKey = "DocMe.EyeInstaller.language";
        public const string NdmfTypeName = "nadena.dev.ndmf.localization.LanguagePrefs, nadena.dev.ndmf";
        const string OldJapaneseKey = "DocMe.EyeInstaller.japanese";

        static IDocMeLanguageStore defaultStore;
        static bool migrated;

        /// <summary>Replaces NDMF and EditorPrefs for tests.</summary>
        public static IDocMeLanguageStore StoreOverride;

        public static IDocMeLanguageStore Store
            => StoreOverride ?? (defaultStore ??= (IDocMeLanguageStore)NdmfStore.TryCreate(NdmfTypeName) ?? new PrefsStore());

        public static string Current
        {
            get
            {
                MigrateOldSetting();
                string code = Normalize(Store.Language);
                return code.Length > 0 ? code : FromSystem(Application.systemLanguage);
            }
        }

        public static void Set(string code) => Store.Language = Normalize(code);

        public static string Normalize(string code) => (code ?? "").Trim().Replace('_', '-').ToLowerInvariant();

        public static string BaseOf(string code)
        {
            code = Normalize(code);
            int dash = code.IndexOf('-');
            return dash < 0 ? code : code.Substring(0, dash);
        }

        public static string FromSystem(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.Japanese: return "ja-jp";
                case SystemLanguage.Korean: return "ko-kr";
                case SystemLanguage.ChineseSimplified: return "zh-hans";
                case SystemLanguage.ChineseTraditional: return "zh-hant";
                case SystemLanguage.German: return "de-de";
                case SystemLanguage.French: return "fr-fr";
                case SystemLanguage.Spanish: return "es-es";
                case SystemLanguage.Portuguese: return "pt-br";
                case SystemLanguage.Russian: return "ru-ru";
                case SystemLanguage.Italian: return "it-it";
                default: return English;
            }
        }

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["en-us"] = "English",
            ["ja-jp"] = "日本語",
            ["ko-kr"] = "한국어",
            ["zh-hans"] = "简体中文",
            ["zh-hant"] = "繁體中文",
        };

        /// <summary>A language in its own name ("日本語"), for the language picker.</summary>
        public static string NativeName(string code)
        {
            code = Normalize(code);
            if (Names.TryGetValue(code, out var name))
                return name;
            try
            {
                var culture = CultureInfo.GetCultureInfo(BaseOf(code));
                string native = culture.NativeName;
                if (!string.IsNullOrEmpty(native) && culture.Name.Length > 0)
                    return char.ToUpper(native[0], culture) + native.Substring(1);
            }
            catch (Exception)
            {
            }
            return code;
        }

        /// <summary>The language buttons at the top of a window. True when the user picked another language.</summary>
        public static bool DrawPicker(IReadOnlyList<string> codes)
        {
            if (codes == null || codes.Count < 2) return false;
            string current = Current;
            int index = Enumerable.Range(0, codes.Count).FirstOrDefault(i => codes[i] == current);
            if (codes[index] != current)
            {
                int sameBase = Enumerable.Range(0, codes.Count).FirstOrDefault(i => BaseOf(codes[i]) == BaseOf(current));
                if (BaseOf(codes[sameBase]) == BaseOf(current)) index = sameBase;
            }
            string tip = L.Text("language.tip");
            var names = codes.Select(c => new GUIContent(NativeName(c), tip)).ToArray();
            int picked = GUILayout.Toolbar(index, names, GUILayout.Width(62 * codes.Count), GUILayout.Height(20));
            if (picked == index) return false;
            Set(codes[picked]);
            GUIUtility.keyboardControl = 0;
            return true;
        }

        // Before 1.5.0 the windows kept their own English or Japanese switch. A Japanese choice carries over once,
        // unless a language was picked since.
        static void MigrateOldSetting()
        {
            if (migrated || StoreOverride != null) return;
            migrated = true;
            if (!EditorPrefs.HasKey(OldJapaneseKey)) return;
            if (EditorPrefs.GetBool(OldJapaneseKey, false) && Normalize(Store.Language).Length == 0) Set("ja-jp");
            EditorPrefs.DeleteKey(OldJapaneseKey);
        }

        sealed class PrefsStore : IDocMeLanguageStore
        {
            string cached;

            public string Language
            {
                get => cached ??= EditorPrefs.GetString(PrefsKey, "");
                set
                {
                    cached = value ?? "";
                    EditorPrefs.SetString(PrefsKey, cached);
                }
            }

            public void Register(string code)
            {
            }
        }

        internal sealed class NdmfStore : IDocMeLanguageStore
        {
            readonly PropertyInfo language;
            readonly MethodInfo register;

            NdmfStore(PropertyInfo language, MethodInfo register)
            {
                this.language = language;
                this.register = register;
            }

            /// <summary>Null when NDMF is missing or its LanguagePrefs has no readable and writable static string Language.</summary>
            public static NdmfStore TryCreate(string typeName)
            {
                try
                {
                    var type = Type.GetType(typeName, false);
                    var language = type?.GetProperty("Language", BindingFlags.Public | BindingFlags.Static);
                    if (language == null || language.PropertyType != typeof(string) || !language.CanRead || !language.CanWrite)
                        return null;
                    var register = type.GetMethod("RegisterLanguage", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                    return new NdmfStore(language, register);
                }
                catch (Exception)
                {
                    return null;
                }
            }

            public string Language
            {
                get
                {
                    try { return (string)language.GetValue(null) ?? ""; }
                    catch (Exception) { return ""; }
                }
                set
                {
                    try { language.SetValue(null, value); }
                    catch (Exception) { }
                }
            }

            public void Register(string code)
            {
                try { register?.Invoke(null, new object[] { code }); }
                catch (Exception) { }
            }
        }
    }
}
