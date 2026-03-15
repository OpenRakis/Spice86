namespace Spice86.ViewModels.ValueViewModels;

public sealed class HttpOperationParameterDefinition {
    public HttpOperationParameterDefinition(string name, string label, string defaultValue, bool required, HttpOperationParameterKind kind) {
        Name = name;
        Label = label;
        DefaultValue = defaultValue;
        Required = required;
        Kind = kind;
    }

    public string Name { get; }
    public string Label { get; }
    public string DefaultValue { get; }
    public bool Required { get; }
    public HttpOperationParameterKind Kind { get; }
}
