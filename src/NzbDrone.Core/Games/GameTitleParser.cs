using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Games
{
    /// <summary>Turns a ROM file name like "Super Mario World (USA) [!]" into a title and a region.</summary>
    public static class GameTitleParser
    {
        private static readonly Regex Tags = new Regex(@"\s*[\(\[][^\)\]]*[\)\]]", RegexOptions.Compiled);
        private static readonly Regex Parenthesised = new Regex(@"\(([^\)]*)\)", RegexOptions.Compiled);
        private static readonly Regex TrailingArticle = new Regex(@"^(?<title>.+?),\s*(?<article>The|A|An)(?<rest>\s+-\s+.*)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] Regions =
        {
            "USA", "Europe", "Japan", "World", "Australia", "Brazil", "Korea", "Asia", "France", "Germany", "Spain", "Italy", "Canada", "Netherlands", "Sweden", "China"
        };

        public static (string Title, string Region) Parse(string fileNameWithoutExtension)
        {
            var name = (fileNameWithoutExtension ?? string.Empty).Replace('_', ' ').Trim();
            var region = Parenthesised.Matches(name)
                                      .Select(m => m.Groups[1].Value)
                                      .SelectMany(v => v.Split(','))
                                      .Select(v => v.Trim())
                                      .FirstOrDefault(v => Regions.Contains(v, System.StringComparer.OrdinalIgnoreCase));

            var title = Tags.Replace(name, string.Empty).Trim();
            var article = TrailingArticle.Match(title);

            if (article.Success)
            {
                title = $"{article.Groups["article"].Value} {article.Groups["title"].Value}{article.Groups["rest"].Value}";
            }

            return (title.Length > 0 ? title : name, region);
        }

        public static string SortTitle(string title)
        {
            var lower = (title ?? string.Empty).ToLowerInvariant();

            foreach (var article in new[] { "the ", "a ", "an " })
            {
                if (lower.StartsWith(article))
                {
                    return lower.Substring(article.Length);
                }
            }

            return lower;
        }
    }
}
