using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace SmwController.ViewModels;

/// <summary>Returns the directory portion of an instrument file path (e.g. /var/user/subdir).</summary>
[ValueConversion(typeof(string), typeof(string))]
public sealed class PathDirectoryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path) return string.Empty;
        var dir = path.Contains('/') ? path[..path.LastIndexOf('/')] : string.Empty;
        return dir;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns just the filename portion of an instrument file path.</summary>
[ValueConversion(typeof(string), typeof(string))]
public sealed class PathFilenameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path) return string.Empty;
        return path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
