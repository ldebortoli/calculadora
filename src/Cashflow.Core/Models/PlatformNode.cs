using System;

namespace Cashflow.Core.Models
{
    public sealed class PlatformNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Nueva plataforma";
        public string Currency { get; set; } = "USD";
        public NodeKind Kind { get; set; } = NodeKind.Intermediate;
        public double X { get; set; }
        public double Y { get; set; }

        public override string ToString() => $"{Name} · {Currency}";
    }
}
