// ViewModelBase.cs - Base class for view models with INotifyPropertyChanged support.
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TID3.ViewModels
{
    /// <summary>
    /// Base class for view models. Provides <see cref="INotifyPropertyChanged"/>
    /// and a <see cref="SetProperty{T}"/> helper that raises change notifications
    /// only when the value actually changes.
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Sets <paramref name="field"/> to <paramref name="value"/> and raises
        /// <see cref="PropertyChanged"/> if the value changed. Returns true when changed.
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
