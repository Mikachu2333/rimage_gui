using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace RimageGui.I18n
{
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
            var binding = new Binding($"[{Key}]")
            {
                Source = Loc.I,
                Mode = BindingMode.OneWay
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
