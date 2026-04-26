using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace dvmig.App.Converters
{
    /// <summary>
    /// A WPF value converter that maps null values to <see cref="Visibility"/>.
    /// Supports an "Inverse" parameter to show elements when value is null.
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// Converts a null value to a Visibility state.
        /// </summary>
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

        /// <summary>
        /// Not implemented.
        /// </summary>
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
