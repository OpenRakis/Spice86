# Spice86 UI Overview

The UI (UserInterface) is an Avalonia-based cross-platform C# application that allows interaction with the Spice86.Core emulator.
It is built by the .NET project `src\Spice86\Spice86.csproj`.

The UI facilitates the playing of DOS programs (games) on modern systems.
It also contains a debugger tailored to reverse engineering projects.

## Documentation Sections

The Spice86 UI documentation is organized into the following sections:

- [Architecture](Architecture.md) - Overview of the UI architecture, design patterns, and technology stack
- [Components](Components.md) - Description of the main UI components and their relationships
- [Debug Window](DebugWindow.md) - Details about the Debug Window and its lazy loading implementation
- [Modern Disassembly View](ModernDisassemblyView.md) - Information about the Modern Disassembly View component
- [Performance](Performance.md) - Performance optimizations implemented in the UI
- [Design Canvas](Design.canvas) - Visual representation of the UI architecture and component relationships
- [Design Canvas Guide](Design_Canvas_Guide.md) - Guide to understanding the Design canvas

## Key Features

- **Cross-Platform**: Built with Avalonia for cross-platform compatibility
- **MVVM Architecture**: Clean separation of concerns using the Model-View-ViewModel pattern
- **Lazy Loading**: Components are only loaded when needed for improved performance
- **Debugging Tools**: Comprehensive debugging capabilities for reverse engineering
- **Emulation Interface**: User-friendly interface for interacting with the emulator

## Technology Stack

- **UI Framework**: Avalonia
- **Language**: C#
- **Pattern**: MVVM (Model-View-ViewModel)
- **Project Structure**: Located in `src\Spice86\Spice86.csproj`
