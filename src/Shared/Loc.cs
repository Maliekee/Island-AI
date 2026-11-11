using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Shared
{
    // THE MODS' WORDS, IN THE PLAYER'S LANGUAGE (Forgejo #19). Every string a player reads
    // goes through one call, with a stable id as the key and the English beside it:
    //
    //     Loc.Get("settings.done", "Done")
    //     Loc.Get("bed.none_with", "No bed with {0}", name)
    //
    // The English in the code is the source and the fallback: a missing file, a missing key
    // or a translation whose {0}s do not format all come out as the English. A sentence built
    // by concatenation cannot be translated - word order is the language's - so it becomes one
    // string with {0}, {1} for what is put into it.
    //
    // The files copy Cosmo's Mod Localization System for Idol Manager (IM-Cosmo-Mod-Library,
    // LOCALIZATION.md) so a translator who knows one knows the other:
    //   plugins/<Mod>/Localization/<lang>/strings.txt, UTF-8, one `key=value` per line;
    //   # ; and // start a comment line; the key is trimmed, the value is not;
    //   \n \r \t in a value are the characters; an empty value is a translation, not a gap;
    //   keys are case-blind; folder codes en, jp, cn, kr, ru, ptbr, fr, es, de (ja/jp, zh/cn,
    //   ko/kr and the long names are aliases, the exact code wins).
    // As in Cosmo's reader, < and > in a value become full-width: a translation cannot
    // open a TextMeshPro tag. So colour and bold stay in the code, around the call.
    //
    // en/strings.txt is not read. It is generated from the calls as
    // the translators' template, so it says what the code says by construction. A mod's data
    // tables are translated beside it (<lang>/item-guide.tsv; DataFiles), their English
    // template copied into en/ by the same generator.
    //
    // THE LANGUAGE, in order: the mod's override; the game's selected Unity locale
    // (LocalizationSettings.SelectedLocale), which the game's own language dropdown sets and
    // a mod that adds a locale to the game sets too; English. The locale's code is mapped to
    // a folder code (ja -> jp, zh -> cn, pt-BR -> ptbr; GameLocale). "pseudo" is the test
    // language: the English, accented, bracketed and about 40% longer, so a string that is
    // not bracketed was missed and one that is clipped will be clipped in German too.
    //
    // Shared SOURCE: each mod compiles its own copy, with its own folder and its own table.
    // One mod owns the override (Island UI, its [Language] Override); the copy that was
    // given it publishes it in the AppDomain, and a copy given none (Island AI, Island Ex)
    // reads it there, so one setting speaks for all three.
    internal static class Loc
    {
        internal const string Pseudo = "pseudo";
        private const string English = "en";

        private const string SharedOverride = "IslandMods.Loc.Override";

        private static string _folder;
        private static Func<string> _override;
        private static string _published;
        private static string _language;                  // what the table holds
        private static readonly Dictionary<string, string> Table =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // bumped on every load: a window built in one language knows to rebuild
        private static int _generation;
        internal static int Generation
        {
            get
            {
                Refresh();
                return _generation;
            }
        }

        // `folder` is the mod's own under plugins/ (IslandUI): a hot-reloaded DLL is loaded
        // from bytes and has no location of its own to look beside. `language` returns a
        // folder code, or empty to follow the game; null reads the owner's from the AppDomain.
        internal static void Init(string folder, Func<string> language)
        {
            _folder = folder;
            _override = language;
            _language = null;
            Table.Clear();
            Refresh();   // the owner publishes its override now, before the others first ask
        }

        internal static string Language
        {
            get
            {
                Refresh();
                return _language;
            }
        }

        // the game's own language as a folder code, whatever the override says: what
        // "Same as the game" would speak
        internal static string GameLanguageCode => GameLanguage() ?? English;

        // read the files again, as after an edit to a strings.txt
        internal static void Reload()
        {
            _language = null;
            Refresh();
        }

        // for the harness: `game.ps1 invoke Shared.Loc Use -Args pseudo`; empty follows the
        // game again. Lasts until the mod reloads; the persistent switch is the mod's config.
        internal static void Use(string language)
        {
            string code = (language ?? string.Empty).Trim();
            _override = () => code;
            Reload();
        }

        // for the harness, which cannot pass an empty string: follow the game again
        internal static void Follow()
        {
            Use(null);
        }

        // A DATA FILE in the player's language (item-guide.tsv, story-choices.tsv): the mod
        // reads its English from the DLL and lays the translation over it row by row, so a
        // row left out stays English. These are the files to read, in the same order as the
        // strings, broad aliases first and the exact folder last, so its rows win; none for
        // English, the pseudo-language or a language without the file.
        internal static List<string> DataFiles(string name)
        {
            Refresh();
            var files = new List<string>();
            if (_language == English || _language == Pseudo || string.IsNullOrEmpty(_folder))
            {
                return files;
            }
            string root = Path.Combine(Path.Combine(BepInEx.Paths.PluginPath, _folder), "Localization");
            foreach (string folder in Folders(_language))
            {
                string path = Path.Combine(Path.Combine(root, folder), name);
                if (File.Exists(path))
                {
                    files.Add(path);
                }
            }
            return files;
        }

        // A data row's English as the language shows it: bracketed in the pseudo-language,
        // so an English row left over is told from a translated one
        internal static string Untranslated(string english)
        {
            Refresh();
            return _language == Pseudo ? Pseudolocalise(english) : english;
        }

        // Words joined in code with no sentence around them, as " · " between hover parts:
        // the separator is the language's, so it is a key too. Chinese and Japanese drop
        // the spaces.
        internal static string Separator => Loc.Get("loc.separator", " · ");

        internal static string Get(string key, string english)
        {
            Refresh();
            if (_language == Pseudo)
            {
                return Pseudolocalise(english);
            }
            return key != null && Table.TryGetValue(key, out string value) ? value : english;
        }

        internal static string Get(string key, string english, params object[] args)
        {
            string format = Get(key, english);
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                // a translation with a broken placeholder: the English still has them right
                return string.Format(english, args);
            }
        }

        // A COUNT IN A SENTENCE: "1 day", "3 days". The English gives its two forms; the
        // file holds key.one and key.other, and a language with more forms adds them
        // (Russian key.few and key.many). {0} is the count, {1} on are `args`.
        //
        //     Loc.Plural("mood.days", "{0} day", "{0} days", n)
        internal static string Plural(string key, string one, string other, int count, params object[] args)
        {
            Refresh();
            string category = PluralCategory(_language, count);
            string english = category == "one" ? one : other;
            string format = english;
            if (_language == Pseudo)
            {
                format = Pseudolocalise(english);
            }
            else if (!Table.TryGetValue(key + "." + category, out format)
                && !Table.TryGetValue(key + ".other", out format))
            {
                format = english;
            }
            var all = new object[args.Length + 1];
            all[0] = count;
            Array.Copy(args, 0, all, 1, args.Length);
            try
            {
                return string.Format(format, all);
            }
            catch (FormatException)
            {
                return string.Format(english, all);
            }
        }

        // CLDR's cardinal categories, for the languages we ship; the rest are one/other
        private static string PluralCategory(string language, int n)
        {
            int abs = Math.Abs(n);
            switch (language)
            {
                case "jp":
                case "ja":
                case "cn":
                case "zh":
                case "kr":
                case "ko":
                    return "other";
                case "ru":
                    int ten = abs % 10, hundred = abs % 100;
                    if (ten == 1 && hundred != 11)
                    {
                        return "one";
                    }
                    if (ten >= 2 && ten <= 4 && (hundred < 12 || hundred > 14))
                    {
                        return "few";
                    }
                    return "many";
                case "ptbr":
                case "pt-br":
                case "fr":
                    return abs <= 1 ? "one" : "other";
                default:
                    return abs == 1 ? "one" : "other";
            }
        }

        private static void Refresh()
        {
            string wanted = Wanted();
            if (wanted == _language)
            {
                return;
            }
            _language = wanted;
            Table.Clear();
            _generation++;
            if (wanted == English || wanted == Pseudo || string.IsNullOrEmpty(_folder))
            {
                return;
            }
            string root = Path.Combine(Path.Combine(BepInEx.Paths.PluginPath, _folder), "Localization");
            List<string> folders = Folders(wanted);
            for (int i = 0; i < folders.Count; i++)
            {
                LoadFile(Path.Combine(Path.Combine(root, folders[i]), "strings.txt"));
            }
        }

        private static string Wanted()
        {
            string code = null;
            try
            {
                if (_override != null)
                {
                    code = _override();
                    if (code != _published)
                    {
                        _published = code;
                        AppDomain.CurrentDomain.SetData(SharedOverride, code ?? string.Empty);
                    }
                }
                else
                {
                    code = AppDomain.CurrentDomain.GetData(SharedOverride) as string;
                }
            }
            catch
            {
            }
            if (!string.IsNullOrEmpty(code))
            {
                return code.Trim().ToLowerInvariant();
            }
            return GameLanguage() ?? English;
        }

        // THE GAME'S LOCALE, as a folder code. Read once the game's localization has
        // initialised, then kept current by its SelectedLocaleChanged event, so a Get costs
        // a field read. Before that, null: the first frames speak English and a window built
        // then is lettered again when the generation moves.
        private static string _gameLanguage;
        private static bool _hooked;
        private static int _lastTry = -1;

        private static string GameLanguage()
        {
            if (_hooked)
            {
                return _gameLanguage;
            }
            int frame = UnityEngine.Time.frameCount;
            if (frame == _lastTry)
            {
                return null;
            }
            _lastTry = frame;
            try
            {
                TryHook();
            }
            catch
            {
                // no Unity Localization to ask (it failed to load): the game's own setting
                _hooked = true;
                _gameLanguage = SavedLanguage();
            }
            return _hooked ? _gameLanguage : null;
        }

        // its own method: a missing Unity.Localization fails when this is compiled, which
        // is inside GameLanguage's try
        private static void TryHook()
        {
            if (!LocalizationSettings.HasSettings || !LocalizationSettings.InitializationOperation.IsDone)
            {
                return;
            }
            LocalizationSettings.SelectedLocaleChanged += OnLocale;
            _hooked = true;
            OnLocale(LocalizationSettings.SelectedLocale);
        }

        private static void OnLocale(Locale locale)
        {
            _gameLanguage = locale != null ? GameLocale(locale.Identifier.Code) : null;
        }

        // the game's three are 0 jp, 1 en, 2 cn
        private static string SavedLanguage()
        {
            try
            {
                int lang = SaveManager.SaveSettingStatic.lang;
                return lang == 0 ? "jp" : lang == 2 ? "cn" : English;
            }
            catch
            {
                return null;
            }
        }

        // A Unity locale code (en, ja, zh-Hans, pt-BR, es-ES) as our folder code. Any
        // Chinese reads the Simplified we ship; a language we do not ship is its own
        // code, and with no folder of that name it falls back to English key by key.
        internal static string GameLocale(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }
            string c = code.Trim().ToLowerInvariant().Replace('_', '-');
            int dash = c.IndexOf('-');
            string language = dash > 0 ? c.Substring(0, dash) : c;
            switch (language)
            {
                case "ja": return "jp";
                case "zh": return "cn";
                case "ko": return "kr";
                case "pt": return "ptbr";
                default: return language;
            }
        }

        // broad aliases first, the exact code last, so the exact folder's lines win
        private static List<string> Folders(string code)
        {
            var folders = new List<string>();
            switch (code)
            {
                case "jp":
                case "ja":
                    folders.AddRange(new[] { "japanese", "ja", "jp" });
                    break;
                case "cn":
                case "zh":
                    folders.AddRange(new[] { "zh", "zh-Hans", "schinese", "cn" });
                    break;
                case "kr":
                case "ko":
                    folders.AddRange(new[] { "korean", "ko", "kr" });
                    break;
                case "ru":
                    folders.AddRange(new[] { "russian", "ru" });
                    break;
                case "ptbr":
                case "pt-br":
                    folders.AddRange(new[] { "portuguese", "brazilian", "pt-BR", "ptbr" });
                    break;
                case "fr":
                    folders.AddRange(new[] { "french", "fr" });
                    break;
                case "es":
                    folders.AddRange(new[] { "spanish", "es" });
                    break;
                case "de":
                    folders.AddRange(new[] { "german", "de" });
                    break;
            }
            if (!folders.Contains(code))
            {
                folders.Add(code);
            }
            return folders;
        }

        private static void LoadFile(string path)
        {
            string[] lines;
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch
            {
                return;
            }
            foreach (string raw in lines)
            {
                string trimmed = raw.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith(";") || trimmed.StartsWith("//"))
                {
                    continue;
                }
                int separator = raw.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }
                string key = raw.Substring(0, separator).Trim();
                if (key.Length == 0)
                {
                    continue;
                }
                Table[key] = raw.Substring(separator + 1)
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t")
                    .Replace('<', '＜')
                    .Replace('>', '＞');
            }
        }

        // Letters swapped for accented ones the kit's Latin-1 atlas has, each line padded by
        // about 40%, the whole in brackets. {0} placeholders and line breaks pass untouched.
        private const string Plain = "aeiouyncAEIOUYNC";
        private const string Accented = "áéíóúýñçÁÉÍÓÚÝÑÇ";
        private const string Padding = "·ñóéá";

        private static string Pseudolocalise(string english)
        {
            if (string.IsNullOrEmpty(english))
            {
                return english;
            }
            var sb = new StringBuilder(english.Length * 2);
            sb.Append('[');
            int line = 0;
            for (int i = 0; i < english.Length; i++)
            {
                char c = english[i];
                if (c == '{')
                {
                    int close = english.IndexOf('}', i);
                    if (close > i)
                    {
                        sb.Append(english, i, close - i + 1);
                        line += close - i + 1;
                        i = close;
                        continue;
                    }
                }
                if (c == '\n')
                {
                    Pad(sb, line);
                    line = 0;
                    sb.Append(c);
                    continue;
                }
                int k = Plain.IndexOf(c);
                sb.Append(k >= 0 ? Accented[k] : c);
                line++;
            }
            Pad(sb, line);
            sb.Append(']');
            return sb.ToString();
        }

        private static void Pad(StringBuilder sb, int length)
        {
            int extra = (length * 2 + 4) / 5;            // 40%, rounded up
            if (extra == 0)
            {
                return;
            }
            sb.Append(' ');
            for (int i = 1; i < extra; i++)
            {
                sb.Append(Padding[i % Padding.Length]);
            }
        }
    }
}
