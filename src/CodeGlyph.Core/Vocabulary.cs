using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeGlyph
{
    /// <summary>
    /// Word rules that need no model call. The tables match vscode/src/vocabulary.ts.
    /// </summary>
    public static class Vocabulary
    {
        private static readonly Regex LowerUpper = new Regex("([a-z0-9])([A-Z])", RegexOptions.Compiled);
        private static readonly Regex UpperRun = new Regex("([A-Z]+)([A-Z][a-z])", RegexOptions.Compiled);
        private static readonly Regex NotWord = new Regex("[^A-Za-z0-9]+", RegexOptions.Compiled);

        private static readonly (string Icon, string Words)[] WordRows =
        {
            ("mail", "email mail smtp"),
            ("key-round", "password secret credential credentials"),
            ("log-in", "login logon signin authenticate authentication"),
            ("log-out", "logout signout"),
            ("shield-check", "permission permissions role roles policy policies authorization"),
            ("shield", "security"),
            ("lock", "encrypt encryption decrypt crypto cipher hash"),
            ("receipt", "invoice receipt billing"),
            ("credit-card", "payment checkout"),
            ("shopping-cart", "order cart basket"),
            ("package", "product inventory stock"),
            ("truck", "shipping shipment delivery"),
            ("user", "user account profile"),
            ("users", "member team"),
            ("heart-pulse", "patient"),
            ("calendar", "appointment schedule calendar date"),
            ("clock", "time timer duration"),
            ("bell", "notification alert"),
            ("message-square", "message chat sms"),
            ("message-circle", "comment"),
            ("phone", "phone call"),
            ("file-text", "document pdf"),
            ("folder", "folder directory"),
            ("image", "image photo picture thumbnail"),
            ("video", "video"),
            ("volume-2", "audio sound"),
            ("upload", "upload"),
            ("download", "download"),
            ("database", "database sql db"),
            ("scroll-text", "log logging logger"),
            ("search", "search"),
            ("funnel", "filter"),
            ("arrow-up-down", "sort"),
            ("settings", "settings config configuration options preferences"),
            ("map-pin", "location address geo"),
            ("languages", "language translation locale"),
            ("palette", "theme color colour"),
            ("chart-column", "report chart analytics statistics stats metrics"),
            ("printer", "print printer"),
            ("qr-code", "barcode qr"),
            ("link", "url uri hyperlink"),
            ("globe", "web http"),
            ("webhook", "webhook"),
            ("network", "network"),
            ("cloud", "cloud"),
            ("bug", "bug debug"),
            ("dices", "random"),
            ("git-branch", "version branch"),
            ("star", "rating favorite"),
            ("tag", "tag label"),
            ("bookmark", "bookmark"),
            ("ticket-percent", "discount coupon promo"),
            ("circle-dollar-sign", "price pricing cost"),
            ("percent", "tax"),
            ("banknote", "currency money"),
            ("wallet", "wallet"),
            ("landmark", "bank"),
            ("sheet", "spreadsheet excel csv"),
            ("archive", "archive zip compress"),
            ("contact", "contact"),
            ("camera", "camera"),
            ("clipboard", "clipboard"),
            ("list-todo", "todo task"),
            ("sticky-note", "note"),
            ("mic", "voice speech microphone"),
            ("cookie", "cookie"),
            ("calculator", "calculator calculate"),
            ("server", "server"),
            ("inbox", "inbox"),
        };

        private static readonly (string Icon, string Words)[] BadgeRows =
        {
            ("plus", "create add new insert register"),
            ("x", "delete remove drop clear cancel"),
            ("search", "get find fetch load read query search list"),
            ("send", "send publish post emit dispatch"),
            ("check", "validate verify check ensure assert is has can"),
            ("pencil", "update edit set save write"),
            ("refresh-cw", "sync refresh reload retry"),
        };

        private static readonly HashSet<string> Noise = new HashSet<string>(StringComparer.Ordinal)
        {
            "the", "and", "for", "from", "with", "all", "by", "to", "of", "on", "or", "id", "async",
            "handler", "helper", "manager", "service", "utility", "util", "utils", "base", "factory", "provider",
            "controller", "impl", "default", "internal", "private", "public", "abstract", "static",
        };

        public static readonly IReadOnlyDictionary<string, string> Words = Expand(WordRows);
        public static readonly IReadOnlyDictionary<string, string> Badges = Expand(BadgeRows);

        private static IReadOnlyDictionary<string, string> Expand((string Icon, string Words)[] rows)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (icon, words) in rows)
                foreach (var w in words.Split(' ')) map[w] = icon;
            return map;
        }

        /// <summary>Splits PascalCase, camelCase, snake_case and kebab-case into lower case words.</summary>
        public static IReadOnlyList<string> Tokens(string? name)
        {
            var s = UpperRun.Replace(LowerUpper.Replace(name ?? "", "$1 $2"), "$1 $2");
            return NotWord.Split(s).Where(t => t.Length > 0).Select(t => t.ToLowerInvariant()).ToList();
        }

        private static string Singular(string w) =>
            w.Length > 3 && w.EndsWith("s", StringComparison.Ordinal) && !w.EndsWith("ss", StringComparison.Ordinal)
                ? w.Substring(0, w.Length - 1) : w;

        /// <summary>The icon for a name when all its known words agree on one icon. Otherwise null.</summary>
        public static string? LocalIcon(string name)
        {
            string? found = null;
            foreach (var t in Tokens(name))
            {
                if (!Words.TryGetValue(t, out var icon) && !Words.TryGetValue(Singular(t), out icon)) continue;
                if (found != null && found != icon) return null;
                found = icon;
            }
            return found;
        }

        /// <summary>The badge icon for a method name that starts with a known verb. Otherwise null.</summary>
        public static string? BadgeFor(SymbolDescriptor d)
        {
            if (d.Kind != SymbolKind.Method) return null;
            var first = Tokens(d.Name).FirstOrDefault();
            return first != null && Badges.TryGetValue(first, out var icon) ? icon : null;
        }

        /// <summary>Words that name a domain concept. Verbs, constructs and short words are left out.</summary>
        public static IReadOnlyList<string> ConceptWords(string name) =>
            Tokens(name).Select(Singular).Distinct()
                .Where(t => t.Length >= 3 && !Noise.Contains(t) && !Badges.ContainsKey(t)).ToList();
    }
}
