using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;

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
            string.Format(Strings.Get(_current, key), args);

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// <c>{loc:Loc AddFiles}</c> — resolves to a one-way binding against
    /// <see cref="Loc.I"/> so language changes propagate automatically.
    /// </summary>
    public sealed class LocExtension : MarkupExtension
    {
        public LocExtension()
        {
        }

        public LocExtension(string key)
        {
            Key = key;
        }

        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding("[" + Key + "]")
            {
                Source = Loc.I,
                Mode = BindingMode.OneWay
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
