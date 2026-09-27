using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AX.SAPB1.Api.Services.FiscalProjects
{
    /// <summary>
    /// Pattern dei codici dei progetti contabili (OPRJ), da <c>SapB1:FiscalProjectCodePattern</c>. Il default
    /// <c>PRJ{yy}_{yy}{seq5}</c> riproduce la numerazione in uso (es. <c>PRJ26_2600016</c>: anno a due cifre,
    /// poi un progressivo di 5 cifre per anno). Segnaposto: <c>{yy}</c>, <c>{yyyy}</c> e UN <c>{seqN}</c>
    /// (N = cifre del progressivo, 1..9). Classe pura: il calcolo del prossimo codice si testa senza SAP.
    /// </summary>
    public sealed class FiscalProjectCodePattern
    {
        public const string DefaultPattern = "PRJ{yy}_{yy}{seq5}";

        /// <summary>Lunghezza di <c>OPRJ.PrjCode</c>.</summary>
        public const int MaxCodeLength = 20;

        private static readonly Regex Token = new(@"\{([^{}]*)\}", RegexOptions.Compiled);

        private readonly string _before;
        private readonly string _after;

        public int SequenceDigits { get; }
        public string Pattern { get; }

        private FiscalProjectCodePattern(string pattern, string before, string after, int digits)
        {
            Pattern = pattern;
            _before = before;
            _after = after;
            SequenceDigits = digits;
        }

        /// <summary>Interpreta il pattern; null/vuoto ⇒ <see cref="DefaultPattern"/>. Lancia se non è valido.</summary>
        public static FiscalProjectCodePattern Parse(string? pattern)
        {
            var p = string.IsNullOrWhiteSpace(pattern) ? DefaultPattern : pattern.Trim();
            int? digits = null;
            int seqStart = -1, seqEnd = -1;

            foreach (Match m in Token.Matches(p))
            {
                var name = m.Groups[1].Value;
                if (name is "yy" or "yyyy") continue;
                var seq = Regex.Match(name, @"^seq([1-9])$");
                if (!seq.Success)
                    throw new ArgumentException($"Segnaposto sconosciuto '{{{name}}}' nel pattern '{p}'.");
                if (digits != null)
                    throw new ArgumentException($"Il pattern '{p}' contiene più di un segnaposto {{seqN}}.");
                digits = int.Parse(seq.Groups[1].Value, CultureInfo.InvariantCulture);
                seqStart = m.Index;
                seqEnd = m.Index + m.Length;
            }

            if (digits == null)
                throw new ArgumentException($"Il pattern '{p}' non contiene il progressivo {{seqN}}.");

            return new FiscalProjectCodePattern(p, p[..seqStart], p[seqEnd..], digits.Value);
        }

        public string Prefix(int year) => Render(_before, year);
        public string Suffix(int year) => Render(_after, year);

        /// <summary>
        /// Pattern LIKE per leggere i codici dell'anno, da usare con <c>ESCAPE '\'</c>: in LIKE '_' è un
        /// jolly, e senza escape <c>PRJ26_26%</c> prenderebbe anche <c>PRJ26X26…</c>.
        /// </summary>
        public string LikePattern(int year) => EscapeLike(Prefix(year)) + "%" + EscapeLike(Suffix(year));

        /// <summary>
        /// Prossimo codice dell'anno: massimo progressivo esistente + 1, oppure 1 se non ce n'è. I codici che
        /// non rispettano ESATTAMENTE il pattern (lunghezza, cifre) sono ignorati: un codice creato a mano con
        /// un formato diverso non deve spostare la numerazione. Lancia se il progressivo si esaurisce o il
        /// codice supera la lunghezza di <c>OPRJ.PrjCode</c>.
        /// </summary>
        public string Next(int year, IEnumerable<string?> existingCodes)
        {
            var prefix = Prefix(year);
            var suffix = Suffix(year);
            var max = 0;
            foreach (var raw in existingCodes)
            {
                var code = raw?.Trim();
                if (code == null || code.Length != prefix.Length + SequenceDigits + suffix.Length) continue;
                if (!code.StartsWith(prefix, StringComparison.Ordinal) || !code.EndsWith(suffix, StringComparison.Ordinal)) continue;
                var seq = code.Substring(prefix.Length, SequenceDigits);
                if (!seq.All(char.IsAsciiDigit)) continue;
                var n = int.Parse(seq, CultureInfo.InvariantCulture);
                if (n > max) max = n;
            }

            var next = max + 1;
            var limit = (int)Math.Pow(10, SequenceDigits) - 1;
            if (next > limit)
                throw new InvalidOperationException($"Progressivo dei progetti contabili esaurito per l'anno {year} (massimo {limit}).");

            var result = prefix + next.ToString(new string('0', SequenceDigits), CultureInfo.InvariantCulture) + suffix;
            if (result.Length > MaxCodeLength)
                throw new InvalidOperationException($"Il codice '{result}' supera i {MaxCodeLength} caratteri di OPRJ.PrjCode: rivedere SapB1:FiscalProjectCodePattern.");
            return result;
        }

        internal static string EscapeLike(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (c is '\\' or '_' or '%') sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Render(string template, int year)
            => template
                .Replace("{yyyy}", year.ToString("D4", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{yy}", (year % 100).ToString("D2", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
