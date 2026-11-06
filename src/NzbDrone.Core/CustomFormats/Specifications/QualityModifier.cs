namespace NzbDrone.Core.CustomFormats
{
    // The same values Radarr saves in its custom formats, so formats can be shared between the two.
    public enum QualityModifier
    {
        None = 0,
        Regional,
        Screener,
        RawHd,
        BrDisk,
        Remux
    }
}
