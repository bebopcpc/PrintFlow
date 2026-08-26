using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PrintFlow.Domain;

namespace PrintFlow.UI;

/// <summary>
/// بيحوّل لون hex لفرشاة. بيستخدم نفس HexColor.TryParse بتاعة الـ Domain،
/// فالمعاينة في الواجهة بتتفق مع اللي بيترسم في الـ PDF بالظبط.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && HexColor.TryParse(hex, out var color))
        {
            return new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        }

        return Brushes.Black;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// بيقلب قيمة منطقية في الاتجاهين. بيستخدم في زرار الراديو "نص" اللي المفروض
/// يبقى متعلّم لما WatermarkIsImage تبقى false.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag ? !flag : DependencyProperty.UnsetValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag ? !flag : DependencyProperty.UnsetValue;
}

/// <summary>نسبة مئوية (0-100) لشفافية WPF (0.0-1.0).</summary>
public sealed class PercentToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int percent ? Math.Clamp(percent, 0, 100) / 100d : 1d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// بيعكس إشارة الزاوية. في الـ PDF بنستخدم السالب عشان الزاوية الموجبة تطلع
/// مايلة لفوق ناحية اليمين، فالمعاينة لازم تعمل نفس الحاجة عشان تطابق الناتج.
/// </summary>
public sealed class NegateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            int i => (double)-i,
            double d => -d,
            _ => 0d
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// بيحوّل مكان العنصر لمحاذاة WPF. الباراميتر "H" للأفقي و"V" للرأسي.
/// المعاينة بتتضبط على LeftToRight عشان "يسار" تبقى يسار فعلًا.
/// </summary>
public sealed class ContentPositionToAlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ContentPosition position)
        {
            return DependencyProperty.UnsetValue;
        }

        bool horizontal = parameter as string == "H";

        if (horizontal)
        {
            return position switch
            {
                ContentPosition.TopLeft or ContentPosition.BottomLeft => HorizontalAlignment.Left,
                ContentPosition.TopCenter or ContentPosition.BottomCenter => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Right
            };
        }

        return position is ContentPosition.TopLeft or ContentPosition.TopCenter or ContentPosition.TopRight
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// بيلوّن سطر الوصف: أحمر لو تحذير، رمادي لو معلومة عادية.
///
/// موجود عشان التحذير اللي بيقول "الدمج شغّال فالتوزيع مش هيعمل اللي إنت
/// متوقعه" لازم يبان مختلف عن باقي الأسطر الرمادية، وإلا هيعدّي في وسطهم.
/// </summary>
public sealed class WarningBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Warning = new(Color.FromRgb(0xC0, 0x39, 0x2B));
    private static readonly SolidColorBrush Normal = new(Color.FromRgb(0x8A, 0x93, 0xA6));

    static WarningBrushConverter()
    {
        Warning.Freeze();
        Normal.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Warning : Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
