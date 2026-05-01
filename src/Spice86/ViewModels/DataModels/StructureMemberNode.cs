namespace Spice86.ViewModels.DataModels;

using Spice86.ViewModels.Services;

using Structurizer.Types;

/// <summary>Wraps a <see cref="StructureMember" /> for display in the <c>StructureHierarchyGrid</c>.</summary>
public class StructureMemberNode {
    /// <summary>Initializes a new instance of <see cref="StructureMemberNode" />.</summary>
    public StructureMemberNode(StructureMember member) {
        List<StructureMember>? members = member.Members;
        Name = member.Name;
        TypeName = member.Type.Type;
        Size = member.Size;
        Value = StructureDataTemplateProvider.FormatValueForDisplay(member);
        Children = members is { Count: > 0 }
            ? members.ConvertAll(m => new StructureMemberNode(m))
            : null;
    }

    /// <summary>Gets the field name.</summary>
    public string Name { get; }

    /// <summary>Gets the type name.</summary>
    public string TypeName { get; }

    /// <summary>Gets the byte size of the field.</summary>
    public int Size { get; }

    /// <summary>Gets the formatted value string for display.</summary>
    public string Value { get; }

    /// <summary>Gets the child member nodes, or <c>null</c> if this is a leaf member.</summary>
    public List<StructureMemberNode>? Children { get; }
}
