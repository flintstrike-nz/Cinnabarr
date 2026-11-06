using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Qualities
{
    [TestFixture]
    public class MovieQualityFixture : CoreTest
    {
        [TestCase(1, "SDTV")]
        [TestCase(2, "DVD")]
        [TestCase(3, "WEBDL-1080p")]
        [TestCase(7, "Bluray-1080p")]
        [TestCase(13, "Bluray-480p")]
        [TestCase(20, "Bluray-1080p Remux")]
        [TestCase(21, "Bluray-2160p Remux")]
        [TestCase(22, "Bluray-576p")]
        public void series_qualities_should_keep_their_saved_numbers(int id, string name)
        {
            Quality.FindById(id).Name.Should().Be(name);
        }

        [TestCase(23, "DVD-R")]
        [TestCase(24, "WORKPRINT")]
        [TestCase(25, "CAM")]
        [TestCase(26, "TELESYNC")]
        [TestCase(27, "TELECINE")]
        [TestCase(28, "DVDSCR")]
        [TestCase(29, "REGIONAL")]
        [TestCase(32, "BR-DISK")]
        public void movie_only_qualities_should_exist(int id, string name)
        {
            Quality.FindById(id).Name.Should().Be(name);
        }

        [Test]
        public void numbers_and_names_should_be_unique()
        {
            Quality.All.Select(q => q.Id).Should().OnlyHaveUniqueItems();
            Quality.All.Select(q => q.Name).Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void every_quality_should_have_a_default_definition()
        {
            var defined = Quality.DefaultQualityDefinitions.Select(d => d.Quality.Id);

            Quality.All.Select(q => q.Id).Should().BeEquivalentTo(defined);
        }

        [Test]
        public void a_source_and_resolution_should_point_to_one_quality_only()
        {
            // QualityFinder looks qualities up by source and resolution; two matches would throw.
            Quality.All.Where(q => q.Resolution > 0)
                .GroupBy(q => (q.Source, q.Resolution))
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .Should().BeEmpty();
        }

        [Test]
        public void weights_should_run_from_the_worst_to_the_best_quality()
        {
            var ordered = Quality.DefaultQualityDefinitions.OrderBy(d => d.Weight).Select(d => d.Quality).ToList();

            ordered.First().Should().Be(Quality.Unknown);
            ordered.Last().Should().Be(Quality.Bluray2160pRemux);
            ordered.IndexOf(Quality.Cam).Should().BeLessThan(ordered.IndexOf(Quality.SDTV));
            ordered.IndexOf(Quality.Bluray1080p).Should().BeLessThan(ordered.IndexOf(Quality.Bluray1080pRemux));
        }

        private static QualityProfile ProfileWith(params Quality[] qualities)
        {
            return new QualityProfile
            {
                Name = "Test",
                Cutoff = qualities[0].Id,
                Items = qualities.Select(q => new QualityProfileQualityItem { Quality = q, Allowed = true }).ToList()
            };
        }

        [Test]
        public void should_add_qualities_a_saved_profile_does_not_list_as_not_allowed()
        {
            var profile = ProfileWith(Quality.SDTV, Quality.HDTV720p, Quality.Bluray1080p);

            profile.AddMissingQualities().Should().BeTrue();

            var all = profile.Items.SelectMany(i => i.GetQualities()).Select(q => q.Id);
            all.Should().BeEquivalentTo(Quality.All.Select(q => q.Id));

            profile.Items.Single(i => i.Quality == Quality.Cam).Allowed.Should().BeFalse();
            profile.Items.Where(i => i.Quality == Quality.SDTV || i.Quality == Quality.HDTV720p || i.Quality == Quality.Bluray1080p)
                .Should().OnlyContain(i => i.Allowed);
        }

        [Test]
        public void should_put_added_qualities_in_their_place_by_weight()
        {
            var profile = ProfileWith(Quality.SDTV, Quality.Bluray1080p);

            profile.AddMissingQualities();

            var order = profile.Items.Select(i => i.Quality).ToList();
            order.IndexOf(Quality.Cam).Should().BeLessThan(order.IndexOf(Quality.SDTV));
            order.IndexOf(Quality.Bluray1080p).Should().BeLessThan(order.IndexOf(Quality.Bluray1080pRemux));
            order.IndexOf(Quality.SDTV).Should().BeLessThan(order.IndexOf(Quality.Bluray1080p));
        }

        [Test]
        public void should_do_nothing_when_the_profile_lists_every_quality()
        {
            var profile = ProfileWith(Quality.All.ToArray());

            profile.AddMissingQualities().Should().BeFalse();
            profile.Items.Should().HaveCount(Quality.All.Count);
        }

        [Test]
        public void should_look_inside_groups()
        {
            var profile = ProfileWith(Quality.All.Where(q => q != Quality.WEBDL720p && q != Quality.WEBRip720p).ToArray());
            profile.Items.Add(new QualityProfileQualityItem
            {
                Name = "WEB 720p",
                Allowed = true,
                Items = new System.Collections.Generic.List<QualityProfileQualityItem>
                {
                    new() { Quality = Quality.WEBDL720p, Allowed = true },
                    new() { Quality = Quality.WEBRip720p, Allowed = true }
                }
            });

            profile.AddMissingQualities().Should().BeFalse();
        }
    }
}
