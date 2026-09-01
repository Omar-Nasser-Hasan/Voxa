using System;
using System.Globalization;
using System.Windows.Data;

namespace Voxa.Converters
{
    /// <summary>Converts a 0-100 ProgressBar value into a 0-1 ScaleTransform factor.</summary>
    public class PercentToScaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var d = value is double dv ? dv : 0d;
            if (double.IsNaN(d) || double.IsInfinity(d)) d = 0;
            return Math.Max(0, Math.Min(1, d / 100.0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
