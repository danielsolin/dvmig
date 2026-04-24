using System.Globalization;
using System.Windows.Data;

namespace dvmig.App.Converters
{
    /// <summary>
    /// A WPF value converter that inverts a boolean value.
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        /// <summary>
        /// Inverts a boolean value.
        /// </summary>
        public object Convert(
            object? value, 
            Type targetType, 
            object? parameter, 
            CultureInfo culture)
        {
            if (value is bool b)
            {
                return !b;
            }

            return false;
        }

        /// <summary>
        /// Inverts a boolean value back to its original state.
        /// </summary>
        public object ConvertBack(
            object? value, 
            Type targetType, 
            object? parameter, 
            CultureInfo culture)
        {
            if (value is bool b)
            {
                return !b;
            }

            return false;
        }
    }
}
