using System;
using System.Data.Common;
using System.Data.SqlClient;
using MySqlConnector;

namespace GptBolDll
{
    public static class DatabaseConnectionTester
    {
        public static void Test(DatabaseProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (string.IsNullOrWhiteSpace(profile.Host))
                throw new InvalidOperationException("Informe o host do banco.");

            var type = profile.Type ?? "";
            if (IsSqlServer(type))
            {
                TestSqlServer(profile);
                return;
            }

            if (IsMySql(type))
            {
                TestMySql(profile);
                return;
            }

            if (IsPostgreSql(type))
            {
                TestExternalProvider(profile, new[] { "Npgsql.NpgsqlConnection, Npgsql" }, BuildPostgreSqlConnectionString(profile));
                return;
            }

            throw new NotSupportedException("Tipo de banco nao suportado para teste: " + type);
        }

        private static void TestSqlServer(DatabaseProfile profile)
        {
            using (var connection = new SqlConnection(BuildSqlServerConnectionString(profile)))
                connection.Open();
        }

        private static void TestMySql(DatabaseProfile profile)
        {
            using (var connection = new MySqlConnection(BuildMySqlConnectionString(profile)))
                connection.Open();
        }

        private static void TestExternalProvider(DatabaseProfile profile, string[] connectionTypeNames, string connectionString)
        {
            Type connectionType = null;
            foreach (var connectionTypeName in connectionTypeNames)
            {
                connectionType = Type.GetType(connectionTypeName, false);
                if (connectionType != null)
                    break;
            }

            if (connectionType == null)
                throw new InvalidOperationException(
                    "Driver de banco nao encontrado. Instale/disponibilize o provider correspondente no aplicativo para testar " +
                    (profile.Type ?? "este banco") + ".");

            using (var connection = (DbConnection)Activator.CreateInstance(connectionType))
            {
                connection.ConnectionString = connectionString;
                connection.Open();
            }
        }

        private static string BuildSqlServerConnectionString(DatabaseProfile profile)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = profile.Port > 0 ? profile.Host + "," + profile.Port : profile.Host,
                InitialCatalog = string.IsNullOrWhiteSpace(profile.DatabaseName) ? "master" : profile.DatabaseName,
                UserID = profile.User ?? "",
                Password = ResolvePassword(profile) ?? "",
                ConnectTimeout = Timeout(profile),
                Encrypt = profile.UseSsl,
                TrustServerCertificate = true
            };

            return builder.ConnectionString;
        }

        private static string BuildMySqlConnectionString(DatabaseProfile profile)
        {
            var builder = new DbConnectionStringBuilder
            {
                ["Server"] = profile.Host,
                ["Port"] = profile.Port > 0 ? profile.Port : 3306,
                ["Database"] = profile.DatabaseName ?? "",
                ["Uid"] = profile.User ?? "",
                ["Pwd"] = ResolvePassword(profile) ?? "",
                ["Connection Timeout"] = Timeout(profile),
                ["SslMode"] = profile.UseSsl ? "Required" : "None"
            };

            if (!string.IsNullOrWhiteSpace(profile.Charset))
                builder["CharSet"] = profile.Charset;

            return builder.ConnectionString;
        }

        private static string BuildPostgreSqlConnectionString(DatabaseProfile profile)
        {
            var builder = new DbConnectionStringBuilder
            {
                ["Host"] = profile.Host,
                ["Port"] = profile.Port > 0 ? profile.Port : 5432,
                ["Database"] = profile.DatabaseName ?? "",
                ["Username"] = profile.User ?? "",
                ["Password"] = ResolvePassword(profile) ?? "",
                ["Timeout"] = Timeout(profile),
                ["SSL Mode"] = profile.UseSsl ? "Require" : "Disable",
                ["Trust Server Certificate"] = true
            };

            return builder.ConnectionString;
        }

        private static int Timeout(DatabaseProfile profile)
        {
            return profile.TimeoutSeconds > 0 ? profile.TimeoutSeconds : 30;
        }

        private static string ResolvePassword(DatabaseProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(profile.PasswordEnv))
                return Environment.GetEnvironmentVariable(profile.PasswordEnv);

            return profile.Password;
        }

        private static bool IsSqlServer(string type)
        {
            return type.IndexOf("sql server", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("mssql", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMySql(string type)
        {
            return type.IndexOf("mysql", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("mariadb", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPostgreSql(string type)
        {
            return type.IndexOf("postgres", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
