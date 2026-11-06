using System.Linq;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Profiles.Qualities
{
    public static class QualityProfileMissingQualities
    {
        /// <summary>
        /// Adds any quality the profile does not list yet (for example the movie-only ones a profile saved before movies
        /// existed) as not allowed, in its place by weight. Returns true when something was added.
        /// </summary>
        public static bool AddMissingQualities(this QualityProfile profile)
        {
            if (profile.Items == null)
            {
                return false;
            }

            var listed = profile.Items.SelectMany(i => i.GetQualities()).Select(q => q.Id).ToHashSet();
            var missing = Quality.All.Where(q => !listed.Contains(q.Id)).ToList();

            if (missing.Count == 0)
            {
                return false;
            }

            foreach (var quality in missing)
            {
                var item = new QualityProfileQualityItem { Quality = quality, Allowed = false };
                var weight = Weight(quality);

                // Items run from the worst quality to the best; slot the new one before the first better item.
                var index = profile.Items.FindIndex(i => i.GetQualities().Max(q => Weight(q)) > weight);

                if (index < 0)
                {
                    profile.Items.Add(item);
                }
                else
                {
                    profile.Items.Insert(index, item);
                }
            }

            return true;
        }

        private static int Weight(Quality quality)
        {
            return Quality.DefaultQualityDefinitions.Single(d => d.Quality == quality).Weight;
        }
    }
}
