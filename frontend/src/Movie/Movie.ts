import ModelBase from 'App/ModelBase';

export interface MovieImage {
  coverType: string;
  url?: string;
  remoteUrl?: string;
}

export type MovieStatus =
  | 'deleted'
  | 'tba'
  | 'announced'
  | 'inCinemas'
  | 'released';

interface Movie extends ModelBase {
  title: string;
  originalTitle?: string;
  sortTitle?: string;
  year: number;
  tmdbId: number;
  imdbId?: string;
  overview?: string;
  status: MovieStatus;
  images: MovieImage[];
  genres: string[];
  runtime: number;
  certification?: string;
  inCinemas?: string;
  physicalRelease?: string;
  digitalRelease?: string;
  studio?: string;
  path: string;
  rootFolderPath?: string;
  qualityProfileId: number;
  monitored: boolean;
  minimumAvailability: MovieStatus;
  tags: number[];
  added: string;
  hasFile: boolean;
  movieFileId: number;
  sizeOnDisk: number;
  isAvailable: boolean;
}

export default Movie;
