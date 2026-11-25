using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class UnmappedMovieFolderServiceFixture : CoreTest<UnmappedMovieFolderService>
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = @"C:\Movies".AsOsAgnostic();

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_root)).Returns(true);
            Mocker.GetMock<IMovieService>().Setup(s => s.AllMoviePaths()).Returns(new Dictionary<int, string>());
        }

        private void GivenFolders(params string[] names)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.GetDirectories(_root))
                  .Returns(System.Array.ConvertAll(names, n => System.IO.Path.Combine(_root, n)));
        }

        [Test]
        public void should_return_nothing_for_a_missing_folder()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns(false);

            Subject.Get(_root).Should().BeEmpty();
        }

        [Test]
        public void should_parse_the_title_and_year_from_the_folder_name()
        {
            GivenFolders("The Matrix (1999)");

            var result = Subject.Get(_root);

            result.Should().ContainSingle();
            result[0].Name.Should().Be("The Matrix (1999)");
            result[0].ParsedTitle.Should().Be("The Matrix");
            result[0].ParsedYear.Should().Be(1999);
        }

        [Test]
        public void should_leave_out_folders_a_movie_already_uses_and_hidden_folders()
        {
            GivenFolders("Alien (1979)", "Heat (1995)", ".cinnabarr-staging");
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(new Dictionary<int, string> { { 1, System.IO.Path.Combine(_root, "Alien (1979)") } });

            Subject.Get(_root).Should().ContainSingle().Which.Name.Should().Be("Heat (1995)");
        }

        [Test]
        public void should_list_loose_video_files_as_well_as_folders()
        {
            GivenFolders("Heat (1995)");
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.GetFiles(_root, false))
                  .Returns(new[] { "Alien.1979.1080p.BluRay.mkv", "notes.txt", "._Alien.1979.mkv" }.Select(n => System.IO.Path.Combine(_root, n)));

            var result = Subject.Get(_root);

            result.Select(f => f.Name).Should().Equal("Alien.1979.1080p.BluRay.mkv", "Heat (1995)");
            result[0].IsFile.Should().BeTrue();
            result[0].ParsedTitle.Should().Be("Alien");
            result[0].ParsedYear.Should().Be(1979);
            result[1].IsFile.Should().BeFalse();
        }

        [Test]
        public void should_fall_back_to_the_folder_name_when_it_cannot_be_parsed()
        {
            GivenFolders("zzz");

            Subject.Get(_root).Should().ContainSingle().Which.ParsedTitle.Should().NotBeNullOrEmpty();
        }
    }
}
