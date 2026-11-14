namespace NzbDrone.Core.RootFolders
{
    /// <summary>What a root folder holds. Any is a root folder nothing has been decided for: it takes every kind of library.</summary>
    public enum LibraryType
    {
        Any = 0,
        Series = 1,
        Movies = 2
    }
}
