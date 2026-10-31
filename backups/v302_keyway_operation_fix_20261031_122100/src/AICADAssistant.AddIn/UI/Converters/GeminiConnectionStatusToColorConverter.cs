using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AICADAssistant.Core.Services;

namespace AICADAssistant.AddIn.UI.Converters
{
    public class GeminiConnectionStatusToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush GreenBrush = new SolidColorBrush(Color.FromRgb(40, 167, 69));      // #28A745 (Connected)
        private static readonly SolidColorBrush BlueBrush = new SolidColorBrush(Color.FromRgb(0, 122, 255));       // #007AFF (Connecting)
        private static readonly SolidColorBrush AmberBrush = new SolidColorBrush(Color.FromRgb(245, 166, 35));     // #F5A623 (ConfiguredUnverified)
        private static readonly SolidColorBrush RedBrush = new SolidColorBrush(Color.FromRgb(217, 48, 37));        // #D93025 (Auth/Rate/Error)
        private static readonly SolidColorBrush GrayBrush = new SolidColorBrush(Color.FromRgb(158, 158, 158));     // #9E9E9E (Unconfigured)

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is GeminiConnectionStatus))
                return GrayBrush;

            var status = (GeminiConnectionStatus)value;
            switch (status)
            {
                case GeminiConnectionStatus.Connected:
                    return GreenBrush;

                case GeminiConnectionStatus.Connecting:
                    return BlueBrush;

                case GeminiConnectionStatus.ConfiguredUnverified:
                    return AmberBrush;

                case GeminiConnectionStatus.AuthenticationFailed:
                case GeminiConnectionStatus.RateLimited:
                case GeminiConnectionStatus.ModelUnavailable:
                case GeminiConnectionStatus.NetworkError:
                case GeminiConnectionStatus.ServiceUnavailable:
                    return RedBrush;

                case GeminiConnectionStatus.Unconfigured:
                default:
                    return GrayBrush;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

