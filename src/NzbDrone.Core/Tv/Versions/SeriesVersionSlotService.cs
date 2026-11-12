using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv.Versions
{
    public interface ISeriesVersionSlotService
    {
        // The extra slots of a series, oldest first. The default slot is not among them.
        List<SeriesVersionSlot> GetBySeries(int seriesId);
        SeriesVersionSlot Get(int id);

        // The series as one of its version slots sees it: the slot's quality profile and monitored state (the series' too).
        // The default slot is the series itself. A view has the series' id and is only for reading and deciding: never
        // save it.
        Series GetView(Series series, int slotId);

        // The episodes as a version slot sees them: each one's file is the slot's file for it (none when the slot has no
        // file for it yet), so the decision engine and the importer work on a slot unchanged. The default slot is the
        // episodes themselves. Views are never saved.
        List<Episode> GetEpisodeViews(List<Episode> episodes, int slotId);

        // The files of one slot, by episode id.
        Dictionary<int, EpisodeFile> GetFiles(List<int> episodeIds, int slotId);

        // The episodes that the monitored versions of monitored series are missing (monitored, aired, no file for the
        // version) and those whose file for the version is below the version's profile cutoff, for the automatic searches
        // and the Wanted lists. Each entry is one version of one series (a view) with its episodes (views). The series'
        // own (default) versions are not among them. Limited to one series when seriesIds is given.
        List<SeriesVersionWanted> GetMissing(ICollection<int> seriesIds = null);
        List<SeriesVersionWanted> GetCutoffUnmet(ICollection<int> seriesIds = null);

        SeriesVersionSlot Add(SeriesVersionSlot slot);
        SeriesVersionSlot Update(SeriesVersionSlot slot);
        void Delete(int id);
    }

    // One version of one series and the episodes it wants.
    public class SeriesVersionWanted
    {
        public Series Series { get; set; }
        public List<Episode> Episodes { get; set; }
    }

    public class SeriesVersionSlotService : ISeriesVersionSlotService,
                                            IHandle<EpisodeFileAddedEvent>,
                                            IHandle<EpisodeFileDeletedEvent>,
                                            IHandleAsync<SeriesDeletedEvent>
    {
        // The default slot plus up to seven extra ones, as for movies.
        public const int MaxSlotsPerSeries = 8;

        private readonly ISeriesVersionSlotRepository _slotRepository;
        private readonly IEpisodeVersionFileRepository _linkRepository;
        private readonly IMediaFileService _mediaFileService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;

        public SeriesVersionSlotService(ISeriesVersionSlotRepository slotRepository,
                                        IEpisodeVersionFileRepository linkRepository,
                                        IMediaFileService mediaFileService,
                                        IQualityProfileService qualityProfileService,
                                        ISeriesService seriesService,
                                        IEpisodeService episodeService)
        {
            _slotRepository = slotRepository;
            _linkRepository = linkRepository;
            _mediaFileService = mediaFileService;
            _qualityProfileService = qualityProfileService;
            _seriesService = seriesService;
            _episodeService = episodeService;
        }

        public List<SeriesVersionSlot> GetBySeries(int seriesId)
        {
            return _slotRepository.GetBySeries(seriesId).OrderBy(s => s.Id).ToList();
        }

        public SeriesVersionSlot Get(int id)
        {
            return _slotRepository.Get(id);
        }

        public Series GetView(Series series, int slotId)
        {
            if (slotId == SeriesVersionSlot.DefaultSlotId)
            {
                return series;
            }

            var slot = _slotRepository.Get(slotId);

            if (slot.SeriesId != series.Id)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "This version belongs to another series");
            }

            return BuildView(series, slot);
        }

        public List<Episode> GetEpisodeViews(List<Episode> episodes, int slotId)
        {
            if (slotId == SeriesVersionSlot.DefaultSlotId)
            {
                return episodes;
            }

            var files = GetFiles(episodes.Select(e => e.Id).ToList(), slotId);

            return episodes.Select(episode =>
            {
                var view = episode.ShallowCopy();
                var file = files.GetValueOrDefault(episode.Id);

                view.EpisodeFileId = file?.Id ?? 0;
                view.EpisodeFile = new LazyLoaded<EpisodeFile>(file);

                return view;
            }).ToList();
        }

        public Dictionary<int, EpisodeFile> GetFiles(List<int> episodeIds, int slotId)
        {
            var links = _linkRepository.GetByEpisodes(episodeIds).Where(l => l.VersionSlotId == slotId).ToList();

            if (links.Empty())
            {
                return new Dictionary<int, EpisodeFile>();
            }

            var files = _mediaFileService.Get(links.Select(l => l.EpisodeFileId).Distinct()).ToDictionary(f => f.Id);

            return links.Where(l => files.ContainsKey(l.EpisodeFileId)).ToDictionary(l => l.EpisodeId, l => files[l.EpisodeFileId]);
        }

        public List<SeriesVersionWanted> GetMissing(ICollection<int> seriesIds = null)
        {
            var now = DateTime.UtcNow;

            return GetWanted(seriesIds, (slot, episode, file) => episode.AirDateUtc.HasValue && episode.AirDateUtc.Value < now && file == null);
        }

        public List<SeriesVersionWanted> GetCutoffUnmet(ICollection<int> seriesIds = null)
        {
            var cutoffs = new Dictionary<int, HashSet<int>>();

            return GetWanted(seriesIds, (slot, episode, file) =>
            {
                if (file?.Quality?.Quality == null)
                {
                    return false;
                }

                // The qualities below the profile's cutoff (or below the best allowed one when it can't be upgraded).
                if (!cutoffs.TryGetValue(slot.QualityProfileId, out var below))
                {
                    var profile = _qualityProfileService.Get(slot.QualityProfileId);
                    var cutoff = profile.UpgradeAllowed ? profile.Cutoff : profile.FirststAllowedQuality().Id;

                    below = profile.Items.Take(profile.GetIndex(cutoff).Index)
                                         .SelectMany(i => i.GetQualities().Select(q => q.Id))
                                         .ToHashSet();
                    cutoffs[slot.QualityProfileId] = below;
                }

                return below.Contains(file.Quality.Quality.Id);
            });
        }

        // The monitored episodes of the monitored versions of monitored series for which the test (given the slot, the
        // episode and the version's file for it, if any) holds.
        private List<SeriesVersionWanted> GetWanted(ICollection<int> seriesIds, Func<SeriesVersionSlot, Episode, EpisodeFile, bool> wanted)
        {
            var slots = _slotRepository.All().Where(s => s.Monitored && (seriesIds == null || seriesIds.Contains(s.SeriesId))).ToList();
            var result = new List<SeriesVersionWanted>();

            if (slots.Empty())
            {
                return result;
            }

            var seriesById = _seriesService.GetSeries(slots.Select(s => s.SeriesId).Distinct()).Where(s => s.Monitored).ToDictionary(s => s.Id);

            foreach (var slot in slots.Where(s => seriesById.ContainsKey(s.SeriesId)))
            {
                var episodes = _episodeService.GetEpisodeBySeries(slot.SeriesId).Where(e => e.Monitored).ToList();
                var files = GetFiles(episodes.Select(e => e.Id).ToList(), slot.Id);

                var wantedEpisodes = episodes.Where(e => wanted(slot, e, files.GetValueOrDefault(e.Id))).ToList();

                if (wantedEpisodes.Empty())
                {
                    continue;
                }

                result.Add(new SeriesVersionWanted
                {
                    Series = BuildView(seriesById[slot.SeriesId], slot),
                    Episodes = GetEpisodeViews(wantedEpisodes, slot.Id)
                });
            }

            return result;
        }

        public SeriesVersionSlot Add(SeriesVersionSlot slot)
        {
            slot.Label = slot.Label?.Trim();

            Validate(slot);

            if (_slotRepository.GetBySeries(slot.SeriesId).Count + 1 >= MaxSlotsPerSeries)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "A series can have at most {0} versions, including the default one", MaxSlotsPerSeries);
            }

            slot.Added = DateTime.UtcNow;

            return _slotRepository.Insert(slot);
        }

        public SeriesVersionSlot Update(SeriesVersionSlot slot)
        {
            slot.Label = slot.Label?.Trim();

            var existing = _slotRepository.Get(slot.Id);

            // A slot never moves to another series.
            slot.SeriesId = existing.SeriesId;
            slot.Added = existing.Added;

            Validate(slot);

            return _slotRepository.Update(slot);
        }

        public void Delete(int id)
        {
            // Deleting a slot never deletes a file: the files of the version are removed first.
            var slot = _slotRepository.Get(id);

            if (_mediaFileService.GetFilesBySeries(slot.SeriesId).Any(f => f.VersionSlotId == id))
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "This version still has files; delete the files first");
            }

            _linkRepository.DeleteForSlots(new List<int> { id });
            _slotRepository.Delete(id);
        }

        // A file of an extra version is recorded for the episodes it covers.
        public void Handle(EpisodeFileAddedEvent message)
        {
            var file = message.EpisodeFile;

            if (file.VersionSlotId == SeriesVersionSlot.DefaultSlotId)
            {
                return;
            }

            foreach (var episode in file.Episodes.Value)
            {
                // The slot has one file per episode: an older one for the same episode is replaced.
                foreach (var old in _linkRepository.GetByEpisodes(new List<int> { episode.Id }).Where(l => l.VersionSlotId == file.VersionSlotId))
                {
                    _linkRepository.Delete(old);
                }

                _linkRepository.Insert(new EpisodeVersionFile { EpisodeId = episode.Id, VersionSlotId = file.VersionSlotId, EpisodeFileId = file.Id });
            }
        }

        public void Handle(EpisodeFileDeletedEvent message)
        {
            if (message.EpisodeFile.VersionSlotId != SeriesVersionSlot.DefaultSlotId)
            {
                _linkRepository.DeleteForFile(message.EpisodeFile.Id);
            }
        }

        public void HandleAsync(SeriesDeletedEvent message)
        {
            var slotIds = new List<int>();

            foreach (var series in message.Series)
            {
                slotIds.AddRange(_slotRepository.GetBySeries(series.Id).Select(s => s.Id));
            }

            _linkRepository.DeleteForSlots(slotIds);
            _slotRepository.DeleteForSeries(message.Series.Select(s => s.Id).ToList());
        }

        private Series BuildView(Series series, SeriesVersionSlot slot)
        {
            var view = series.ShallowCopy();

            view.Slot = slot;
            view.QualityProfileId = slot.QualityProfileId;
            view.QualityProfile = _qualityProfileService.Get(slot.QualityProfileId);
            view.Monitored = series.Monitored && slot.Monitored;

            return view;
        }

        private void Validate(SeriesVersionSlot slot)
        {
            if (slot.Label.IsNullOrWhiteSpace())
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "A version needs a label, for example 4K");
            }

            // The label goes into file names, so it can't hold a path separator.
            if (slot.Label.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "A version's label can't contain / \\ : * ? \" < > |");
            }

            if (!_qualityProfileService.Exists(slot.QualityProfileId))
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "The quality profile of a version has to exist");
            }

            if (_slotRepository.GetBySeries(slot.SeriesId).Any(s => s.Id != slot.Id && string.Equals(s.Label, slot.Label, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "This series already has a version called {0}", slot.Label);
            }
        }
    }
}
