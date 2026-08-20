using System;
using System.IO;
using System.Text.Json;

namespace Cashflow.Windows.Data
{
    public sealed class ScenarioStore
    {
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public string FilePath { get; }

        public ScenarioStore()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RutaCashflow");
            FilePath = Path.Combine(folder, "scenarios.json");
        }

        public ScenarioDocument Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var document = JsonSerializer.Deserialize<ScenarioDocument>(json, _options);
                    if (document != null && document.Scenarios.Count > 0)
                    {
                        document.MusicSession ??= new MusicSessionSettings();
                        document.Retirement ??= new RetirementSettings();
                        document.ManualExchangeRates ??= new System.Collections.Generic.List<ManualExchangeRateSetting>();
                        var retirementMoneyUpgraded = document.Retirement.MigrateLegacyMoneyToCents();
                        var retirementPlanningUpgraded = document.Retirement.EnsurePlanningCollections();
                        if (StarterScenarioFactory.UpgradeStarterTemplates(document) ||
                            ManualExchangeRateSynchronizer.EnsureSynchronized(document) ||
                            retirementMoneyUpgraded ||
                            retirementPlanningUpgraded)
                        {
                            TrySaveUpgrade(document);
                        }

                        return document;
                    }
                }
            }
            catch (JsonException)
            {
                BackupUnreadableFile();
            }
            catch (IOException)
            {
                // Se conserva una sesion util en memoria aun si el disco no esta disponible.
            }

            return StarterScenarioFactory.CreateStarterDocument();
        }

        public void Save(ScenarioDocument document)
        {
            ManualExchangeRateSynchronizer.EnsureSynchronized(document);
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, _options));
            File.Move(temporaryPath, FilePath, true);
        }

        private void TrySaveUpgrade(ScenarioDocument document)
        {
            try
            {
                Save(document);
            }
            catch (IOException)
            {
                // La plantilla actualizada sigue disponible en memoria aunque no se pueda persistir todavía.
            }
            catch (UnauthorizedAccessException)
            {
                // La interfaz informará cualquier problema de guardado cuando el usuario aplique cambios.
            }
        }

        private void BackupUnreadableFile()
        {
            try
            {
                var backup = FilePath + ".invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(FilePath, backup);
            }
            catch (IOException)
            {
                // No se reemplaza ni elimina un archivo que no se pudo respaldar.
            }
        }
    }
}
