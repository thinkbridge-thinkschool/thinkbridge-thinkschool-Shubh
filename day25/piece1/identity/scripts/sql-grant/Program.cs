using Microsoft.Data.SqlClient;

// One-time setup tool: connects to the Day 25 Azure SQL database as the Microsoft Entra
// admin (via "Active Directory Default" — resolves through az login locally) and creates a
// contained database user mapped to the App Service's managed identity, then grants it the
// minimum roles the app actually needs. Not part of the deployed app; run once by hand.
var server = args.Length > 0 ? args[0] : throw new ArgumentException("server required");
var database = args.Length > 1 ? args[1] : throw new ArgumentException("database required");
var appDisplayName = args.Length > 2 ? args[2] : throw new ArgumentException("managed identity display name required");

var connectionString =
    $"Server=tcp:{server},1433;Database={database};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;";

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
Console.WriteLine($"Connected to {database} on {server} as {connection.DataSource}.");

var statements = new[]
{
    $"IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '{appDisplayName}') CREATE USER [{appDisplayName}] FROM EXTERNAL PROVIDER;",
    $"ALTER ROLE db_datareader ADD MEMBER [{appDisplayName}];",
    $"ALTER ROLE db_datawriter ADD MEMBER [{appDisplayName}];"
};

foreach (var sql in statements)
{
    await using var cmd = new SqlCommand(sql, connection);
    await cmd.ExecuteNonQueryAsync();
    Console.WriteLine($"OK: {sql}");
}

Console.WriteLine("Done. Contained user created with db_datareader + db_datawriter only (no db_owner).");
