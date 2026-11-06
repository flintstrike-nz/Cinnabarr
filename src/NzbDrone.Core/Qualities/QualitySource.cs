namespace NzbDrone.Core.Qualities
{
    public enum QualitySource
    {
        Unknown,
        Television,
        TelevisionRaw,
        Web,
        WebRip,
        DVD,
        Bluray,
        BlurayRaw,

        // Movie-only sources. Added after the series ones: the numbers are saved in custom formats.
        Cam,
        Telesync,
        Telecine,
        Workprint,
        DvdScreener,
        DvdRegional,
        DvdRaw,
        BlurayDisk
    }
}
