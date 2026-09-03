using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using PrintFlow.Domain;

namespace PrintFlow.Tests;

public class PrintSettingsTests
{
    /// <summary>
    /// التست الحارس. بيمشي على كل خاصية في PrintSettings بالـ Reflection، بيديها قيمة
    /// مختلفة عن الافتراضي، بينسخ، وبيتأكد إن كل واحدة وصلت.
    ///
    /// فايدته: لما تضيف خيار جديد في PrintSettings وتنسى تضيفه في CopyFrom،
    /// التست ده هيقع ويقولك اسم الخاصية بالظبط. من غيره الـ Preset هيحفظ الخيار
    /// ويرجّعه ناقص، وده نوع باج صعب تلاقيه بالتجربة اليدوية.
    /// </summary>
    [Fact]
    public void CopyFrom_Copies_Every_Property()
    {
        var source = new PrintSettings();
        var properties = WritableProperties();

        Assert.NotEmpty(properties);

        foreach (var property in properties)
        {
            property.SetValue(source, DifferentValue(property, property.GetValue(source)));
        }

        var target = new PrintSettings();
        target.CopyFrom(source);

        var missing = properties
            .Where(p => !ValuesMatch(p.GetValue(source), p.GetValue(target)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"الخصايص دي مش متنسخة في PrintSettings.CopyFrom: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Clone_Produces_Independent_Copy()
    {
        var original = new PrintSettings
        {
            TotalCopies = 5,
            PaperSize = "A3",
            Grayscale = true,
            SelectedPrinters = new List<string> { "HP-1", "HP-2" }
        };

        var clone = original.Clone();

        clone.TotalCopies = 99;
        clone.PaperSize = "Letter";
        clone.SelectedPrinters.Add("HP-3");

        Assert.Equal(5, original.TotalCopies);
        Assert.Equal("A3", original.PaperSize);
        Assert.Equal(2, original.SelectedPrinters.Count);
        Assert.NotSame(original.SelectedPrinters, clone.SelectedPrinters);
    }

    /// <summary>ده اللي تاب "الإعدادات المسبقة" هيعتمد عليه بالكامل.</summary>
    [Fact]
    public void Json_RoundTrip_Preserves_Every_Property()
    {
        var source = new PrintSettings();
        foreach (var property in WritableProperties())
        {
            property.SetValue(source, DifferentValue(property, property.GetValue(source)));
        }

        string json = JsonSerializer.Serialize(source);
        var restored = JsonSerializer.Deserialize<PrintSettings>(json);

        Assert.NotNull(restored);

        var mismatched = WritableProperties()
            .Where(p => !ValuesMatch(p.GetValue(source), p.GetValue(restored)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            mismatched.Count == 0,
            $"الخصايص دي ضاعت في الـ JSON: {string.Join(", ", mismatched)}");
    }

    [Fact]
    public void PropertyChanged_Uses_Correct_Property_Name()
    {
        var settings = new PrintSettings();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)settings).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        settings.Grayscale = true;
        settings.PaperSize = "A3";

        Assert.Equal(new[] { nameof(PrintSettings.Grayscale), nameof(PrintSettings.PaperSize) }, raised);
    }

    [Fact]
    public void Setting_Same_Value_Does_Not_Raise()
    {
        var settings = new PrintSettings { TotalCopies = 3 };
        int count = 0;
        ((INotifyPropertyChanged)settings).PropertyChanged += (_, _) => count++;

        settings.TotalCopies = 3;

        Assert.Equal(0, count);
    }

    /// <summary>
    /// لو المستخدم كتب صفر في خانة عدد النسخ، القيمة بتترد لـ 1.
    /// المهم إن الإشعار يتبعت برضه، عشان الخانة في الواجهة ترجع تعرض 1 مش تفضل 0.
    /// </summary>
    [Fact]
    public void Coerced_Value_Still_Notifies_So_UI_Refreshes()
    {
        var settings = new PrintSettings();
        Assert.Equal(1, settings.TotalCopies);

        var raised = new List<string?>();
        ((INotifyPropertyChanged)settings).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        settings.TotalCopies = 0;

        Assert.Equal(1, settings.TotalCopies);
        Assert.Contains(nameof(PrintSettings.TotalCopies), raised);
    }
    
    /// <summary>
    /// ⚠ الحد الأعلى لعدد النسخ.
    ///
    /// من غيره، «١٠٠» اللي اتكتبت «١٠٠٠٠٠٠» بالغلط بتتحوّل لملايين وحدات
    /// شغل في <c>WorkloadBalancer.Balance</c> — البرنامج بيتجمّد والمستخدم
    /// مش عارف ليه.
    ///
    /// والإشعار لازم يتبعت، عشان الخانة في الواجهة تعرض الرقم المقصوص
    /// بدل ما تفضل مكتوب فيها اللي المستخدم كتبه.
    /// </summary>
    [Fact]
    public void A_Mistyped_Copy_Count_Is_Capped()
    {
        var settings = new PrintSettings();

        var raised = new List<string?>();
        ((INotifyPropertyChanged)settings).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        settings.TotalCopies = 1_000_000;

        Assert.Equal(PrintSettings.MaximumCopies, settings.TotalCopies);
        Assert.Contains(nameof(PrintSettings.TotalCopies), raised);
    }

    /// <summary>الأوردر الكبير الحقيقي لازم يعدّي زي ما هو — الحد مش مفروض يوقف شغل.</summary>
    [Fact]
    public void A_Big_But_Real_Order_Is_Not_Touched()
    {
        var settings = new PrintSettings { TotalCopies = 5_000 };

        Assert.Equal(5_000, settings.TotalCopies);
    }

    [Theory]
    [InlineData(5, 10)]
    [InlineData(1000, 400)]
    [InlineData(150, 150)]
    public void ScalePercent_Is_Clamped(int input, int expected)
    {
        var settings = new PrintSettings { ScalePercent = input };
        Assert.Equal(expected, settings.ScalePercent);
    }

    // ══════════ مساعدات ══════════

    private static List<PropertyInfo> WritableProperties() =>
        typeof(PrintSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .OrderBy(p => p.Name)
            .ToList();

    private static object DifferentValue(PropertyInfo property, object? current)
    {
        Type type = property.PropertyType;

        if (type == typeof(bool))
        {
            return !(bool)(current ?? false);
        }

        if (type == typeof(int))
        {
            return (int)(current ?? 0) + 7;
        }

        if (type == typeof(string))
        {
            return "قيمة_" + property.Name;
        }

        if (type.IsEnum)
        {
            return Enum.GetValues(type).Cast<object>().First(v => !Equals(v, current));
        }

        if (type == typeof(List<string>))
        {
            return new List<string> { "طابعة-أ", "طابعة-ب" };
        }

        throw new NotSupportedException(
            $"الخاصية {property.Name} نوعها {type.Name} — ضيف حالة ليها هنا في التست.");
    }

    private static bool ValuesMatch(object? left, object? right)
    {
        if (left is IEnumerable leftList and not string && right is IEnumerable rightList and not string)
        {
            return leftList.Cast<object>().SequenceEqual(rightList.Cast<object>());
        }

        return Equals(left, right);
    }
}
