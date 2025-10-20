using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Google_Bookmarks_Manager_for_GPOs
{
    // Shows Visible when the bound boolean is true, otherwise Collapsed.
    public class TrueToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
