using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AICADAssistant.AddIn.UI.Converters
{
    public enum OperationStatusType
    {
        Ready,
        Analyzing,
        WaitingClarification,
        Planning,
        Validating,
        BuildingModel,
        Rebuilding,
        Success,
        Warning,
        Failed,
        Cancelled
    }

    public class StatusToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush GreenBrush = new SolidColorBrush(Color.FromRgb(40, 167, 69));
        private static readonly SolidColorBrush RedBrush = new SolidColorBrush(Color.FromRgb(204, 0, 0));
        private static readonly SolidColorBrush BlueBrush = new SolidColorBrush(Color.FromRgb(0, 102, 204));
        private static readonly SolidColorBrush OrangeBrush = new SolidColorBrush(Color.FromRgb(255, 136, 0));
        private static readonly SolidColorBrush GrayBrush = new SolidColorBrush(Color.FromRgb(108, 117, 125));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is OperationStatusType))
                return GrayBrush;

            var status = (OperationStatusType)value;
            switch (status)
            {
                case OperationStatusType.Success:
                    return GreenBrush;

                case OperationStatusType.Failed:
                    return RedBrush;

                case OperationStatusType.Warning:
                case OperationStatusType.WaitingClarification:
                    return OrangeBrush;

                case OperationStatusType.Analyzing:
                case OperationStatusType.Planning:
                case OperationStatusType.Validating:
                case OperationStatusType.BuildingModel:
                case OperationStatusType.Rebuilding:
                    return BlueBrush;

                case OperationStatusType.Ready:
                case OperationStatusType.Cancelled:
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
