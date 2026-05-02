using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace radians.beamlab.app;

/// <summary>
/// Tiny INotifyPropertyChanged base. <see cref="SetField"/> updates a backing
/// field, raises <see cref="PropertyChanged"/> if the value changed, and
/// returns true so the caller can chain side-effects (e.g. update derived
/// properties or trigger a scene rebuild).
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
