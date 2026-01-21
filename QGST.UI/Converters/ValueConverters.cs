using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace QGST.UI.Converters;

/// <summary>
/// Converts vendor name to first initial for badge display
/// </summary>
public class VendorInitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var vendor = value?.ToString()?.ToUpperInvariant() ?? "";
        return vendor switch
        {
            "NVIDIA" => "N",
            "AMD" => "A",
            "INTEL" => "I",
            _ => "G"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts vendor name to brand color
/// </summary>
public class VendorColorConverter : IValueConverter
{
    private static readonly SolidColorBrush NvidiaGreen = new(Color.FromRgb(118, 185, 0));
    private static readonly SolidColorBrush AmdRed = new(Color.FromRgb(237, 28, 36));
    private static readonly SolidColorBrush IntelBlue = new(Color.FromRgb(0, 113, 197));
    private static readonly SolidColorBrush DefaultGray = new(Color.FromRgb(160, 160, 160));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var vendor = value?.ToString()?.ToUpperInvariant() ?? "";
        return vendor switch
        {
            "NVIDIA" => NvidiaGreen,
            "AMD" => AmdRed,
            "INTEL" => IntelBlue,
            _ => DefaultGray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts IsIntegrated to type label
/// </summary>
public class GpuTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isIntegrated)
        {
            return isIntegrated ? "iGPU" : "dGPU";
        }
        return "GPU";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts IsIntegrated to background color
/// </summary>
public class GpuTypeBgConverter : IValueConverter
{
    private static readonly SolidColorBrush IntegratedBg = new(Color.FromRgb(45, 45, 45));
    private static readonly SolidColorBrush DiscreteBg = new(Color.FromRgb(16, 124, 16));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isIntegrated)
        {
            return isIntegrated ? IntegratedBg : DiscreteBg;
        }
        return IntegratedBg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts IsIntegrated to foreground color
/// </summary>
public class GpuTypeFgConverter : IValueConverter
{
    private static readonly SolidColorBrush IntegratedFg = new(Color.FromRgb(160, 160, 160));
    private static readonly SolidColorBrush DiscreteFg = new(Color.FromRgb(255, 255, 255));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isIntegrated)
        {
            return isIntegrated ? IntegratedFg : DiscreteFg;
        }
        return IntegratedFg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts VRAM bytes to GB string
/// </summary>
public class VramToGbConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ulong bytes)
        {
            var gb = Math.Round(bytes / (1024.0 * 1024.0 * 1024.0), 1);
            return $"{gb} GB";
        }
        return "? GB";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Boolean to Visibility converter
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            bool invert = parameter?.ToString() == "Invert";
            return (b ^ invert) ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
