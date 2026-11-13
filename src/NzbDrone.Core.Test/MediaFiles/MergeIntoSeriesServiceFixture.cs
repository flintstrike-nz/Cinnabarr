using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MediaFiles.EpisodeImport.Manual;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class MergeIntoSeriesServiceFixture : CoreTest<MergeIntoSeriesService>
    {
        private Series _series;
        private string _root;
        private string _folder;
        private List<ManualImportItem> _items;
        private ManualImportCommand _imported;

        [SetUp]
        public void Setup()
        {
            _root = @"C:\Media\TV".AsOsAgnostic();
            _folder = System.IO.Path.Combine(_root, "Show.S01.1080p-GRP");
            _series = new Series { Id = 5, Title = "Show", Path = System.IO.Path.Combine(_root, "Show") };
            _imported = null;

            _items = new List<ManualImportItem>
            {
                Item("Show.S01E01.mkv", 11),
                Item("Show.S01E02.mkv", 12)
            };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(5)).Returns(_series);
            Mocker.GetMock<IRootFolderService>().Setup(r => r.GetBestRootFolderPath(It.IsAny<string>())).Returns(_root);
            Mocker.GetMock<IManualImportService>().Setup(m => m.GetMediaFiles(_folder, null, 5, true)).Returns(() => _items);

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_folder)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns(false);

            Mocker.GetMock<IExecute<ManualImportCommand>>()
                  .Setup(e => e.Execute(It.IsAny<ManualImportCommand>()))
                  .Callback<ManualImportCommand>(c => _imported = c);
        }

        private ManualImportItem Item(string name, int episodeId, params ImportRejection[] rejections)
        {
            return new ManualImportItem
            {
                Path = System.IO.Path.Combine(_folder, name),
                FolderName = "Show.S01.1080p-GRP",
                Series = _series,
                Episodes = new List<Episode> { new Episode { Id = episodeId } },
                Quality = new QualityModel(Quality.HDTV1080p),
                Rejections = rejections
            };
        }

        [Test]
        public void should_move_each_matched_file_into_the_series_and_remove_the_emptied_folder()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFiles(_folder, true)).Returns(new List<string>());

            Subject.Execute(new MergeIntoSeriesCommand { SeriesId = 5, Paths = new List<string> { _folder } });

            _imported.ImportMode.Should().Be(ImportMode.Move);
            _imported.Files.Select(f => (f.SeriesId, f.EpisodeIds.Single())).Should().Equal((5, 11), (5, 12));
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFolder(_folder, true), Times.Once());
        }

        [Test]
        public void should_leave_files_that_do_not_match_or_are_rejected_and_keep_a_folder_that_still_has_files()
        {
            _items.Add(Item("Show.S01E03.mkv", 13, new ImportRejection(ImportRejectionReason.Unknown, "Not an upgrade")));
            _items.Add(new ManualImportItem { Path = System.IO.Path.Combine(_folder, "extra.mkv"), Episodes = new List<Episode>() });

            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFiles(_folder, true)).Returns(new List<string> { "Show.S01E03.mkv" });

            Subject.Execute(new MergeIntoSeriesCommand { SeriesId = 5, Paths = new List<string> { _folder } });

            _imported.Files.Should().HaveCount(2);
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_not_touch_the_root_folder_the_series_folder_or_a_missing_path()
        {
            var inside = System.IO.Path.Combine(_series.Path, "Season 01");

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_root)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(inside)).Returns(true);

            Subject.Execute(new MergeIntoSeriesCommand { SeriesId = 5, Paths = new List<string> { _root, inside, System.IO.Path.Combine(_root, "Gone") } });

            Mocker.GetMock<IExecute<ManualImportCommand>>().Verify(e => e.Execute(It.IsAny<ManualImportCommand>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            ExceptionVerification.IgnoreWarns();
        }
    }
}
