using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Serva.App.ViewModels;

/// <summary>极简 <see cref="INotifyPropertyChanged"/> 基类（不引入第三方 MVVM 框架）。</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>设置字段并在值变化时发出通知；返回是否确实发生了变化。</summary>
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
}
