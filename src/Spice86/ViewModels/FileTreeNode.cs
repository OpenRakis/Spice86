namespace Spice86.ViewModels;

using System.Collections.ObjectModel;

/// <summary>
/// Represents a single file or directory node in the DOS file-tree view.
/// </summary>
public sealed class FileTreeNode {
    /// <summary>Gets the DOS 8.3 name of this entry.</summary>
    public string Name { get; }

    /// <summary>Gets the formatted file size, or an empty string for directories.</summary>
    public string Size { get; }

    /// <summary>Gets the DOS attribute flags string (e.g. "R", "HS", "---").</summary>
    public string Attributes { get; }

    /// <summary>Gets a value indicating whether this node is a directory.</summary>
    public bool IsDirectory { get; }

    /// <summary>Gets the child nodes (populated for directories, empty for files).</summary>
    public ObservableCollection<FileTreeNode> Children { get; }

    /// <summary>Initialises a new <see cref="FileTreeNode"/>.</summary>
    /// <param name="name">The DOS 8.3 name.</param>
    /// <param name="size">The formatted file size string.</param>
    /// <param name="attributes">The attribute flags string.</param>
    /// <param name="isDirectory">Whether this node is a directory.</param>
    /// <param name="children">The child nodes.</param>
    public FileTreeNode(string name, string size, string attributes, bool isDirectory, ObservableCollection<FileTreeNode> children) {
        Name = name;
        Size = size;
        Attributes = attributes;
        IsDirectory = isDirectory;
        Children = children;
    }
}