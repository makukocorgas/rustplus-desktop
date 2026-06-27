using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RustPlusDesk.Converters;

public class BoolToOnlineColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true
            ? new SolidColorBrush(Color.FromRgb(63, 185, 80))   // verde = online
            : new SolidColorBrush(Color.FromRgb(110, 118, 129)); // cinzento = offline

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

public class NullOrEmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}