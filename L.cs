using System.Windows.Data;
using System.Windows.Markup;
using CodeSearch.Core;

namespace CodeSearch;

/// <summary>XAML shorthand for a live-updating localized string: Text="{local:L key}".</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class L : MarkupExtension
{
    public L() { }
    public L(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider sp) =>
        new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay }.ProvideValue(sp);
}
