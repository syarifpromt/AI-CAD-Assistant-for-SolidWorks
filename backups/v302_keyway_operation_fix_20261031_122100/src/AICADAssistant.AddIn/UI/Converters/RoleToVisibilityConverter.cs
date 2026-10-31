using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AICADAssistant.Core.Models;

namespace AICADAssistant.AddIn.UI.Converters
{
    public class RoleToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MessageRole)
            {
                MessageRole role = (MessageRole)value;
                string target = parameter != null ? parameter.ToString() : "User";

                if (string.Equals(target, "User", StringComparison.OrdinalIgnoreCase))
                {
                    return role == MessageRole.User ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    return (role == MessageRole.Assistant || role == MessageRole.System) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

