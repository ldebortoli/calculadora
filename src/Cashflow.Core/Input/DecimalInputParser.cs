using System.Globalization;

namespace Cashflow.Core.Input
{
    public static class DecimalInputParser
    {
        public static bool TryParse(string value, out decimal result)
        {
            var normalized = value.Trim().Replace(" ", string.Empty).Replace("\u00A0", string.Empty);
            var comma = normalized.LastIndexOf(',');
            var dot = normalized.LastIndexOf('.');

            if (comma >= 0 && dot >= 0)
            {
                if (comma > dot)
                {
                    normalized = normalized.Replace(".", string.Empty).Replace(',', '.');
                }
                else
                {
                    normalized = normalized.Replace(",", string.Empty);
                }
            }
            else if (comma >= 0)
            {
                normalized = normalized.Replace(',', '.');
            }

            const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
            return decimal.TryParse(normalized, styles, CultureInfo.InvariantCulture, out result);
        }
    }
}
