using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrintFlow.Domain;

/// <summary>
/// أساس بسيط لأي موديل عايز يبلّغ الواجهة إن قيمة اتغيرت (INotifyPropertyChanged).
/// مكتوب من الصفر عن قصد: INotifyPropertyChanged موجودة في الـ BCL نفسها،
/// فمحتاجينش أي مكتبة MVVM خارجية ولا أي التزام ترخيص جديد.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// بتحط القيمة الجديدة وترجّع true لو اتغيرت فعلاً.
    /// [CallerMemberName] معناها إن اسم البروبرتي بيتملى تلقائيًا — مفيش سترينج مكتوب بالإيد يغلط.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// نفس SetProperty بس بتصحّح القيمة الأول (clamp / null-guard).
    ///
    /// ليه دي مهمة: لو المستخدم كتب 0 في خانة عدد النسخ والقيمة المخزنة أصلاً 1،
    /// التصحيح هيرجّعها 1 — يعني القيمة "متغيرتش" ومفيش PropertyChanged، والخانة
    /// هتفضل مكتوب فيها 0 غلط قدام المستخدم. هنا بنبعت الإشعار برضه في الحالة دي
    /// عشان الواجهة ترجع تعرض القيمة المصححة.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, Func<T, T> coerce, [CallerMemberName] string? propertyName = null)
    {
        T coerced = coerce(value);
        bool changed = SetProperty(ref field, coerced, propertyName);

        if (!changed && !EqualityComparer<T>.Default.Equals(coerced, value))
        {
            OnPropertyChanged(propertyName);
        }

        return changed;
    }
}
