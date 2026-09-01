using System;
using System.Globalization;
using System.Windows.Data;

namespace Voxa.Converters
{
    /// <summary>
    /// Converts a single normalized waveform peak (0-1) into a pixel height for the
    /// little bar that represents it. ConverterParameter is the max bar height available
    /// (e.g. "64"). A small minimum height is enforced so near-silent sections still
    /// render as a thin visible line rather than disappearing entirely.
    /// </summary>
    public class PeakToHeightConverter : IValueConverter
    {
        private const double MinHeight = 2.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var peak = value is float f ? f : value is double d ? d : 0.0;
            if (double.IsNaN(peak) || double.IsInfinity(peak)) peak = 0;
            peak = Math.Max(0, Math.Min(1, peak));

            var maxHeight = 64.0;
            if (parameter != null && double.TryParse(
                    parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                maxHeight = parsed;
            }

            return Math.Max(MinHeight, peak * maxHeight);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
