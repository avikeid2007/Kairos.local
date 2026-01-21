using KaiROS.Mobile.Models;
using System.Globalization;

namespace KaiROS.Mobile.Converters;

/// <summary>
/// Converts ChatRole to background color for message bubbles.
/// </summary>
public class RoleToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ChatRole role)
        {
            return role switch
            {
                ChatRole.User => Color.FromArgb("#3B82F6"),      // Blue for user
                ChatRole.Assistant => Color.FromArgb("#1E293B"), // Dark for assistant
                ChatRole.System => Color.FromArgb("#4A4A4A"),    // Gray for system
                _ => Colors.Transparent
            };
        }
        return Colors.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts ChatRole to text color for message content.
/// </summary>
public class RoleToTextColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Colors.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Inverts a boolean value.
/// </summary>
public class InvertBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }
}

/// <summary>
/// Returns true if role is User (for visibility binding).
/// </summary>
public class RoleToUserVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ChatRole role)
            return role == ChatRole.User;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns true if role is Assistant (for visibility binding).
/// </summary>
public class RoleToAssistantVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ChatRole role)
            return role == ChatRole.Assistant;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts bool to status indicator color (green=true, gray=false).
/// </summary>
public class BoolToStatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isReady)
            return isReady ? Color.FromArgb("#10B981") : Color.FromArgb("#64748B");
        return Color.FromArgb("#64748B");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts bool (listening state) to microphone icon.
/// </summary>
public class BoolToMicIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isListening)
            return isListening ? "⏹" : "🎤";
        return "🎤";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
