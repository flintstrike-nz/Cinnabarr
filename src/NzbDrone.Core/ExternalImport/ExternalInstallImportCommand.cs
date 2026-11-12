using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.ExternalImport
{
    public class ExternalInstallImportCommand : Command
    {
        // "sonarr", "radarr" or "prowlarr".
        public string Kind { get; set; }
        public string Folder { get; set; }
        public string PathFrom { get; set; }
        public string PathTo { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsLongRunning => true;
        public override string CompletionMessage => "Import finished";
    }

    public class ExternalInstallImportExecutor : IExecute<ExternalInstallImportCommand>
    {
        private readonly IExternalInstallImporter _importer;

        public ExternalInstallImportExecutor(IExternalInstallImporter importer)
        {
            _importer = importer;
        }

        public void Execute(ExternalInstallImportCommand message)
        {
            _importer.Import(ExternalImportOptionsParser.Parse(message.Kind, message.Folder, message.PathFrom, message.PathTo));
        }
    }

    public static class ExternalImportOptionsParser
    {
        public static ExternalImportOptions Parse(string kind, string folder, string pathFrom, string pathTo)
        {
            if (!System.Enum.TryParse<ExternalInstallKind>(kind, true, out var parsed))
            {
                throw new System.ArgumentException("The kind has to be sonarr, radarr or prowlarr", nameof(kind));
            }

            return new ExternalImportOptions { Kind = parsed, Folder = folder, PathFrom = pathFrom, PathTo = pathTo };
        }
    }
}
