using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.TorrentRss;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class TrackedDownloadServiceFixture : CoreTest<TrackedDownloadService>
    {
        private void GivenDownloadHistory()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
                .Returns([
                    new EpisodeHistory()
                    {
                        DownloadId = "35238",
                        SourceTitle = "TV Series S01",
                        SeriesId = 5,
                        EpisodeId = 4
                    }
                ]);
        }

        [Test]
        public void should_track_downloads_using_the_source_title_if_it_cannot_be_found_using_the_download_title()
        {
            GivenDownloadHistory();

            var remoteEpisode = new RemoteEpisode
            {
                Series = new Series() { Id = 5 },
                Episodes = [new Episode { Id = 4 }],
                ParsedEpisodeInfo = new ParsedEpisodeInfo()
                {
                    SeriesTitle = "TV Series",
                    SeasonNumbers = [1]
                },
                MappedSeasonNumber = 1
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedEpisodeInfo>(i => i.SeasonNumbers.Contains(1) && i.SeriesTitle == "TV Series"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteEpisode);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "The torrent release folder",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Series.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Series.Id.Should().Be(5);
            trackedDownload.RemoteEpisode.Episodes.First().Id.Should().Be(4);
            trackedDownload.RemoteEpisode.ParsedEpisodeInfo.SeasonNumbers.Should().Equal(1);
            trackedDownload.RemoteEpisode.MappedSeasonNumber.Should().Be(1);
        }

        private const string EngineHash = "0123456789abcdef0123456789abcdef01234567";

        private DownloadClientDefinition GivenEngineClient(bool isEngine = true)
        {
            Mocker.GetMock<IEngineClientRegistry>()
                .Setup(s => s.IsEngineClient(1))
                .Returns(isEngine);

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(EngineHash))
                .Returns([]);

            return new DownloadClientDefinition { Id = 1, Name = "Embarr Engine", Protocol = DownloadProtocol.Torrent };
        }

        private DownloadClientItem GivenEngineItem(DownloadClientDefinition client)
        {
            return new DownloadClientItem
            {
                Title = "TV.Series.S01E01.720p.HDTV-Group",
                DownloadId = EngineHash,
                DownloadClientInfo = new DownloadClientItemClientInfo { Protocol = client.Protocol, Id = client.Id, Name = client.Name }
            };
        }

        [Test]
        public void should_mark_engine_download_without_any_record_as_unlinked_and_not_match_it_by_name()
        {
            var client = GivenEngineClient();

            var trackedDownload = Subject.TrackDownload(client, GivenEngineItem(client));

            trackedDownload.IsUnlinked.Should().BeTrue();
            trackedDownload.RemoteEpisode.Should().BeNull();
            trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
            trackedDownload.StatusMessages.Should().ContainSingle()
                .Which.Messages.Should().Equal(TrackedDownload.UnlinkedMessage);

            Mocker.GetMock<IParsingService>()
                .Verify(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), null), Times.Never());
        }

        [Test]
        public void should_link_engine_download_restored_from_its_sidecar()
        {
            var client = GivenEngineClient();
            var item = GivenEngineItem(client);

            var history = new EpisodeHistory
            {
                DownloadId = EngineHash,
                SourceTitle = item.Title,
                SeriesId = 5,
                EpisodeId = 4,
                EventType = EpisodeHistoryEventType.Grabbed
            };

            Mocker.GetMock<IHistoryService>()
                .SetupSequence(s => s.FindByDownloadId(EngineHash))
                .Returns([])
                .Returns([history]);

            Mocker.GetMock<IEngineLinkService>()
                .Setup(s => s.TryRestore(client, EngineHash))
                .Returns(true);

            var remoteEpisode = new RemoteEpisode
            {
                Series = new Series { Id = 5 },
                Episodes = [new Episode { Id = 4 }],
                ParsedEpisodeInfo = new ParsedEpisodeInfo { SeriesTitle = "TV Series", SeasonNumbers = [1] }
            };

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), 5, It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 4 }))))
                .Returns(remoteEpisode);

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.IsUnlinked.Should().BeFalse();
            trackedDownload.RemoteEpisode.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Series.Id.Should().Be(5);
            trackedDownload.Added.Should().Be(history.Date);
        }

        [Test]
        public void should_take_engine_download_series_from_history_not_from_title()
        {
            var client = GivenEngineClient();
            var item = GivenEngineItem(client);

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(EngineHash))
                .Returns([new EpisodeHistory { DownloadId = EngineHash, SourceTitle = item.Title, SeriesId = 5, EpisodeId = 4, EventType = EpisodeHistoryEventType.Grabbed }]);

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), null))
                .Returns(new RemoteEpisode { Series = new Series { Id = 9 }, Episodes = [new Episode { Id = 90 }] });

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), 5, It.IsAny<IEnumerable<int>>()))
                .Returns(new RemoteEpisode { Series = new Series { Id = 5 }, Episodes = [new Episode { Id = 4 }], ParsedEpisodeInfo = new ParsedEpisodeInfo() });

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.RemoteEpisode.Series.Id.Should().Be(5);
            trackedDownload.RemoteEpisode.Episodes.Single().Id.Should().Be(4);

            Mocker.GetMock<IParsingService>()
                .Verify(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), null), Times.Never());
        }

        [Test]
        public void should_link_unlinked_engine_download_after_manual_import()
        {
            var client = GivenEngineClient();
            var item = GivenEngineItem(client);

            var trackedDownload = Subject.TrackDownload(client, item);
            trackedDownload.IsUnlinked.Should().BeTrue();

            // Manual Import records history for the download id and marks the cached item imported.
            trackedDownload.State = TrackedDownloadState.Imported;

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(EngineHash))
                .Returns([new EpisodeHistory { DownloadId = EngineHash, SourceTitle = item.Title, SeriesId = 5, EpisodeId = 4, EventType = EpisodeHistoryEventType.DownloadFolderImported }]);

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem(EngineHash))
                .Returns(new DownloadHistory { DownloadId = EngineHash, SeriesId = 5, EventType = DownloadHistoryEventType.DownloadImported });

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), 5, It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 4 }))))
                .Returns(new RemoteEpisode { Series = new Series { Id = 5 }, Episodes = [new Episode { Id = 4 }], ParsedEpisodeInfo = new ParsedEpisodeInfo() });

            var relinked = Subject.TrackDownload(client, item);

            relinked.IsUnlinked.Should().BeFalse();
            relinked.State.Should().Be(TrackedDownloadState.Imported);
            relinked.Status.Should().Be(TrackedDownloadStatus.Ok);
            relinked.RemoteEpisode.Series.Id.Should().Be(5);
        }

        [Test]
        public void should_not_match_unlinked_engine_download_by_name_when_a_series_is_added()
        {
            var client = GivenEngineClient();

            var trackedDownload = Subject.TrackDownload(client, GivenEngineItem(client));

            Subject.Handle(new SeriesAddedEvent(new Series { Id = 9, TvdbId = 99 }));

            trackedDownload.RemoteEpisode.Should().BeNull();

            Mocker.GetMock<IParsingService>()
                .Verify(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), null), Times.Never());
        }

        [Test]
        public void should_not_try_to_restore_or_unlink_downloads_from_other_clients()
        {
            var client = GivenEngineClient(false);

            var trackedDownload = Subject.TrackDownload(client, GivenEngineItem(client));

            trackedDownload.IsUnlinked.Should().BeFalse();

            Mocker.GetMock<IEngineLinkService>()
                .Verify(s => s.TryRestore(It.IsAny<DownloadClientDefinition>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_set_indexer()
        {
            var episodeHistory = new EpisodeHistory()
            {
                DownloadId = "35238",
                SourceTitle = "TV Series S01",
                SeriesId = 5,
                EpisodeId = 4,
                EventType = EpisodeHistoryEventType.Grabbed,
            };
            episodeHistory.Data.Add("indexer", "MyIndexer (Prowlarr)");
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
                .Returns([
                    episodeHistory
                ]);

            var indexerDefinition = new IndexerDefinition
            {
                Id = 1,
                Name = "MyIndexer (Prowlarr)",
                Settings = new TorrentRssIndexerSettings { MultiLanguages = new List<int> { Language.Original.Id, Language.French.Id } }
            };
            Mocker.GetMock<IIndexerFactory>()
                .Setup(v => v.Get(indexerDefinition.Id))
                .Returns(indexerDefinition);
            Mocker.GetMock<IIndexerFactory>()
                .Setup(v => v.All())
                .Returns([indexerDefinition]);

            var remoteEpisode = new RemoteEpisode
            {
                Series = new Series() { Id = 5 },
                Episodes = [new Episode { Id = 4 }],
                ParsedEpisodeInfo = new ParsedEpisodeInfo()
                {
                    SeriesTitle = "TV Series",
                    SeasonNumbers = [1]
                },
                MappedSeasonNumber = 1
            };

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), null))
                .Returns(remoteEpisode);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "TV.Series.S01.MULTi.1080p.WEB.H265-RlsGroup",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Release.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Release.Indexer.Should().Be("MyIndexer (Prowlarr)");
        }

        [Test]
        public void should_parse_as_special_when_source_title_parsing_fails()
        {
            var remoteEpisode = new RemoteEpisode
            {
                Series = new Series() { Id = 5 },
                Episodes = [new Episode { Id = 4 }],
                ParsedEpisodeInfo = new ParsedEpisodeInfo()
                {
                    SeriesTitle = "TV Series",
                    SeasonNumbers = [0],
                    EpisodeNumbers = [1]
                },
                MappedSeasonNumber = 0
            };

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
                .Returns([
                    new EpisodeHistory()
                    {
                        DownloadId = "35238",
                        SourceTitle = "TV Series Special",
                        SeriesId = 5,
                        EpisodeId = 4
                    }
                ]);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedEpisodeInfo>(i => i.SeasonNumbers.Contains(0) && i.SeriesTitle == "TV Series"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteEpisode);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.ParseSpecialEpisodeTitle(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), null))
                  .Returns(remoteEpisode.ParsedEpisodeInfo);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "The torrent release folder",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Series.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Series.Id.Should().Be(5);
            trackedDownload.RemoteEpisode.Episodes.First().Id.Should().Be(4);
            trackedDownload.RemoteEpisode.ParsedEpisodeInfo.SeasonNumbers.Should().Equal(0);
            trackedDownload.RemoteEpisode.MappedSeasonNumber.Should().Be(0);
        }

        [Test]
        public void should_unmap_tracked_download_if_episode_deleted()
        {
            GivenDownloadHistory();

            var remoteEpisode = new RemoteEpisode
                                {
                                    Series = new Series() { Id = 5 },
                                    Episodes = [new Episode { Id = 4 }],
                                    ParsedEpisodeInfo = new ParsedEpisodeInfo()
                                                        {
                                                            SeriesTitle = "TV Series",
                                                            SeasonNumbers = [1],
                                                            EpisodeNumbers = new[] { 1 }
                                                        },
                                    MappedSeasonNumber = 0
                                };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), null))
                  .Returns(remoteEpisode);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns([]);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "TV Series - S01E01",
                DownloadId = "12345",
                DownloadClientInfo = new DownloadClientItemClientInfo
                                     {
                                         Id = 1,
                                         Type = "Blackhole",
                                         Name = "Blackhole Client",
                                         Protocol = DownloadProtocol.Torrent
                                     }
            };

            Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), null))
                  .Returns(default(RemoteEpisode));

            Subject.Handle(new EpisodeInfoRefreshedEvent(remoteEpisode.Series, new List<Episode>(), new List<Episode>(), remoteEpisode.Episodes));

            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_not_throw_when_processing_deleted_episodes()
        {
            GivenDownloadHistory();

            var remoteEpisode = new RemoteEpisode
            {
                Series = new Series() { Id = 5 },
                Episodes = [new Episode { Id = 4 }],
                ParsedEpisodeInfo = new ParsedEpisodeInfo()
                {
                    SeriesTitle = "TV Series",
                    SeasonNumbers = [1],
                    EpisodeNumbers = [1]
                },
                MappedSeasonNumber = 0
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), null))
                  .Returns(default(RemoteEpisode));

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns([]);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "TV Series - S01E01",
                DownloadId = "12345",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Id = 1,
                    Type = "Blackhole",
                    Name = "Blackhole Client",
                    Protocol = DownloadProtocol.Torrent
                }
            };

            Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), null))
                  .Returns(default(RemoteEpisode));

            Subject.Handle(new EpisodeInfoRefreshedEvent(remoteEpisode.Series, new List<Episode>(), new List<Episode>(), remoteEpisode.Episodes));

            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_not_throw_when_processing_deleted_series()
        {
            GivenDownloadHistory();

            var remoteEpisode = new RemoteEpisode
            {
                Series = new Series() { Id = 5 },
                Episodes = [new Episode { Id = 4 }],
                ParsedEpisodeInfo = new ParsedEpisodeInfo()
                {
                    SeriesTitle = "TV Series",
                    SeasonNumbers = [1],
                    EpisodeNumbers = [1]
                },
                MappedSeasonNumber = 0
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), null))
                  .Returns(default(RemoteEpisode));

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns([]);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "TV Series - S01E01",
                DownloadId = "12345",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Id = 1,
                    Type = "Blackhole",
                    Name = "Blackhole Client",
                    Protocol = DownloadProtocol.Torrent
                }
            };

            Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), null))
                  .Returns(default(RemoteEpisode));

            Subject.Handle(new SeriesDeletedEvent([remoteEpisode.Series], true, true));

            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_retrack_as_downloading_when_grabbed_again_after_being_imported()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
                .Returns([]);

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem(It.Is<string>(sr => sr == "35238")))
                .Returns(new DownloadHistory
                {
                    SeriesId = 5,
                    EventType = DownloadHistoryEventType.DownloadImported,
                });

            var remoteEpisode = new RemoteEpisode
            {
                Series = new Series { Id = 5 },
                Episodes = [new Episode { Id = 4 }],
                ParsedEpisodeInfo = new ParsedEpisodeInfo
                {
                    SeriesTitle = "TV Series",
                    SeasonNumbers = [1]
                },
                MappedSeasonNumber = 1
            };

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.Is<ParsedEpisodeInfo>(i => i.SeasonNumbers.Contains(1) && i.SeriesTitle == "TV Series"), It.IsAny<Series>()))
                .Returns(remoteEpisode);

            var client = new DownloadClientDefinition
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem
            {
                Title = "TV Series - S01E01",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            var imported = Subject.TrackDownload(client, item);
            imported.State.Should().Be(TrackedDownloadState.Imported);

            Subject.Handle(new EpisodeGrabbedEvent(remoteEpisode) { DownloadId = "35238" });

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem(It.Is<string>(sr => sr == "35238")))
                .Returns(new DownloadHistory
                {
                    SeriesId = 5,
                    EventType = DownloadHistoryEventType.DownloadGrabbed,
                });

            var regrabbed = Subject.TrackDownload(client, item);
            regrabbed.State.Should().Be(TrackedDownloadState.Downloading);
        }

        [Test]
        public void should_track_downloads_using_the_series_id_for_already_imported_downloads()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
                .Returns([]);

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem(It.Is<string>(sr => sr == "35238")))
                .Returns(new DownloadHistory
                {
                    SeriesId = 5,
                    EventType = DownloadHistoryEventType.DownloadImported,
                });

            var remoteEpisode = new RemoteEpisode
            {
                Series = new Series { Id = 5 },
                Episodes = [new Episode { Id = 4 }],
                ParsedEpisodeInfo = new ParsedEpisodeInfo
                {
                    SeriesTitle = "TV Series",
                    SeasonNumbers = [1]
                },
                MappedSeasonNumber = 1
            };

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.Is<ParsedEpisodeInfo>(i => i.SeasonNumbers.Contains(1) && i.SeriesTitle == "TV Series"), It.IsAny<Series>()))
                .Returns(remoteEpisode);

            var client = new DownloadClientDefinition
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem
            {
                Title = "TV Series - S01E01",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Series.Should().NotBeNull();
            trackedDownload.RemoteEpisode.Series.Id.Should().Be(5);
            trackedDownload.RemoteEpisode.Episodes.First().Id.Should().Be(4);
            trackedDownload.RemoteEpisode.ParsedEpisodeInfo.SeasonNumbers.Should().Equal(1);
            trackedDownload.RemoteEpisode.MappedSeasonNumber.Should().Be(1);
        }

        [Test]
        public void should_stop_tracking_downloads_missing_from_their_download_client()
        {
            GivenTrackedDownload(1, "present");
            GivenTrackedDownload(1, "missing");
            GivenTrackedDownload(2, "other");

            Subject.StopTrackingMissing(1, new List<string> { "present" });

            Subject.Find("present").Should().NotBeNull();
            Subject.Find("missing").Should().BeNull();
            Subject.Find("other").Should().NotBeNull();
        }

        private void GivenTrackedDownload(int downloadClientId, string downloadId)
        {
            var client = new DownloadClientDefinition
            {
                Id = downloadClientId,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem
            {
                Title = "Unparsable",
                DownloadId = downloadId,
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id
                }
            };

            Subject.TrackDownload(client, item);
        }
    }
}
