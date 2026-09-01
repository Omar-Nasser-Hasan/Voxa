using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Voxa.Models;

namespace Voxa.Converters
{
    public class StatusToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush Success = new(Color.FromRgb(0x2E, 0xA0, 0x4A));
        private static readonly SolidColorBrush Failed = new(Color.FromRgb(0xD3, 0x2F, 0x2F));
        private static readonly SolidColorBrush Processing = new(Color.FromRgb(0x1E, 0x88, 0xE5));
        private static readonly SolidColorBrush Skipped = new(Color.FromRgb(0x9E, 0x9E, 0x9E));
        private static readonly SolidColorBrush Pending = new(Color.FromRgb(0x9A, 0x9F, 0xA8));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is ProcessingStatus status
                ? status switch
                {
                    ProcessingStatus.Success => Success,
                    ProcessingStatus.Failed => Failed,
                    ProcessingStatus.Processing => Processing,
                    ProcessingStatus.Skipped => Skipped,
                    _ => Pending
                }
                : Pending;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
