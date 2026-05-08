namespace FootballFormation.Core.Data;

public static class DatabasePathHelper
{
    public static string GetDatabasePath()
    {
        string dbFolder;

        if (Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") != null)
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
