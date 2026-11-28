using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class FreeFilePathFixture : CoreTest
    {
        private Mock<IDiskProvider> _disk;
        private HashSet<string> _onDisk;
        private string _folder;

        [SetUp]
        public void Setup()
        {
            _folder = @"C:\Test\TV\Series\Season 01".AsOsAgnostic();
            _onDisk = new HashSet<string>();
            _disk = new Mock<IDiskProvider>();
            _disk.Setup(d => d.FileExists(It.IsAny<string>())).Returns<string>(p => _onDisk.Contains(p));
        }

        private string In(string name) => Path.Combine(_folder, name);

        [Test]
        public void should_keep_a_free_destination()
        {
            FreeFilePath.Find(_disk.Object, In("Show - S01E01.mkv")).Should().Be(In("Show - S01E01.mkv"));
        }

        [Test]
        public void should_number_a_taken_destination()
        {
            _onDisk.Add(In("Show - S01E01.mkv"));
            _onDisk.Add(In("Show - S01E01 (2).mkv"));

            FreeFilePath.Find(_disk.Object, In("Show - S01E01.mkv"), In("download.mkv")).Should().Be(In("Show - S01E01 (3).mkv"));
        }

        [Test]
        public void should_keep_the_name_a_numbered_file_already_has()
        {
            _onDisk.Add(In("Show - S01E01.mkv"));
            _onDisk.Add(In("Show - S01E01 (2).mkv"));

            FreeFilePath.Find(_disk.Object, In("Show - S01E01.mkv"), In("Show - S01E01 (2).mkv")).Should().Be(In("Show - S01E01 (2).mkv"));
        }

        [Test]
        public void should_not_number_a_change_of_case()
        {
            _onDisk.Add(In("show - s01e01.mkv"));

            FreeFilePath.Find(_disk.Object, In("Show - S01E01.mkv"), In("show - s01e01.mkv")).Should().Be(In("Show - S01E01.mkv"));
        }

        [Test]
        public void should_skip_names_taken_earlier_in_the_run()
        {
            _onDisk.Add(In("Show - S01E01.mkv"));
            var taken = new HashSet<string> { In("Show - S01E01 (2).mkv") };

            FreeFilePath.Find(_disk.Object, In("Show - S01E01.mkv"), In("a.mkv"), taken).Should().Be(In("Show - S01E01 (3).mkv"));
        }

        [TestCase("Show - S01E01 (2).mkv", true)]
        [TestCase("Show - S01E01 (12).mkv", true)]
        [TestCase("Show - S01E01 (1).mkv", false)]
        [TestCase("Show - S01E01 (02).mkv", false)]
        [TestCase("Show - S01E01 (x).mkv", false)]
        [TestCase("Show - S01E01 (2).mp4", false)]
        [TestCase("Show - S01E02 (2).mkv", false)]
        public void should_know_a_numbered_copy(string name, bool expected)
        {
            _onDisk.Add(In("Show - S01E01.mkv"));

            FreeFilePath.IsNumberedCopy(_disk.Object, In(name), In("Show - S01E01.mkv")).Should().Be(expected);
        }

        [Test]
        public void should_rename_a_numbered_copy_once_the_other_file_is_gone()
        {
            FreeFilePath.IsNumberedCopy(_disk.Object, In("Show - S01E01 (2).mkv"), In("Show - S01E01.mkv")).Should().BeFalse();
        }
    }
}
