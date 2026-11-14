using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Doctor;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Test.DoctorTests
{
    [TestFixture]
    public class DoctorAnalyzerFixture
    {
        private static DoctorCandidate File(int id, Quality quality, long size = 2_000_000_000, long bitrate = 8_000_000, bool isProtected = false, int daysOld = 0)
        {
            return new DoctorCandidate
            {
                MediaType = DoctorMediaType.Movie,
                ItemId = 1,
                GroupKey = "movie",
                FileId = id,
                Size = size,
                DateAdded = new DateTime(2024, 1, 1).AddDays(-daysOld),
                Quality = new QualityModel(quality),
                MediaInfo = new MediaInfoModel { VideoBitrate = bitrate, Height = quality.Resolution },
                IsProtected = isProtected
            };
        }

        [Test]
        public void should_not_flag_a_single_file()
        {
            DoctorAnalyzer.FindDuplicates(new[] { File(1, Quality.Bluray1080p) }).Should().BeEmpty();
        }

        [Test]
        public void should_not_flag_versions_at_different_resolutions()
        {
            DoctorAnalyzer.FindDuplicates(new[] { File(1, Quality.Bluray2160p), File(2, Quality.Bluray1080p) }).Should().BeEmpty();
        }

        [Test]
        public void should_flag_the_lower_source()
        {
            var result = DoctorAnalyzer.FindDuplicates(new[] { File(1, Quality.WEBDL1080p), File(2, Quality.Bluray1080p) });

            result.Should().ContainSingle();
            result[0].Worse.FileId.Should().Be(1);
            result[0].Better.FileId.Should().Be(2);
            result[0].Reason.Should().StartWith("Lower source");
        }

        [Test]
        public void should_flag_the_lower_bitrate_when_source_matches()
        {
            var result = DoctorAnalyzer.FindDuplicates(new[] { File(1, Quality.Bluray1080p, bitrate: 4_000_000), File(2, Quality.Bluray1080p, bitrate: 9_000_000) });

            result.Should().ContainSingle();
            result[0].Worse.FileId.Should().Be(1);
            result[0].Reason.Should().StartWith("Lower video bitrate");
        }

        [Test]
        public void should_ignore_small_size_differences()
        {
            var result = DoctorAnalyzer.FindDuplicates(new[] { File(1, Quality.Bluray1080p, size: 2_000_000_000, bitrate: 0, daysOld: 0), File(2, Quality.Bluray1080p, size: 2_100_000_000, bitrate: 0, daysOld: 5) });

            // Within 10%: a tie, so the copy that has been there longest is kept.
            result.Should().ContainSingle();
            result[0].Better.FileId.Should().Be(2);
            result[0].Reason.Should().Be("Same quality; added later");
        }

        [Test]
        public void should_flag_the_smaller_file_when_nothing_else_differs()
        {
            var result = DoctorAnalyzer.FindDuplicates(new[] { File(1, Quality.Bluray1080p, size: 1_000_000_000, bitrate: 0), File(2, Quality.Bluray1080p, size: 3_000_000_000, bitrate: 0) });

            result[0].Worse.FileId.Should().Be(1);
            result[0].Reason.Should().StartWith("Smaller file");
        }

        [Test]
        public void should_keep_the_protected_copy_even_when_it_is_worse()
        {
            var result = DoctorAnalyzer.FindDuplicates(new[] { File(1, Quality.WEBDL1080p, isProtected: true), File(2, Quality.Bluray1080p) });

            result.Should().ContainSingle();
            result[0].Worse.FileId.Should().Be(2);
            result[0].Better.FileId.Should().Be(1);
        }

        [Test]
        public void should_flag_all_but_the_best_of_three()
        {
            var result = DoctorAnalyzer.FindDuplicates(new[] { File(1, Quality.WEBRip1080p), File(2, Quality.Bluray1080p), File(3, Quality.WEBDL1080p) });

            result.Select(r => r.Worse.FileId).Should().BeEquivalentTo(new List<int> { 1, 3 });
            result.All(r => r.Better.FileId == 2).Should().BeTrue();
        }
    }
}
