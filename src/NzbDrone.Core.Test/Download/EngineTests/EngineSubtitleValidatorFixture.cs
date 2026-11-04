using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineSubtitleValidatorFixture : CoreTest<EngineSubtitleValidator>
    {
        private string _path;

        [SetUp]
        public void Setup()
        {
            _path = Path.Combine(TempFolder, ".Show - S01E01.en.srt.abcdef01.pending");

            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileSize(_path)).Returns(() => new FileInfo(_path).Length);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.OpenReadStream(_path)).Returns(() => File.OpenRead(_path));
        }

        private void GivenFile(byte[] content)
        {
            File.WriteAllBytes(_path, content);
        }

        [TestCase("Show - S01E01.en.srt", "1\n00:00:01,000 --> 00:00:02,000\nHello\n")]
        [TestCase("Show - S01E01.en.vtt", "WEBVTT\n\n00:01.000 --> 00:02.000\nHello\n")]
        [TestCase("Show - S01E01.en.ass", "[Script Info]\nTitle: x\n\n[Events]\nDialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Hello\n")]
        public void should_pass_a_text_subtitle_that_reads_as_its_format(string name, string content)
        {
            GivenFile(Encoding.UTF8.GetBytes(content));

            Subject.Validate(_path, name).Should().BeNull();
        }

        [Test]
        public void should_pass_a_utf16_subtitle()
        {
            GivenFile(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("1\r\n00:00:01,000 --> 00:00:02,000\r\nHello\r\n")).ToArray());

            Subject.Validate(_path, "Show - S01E01.en.srt").Should().BeNull();
        }

        [Test]
        public void should_reject_an_empty_subtitle()
        {
            GivenFile(new byte[0]);

            Subject.Validate(_path, "Show - S01E01.en.srt").Should().NotBeNull();
        }

        [Test]
        public void should_reject_a_binary_file_named_as_a_text_subtitle()
        {
            GivenFile(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x00, 0x00, 0x01 });

            Subject.Validate(_path, "Show - S01E01.en.srt").Should().NotBeNull();
        }

        [Test]
        public void should_reject_text_that_is_not_a_subtitle()
        {
            GivenFile(Encoding.UTF8.GetBytes("<html><body>Download more at example.com</body></html>"));

            Subject.Validate(_path, "Show - S01E01.en.srt").Should().NotBeNull();
        }

        [Test]
        public void should_only_check_the_size_of_image_subtitles()
        {
            GivenFile(new byte[] { 0x50, 0x47, 0x00, 0x01 });

            Subject.Validate(_path, "Show - S01E01.en.sup").Should().BeNull();
        }
    }
}
