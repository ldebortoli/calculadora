using System;
using System.IO;
using Cashflow.Windows.Data;
using Cashflow.Windows.Localization;

namespace Cashflow.Windows.Tests;

internal static class TestDataIsolation
{
    internal static void Configure()
    {
        const string variable = "CASHFLOW_DATA_DIRECTORY";
        // Inspect default paths without reading or writing the user's files.
        Environment.SetEnvironmentVariable(variable, null);
        var defaults = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RutaCashflow");
        Check(new ScenarioStore().FilePath == Path.Combine(defaults, "scenarios.json"));
        Check(new LanguagePreferenceStore().FilePath == Path.Combine(defaults, "language.json"));
        var isolated = Path.Combine(Path.GetTempPath(), "cashflow-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolated);
        Environment.SetEnvironmentVariable(variable, isolated);
        Check(new ScenarioStore().FilePath == Path.Combine(isolated, "scenarios.json"));
        Check(new LanguagePreferenceStore().FilePath == Path.Combine(isolated, "language.json"));
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Test data paths are not isolated.");
    }
}
