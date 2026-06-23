using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Resumenes.Ui.ViewModels;

/// <summary>
/// Convierte bool? a bool para RadioButton: Convert recibe el valor del VM (Vf),
/// ConverterParameter es el valor esperado ("True" o "False").
/// Permite RadioButton bindeados a la misma propiedad nullable.
/// </summary>
[ValueConversion(typeof(bool?), typeof(bool))]
public sealed class NullableBoolToCheckedConverter : IValueConverter
{
    public static readonly NullableBoolToCheckedConverter Instancia = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && parameter is string s && bool.TryParse(s, out var expected))
            return b == expected;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string s && bool.TryParse(s, out var v)) return (bool?)v;
        return Binding.DoNothing;
    }
}

/// <summary>
/// Convierte bool a Visibility de forma inversa: true → Collapsed, false → Visible.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instancia = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>
/// Convierte una fracción 0.0–1.0 a un porcentaje 0–100 para ui:ProgressRing.Progress.
/// </summary>
[ValueConversion(typeof(double), typeof(double))]
public sealed class FraccionAPorcentajeConverter : IValueConverter
{
    public static readonly FraccionAPorcentajeConverter Instancia = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? d * 100.0 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? d / 100.0 : 0.0;
}

/// <summary>
/// Convierte un string a Visibility: Visible si no es vacío, Collapsed si es vacío/null.
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class StringToVisibilityConverter : IValueConverter
{
    public static readonly StringToVisibilityConverter Instancia = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
