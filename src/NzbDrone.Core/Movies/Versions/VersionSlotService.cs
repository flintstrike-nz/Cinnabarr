using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Movies.Versions
{
    public interface IVersionSlotService
    {
        // The extra slots of a movie, oldest first. The default slot is not among them.
        List<VersionSlot> GetByMovie(int movieId);
        VersionSlot Get(int id);

        // The movie as one of its version slots sees it: the slot's quality profile, monitored state (the movie's too)
        // and file, so the decision engine and the importer can work on a slot unchanged. The default slot is the movie
        // itself. A view is only for reading and deciding: never save it (it has the movie's id).
        Movie GetView(Movie movie, int slotId);

        // The monitored versions of monitored movies that have no file yet, and those whose file is still below their
        // profile's cutoff, as views, for the automatic searches. The movies' own (default) versions are not among them.
        List<Movie> GetMissingViews();
        List<Movie> GetCutoffUnmetViews();
        VersionSlot Add(VersionSlot slot);
        VersionSlot Update(VersionSlot slot);
        void Delete(int id);
    }

    public class VersionSlotService : IVersionSlotService, IHandleAsync<MoviesDeletedEvent>
    {
        // The default slot plus up to seven extra ones. A fixed limit until the owner picks one (decision 6).
        public const int MaxSlotsPerMovie = 8;

        private readonly IVersionSlotRepository _repository;
        private readonly IMovieFileService _movieFileService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly IMovieService _movieService;

        public VersionSlotService(IVersionSlotRepository repository, IMovieFileService movieFileService, IQualityProfileService qualityProfileService, IMovieService movieService)
        {
            _repository = repository;
            _movieFileService = movieFileService;
            _qualityProfileService = qualityProfileService;
            _movieService = movieService;
        }

        public List<VersionSlot> GetByMovie(int movieId)
        {
            return _repository.GetByMovie(movieId).OrderBy(s => s.Id).ToList();
        }

        public VersionSlot Get(int id)
        {
            return _repository.Get(id);
        }

        public Movie GetView(Movie movie, int slotId)
        {
            if (slotId == VersionSlot.DefaultSlotId)
            {
                return movie;
            }

            var slot = _repository.Get(slotId);

            if (slot.MovieId != movie.Id)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "This version belongs to another movie");
            }

            return BuildView(movie, slot, _movieFileService.GetFilesByMovie(movie.Id).FirstOrDefault(f => f.VersionSlotId == slotId));
        }

        private Movie BuildView(Movie movie, VersionSlot slot, MovieFile file)
        {
            var view = movie.ShallowCopy();

            view.Slot = slot;
            view.QualityProfileId = slot.QualityProfileId;
            view.QualityProfile = _qualityProfileService.Get(slot.QualityProfileId);
            view.Monitored = movie.Monitored && slot.Monitored;
            view.MovieFileId = file?.Id ?? 0;
            view.MovieFile = file;

            return view;
        }

        public List<Movie> GetMissingViews()
        {
            return GetViews((slot, file) => file == null);
        }

        public List<Movie> GetCutoffUnmetViews()
        {
            var cutoffs = new Dictionary<int, HashSet<int>>();

            return GetViews((slot, file) =>
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

        // The views of the monitored slots of monitored movies for which the test (given the slot and its file, if any) holds.
        private List<Movie> GetViews(Func<VersionSlot, MovieFile, bool> wanted)
        {
            var slots = _repository.All().Where(s => s.Monitored).ToList();

            if (slots.Empty())
            {
                return new List<Movie>();
            }

            var movies = _movieService.GetMovies(slots.Select(s => s.MovieId).Distinct()).Where(m => m.Monitored).ToDictionary(m => m.Id);
            var files = _movieFileService.GetFilesByMovies(movies.Keys).ToLookup(f => f.MovieId);

            return slots.Where(s => movies.ContainsKey(s.MovieId))
                        .Select(s => (Slot: s, File: files[s.MovieId].FirstOrDefault(f => f.VersionSlotId == s.Id)))
                        .Where(x => wanted(x.Slot, x.File))
                        .Select(x => BuildView(movies[x.Slot.MovieId], x.Slot, x.File))
                        .ToList();
        }

        public VersionSlot Add(VersionSlot slot)
        {
            slot.Label = slot.Label?.Trim();

            Validate(slot);

            if (_repository.GetByMovie(slot.MovieId).Count + 1 >= MaxSlotsPerMovie)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "A movie can have at most {0} versions, including the default one", MaxSlotsPerMovie);
            }

            slot.Added = DateTime.UtcNow;

            return _repository.Insert(slot);
        }

        public VersionSlot Update(VersionSlot slot)
        {
            slot.Label = slot.Label?.Trim();

            var existing = _repository.Get(slot.Id);

            // A slot never moves to another movie.
            slot.MovieId = existing.MovieId;
            slot.Added = existing.Added;

            Validate(slot);

            return _repository.Update(slot);
        }

        public void Delete(int id)
        {
            // Deleting a slot never deletes a file: the files of the version are removed first.
            if (_movieFileService.GetFilesByMovie(_repository.Get(id).MovieId).Any(f => f.VersionSlotId == id))
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "This version still has a file; delete the file first");
            }

            _repository.Delete(id);
        }

        public void HandleAsync(MoviesDeletedEvent message)
        {
            _repository.DeleteForMovies(message.Movies.Select(m => m.Id).ToList());
        }

        private void Validate(VersionSlot slot)
        {
            if (slot.Label.IsNullOrWhiteSpace())
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "A version needs a label, for example 4K");
            }

            // The label goes into file names, so it can't hold a path separator.
            if (slot.Label.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "A version label can't contain / \\ : * ? \" < > |");
            }

            if (_repository.GetByMovie(slot.MovieId).Any(s => s.Id != slot.Id && string.Equals(s.Label, slot.Label, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "This movie already has a version labelled {0}", slot.Label);
            }
        }
    }
}
