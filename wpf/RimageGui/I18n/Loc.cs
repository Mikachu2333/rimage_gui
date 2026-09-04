using System.ComponentModel;
using System.Globalization;

namespace RimageGui.I18n
{
    /// <summary>
    /// Bindable façade over <see cref="Strings"/>. XAML binds through the string
    /// indexer, so switching <see cref="Current"/> refreshes every localized
    /// element without rebuilding the window.
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        public static Loc I { get; } = new Loc();

        private Language _current = Language.System;

        public Language Current
        {
            get => _current;
            set
            {
                if (_current == value)
                {
                    return;
                }

                _current = value;
                // A null/empty property name invalidates every binding on this
                // source, which is exactly what an indexer-backed catalog needs.
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            }
        }

        public string this[string key] => Strings.Get(_current, key);

        /// <summary>Formats a catalog entry that contains composite placeholders.</summary>
        public string Format(string key, params object[] args) =>
            string.Format(CultureInfo.CurrentCulture, Strings.Get(_current, key), args);

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
