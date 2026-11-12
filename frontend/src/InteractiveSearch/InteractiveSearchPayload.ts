// versionSlotId: search for one of the series' extra versions (0 or left out is the series' default version).
interface EpisodeSearchPayload {
  episodeId: number;
  versionSlotId?: number;
}

interface SeasonSearchPayload {
  seriesId: number;
  seasonNumber: number;
  versionSlotId?: number;
}

type InteractiveSearchPayload = EpisodeSearchPayload | SeasonSearchPayload;

export default InteractiveSearchPayload;
