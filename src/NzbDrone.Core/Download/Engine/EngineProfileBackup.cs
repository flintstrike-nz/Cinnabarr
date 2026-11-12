using System;
using System.IO;
using System.IO.Compression;
using NLog;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineProfileBackup
    {
        // Zips the engine profile (its settings, resume data and Cinnabarr's link records) into the file; false when there
        // is no profile yet. The API key and process id are left out, and so are the logs and cache.
        bool CreateArchive(string zipPath);

        // Unpacks an archive next to the live profile, to be swapped in the next time the engine starts.
        void StageRestore(string zipPath);

        // Called before the engine starts: puts a staged profile in place, keeping the old one beside it.
        void ApplyStagedRestore();
    }

    // The engine's profile is part of a backup (roadmap MIG-06): without its resume data a restored install would have to
    // check or re-download every torrent it is still seeding.
    public class EngineProfileBackup : IEngineProfileBackup
    {
        private static readonly string[] SkippedFiles = { "api.key", "engine.pid" };
        private static readonly string[] SkippedFolders = { "logs", "cache" };

        private readonly IEngineClientRegistry _engineClientRegistry;
        private readonly Logger _logger;

        public EngineProfileBackup(IEngineClientRegistry engineClientRegistry, Logger logger)
        {
            _engineClientRegistry = engineClientRegistry;
            _logger = logger;
        }

        private string ProfileFolder => _engineClientRegistry.EngineFolder;
        private string StagedFolder => ProfileFolder + ".restore";
        private string PreviousFolder => ProfileFolder + ".pre-restore";

        public bool CreateArchive(string zipPath)
        {
            if (!Directory.Exists(ProfileFolder))
            {
                return false;
            }

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            foreach (var file in Directory.EnumerateFiles(ProfileFolder, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(ProfileFolder, file);

                if (IsSkipped(relative))
                {
                    continue;
                }

                try
                {
                    // The engine may be writing; share the file for reading rather than failing the backup.
                    using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var entry = archive.CreateEntry(relative.Replace('\\', '/'), CompressionLevel.Optimal);
                    using var target = entry.Open();
                    source.CopyTo(target);
                }
                catch (IOException ex)
                {
                    _logger.Warn(ex, "Unable to back up engine file {0}", relative);
                }
            }

            return true;
        }

        public void StageRestore(string zipPath)
        {
            if (Directory.Exists(StagedFolder))
            {
                Directory.Delete(StagedFolder, true);
            }

            ZipFile.ExtractToDirectory(zipPath, StagedFolder);

            _logger.Info("The engine profile from the backup will be put in place the next time the engine starts");
        }

        public void ApplyStagedRestore()
        {
            if (!Directory.Exists(StagedFolder))
            {
                return;
            }

            try
            {
                if (Directory.Exists(PreviousFolder))
                {
                    Directory.Delete(PreviousFolder, true);
                }

                if (Directory.Exists(ProfileFolder))
                {
                    Directory.Move(ProfileFolder, PreviousFolder);

                    // The key belongs to this install, not to the backup.
                    var key = Path.Combine(PreviousFolder, "api.key");

                    if (File.Exists(key))
                    {
                        Directory.CreateDirectory(StagedFolder);
                        File.Copy(key, Path.Combine(StagedFolder, "api.key"), true);
                    }
                }

                Directory.Move(StagedFolder, ProfileFolder);

                _logger.Info("Restored the engine profile from the backup; the previous one is kept in '{0}'", PreviousFolder);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to restore the engine profile from the backup; the engine starts with its current profile");
            }
        }

        private static bool IsSkipped(string relativePath)
        {
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return Array.Exists(SkippedFiles, f => string.Equals(parts[^1], f, StringComparison.OrdinalIgnoreCase)) ||
                   Array.Exists(parts[..^1], p => Array.Exists(SkippedFolders, f => string.Equals(p, f, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
