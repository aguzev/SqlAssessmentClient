using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Assessment.Configuration;

namespace AssessmentClient.SMO
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.SqlServer.Management.Assessment;
    using Microsoft.SqlServer.Management.Assessment.Checks;

    public static class Program
    {
        private static async Task Main(string[] args)
        {
            // Create a SQL Assessment engine
            //
            // It will load the default ruleset,
            // then it will dispatch assessment requests
            var engine = new Engine();

            // Connect to a server
            string connectionString = GetConnectionString(args);
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);

                try
                {
                    ISqlObjectLocator target = await GetInstance(connection).ConfigureAwait(false);

                    // Get a list of available SQL Assessment checks
                    IEnumerable<ICheck> checklist = engine.GetChecks(target);


                    // Checks are tagged with strings corresponding to
                    // categories like "Performance", "Storage", or "Security"
                    var allTags = new SortedSet<string>(checklist.SelectMany(c => c.Tags));

                    DisplayCategories(target.Name, allTags);

                    if (Prompt(out string? line))
                    {
                        // Run assessment
                        List<IAssessmentResult> assessmentResults = string.IsNullOrWhiteSpace(line)
                            ? await engine.GetAssessmentResultsList(target).ConfigureAwait(false) // all checks
                            : await engine.GetAssessmentResultsList(target, line.Split())
                                .ConfigureAwait(false); // selected checks

                        DisplayAssessmentResults(assessmentResults);
                    }
                }
                finally
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
        }

        private static string GetConnectionString(string[] args)
        {
            var sb = new SqlConnectionStringBuilder
            {
                Authentication = SqlAuthenticationMethod.ActiveDirectoryIntegrated,
                DataSource = "."
            };

            return sb.ToString();
        }

        private const string Query =
            @"SELECT SERVERPROPERTY('ProductVersion') AS version,
                SERVERPROPERTY('EngineEdition') AS edition,
                SERVERPROPERTY('ServerName') AS name,
                host_platform AS platform
              FROM sys.dm_os_host_info";

        private static async Task<SqlObjectLocator> GetInstance(SqlConnection connection)
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = Query;
                var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                try
                {
                    await reader.ReadAsync().ConfigureAwait(false);
                    string name = (string) reader["name"];
                    return new SqlObjectLocator
                    {
                        Connection = connection,
                        EngineEdition = TranslateEdition((int) reader["edition"]),
                        Name = name,
                        Platform = (string) reader["platform"],
                        ServerName = name,
                        Type = SqlObjectType.Server,
                        Urn = name,
                        Version = Version.Parse((string) reader["version"])
                    };
                }
                finally
                {
                    await reader.CloseAsync().ConfigureAwait(false);
                }
            }
        }

        private static SqlEngineEdition TranslateEdition(int i)
        {
            switch (i)
            {
                case 1:
                    return SqlEngineEdition.PersonalOrDesktopEngine;
                case 2:
                    return SqlEngineEdition.Standard;
                case 3:
                    return SqlEngineEdition.Enterprise;
                case 4:
                    return SqlEngineEdition.Express;
                case 5:
                    return SqlEngineEdition.AzureDatabase;
                case 6:
                    return SqlEngineEdition.DataWarehouse;
                case 7:
                    return SqlEngineEdition.StretchDatabase;
                case 8:
                    return SqlEngineEdition.ManagedInstance;
                default:
                    throw new ArgumentException(nameof(i));
            }
        }

        private static void DisplayAssessmentResults(List<IAssessmentResult> assessmentResults)
        {
            // Properties of IAssessmentResult provide
            // recommendation text, help link, etc
            foreach (var result in assessmentResults)
            {
                Console.WriteLine("-------");
                Console.Write("  ");
                Console.WriteLine(result.Message);
                Console.Write("  ");
                Console.WriteLine(result.Check.HelpLink);
            }
        }

        private static bool Prompt(out string? line)
        {
            Console.Write("Enter category (ENTER for all categories, 'exit' to leave) > ");
            line = Console.ReadLine();

            return string.Compare(line, "exit", StringComparison.OrdinalIgnoreCase) != 0;
        }

        private static void DisplayCategories(string targetName, IEnumerable<string> allTags)
        {
            Console.WriteLine($"All categories available for {targetName}:\n");

            foreach (var tag in allTags)
            {
                Console.WriteLine($"  {tag}");
            }
        }
    }
}
