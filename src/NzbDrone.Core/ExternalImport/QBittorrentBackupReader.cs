using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonoTorrent.BEncoding;
using NLog;

namespace NzbDrone.Core.ExternalImport
{
    public class ExternalTorrent
    {
        public string Hash { get; set; }
        public string Name { get; set; }
        public string SavePath { get; set; }
        public bool Stopped { get; set; }
        public string TorrentFile { get; set; }
    }

    public interface IQBittorrentBackupReader
    {
        // The torrents in a qBittorrent profile: the folder that holds BT_backup, or BT_backup itself.
        List<ExternalTorrent> Read(string folder);
    }

    /// <summary>Reads the torrents of another qBittorrent from its BT_backup folder (a .torrent and a .fastresume file for each).</summary>
    public class QBittorrentBackupReader : IQBittorrentBackupReader
    {
        private readonly Logger _logger;

        public QBittorrentBackupReader(Logger logger)
        {
            _logger = logger;
        }

        public List<ExternalTorrent> Read(string folder)
        {
            var backup = FindBackupFolder(folder);
            var torrents = new List<ExternalTorrent>();

            foreach (var resumeFile in Directory.GetFiles(backup, "*.fastresume").OrderBy(f => f, StringComparer.Ordinal))
            {
                var torrentFile = Path.ChangeExtension(resumeFile, ".torrent");

                if (!File.Exists(torrentFile))
                {
                    continue;
                }

                try
                {
                    var resume = (BEncodedDictionary)BEncodedValue.Decode(File.ReadAllBytes(resumeFile));

                    var savePath = GetString(resume, "qBt-savePath") ?? GetString(resume, "save_path");

                    if (string.IsNullOrEmpty(savePath))
                    {
                        continue;
                    }

                    torrents.Add(new ExternalTorrent
                    {
                        Hash = Path.GetFileNameWithoutExtension(resumeFile).ToUpperInvariant(),
                        Name = GetString(resume, "qBt-name") is { Length: > 0 } name ? name : ReadName(torrentFile),
                        SavePath = savePath,

                        // Stopped by the user (a stopped torrent is neither running nor queued).
                        Stopped = resume.TryGetValue("paused", out var paused) && paused is BEncodedNumber { Number: not 0 } &&
                                  !(resume.TryGetValue("auto_managed", out var auto) && auto is BEncodedNumber { Number: not 0 }),
                        TorrentFile = torrentFile
                    });
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to read '{0}'", resumeFile);
                }
            }

            return torrents;
        }

        private static string FindBackupFolder(string folder)
        {
            foreach (var candidate in new[] { folder, Path.Combine(folder ?? string.Empty, "BT_backup"), Path.Combine(folder ?? string.Empty, "data", "BT_backup"), Path.Combine(folder ?? string.Empty, "qBittorrent", "data", "BT_backup") })
            {
                if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.fastresume").Any())
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("There is no BT_backup folder with torrents in it there");
        }

        private static string GetString(BEncodedDictionary dictionary, string key)
        {
            return dictionary.TryGetValue(key, out var value) && value is BEncodedString text ? text.Text : null;
        }

        private static string ReadName(string torrentFile)
        {
            var metaInfo = (BEncodedDictionary)BEncodedValue.Decode(File.ReadAllBytes(torrentFile));

            return metaInfo.TryGetValue("info", out var info) && info is BEncodedDictionary infoDictionary && GetString(infoDictionary, "name") is { } name
                ? name
                : Path.GetFileNameWithoutExtension(torrentFile);
        }
    }
}
