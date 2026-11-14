using System.Data;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // A root folder holds one kind of library: series or movies (0 is "any", for a root folder nothing has been decided
    // for). Existing root folders that hold only series, or only movies, get that kind so they stay as they are used.
    [Migration(255)]
    public class root_folder_library : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("RootFolders").AddColumn("Library").AsInt32().NotNullable().WithDefaultValue(0);

            Execute.WithConnection(AssignLibraries);
        }

        private static void AssignLibraries(IDbConnection conn, IDbTransaction tran)
        {
            var roots = new System.Collections.Generic.List<(int Id, string Path)>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tran;
                cmd.CommandText = "SELECT \"Id\", \"Path\" FROM \"RootFolders\"";

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    roots.Add((reader.GetInt32(0), reader.GetString(1)));
                }
            }

            foreach (var (id, path) in roots)
            {
                var series = CountUnder(conn, tran, "Series", path);
                var movies = CountUnder(conn, tran, "Movies", path);
                var library = series > 0 && movies == 0 ? 1 : movies > 0 && series == 0 ? 2 : 0;

                if (library == 0)
                {
                    continue;
                }

                using var update = conn.CreateCommand();
                update.Transaction = tran;
                update.CommandText = $"UPDATE \"RootFolders\" SET \"Library\" = {library} WHERE \"Id\" = {id}";
                update.ExecuteNonQuery();
            }
        }

        private static long CountUnder(IDbConnection conn, IDbTransaction tran, string table, string root)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tran;
            cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE \"Path\" LIKE @root";

            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "@root";
            parameter.Value = root.TrimEnd('/', '\\') + "%";
            cmd.Parameters.Add(parameter);

            return (long)cmd.ExecuteScalar();
        }
    }
}
