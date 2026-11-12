using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Versions;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class RenameSeriesVersionFileFixture : CoreTest<RenameEpisodeFileService>
    {
        private Series _series;
        private Series _view;
        private Episode _episode;
        private EpisodeFile _defaultFile;
        private EpisodeFile _slotFile;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 5, Path = @"C:\Test\TV\Series".AsOsAgnostic() };
            _view = _series.ShallowCopy();
            _view.Slot = new SeriesVersionSlot { Id = 7, SeriesId = 5, Label = "4K" };

            _episode = new Episode { Id = 10, SeriesId = 5, SeasonNumber = 1, EpisodeNumber = 1, EpisodeFileId = 70 };
            _defaultFile = new EpisodeFile { Id = 70, SeriesId = 5, RelativePath = "Season 1/old.mkv" };
            _slotFile = new EpisodeFile
            {
                Id = 71,
                SeriesId = 5,
                VersionSlotId = 7,
                RelativePath = "Season 1/old 4K.mkv",
                Episodes = new LazyLoaded<List<Episode>>(new List<Episode> { _episode })
            };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(5)).Returns(_series);
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(It.IsAny<IEnumerable<int>>())).Returns(new List<Series> { _series });
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodesBySeries(It.IsAny<List<int>>())).Returns(new List<Episode> { _episode });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesBySeriesIds(It.IsAny<List<int>>())).Returns(new List<EpisodeFile> { _defaultFile, _slotFile });
            Mocker.GetMock<ISeriesVersionSlotService>().Setup(s => s.GetView(_series, 7)).Returns(_view);

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(b => b.BuildFilePath(It.IsAny<List<Episode>>(), It.IsAny<Series>(), It.IsAny<EpisodeFile>(), It.IsAny<string>(), It.IsAny<NamingConfig>(), It.IsAny<List<NzbDrone.Core.CustomFormats.CustomFormat>>()))
                  .Returns<List<Episode>, Series, EpisodeFile, string, NamingConfig, List<NzbDrone.Core.CustomFormats.CustomFormat>>((_, series, _, ext, _, _) =>
                      System.IO.Path.Combine(_series.Path, "Season 01", series.VersionLabel == null ? "Show" + ext : "Show - " + series.VersionLabel + ext));
        }

        [Test]
        public void should_preview_a_version_file_named_as_that_version_for_the_episodes_recorded_for_it()
        {
            var previews = Subject.GetRenamePreviews(new List<int> { 5 });

            previews.Should().HaveCount(2);

            var slotPreview = previews.Find(p => p.EpisodeFileId == 71);

            slotPreview.NewPath.Should().EndWith("Show - 4K.mkv");
            slotPreview.EpisodeNumbers.Should().Equal(1);

            previews.Find(p => p.EpisodeFileId == 70).NewPath.Should().EndWith("Show.mkv");
        }

        [Test]
        public void should_rename_a_version_file_as_that_version()
        {
            Mocker.GetMock<IMediaFileService>().Setup(s => s.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<EpisodeFile> { _defaultFile, _slotFile });

            Subject.Execute(new RenameFilesCommand(5, new List<int> { 70, 71 }));

            Mocker.GetMock<IMoveEpisodeFiles>().Verify(m => m.MoveEpisodeFile(_defaultFile, _series), Times.Once());
            Mocker.GetMock<IMoveEpisodeFiles>().Verify(m => m.MoveEpisodeFile(_slotFile, _view), Times.Once());
        }
    }
}
