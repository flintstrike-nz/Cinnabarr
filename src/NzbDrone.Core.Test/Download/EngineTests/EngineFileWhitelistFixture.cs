using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineFileWhitelistFixture : TestBase
    {
        [TestCase("Show.S01E01.mkv")]
        [TestCase("Folder/Show.S01E01.MP4")]
        [TestCase("Show.S01E01.m2ts")]
        [TestCase(@"Folder\Show.S01E01.avi")]
        public void should_keep_video(string name)
        {
            EngineFileWhitelist.IsVideo(name).Should().BeTrue();
            EngineFileWhitelist.IsWanted(name).Should().BeTrue();
        }

        [TestCase("Show.S01E01.en.srt")]
        [TestCase("Subs/Show.S01E01.forced.ass")]
        [TestCase("Show.S01E01.idx")]
        public void should_keep_subtitles(string name)
        {
            EngineFileWhitelist.IsSubtitle(name).Should().BeTrue();
            EngineFileWhitelist.IsWanted(name).Should().BeTrue();
            EngineFileWhitelist.IsVideo(name).Should().BeFalse();
        }

        [TestCase("Show.S01E01.mkv.exe")]
        [TestCase("Show.S01E01.nfo")]
        [TestCase("Show.S01E01.rar")]
        [TestCase("Show.S01E01.r00")]
        [TestCase("Sample/sample.jpg")]
        [TestCase("mkv")]
        [TestCase(null)]
        public void should_not_keep_anything_else(string name)
        {
            EngineFileWhitelist.IsWanted(name).Should().BeFalse();
        }
    }
}
