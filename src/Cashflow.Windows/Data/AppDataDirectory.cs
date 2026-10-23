using System;

namespace Cashflow.Windows.Data;

internal static class AppDataDirectory
{
    internal const string EnvironmentVariable = "CASHFLOW_DATA_DIRECTORY";

    internal static string Path => Environment.GetEnvironmentVariable(EnvironmentVariable)
        ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RutaCashflow");
}
