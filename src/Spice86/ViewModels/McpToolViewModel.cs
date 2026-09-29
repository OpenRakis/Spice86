namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using System;
using System.Linq;

public partial class McpToolViewModel : ViewModelBase {
    /// <summary>Tool-name fragments mapped to a display category, tested in order so that
    /// the more specific device categories win over the generic ones.</summary>
    private static readonly (string Fragment, string Category)[] CategoryRules = [
        ("gus", "Gravis UltraSound"),
        ("sound_blaster", "Sound Blaster"),
        ("opl", "OPL / AdLib"),
        ("pc_speaker", "PC Speaker"),
        ("midi", "MIDI"),
        ("video", "Video"),
        ("screenshot", "Video"),
        ("keyboard", "Input"),
        ("mouse", "Input"),
        ("breakpoint", "Breakpoints"),
        ("ems", "EMS"),
        ("xms", "XMS"),
        ("dos", "DOS"),
        ("bios", "BIOS"),
        ("interrupt_vector", "BIOS"),
        ("io_port", "I/O Ports"),
        ("function", "Functions"),
        ("memory", "Memory"),
        ("stack", "Memory"),
        ("cpu", "CPU"),
        ("disassembly", "CPU"),
        ("mcp_", "Server")
    ];

    private static readonly string[] ExecutionToolNames = [
        "pause_emulator", "resume_emulator", "go", "step", "step_over"
    ];

    /// <summary>Category assigned to tools that match no rule.</summary>
    public const string FallbackCategory = "Other";

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string _argumentsTemplateJson;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _canToggle;

    public McpToolViewModel(string name, string description, string argumentsTemplateJson,
        bool isEnabled, bool canToggle) {
        _name = name;
        _description = description;
        _argumentsTemplateJson = argumentsTemplateJson;
        _isEnabled = isEnabled;
        _canToggle = canToggle;
        Category = ResolveCategory(name);
    }

    public string Category { get; }

    public static string ResolveCategory(string toolName) {
        if (ExecutionToolNames.Contains(toolName, StringComparer.OrdinalIgnoreCase)) {
            return "Execution";
        }

        foreach ((string fragment, string category) in CategoryRules) {
            if (toolName.Contains(fragment, StringComparison.OrdinalIgnoreCase)) {
                return category;
            }
        }

        return FallbackCategory;
    }

    public bool MatchesFilter(string filter) {
        if (string.IsNullOrWhiteSpace(filter)) {
            return true;
        }

        return Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

}