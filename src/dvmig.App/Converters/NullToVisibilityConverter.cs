using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace dvmig.App.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(
            object? value, 
            Type targetType, 
            object? parameter, 
            CultureInfo culture)
        {
            var isNull = value == null;
            var inverse = parameter as string == "Inverse";

            if (inverse)
            {
                return isNull ? Visibility.Visible : Visibility.Collapsed;
            }

            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(
            object? value, 
            Type targetType, 
            object? parameter, 
            CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
