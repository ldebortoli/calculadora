using System;
using System.Collections.Generic;

namespace Cashflow.Core.Models
{
    public sealed class CashflowScenario
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Mi circuito de cobro";
        public List<PlatformNode> Nodes { get; set; } = new List<PlatformNode>();
        public List<TransferRoute> Routes { get; set; } = new List<TransferRoute>();
    }
}
