namespace Spice86.ViewModels.ValueViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.ViewModels.ValueViewModels;

public sealed partial class HttpOperationParameter : ObservableObject {
    public HttpOperationParameter(HttpOperationParameterDefinition definition) {
        Name = definition.Name;
        Label = definition.Label;
        Required = definition.Required;
        Kind = definition.Kind;
        _value = definition.DefaultValue;
    }

    public string Name { get; }
    public string Label { get; }
    public bool Required { get; }
    public HttpOperationParameterKind Kind { get; }

    [ObservableProperty]
    private string _value;
}
