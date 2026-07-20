namespace FootballFormation.Core.Data;

public static class DatabasePathHelper
{
    public static string GetDatabasePath()
    {
        string dbFolder;

        if (Environment.GetEnvironmentVariable("APP_DATA_DIR") is { Length: > 0 } appDataDir)
        {
            // Explicit override — the persistent volume when hosted (e.g. /data on Fly.io)
            dbFolder = appDataDir;
        }
        else if (Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") != null)
        {
            // Running on Azure App Service
            dbFolder = Path.Combine("/home", "data");
        }
        else
        {
            // Running locally
            dbFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FootballFormation");
        }

        Directory.CreateDirectory(dbFolder);
        return Path.Combine(dbFolder, "footballformation.db");
    }
}
