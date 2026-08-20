using System;
using System.Linq;
using Cashflow.Core.Models;

namespace Cashflow.Windows.Data
{
    public static class ScenarioDocumentEditor
    {
        public static bool TryDeleteScenario(ScenarioDocument document, string scenarioId, out CashflowScenario? nextScenario)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            nextScenario = null;
            if (document.Scenarios.Count <= 1)
            {
                return false;
            }

            var index = document.Scenarios.FindIndex(scenario => scenario.Id == scenarioId);
            if (index < 0)
            {
                return false;
            }

            document.Scenarios.RemoveAt(index);
            nextScenario = document.Scenarios[Math.Min(index, document.Scenarios.Count - 1)];
            document.ActiveScenarioId = nextScenario.Id;
            return true;
        }
    }
}
