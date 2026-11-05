using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Parser
{
    public static class MovieTitleExtensions
    {
        // Same rules Radarr uses to turn a title into something comparable: drop articles and punctuation, lower-case,
        // spell out German umlauts and remove accents.
        private static readonly Regex NormalizeRegex = new(@"((?:\b|_)(?<!^|[^a-zA-Z0-9_']\w[^a-zA-Z0-9_'])([aà](?!$|[^a-zA-Z0-9_']\w[^a-zA-Z0-9_'])|an|the|and|or|of)(?!$)(?:\b|_))|\W|_",
                                                           RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string CleanMovieTitle(this string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return title;
            }

            // If Title only contains numbers return it as is.
            if (long.TryParse(title, out _))
            {
                return title;
            }

            return ReplaceGermanUmlauts(NormalizeRegex.Replace(title, string.Empty).ToLowerInvariant()).RemoveDiacritics();
        }

        private static string ReplaceGermanUmlauts(string s)
        {
            return s.Replace("ä", "ae")
                    .Replace("ö", "oe")
                    .Replace("ü", "ue")
                    .Replace("Ä", "Ae")
                    .Replace("Ö", "Oe")
                    .Replace("Ü", "Ue")
                    .Replace("ß", "ss");
        }
    }
}
