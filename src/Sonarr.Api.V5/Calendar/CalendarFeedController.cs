using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Tv;
using Sonarr.Http;

namespace Sonarr.Api.V5.Calendar;

[V5FeedController("calendar")]
public class CalendarFeedController : Controller
{
    private readonly IEpisodeService _episodeService;
    private readonly ISeriesService _seriesService;
    private readonly ITagService _tagService;
    private readonly IMovieService _movieService;

    public CalendarFeedController(IEpisodeService episodeService, ISeriesService seriesService, ITagService tagService, IMovieService movieService)
    {
        _movieService = movieService;
        _episodeService = episodeService;
        _seriesService = seriesService;
        _tagService = tagService;
    }

    [HttpGet("Sonarr.ics")]
    public Results<ContentHttpResult, NoContent> GetCalendarFeed(int pastDays = 7, int futureDays = 28, string tags = "", bool unmonitored = false, bool premieresOnly = false, bool asAllDay = false, bool includeSpecials = true)
    {
        var start = DateTime.Today.AddDays(-pastDays);
        var end = DateTime.Today.AddDays(futureDays);
        var parsedTags = new List<int>();

        if (tags.IsNotNullOrWhiteSpace())
        {
            parsedTags.AddRange(tags.Split(',').Select(_tagService.GetTag).Select(t => t.Id));
        }

        var episodes = _episodeService.EpisodesBetweenDates(start, end, unmonitored, includeSpecials);
        var allSeries = _seriesService.GetAllSeries();
        var calendar = new Ical.Net.Calendar
        {
            ProductId = "-//sonarr.tv//Sonarr//EN"
        };

        var calendarName = $"{BuildInfo.AppName} Schedule";
        calendar.AddProperty(new CalendarProperty("NAME", calendarName));
        calendar.AddProperty(new CalendarProperty("X-WR-CALNAME", calendarName));

        foreach (var episode in episodes.OrderBy(v => v.AirDateUtc!.Value))
        {
            var series = allSeries.SingleOrDefault(s => s.Id == episode.SeriesId);

            if (series == null)
            {
                continue;
            }

            if (premieresOnly && (episode.SeasonNumber == 0 || episode.EpisodeNumber != 1))
            {
                continue;
            }

            if (parsedTags.Any() && parsedTags.None(series.Tags.Contains))
            {
                continue;
            }

            var occurrence = calendar.Create<CalendarEvent>();
            occurrence.Uid = "NzbDrone_episode_" + episode.Id;
            occurrence.Status = episode.HasFile ? EventStatus.Confirmed : EventStatus.Tentative;
            occurrence.Description = episode.Overview;
            occurrence.Categories = new List<string>() { series.Network };

            if (asAllDay)
            {
                occurrence.Start = new CalDateTime(episode.AirDateUtc!.Value, false);
            }
            else
            {
                occurrence.Start = new CalDateTime(episode.AirDateUtc!.Value, true);
                occurrence.End = new CalDateTime(episode.AirDateUtc.Value.AddMinutes(series.Runtime), true);
            }

            switch (series.SeriesType)
            {
                case SeriesTypes.Daily:
                    occurrence.Summary = $"{series.Title} - {episode.Title}";
                    break;
                default:
                    occurrence.Summary = $"{series.Title} - {episode.SeasonNumber}x{episode.EpisodeNumber:00} - {episode.Title}";
                    break;
            }
        }

        // Movies: one all-day event per cinema, digital and physical release that falls inside the window.
        if (!premieresOnly)
        {
            foreach (var movie in _movieService.GetMoviesBetweenDates(start, end, unmonitored))
            {
                if (parsedTags.Any() && parsedTags.None(movie.Tags.Contains))
                {
                    continue;
                }

                var metadata = movie.MovieMetadata.Value;
                var releases = new[]
                {
                    ("cinemas", "In cinemas", metadata.InCinemas),
                    ("digital", "Digital release", metadata.DigitalRelease),
                    ("physical", "Physical release", metadata.PhysicalRelease)
                };

                foreach (var (key, label, date) in releases)
                {
                    if (!date.HasValue || date.Value < start || date.Value > end)
                    {
                        continue;
                    }

                    var occurrence = calendar.Create<CalendarEvent>();
                    occurrence.Uid = $"Embarr_movie_{movie.Id}_{key}";
                    occurrence.Status = movie.HasFile ? EventStatus.Confirmed : EventStatus.Tentative;
                    occurrence.Description = metadata.Overview;
                    occurrence.Summary = $"{metadata.Title} ({metadata.Year}) - {label}";
                    occurrence.Start = new CalDateTime(date.Value, false);
                }
            }
        }

        var serializer = new SerializerFactory().Build(calendar.GetType(), new SerializationContext()) as IStringSerializer;
        var icalendar = serializer?.SerializeToString(calendar);

        return icalendar is null ? TypedResults.NoContent() : TypedResults.Content(icalendar, "text/calendar");
    }
}
