using System;
using System.Globalization;
using System.Windows.Data;

namespace radians.beamlab.app;

/// <summary>Boolean value converter that returns the logical negation (for IsEnabled bindings).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
