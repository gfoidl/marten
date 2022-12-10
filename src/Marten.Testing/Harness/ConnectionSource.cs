using System;

namespace Marten.Testing.Harness;

public class ConnectionSource : ConnectionFactory
{
    // Keep the default timeout pretty short
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable("marten_testing_database")
                                                     ?? "Host=10.0.0.20;Port=5432;Database=test_db;Username=root;password=root;Command Timeout=5";

    static ConnectionSource()
    {
        if (ConnectionString.IsEmpty())
            throw new Exception(
                "You need to set the connection string for your local Postgresql database in the environment variable 'marten_testing_database'");
    }


    public ConnectionSource() : base(ConnectionString)
    {
    }
}
