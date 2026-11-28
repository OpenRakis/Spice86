namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Decides, at emission, which register SSA webs print as C# locals: a web joins the versions of one
/// register through phis and through calls whose callee provably leaves the register alone, and a web
/// whose definitions and uses the local can all serve becomes a local (<c>ax</c> inside one block, a
/// numbered <c>ax1</c> when it spans blocks or a call), written back to the register
/// (<c>AX = ax;</c>) before every use the local cannot serve where the register may hold another
/// value: a use whose every incoming path still holds the local takes no write-back. A register node
/// instance shared between a definition target and an operand (<c>inc bx</c> gives both positions one
/// node) joins the plan only when every root holding it uses a web member; elsewhere the local would
/// rename a read of another version. Only printing changes: the plan maps
/// read and target node instances to the local name, like
/// <see cref="CSharpExpressionPrinter.ParameterReads"/> does for typed parameters.
/// </summary>
internal static partial class RegisterLocalPromotion {
    /// <summary>The register bytes a web can be promoted to a local for: the values whose exit
    /// versions need a write-back when promoted.</summary>
    internal static readonly CpuValues PromotableMask =
        CpuValues.Eax | CpuValues.Ecx | CpuValues.Edx | CpuValues.Ebx
        | CpuValues.Ebp | CpuValues.Esi | CpuValues.Edi | CpuValues.Es | CpuValues.Ds
        | CpuValues.Carry | CpuValues.Parity | CpuValues.Auxiliary | CpuValues.Zero
        | CpuValues.Sign | CpuValues.Overflow | CpuValues.Direction;

    private static readonly IrSsaValue[] Promotable = IrValueSets.ValuesIn(PromotableMask);

    /// <summary>The local-name bases of the promotable flags, in <see cref="IrSsaValue"/> order
    /// from <see cref="IrSsaValue.Carry"/>.</summary>
    private static readonly string[] FlagLocalNames =
        ["carry", "parity", "auxiliary", "zero", "sign", "overflow", "direction"];


    /// <summary>
    /// Plans the register locals of <paramref name="method"/> under <paramref name="ssa"/>: the reads
    /// and assignment targets to print as a local, the declarations and the write-backs.
    /// <paramref name="reservedNames"/> holds every name a local may not take: the typed signature's
    /// parameter names, the frame variable, and every temporary or other local of the method.
    /// <paramref name="callPreserves"/> answers whether a call statement's callee provably returns
    /// without changing a byte of the mask: a resolved call site, every target returning, and no
    /// redirected-return continuation.
    /// <paramref name="unprinted"/> holds the node instances another plan replaces when printing
    /// (the descendants of a frame plan's pointer nodes, printed as frame slots): a web whose
    /// reads are all in it, that has no write-back and no call definition, and whose every non-phi
    /// definition assigns a register or constant is dropped with its definitions - nothing printed
    /// reads the local, the register value is never observed without an implicit use, and a plain
    /// copy or constant definition has no other effect.
    /// </summary>

    public static RegisterLocalPlan Plan(IrMethod method, IrSsa ssa, IReadOnlySet<string> reservedNames,
        IReadOnlySet<IVisitableAstNode> unprinted, TypedSignature? signature,
        TypedSignatures? typedSignatures,
        Func<IrStatement, CpuValues, bool> callPreserves, RoleNames.RoleNameContext roles,
        GenerationPolicy policy) {
        Dictionary<IVisitableAstNode, SubstitutedRead> reads = new(ReferenceEqualityComparer.Instance);
        List<LocalDeclarationNode> declarations = [];
        HashSet<string> declared = new(StringComparer.Ordinal);
        List<RegisterLocalWriteBack> writeBacks = [];
        Dictionary<ProcedureReturnHelperNode, string?[]> returnOutputs =
            new(ReferenceEqualityComparer.Instance);
        List<(Candidate Candidate, string Name, bool Signed,
            ProcedureReturnHelperNode Helper, int Element)> returnServed = [];
        List<(IrBlock Block, string Name, int Start, int End)> ranges = [];
        Dictionary<IrBlock, int> blockOrder = [];
        for (int i = 0; i < method.Blocks.Count; i++) {
            blockOrder[method.Blocks[i]] = i;
        }
        HashSet<IVisitableAstNode> nonSubstitutable = NonSubstitutableInstances(method);
        Dictionary<IVisitableAstNode, List<IVisitableAstNode>> occurrences = RegisterOccurrences(method);
        int[] parent = new int[ssa.VersionCount];
        for (int v = 0; v < parent.Length; v++) {
            parent[v] = v;
        }
        int Find(int version) {
            int root = version;
            while (parent[root] != root) {
                root = parent[root];
            }
            while (parent[version] != root) {
                int next = parent[version];
                parent[version] = root;
                version = next;
            }
            return root;
        }
        void Union(int first, int second) => parent[Find(first)] = Find(second);
        foreach (IrSsaPhi phi in ssa.Phis) {
            foreach ((IrBlock _, int operandVersion) in phi.Operands) {
                Union(phi.Result, operandVersion);
            }
        }
        for (int version = 0; version < ssa.VersionCount; version++) {
            if (ssa.Info(version).Kind != IrSsaDefinitionKind.MethodEntry) {
                continue;
            }
            foreach (IrSsaPhi phi in ssa.Uses(version).Select(use => use.Phi).OfType<IrSsaPhi>()) {
                Union(version, phi.Result);
            }
        }
        Dictionary<IrStatement, HashSet<IrSsaValue>> preservedByCall =
            new(ReferenceEqualityComparer.Instance);
        JoinAcrossCalls(method, ssa, callPreserves, Find, Union, preservedByCall);
        BlockGraph<IrBlock> graph = method.ToBlockGraph();
        HashSet<IVisitableAstNode> dropped = new(ReferenceEqualityComparer.Instance);
        List<Candidate> candidates = [.. WebsOf(ssa, Find).Values
            .Select(members => CandidateOf(method, graph, ssa, members, preservedByCall,
                nonSubstitutable, occurrences, unprinted, dropped, signature, typedSignatures))
            .OfType<Candidate>()];
        Dictionary<string, int> counters = new(StringComparer.Ordinal);
        List<(Candidate Candidate, string LegacyName)> promotable = [];
        foreach (Candidate candidate in candidates.OrderBy(candidate => candidate.FirstBlockOrder)
                .ThenBy(candidate => candidate.FirstIndex)) {
            string legacyName = candidate.SingleBlock ? candidate.BaseName : candidate.BaseName + NextNumber(counters, candidate.BaseName);
            if (reservedNames.Contains(legacyName)) {
                continue;
            }
            if (candidate.SingleBlock) {
                if (Overlaps(ranges, candidate)) {
                    continue;
                }
                ranges.Add((candidate.Block, legacyName, candidate.DefIndex, candidate.End));
            }
            promotable.Add((candidate, legacyName));
        }
        // The printed name is the semantic name when one rule gives a single answer, else
        // the width prefix numbered by the first definition address; the legacy register
        // name above only decides which webs promote, as before. Webs that shared one local keep
        // sharing it: they had one legacy name.
        HashSet<string> taken = new(reservedNames, StringComparer.Ordinal);
        Dictionary<string, string> names = new(StringComparer.Ordinal);
        foreach ((Candidate candidate, string legacyName) in promotable
                .OrderBy(item => item.Candidate.FirstAddress)
                .ThenBy(item => item.Candidate.FirstBlockOrder)
                .ThenBy(item => item.Candidate.FirstIndex)
                .ThenBy(item => item.Candidate.BaseName, StringComparer.Ordinal)) {
            if (names.ContainsKey(legacyName)) {
                continue;
            }
            string? hint = CandidateHint(candidate, occurrences, roles);
            string name = candidate.Parameter is { } parameter ? parameter.Name
                : hint is null || !RoleNames.IsIdentifier(hint)
                ? GenericValueNames.Next(
                    candidate.Register.DataType == DataType.BOOL ? "flag"
                        : GenericValueNames.Prefix(candidate.Register.DataType.BitWidth), taken)
                : RoleNames.Next(hint, taken);
            taken.Add(name);
            names[legacyName] = name;
        }
        // A shared legacy name that resolved to a parameter or another reserved local cannot back
        // a second, non-parameter web: its declaration would collide with the existing one.
        List<(Candidate Candidate, string Name)> accepted = [.. promotable
            .Select(item => (item.Candidate, Name: names[item.LegacyName]))
            .Where(item => item.Candidate.Parameter is not null
                || !reservedNames.Contains(item.Name))];
        // The tuple helpers and the service calls hand back unsigned storage halves even for a
        // signed operation, so a local defined by one of their targets stays unsigned whatever the
        // web's signed reads vote.
        HashSet<IVisitableAstNode> unsignedTupleResults = new(ReferenceEqualityComparer.Instance);
        foreach (IVisitableAstNode node in method.Blocks.SelectMany(block => block.Statements)
            .Select(statement => statement.Node)
            .Where(node => node is MultiplicationNode or DivisionNode or InterruptServiceCallNode)) {
            unsignedTupleResults.UnionWith(DefinitionTargets(node));
        }
        HashSet<string> unsignedNames = accepted
            .Where(item => item.Candidate.DefTargets.OfType<ValueNode>()
                    .Any(unsignedTupleResults.Contains)
                || item.Candidate.DefTargets.OfType<CallOutputTarget>().Any())
            .Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        List<(CallOutputTarget Target, string Name)> promotedCallOutputs = [];
        Dictionary<string, (int Signed, int Unsigned)> votes = new(StringComparer.Ordinal);
        foreach ((Candidate candidate, string name) in accepted) {
            (int signed, int unsigned) = (0, 0);
            if (votes.TryGetValue(name, out (int Signed, int Unsigned) soFar)) {
                (signed, unsigned) = soFar;
            }
            votes[name] = (signed + candidate.SignedVotes, unsigned + candidate.UnsignedVotes);
        }
        foreach ((Candidate candidate, string name) in accepted) {
            bool flag = candidate.Register.DataType == DataType.BOOL;
            DataType signedType = flag ? DataType.BOOL
                : DataType.SignedFromBitWidth(candidate.Register.DataType.BitWidth);
            bool signed = !flag && candidate.Parameter is null && !unsignedNames.Contains(name)
                && votes[name].Unsigned < votes[name].Signed;
            DataType localType = signed ? signedType : candidate.Register.DataType;
            if (candidate.Parameter is null && declared.Add(name)) {
                declarations.Add(new LocalDeclarationNode(localType, name,
                    candidate.SingleBlock ? null : new ConstantNode(localType, 0)));
            }
            foreach (object target in candidate.DefTargets) {
                if (target is ValueNode valueNode) {
                    reads[valueNode] = new SubstitutedRead(name, signed ? localType : null);
                } else if (target is CallOutputTarget callOutput) {
                    promotedCallOutputs.Add((callOutput, name));
                }
            }
            foreach ((IVisitableAstNode node, IVisitableAstNode? readParent) in candidate.Reads) {
                if (reads.ContainsKey(node)) {
                    continue;
                }
                if (!signed) {
                    reads[node] = new SubstitutedRead(name, null);
                } else if (ConvertsTo(readParent, localType)) {
                    reads[node] = new SubstitutedRead(name, localType);
                } else {
                    reads[node] = new SubstitutedRead(
                        $"({CSharpExpressionPrinter.ToCSharpType(candidate.Register.DataType)}){name}", null);
                }
            }
            foreach (Use use in candidate.WriteBackUses) {
                ValueNode fresh = FreshRegister(candidate.Register);
                writeBacks.Add(new RegisterLocalWriteBack(use.Origin, AnchorOf(use),
                    new RegisterWriteBackNode(fresh, new RegisterLocalNode(fresh, name, localType))));
            }
            foreach ((ProcedureReturnHelperNode helper, int element)
                    in candidate.Uses.ReturnUses) {
                returnServed.Add((candidate, name, signed, helper, element));
            }
        }
        Dictionary<IVisitableAstNode, IVisitableAstNode> replacements =
            new(ReferenceEqualityComparer.Instance);
        PlanPairs(method, [.. accepted.Where(item => item.Candidate.Parameter is null
            && item.Candidate.Register is RegisterNode)],
            reservedNames, declared, reads, declarations, writeBacks,
            dropped, replacements);
        Dictionary<ProcedureReturnHelperNode, IReadOnlyList<ReturnExpression?>> returnExpressions =
            new(ReferenceEqualityComparer.Instance);
        if (signature is { } returnSignature) {
            foreach ((Candidate candidate, string name, bool signed,
                    ProcedureReturnHelperNode helper, int element) in returnServed) {
                if (!returnOutputs.TryGetValue(helper, out string?[]? texts)) {
                    texts = new string?[returnSignature.Outputs.Count];
                    returnOutputs[helper] = texts;
                }
                texts[element] = ReturnText(candidate, name, signed, reads);
            }
            PlanReturnExpressions(method, ssa, returnSignature, returnOutputs, dropped,
                returnExpressions, policy);
        }
        return new RegisterLocalPlan(reads, declarations, writeBacks, dropped, replacements,
            accepted.Select(item => item.Candidate.Parameter).OfType<TypedParameter>().ToHashSet(),
            PlanCallOutputs(method, ssa, typedSignatures, promotedCallOutputs),
            returnOutputs.ToDictionary<KeyValuePair<ProcedureReturnHelperNode, string?[]>,
                ProcedureReturnHelperNode, IReadOnlyList<string?>>(
                pair => pair.Key, pair => pair.Value, ReferenceEqualityComparer.Instance),
            returnExpressions);
    }

    /// <summary>The output elements a typed <c>return</c> serves from a merged assignment: a
    /// plain <c>R = e</c> whose version's only live use is the exit's read of the output prints
    /// <c>return e;</c> and the assignment goes (<see cref="IrExpressionPropagation.MergedIntoReturn"/>
    /// proves the merge legal). The writer's statement node joins <paramref name="dropped"/> so the
    /// printed body removes it. An element a promoted local already serves keeps the local.</summary>
    private static void PlanReturnExpressions(IrMethod method, IrSsa ssa, TypedSignature signature,
        Dictionary<ProcedureReturnHelperNode, string?[]> returnOutputs,
        HashSet<IVisitableAstNode> dropped,
        Dictionary<ProcedureReturnHelperNode, IReadOnlyList<ReturnExpression?>> returnExpressions,
        GenerationPolicy policy) {
        HashSet<ICfgNode> entryNodes = [.. method.Entries.Select(entry => entry.Node)];
        IrMemoryAliasAnalysis alias = new(method, ssa, policy);
        Dictionary<ProcedureReturnHelperNode, ReturnExpression?[]> merged = new(ReferenceEqualityComparer.Instance);
        foreach (IrBlock block in method.Blocks) {
            for (int i = 0; i < block.Statements.Count; i++) {
                if (IrExpressionPropagation.MergedIntoReturn(block, i, entryNodes, ssa, alias)
                        is not ({ } writer, { } register, { } expression, { } helper)
                    || dropped.Contains(writer.Node)) {
                    continue;
                }
                int element = -1;
                for (int index = 0; index < signature.Outputs.Count; index++) {
                    if (signature.Outputs[index].Mask == IrValueSets.Of(register)) {
                        element = index;
                    }
                }
                if (element < 0
                    || (returnOutputs.TryGetValue(helper, out string?[]? texts)
                        && texts[element] is not null)) {
                    continue;
                }
                dropped.Add(writer.Node);
                if (!merged.TryGetValue(helper, out ReturnExpression?[]? expressions)) {
                    expressions = new ReturnExpression?[signature.Outputs.Count];
                    merged[helper] = expressions;
                }
                expressions[element] = new ReturnExpression(expression, register.DataType);
            }
        }
        foreach ((ProcedureReturnHelperNode helper, ReturnExpression?[] expressions) in merged) {
            returnExpressions[helper] = expressions;
        }
    }

    /// <summary>The unsigned text <paramref name="candidate"/>'s promoted web prints as a read:
    /// the pair's slice (<c>(ushort)dwordN</c>) when pairing rewrote the reads after
    /// <paramref name="name"/> was chosen, else the bare name or the signed local's cast. Reads
    /// are consulted after <see cref="PlanPairs"/> so a paired web serves its return element with
    /// the same text its other reads print.</summary>
    private static string ReturnText(Candidate candidate, string name, bool signed,
        Dictionary<IVisitableAstNode, SubstitutedRead> reads) {
        foreach ((IVisitableAstNode node, IVisitableAstNode? _) in candidate.Reads) {
            if (reads.TryGetValue(node, out SubstitutedRead? read) && read.Type is null) {
                return read.Text;
            }
        }
        return signed
            ? $"({CSharpExpressionPrinter.ToCSharpType(candidate.Register.DataType)}){name}" : name;
    }

    /// <summary>The printed left side of each typed call site in <paramref name="method"/>: a
    /// promoted call-defined web's output prints the local's name, an output version with no live
    /// use prints <c>_</c> and any other output keeps its register name. Calls whose outputs all
    /// keep the register take no entry.</summary>
    private static Dictionary<ProcedureCallHelperNode, IReadOnlyList<string?>> PlanCallOutputs(
        IrMethod method, IrSsa ssa, TypedSignatures? typedSignatures,
        List<(CallOutputTarget Target, string Name)> promotedCallOutputs) {
        Dictionary<ProcedureCallHelperNode, IReadOnlyList<string?>> callOutputs =
            new(ReferenceEqualityComparer.Instance);
        if (typedSignatures is null) {
            return callOutputs;
        }
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                ProcedureCallHelperNode? call = statement.Node switch {
                    ProcedureCallHelperNode helper => helper,
                    StackArgumentCallNode stackCall => stackCall.Call,
                    _ => null
                };
                if (call is null || typedSignatures.TypedCallOf(call) is not { } callSignature
                    || callSignature.Outputs.Count == 0) {
                    continue;
                }
                string?[] names = new string?[callSignature.Outputs.Count];
                for (int element = 0; element < callSignature.Outputs.Count; element++) {
                    IrSsaValue? value = TypedSignatures.ValueOf(callSignature.Outputs[element].Mask);
                    if (value is null || !Promotable.Contains(value.Value)) {
                        continue;
                    }
                    int version = ssa.Definitions(statement)
                        .Where(definition => definition.Value == value)
                        .Select(definition => definition.Version).FirstOrDefault(-1);
                    if (version < 0) {
                        continue;
                    }
                    if (!ssa.IsLive(version)) {
                        names[element] = "_";
                    }
                }
                if (names.Any(name => name is not null)) {
                    callOutputs[call] = names;
                }
            }
        }
        foreach ((CallOutputTarget target, string name) in promotedCallOutputs) {
            if (!callOutputs.TryGetValue(target.Call, out IReadOnlyList<string?>? texts)
                || texts is not string?[] names) {
                names = new string?[target.Signature.Outputs.Count];
                callOutputs[target.Call] = names;
            }
            names[target.Element] = name;
        }
        return callOutputs;
    }

    /// <summary>The next number for <paramref name="baseName"/>'s numbered names (<c>ax1</c>,
    /// <c>ax2</c>, ...), counting per base name.</summary>
    private static int NextNumber(Dictionary<string, int> counters, string baseName) {
        counters.TryGetValue(baseName, out int count);
        counters[baseName] = count + 1;
        return count + 1;
    }

    /// <summary>The webs of promotable versions under the current union-find state: every version
    /// of a promotable value keyed by its union-find root.</summary>
    private static Dictionary<int, List<int>> WebsOf(IrSsa ssa, Func<int, int> find) {
        Dictionary<int, List<int>> webs = [];
        for (int version = 0; version < ssa.VersionCount; version++) {
            if (!Promotable.Contains(ssa.Info(version).Value)) {
                continue;
            }
            int root = find(version);
            if (!webs.TryGetValue(root, out List<int>? members)) {
                members = [];
                webs[root] = members;
            }
            members.Add(version);
        }
        return webs;
    }

    /// <summary>
    /// Joins, to a fixpoint, the version a statement uses of a promotable value with the version it
    /// defines when <paramref name="callPreserves"/> proves the call leaves the merged web's register
    /// alone. The mask tested is the register of the merged web's plain definitions; a web without a
    /// uniform register yet does not join. <paramref name="preservedByCall"/> records, per call
    /// statement, the values it was found to preserve. An external event check never joins: it defines
    /// a fresh version exactly when a hardware handler may change the register, so the web ends there.
    /// </summary>
    private static void JoinAcrossCalls(IrMethod method, IrSsa ssa,
        Func<IrStatement, CpuValues, bool> callPreserves, Func<int, int> find,
        Action<int, int> union, Dictionary<IrStatement, HashSet<IrSsaValue>> preservedByCall) {
        List<(IrStatement Statement, IrBlock Block, int Index)> calls = [];
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                if (ssa.Definitions(block.Statements[index]).Count > 0) {
                    calls.Add((block.Statements[index], block, index));
                }
            }
        }
        bool changed = true;
        Dictionary<int, List<int>> webs = WebsOf(ssa, find);
        while (changed) {
            changed = false;
            foreach ((IrStatement statement, IrBlock block, int index) in calls) {
                foreach ((IrSsaValue value, int defined) in ssa.Definitions(statement)) {
                    if (!Promotable.Contains(value)
                        || (statement.Effects.Reads & IrSsaValues.MaskOf(value)) == CpuValues.None) {
                        continue;
                    }
                    int used = ssa.VersionBefore(block, index, value);
                    int usedRoot = find(used);
                    int definedRoot = find(defined);
                    if (usedRoot == definedRoot) {
                        continue;
                    }
                    List<int> merged = [.. webs[usedRoot], .. webs[definedRoot]];
                    if (UniformRegister(ssa, merged) is not { } register
                        || !callPreserves(statement, MaskOf(register))) {
                        continue;
                    }
                    union(used, defined);
                    // union points the used root at the defined root: keep the member lists
                    // current instead of rebuilding them for the next iteration.
                    webs[find(used)] = merged;
                    webs.Remove(usedRoot);
                    if (!preservedByCall.TryGetValue(statement, out HashSet<IrSsaValue>? values)) {
                        values = [];
                        preservedByCall[statement] = values;
                    }
                    values.Add(value);
                    changed = true;
                }
            }
        }
    }

    /// <summary>The value outputs a statement defines: an assignment's register or flag left,
    /// both halves of a multiplication or division tuple, or a service call's result lvalues.</summary>
    private static IEnumerable<ValueNode> DefinitionTargets(IVisitableAstNode node) {
        switch (node) {
            case BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                    Left: RegisterNode or CpuFlagNode } assignment:
                yield return assignment.Left;
                break;
            case MultiplicationNode multiplication:
                yield return multiplication.Low;
                yield return multiplication.High;
                break;
            case DivisionNode division:
                yield return division.Division.Low;
                yield return division.Division.High;
                break;
            case InterruptServiceCallNode serviceCall:
                foreach (ValueNode result in serviceCall.Results
                        .Where(result => result is RegisterNode or CpuFlagNode)) {
                    yield return result;
                }
                break;
        }
    }

    /// <summary>The statement of <paramref name="info"/> that defines <paramref name="version"/>,
    /// with its index in the block's current statement order, or <c>null</c> when the defining
    /// statement is gone. SSA records statement indices of the list as built; later passes edited
    /// <see cref="IrBlock.Statements"/>, so the site is located by the definitions themselves.
    /// </summary>
    private static (IrStatement Statement, int Index)? DefinitionSite(
        IrSsa ssa, IrSsaVersionInfo info, int version) {
        for (int index = 0; index < info.Block.Statements.Count; index++) {
            IrStatement statement = info.Block.Statements[index];
            if (ssa.Definitions(statement).Any(pair => pair.Value == info.Value && pair.Version == version)) {
                return (statement, index);
            }
        }
        return null;
    }

    /// <summary>The one register the plain definitions of <paramref name="members"/> assign, or
    /// <c>null</c> when they do not agree or there is none yet.</summary>
    private static ValueNode? UniformRegister(IrSsa ssa, List<int> members) {
        ValueNode? register = null;
        foreach (int version in members) {
            IrSsaVersionInfo info = ssa.Info(version);
            if (info.Kind is not (IrSsaDefinitionKind.Definition
                    or IrSsaDefinitionKind.PartialDefinition)) {
                continue;
            }
            if (DefinitionSite(ssa, info, version) is not { } site) {
                continue;
            }
            ValueNode[] candidates = [.. DefinitionTargets(site.Statement.Node)
                .Where(target => RegisterMatchesValue(target, info.Value)).Take(2)];
            if (candidates.Length != 1) {
                continue;
            }
            if (register is not null && !register.Equals(candidates[0])) {
                return null;
            }
            register = candidates[0];
        }
        return register;
    }

    /// <summary>
    /// Inserts the plan's write-backs into <paramref name="body"/> and removes its dropped
    /// definition statements: each write-back goes to the start of the boundary of its instruction
    /// that holds the implicit use, right after the <see cref="InstructionStartNode"/>. Returns the
    /// rewritten body, or <c>null</c> when a boundary is not found; the caller then prints the
    /// method without promotion.
    /// </summary>
    public static BlockNode? ApplyToBody(BlockNode body, RegisterLocalPlan plan) {
        List<RegisterLocalWriteBack> pending = [.. plan.WriteBacks];
        IVisitableAstNode rewritten = Insert(body);
        return pending.Count == 0 ? (BlockNode)rewritten : null;

        IVisitableAstNode Insert(IVisitableAstNode node) {
            if (node is InstructionBoundaryNode boundary) {
                // Anchors and dropped statements match on the original boundary; the surviving
                // statements are then rewritten recursively so pair replacements under a dropped
                // sibling still apply.
                List<RegisterLocalWriteBack> mine = [.. plan.WriteBacks.Where(writeBack =>
                    boundary.Node.Equals(writeBack.Instruction)
                    && IrAstChildren.Descendants(boundary)
                        .Any(descendant => ReferenceEquals(descendant, writeBack.Anchor)))];
                foreach (RegisterLocalWriteBack writeBack in mine) {
                    pending.Remove(writeBack);
                }
                List<IVisitableAstNode> kept = [.. boundary.Body.Statements
                    .Where(statement => !plan.Dropped.Contains(statement))
                    .Select(Insert)];
                int insertionIndex = kept
                        .FindIndex(statement => statement is InstructionStartNode) + 1;
                kept.InsertRange(insertionIndex, mine.Select(writeBack => writeBack.Node));
                return boundary with { Body = new BlockNode([.. kept]) };
            }
            if (plan.Replacements.TryGetValue(node, out IVisitableAstNode? replacement)) {
                // The replacement keeps original subtrees (an initializer's packed reads): walk
                // it so replacements nested inside still apply.
                return IrAstChildren.MapChildren(replacement, Insert);
            }
            return IrAstChildren.MapChildren(node, Insert);
        }
    }

    /// <summary>The promotion of one web of versions, or <c>null</c> when the web is not a
    /// candidate.</summary>
    private static Candidate? CandidateOf(IrMethod method, BlockGraph<IrBlock> graph, IrSsa ssa,
        List<int> members,
        Dictionary<IrStatement, HashSet<IrSsaValue>> preservedByCall,
        HashSet<IVisitableAstNode> nonSubstitutable,
        Dictionary<IVisitableAstNode, List<IVisitableAstNode>> occurrences,
        IReadOnlySet<IVisitableAstNode> unprinted, HashSet<IVisitableAstNode> dropped,
        TypedSignature? signature, TypedSignatures? typedSignatures) {
        IrSsaValue value = ssa.Info(members[0]).Value;
        HashSet<int> memberVersions = [.. members];
        WebDefinitions? collected = CollectWebDefinitions(method, ssa, members, memberVersions,
            value, preservedByCall, occurrences, signature, typedSignatures);
        if (collected is null || collected.DefTargets.Count == 0
            || collected.DefTargets.Any(target => TargetMask(target)
                != TargetMask(collected.DefTargets[0]))) {
            return null;
        }
        ValueNode webRegister = TargetNode(collected.DefTargets[0]);
        CpuValues registerBytes = MaskOf(webRegister);
        // A method-entry member promotes only as a typed parameter: the signature must reach it
        // from its own entry block and a parameter must cover the register's exact bytes. The
        // parameter name is the local's, so nothing declares or copies it in.
        TypedParameter? entryParameter = null;
        if (collected.EntryBlock is { } entryBlock) {
            if (signature is null || entryBlock != signature.EntryBlock
                || signature.Parameters.FirstOrDefault(parameter => parameter.Mask == registerBytes)
                    is not { } match) {
                return null;
            }
            entryParameter = match;
        }
        string baseName = LocalName(webRegister);
        List<Use>? uses = CollectWebUses(ssa, members);
        if (uses is null) {
            return null;
        }
        WebUses? classification = ClassifyWebUses(webRegister, baseName, value, uses,
            nonSubstitutable, [.. collected.DefTargets.OfType<ValueNode>()], signature);
        if (classification is null) {
            return null;
        }
        if (!ReadsStayInside(occurrences, classification.WebReads, classification.ExplicitRoots)
            || MidBlockEntryInside(method, collected.Sites, uses)) {
            return null;
        }
        List<Use> writeBackUses = [];
        if (!PlanWriteBacks(method, graph, value, registerBytes, collected.Sites,
                classification.ImplicitUses, classification.KillingUses, uses, preservedByCall,
                writeBackUses)) {
            return null;
        }

        // A call-defined web that needs any write-back keeps the register form: the call writes
        // the register and a promoted local would only add a copy the reader sees as noise.
        if (collected.HasCallOutput && writeBackUses.Count > 0) {
            return null;
        }

        if (DroppableWeb(collected, classification, unprinted, writeBackUses)) {
            foreach (IVisitableAstNode node in collected.Sites
                    .Select(definition => definition.Statement?.Node).OfType<IVisitableAstNode>()) {
                dropped.Add(node);
            }
            return null;
        }
        // A web read by a typed-call operand loses nothing by staying a register when the
        // promotion's whole win is the operand read: a non-promotable reader keeps the register
        // live, so the local would print a declaration and a write-back copy where the register
        // form prints the definition once - the operand reads the register instead.
        int operandReads = classification.WebReads.Count(read =>
            read.Parent is ProcedureCallHelperNode or StackArgumentCallNode);
        if (writeBackUses.Count > 0 && operandReads > 0
            && classification.WebReads.Count - operandReads + collected.DefTargets.Count
                <= writeBackUses.Count + 1) {
            return null;
        }
        if (!PrintsFewerRegisterAccesses(collected, classification, uses, preservedByCall, value,
                writeBackUses, entryParameter)) {
            return null;
        }
        (int signedVotes, int unsignedVotes) = webRegister is CpuFlagNode
            ? (0, classification.WebReads.Count)
            : CountSignedVotes(webRegister, classification.WebReads,
                collected.Sites, writeBackUses);
        HashSet<IrBlock> touched = [.. collected.Sites.Select(definition => definition.Block),
            .. uses.Select(use => use.Site.Block)];
        bool singleBlock = !collected.HasCall && touched.Count == 1;
        int defIndex = collected.Sites.Where(definition => definition.Statement is not null)
            .Select(definition => definition.Index).DefaultIfEmpty(-1).Min();
        // A back-edge can put a use before the block's first definition (a loop phi merges
        // the later definition's version): the uninitialized local would be read there.
        if (singleBlock && uses.Any(use => use.Position < defIndex)) {
            return null;
        }
        int end = uses.Select(use => use.Position).DefaultIfEmpty(-1).Max();
        int firstBlockOrder = collected.Sites
            .Select(definition => OrderOf(method, definition.Block)).DefaultIfEmpty(0).Min();
        int firstIndex = collected.Sites
            .Where(definition => OrderOf(method, definition.Block) == firstBlockOrder)
            .Select(definition => definition.Index).DefaultIfEmpty(-1).Min();
        Definition first = collected.Sites
            .Where(definition => OrderOf(method, definition.Block) == firstBlockOrder
                && definition.Index == firstIndex).First();
        SegmentedAddress firstAddress = first.Statement?.Origin.Address
            ?? first.Block.EntryOrigin.Address;
        return new Candidate(touched.First(), singleBlock, firstBlockOrder, firstIndex, defIndex,
            end, webRegister, baseName, collected.DefTargets, collected.Sites,
            classification.WebReads, writeBackUses, signedVotes, unsignedVotes, firstAddress,
            classification, entryParameter);
    }

    /// <summary>The definition sites of the web's versions, or <c>null</c> when one disqualifies
    /// the web: a method-entry version, a version with no site, a faulting definition, a statement
    /// with no register definition target (an <c>ASSIGN</c> left, a multiplication or division
    /// tuple half, a service call result), a target of another register or more than one target on
    /// the web's value, or a target instance shared into a site outside the web.</summary>
    private static WebDefinitions? CollectWebDefinitions(IrMethod method, IrSsa ssa,
        List<int> members, HashSet<int> memberVersions,
        IrSsaValue value, Dictionary<IrStatement, HashSet<IrSsaValue>> preservedByCall,
        Dictionary<IVisitableAstNode, List<IVisitableAstNode>> occurrences,
        TypedSignature? signature, TypedSignatures? typedSignatures) {
        List<object> defTargets = [];
        List<Definition> definitions = [];
        bool hasCall = false;
        bool hasCallOutput = false;
        IrBlock? entryBlock = null;
        foreach (int version in members) {
            IrSsaVersionInfo info = ssa.Info(version);
            if (info.Kind is IrSsaDefinitionKind.MethodEntry) {
                if (signature is null) {
                    return null;
                }
                // The entry version acts like a definition the machine register does not mirror:
                // implicit uses reached from it without another definition take a write-back.
                entryBlock = info.Block;
                definitions.Add(new Definition(info.Block, -1, null));
                continue;
            }
            if (info.Kind is IrSsaDefinitionKind.Phi) {
                definitions.Add(new Definition(info.Block, -1, null));
                continue;
            }
            if (DefinitionSite(ssa, info, version) is not { } site) {
                return null;
            }
            IrStatement definition = site.Statement;
            int definitionIndex = site.Index;
            if (preservedByCall.TryGetValue(definition, out HashSet<IrSsaValue>? preserved)
                && preserved.Contains(value)) {
                hasCall = true;
                definitions.Add(new Definition(info.Block, definitionIndex, definition));
                continue;
            }
            if (definition.Effects.MayFault) {
                return null;
            }
            // A typed call's statement defines one version per signature output: the target is the
            // (call, output index) pair, printed through CallOutputs rather than a register lvalue.
            ProcedureCallHelperNode? call = definition.Node switch {
                ProcedureCallHelperNode helper => helper,
                StackArgumentCallNode stackCall => stackCall.Call,
                _ => null
            };
            if (call is not null && typedSignatures?.TypedCallOf(call) is { } callSignature) {
                int element = callSignature.Outputs.TakeWhile(output
                    => TypedSignatures.ValueOf(output.Mask) != value).Count();
                if (element == callSignature.Outputs.Count
                    || defTargets.OfType<CallOutputTarget>()
                        .Any(target => ReferenceEquals(target.Call, call)
                            && target.Element == element)) {
                    return null;
                }
                defTargets.Add(new CallOutputTarget(call, element,
                    callSignature.Outputs[element].Mask, callSignature));
                hasCall = true;
                hasCallOutput = true;
                definitions.Add(new Definition(info.Block, definitionIndex, definition));
                continue;
            }
            // The def target is the statement's register output that covers the web's value: an
            // assignment's left, one half of a multiplication or division tuple, or the service
            // call's result lvalue. A byte division can put two halves (AL and AH) on the same SSA
            // value: with more than one matching target the local would rename one half while the
            // tuple writes both, so the web refuses.
            ValueNode[] targets = [.. DefinitionTargets(definition.Node)
                .Where(target => RegisterMatchesValue(target, value)).Take(2)];
            if (targets.Length != 1
                || !OccurrencesInWeb(method, occurrences, targets[0], definition, ssa, value,
                    memberVersions)) {
                return null;
            }
            if (definition.Node is InterruptServiceCallNode) {
                hasCall = true;
            }
            defTargets.Add(targets[0]);
            definitions.Add(new Definition(info.Block, definitionIndex, definition));
        }
        return new WebDefinitions(defTargets, definitions, hasCall, hasCallOutput, entryBlock);
    }

    /// <summary>The use sites of the web's versions, resolved in each block's current statement
    /// order, or <c>null</c> when a use's statement is gone.</summary>
    private static List<Use>? CollectWebUses(IrSsa ssa, List<int> members) {
        List<Use> uses = [];
        foreach (int version in members) {
            foreach (IrSsaUse site in ssa.Uses(version)) {
                if (site.Phi is not null) {
                    continue;
                }
                // SSA indices were recorded before later passes edited the statement list: resolve the
                // use's position in the block's current statement order, which is the order the body
                // prints and runs in. A use whose statement is gone cannot print, but refusing keeps
                // the web's invariants honest.
                int position = PositionOf(site);
                if (position < 0) {
                    return null;
                }
                uses.Add(new Use(site, position));
            }
        }
        return [.. uses.DistinctBy(UseSite)];
    }

    /// <summary>The web's uses split into explicit reads (which the local substitutes), implicit
    /// uses (which take a write-back) and killing uses, or <c>null</c> when a use is a terminator
    /// the local cannot serve or the web has no explicit read at all.</summary>
    private static WebUses? ClassifyWebUses(ValueNode webRegister, string baseName,
        IrSsaValue value, List<Use> uses, HashSet<IVisitableAstNode> nonSubstitutable,
        IReadOnlyList<ValueNode> defTargets, TypedSignature? signature) {
        List<(IVisitableAstNode Node, IVisitableAstNode? Parent)> webReads = [];
        HashSet<IVisitableAstNode> explicitRoots = new(ReferenceEqualityComparer.Instance);
        List<Use> implicitUses = [];
        List<Use> killingUses = [];
        List<(ProcedureReturnHelperNode Helper, int Element)> returnUses = [];
        foreach (Use use in uses) {
            if (IsKillingUse(webRegister, use)) {
                killingUses.Add(use);
                continue;
            }
            if (Classify(webRegister, baseName, use, nonSubstitutable, defTargets, webReads,
                    explicitRoots)) {
                // A statement can read the register and partially define it in the same use (the
                // link to the previous version swallows the explicit read): the partial write still
                // needs the register to hold the local's value for the bytes it does not rewrite.
                if (PartialWriteNeedsRegister(webRegister, value, use)) {
                    implicitUses.Add(use);
                }
                continue;
            }
            if (use.Site.Terminator is not null and not IrExit) {
                return null;
            }
            if (ServedReturn(signature, MaskOf(webRegister), use) is { } served) {
                returnUses.Add(served);
                continue;
            }
            implicitUses.Add(use);
        }
        if (explicitRoots.Count == 0) {
            return null;
        }
        return new WebUses(webReads, explicitRoots, implicitUses, killingUses, returnUses);
    }

    /// <summary>The <c>return</c> element a typed return exit serves from the local, or
    /// <c>null</c> when the exit keeps a write-back: in a typed method, the C# return hands the
    /// outputs to the caller, which writes each register itself, so the exit's read of an output
    /// the web covers exactly (the register mask equals the output's) is an explicit read the
    /// local serves. Only a <see cref="ProcedureReturnHelperNode"/> transfer that cannot fault
    /// (a fault would observe the register) qualifies; any other exit use - a value
    /// outside <c>signature.Outputs</c>, or a partial-width web - stays an implicit use.</summary>
    private static (ProcedureReturnHelperNode Helper, int Element)? ServedReturn(
        TypedSignature? signature, CpuValues registerBytes, Use use) {
        if (signature is null || use.Site.Terminator is not IrExit {
                Transfer: ReturnNode { Value: ProcedureReturnHelperNode helper } } exit) {
            return null;
        }
        int element = -1;
        for (int index = 0; index < signature.Outputs.Count; index++) {
            if (signature.Outputs[index].Mask == registerBytes) {
                element = index;
            }
        }
        return element < 0 || exit.Effects.MayFault ? null : (helper, element);
    }

    /// <summary>Whether the web vanishes from the printed method entirely: every read is a node
    /// <paramref name="unprinted"/> replaces (a frame-slot access), no write-back is needed, no
    /// definition is a call, no typed return serves it, and every non-phi definition is a plain
    /// copy or constant with no effect besides the target write. With no implicit use the register
    /// value is never observed, so not writing it changes nothing; a served return still reads the
    /// register's final value, so dropping its definition would leave the return reading a stale
    /// register.</summary>
    private static bool DroppableWeb(WebDefinitions collected, WebUses classification,
        IReadOnlySet<IVisitableAstNode> unprinted, List<Use> writeBackUses) =>
        unprinted.Count > 0 && writeBackUses.Count == 0 && !collected.HasCall
        && classification.ReturnUses.Count == 0
        && classification.WebReads.All(read => unprinted.Contains(read.Node))
        && collected.Sites.All(definition => definition.Statement is null
            || definition.Statement.Node is BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode,
                Right: RegisterNode or ConstantNode });

    /// <summary>Whether printing the web as a local removes more register accesses than it adds:
    /// saved is the register reads and definition targets the local replaces, cost is the printed
    /// write-backs plus the declaration - plus, for a segment web, each use site touching a
    /// <see cref="SegmentedPointerNode"/> through that segment, since a pointer segment is never
    /// substituted and still needs the register. Refusing keeps the register form, which is
    /// always exact. A web whose every non-phi, non-call definition assigns a constant also keeps
    /// the register form when it needs a write-back.</summary>
    private static bool PrintsFewerRegisterAccesses(WebDefinitions definitions,
        WebUses classification, List<Use> uses,
        Dictionary<IrStatement, HashSet<IrSsaValue>> preservedByCall, IrSsaValue value,
        List<Use> writeBackUses, TypedParameter? entryParameter) {
        // A parameter web also removes its copy-in line, so it may break even where a plain web
        // would not.
        int saved = classification.WebReads.Count + definitions.DefTargets.Count
            + (entryParameter is null ? 0 : 1);
        int cost = writeBackUses.Count + 1;
        if (TargetNode(definitions.DefTargets[0]) is SegmentRegisterNode segment) {
            cost += uses
                .Where(use => UseRoots(use).SelectMany(IrAstChildren.Descendants)
                    .OfType<SegmentedPointerNode>()
                    .Any(pointer => pointer.Segment.Equals(segment)))
                .Select(UseSite)
                .Distinct()
                .Count();
        }
        if (saved <= cost) {
            return false;
        }
        if (writeBackUses.Count == 0) {
            return true;
        }
        return definitions.Sites.Any(definition => definition.Statement is { } statement
            && !IsCallDefinition(definition, value, preservedByCall)
            && !(statement.Node is BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN, Right: ConstantNode }));
    }

    /// <summary>The statement node or terminator roots a use reads from, for a pointer-segment
    /// lookup.</summary>
    private static IEnumerable<IVisitableAstNode> UseRoots(Use use) {
        if (use.Site.Statement is { } statement) {
            return [statement.Node];
        }
        if (use.Site.Terminator is { } terminator) {
            return IrAstChildren.RootsOf(terminator);
        }
        return [];
    }

    /// <summary>The signed-type vote of the web: reads under a same-width signed conversion and
    /// definitions from one on the signed side, every other read, definition and write-back on the
    /// unsigned side.</summary>
    private static (int Signed, int Unsigned) CountSignedVotes(ValueNode webRegister,
        List<(IVisitableAstNode Node, IVisitableAstNode? Parent)> webReads,
        List<Definition> definitions, List<Use> writeBackUses) {
        DataType signedType = DataType.SignedFromBitWidth(webRegister.DataType.BitWidth);
        int signedVotes = 0;
        int unsignedVotes = 0;
        foreach ((IVisitableAstNode _, IVisitableAstNode? parent) in webReads) {
            if (ConvertsTo(parent, signedType)) {
                signedVotes++;
            } else {
                unsignedVotes++;
            }
        }
        foreach (Definition definition in definitions) {
            if (definition.Statement?.Node is not BinaryOperationNode {
                    BinaryOperation: BinaryOperation.ASSIGN, Right: { } right }) {
                continue;
            }
            if (ConvertsFrom(right, signedType)) {
                signedVotes++;
            } else {
                unsignedVotes++;
            }
        }
        unsignedVotes += writeBackUses.Count;
        return (signedVotes, unsignedVotes);
    }

    /// <summary>The position of <paramref name="block"/> in the method's block list.</summary>
    private static int OrderOf(IrMethod method, IrBlock block) => method.Blocks.IndexOf(block);

    /// <summary>
    /// Picks the implicit uses that need a write-back: the register must hold the local's value at
    /// <paramref name="implicitUses"/>, unless it already holds it on every path to the use. The
    /// currency of the register is a forward must-dataflow over the block graph, like available
    /// expressions: a block starts current when every predecessor ends current (an entry block or a
    /// block without a predecessor starts stale); inside a block the same events as before decide -
    /// an earlier write-back or implicit use of the same web keeps it current, a web definition, a
    /// killing use or a statement writing a byte of the register (except a call that preserves it)
    /// makes it stale again. The rule is exact because a web is one local: any byte write of the
    /// register on a path to a use of the web creates a new SSA version, which is either a web
    /// definition (an event that resets the currency) or ends the web, so current on every
    /// predecessor end means the register holds the local at block start. Every chosen use is
    /// appended to <paramref name="writeBackUses"/>; <c>false</c> when one is not legal.
    /// </summary>
    private static bool PlanWriteBacks(IrMethod method, BlockGraph<IrBlock> graph, IrSsaValue value,
        CpuValues registerBytes, List<Definition> definitions, List<Use> implicitUses,
        List<Use> killingUses, List<Use> uses,
        Dictionary<IrStatement, HashSet<IrSsaValue>> preservedByCall, List<Use> writeBackUses) {
        Dictionary<IrBlock, List<WriteBackEvent>> eventsByBlock = [];
        void AddEvent(IrBlock block, WriteBackEvent writeBackEvent) {
            if (!eventsByBlock.TryGetValue(block, out List<WriteBackEvent>? events)) {
                events = [];
                eventsByBlock[block] = events;
            }
            events.Add(writeBackEvent);
        }
        foreach (Use use in implicitUses) {
            AddEvent(use.Site.Block, new WriteBackEvent(use.Position, 0, use, false));
        }
        foreach (Use use in killingUses) {
            AddEvent(use.Site.Block, new WriteBackEvent(use.Position, 0, use, true));
        }
        foreach (Definition definition in definitions.Where(definition =>
                definition.Statement is not null
                && !IsCallDefinition(definition, value, preservedByCall))) {
            AddEvent(definition.Block, new WriteBackEvent(definition.Index, 1, null, false));
        }
        foreach (List<WriteBackEvent> events in eventsByBlock.Values) {
            events.Sort((first, second) => first.Index != second.Index
                ? first.Index - second.Index : first.Order - second.Order);
        }
        HashSet<IrBlock> entries = [.. graph.EntryBlocks];
        Dictionary<IrBlock, bool> currentAtEnd =
            method.Blocks.ToDictionary(block => block, _ => true);
        bool CurrentAtStart(IrBlock block) {
            if (entries.Contains(block)) {
                return false;
            }
            IReadOnlyList<IrBlock> predecessors = graph.GetPredecessors(block);
            if (predecessors.Count == 0) {
                return false;
            }
            return predecessors.All(predecessor => currentAtEnd[predecessor]);
        }
        bool changed = true;
        while (changed) {
            changed = false;
            foreach (IrBlock block in method.Blocks) {
                bool end = eventsByBlock.TryGetValue(block, out List<WriteBackEvent>? events)
                    ? WalkWriteBackEvents(events, CurrentAtStart(block), value, registerBytes,
                        preservedByCall, null)
                    : CurrentAtStart(block);
                if (end != currentAtEnd[block]) {
                    currentAtEnd[block] = end;
                    changed = true;
                }
            }
        }
        foreach (IrBlock block in definitions.Select(definition => definition.Block)
                .Concat(uses.Select(use => use.Site.Block)).Distinct()) {
            if (!eventsByBlock.TryGetValue(block, out List<WriteBackEvent>? events)) {
                continue;
            }
            int first = writeBackUses.Count;
            WalkWriteBackEvents(events, CurrentAtStart(block), value, registerBytes,
                preservedByCall, writeBackUses);
            for (int index = first; index < writeBackUses.Count; index++) {
                if (!WriteBackLegal(block, value, definitions, writeBackUses[index])) {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>Runs the currency walk of one block's events from <paramref name="current"/> and
    /// returns the currency at its end: a killing use or a web definition makes the register stale,
    /// an implicit use takes a write-back into <paramref name="writeBackUses"/> (when not
    /// <c>null</c>) unless it is already current, and a use's own statement can disturb it again.
    /// Legality of the write-backs is not checked here.</summary>
    private static bool WalkWriteBackEvents(List<WriteBackEvent> events, bool current,
        IrSsaValue value, CpuValues registerBytes,
        Dictionary<IrStatement, HashSet<IrSsaValue>> preservedByCall, List<Use>? writeBackUses) {
        foreach (WriteBackEvent writeBackEvent in events) {
            if (writeBackEvent.Kills || writeBackEvent.Use is not { } use) {
                current = false;
                continue;
            }
            if (!current) {
                writeBackUses?.Add(use);
            }
            current = true;
            if (use.Site.Statement is { } statement
                && Disturbs(statement, value, registerBytes, preservedByCall)) {
                current = false;
            }
        }
        return current;
    }

    /// <summary>Whether <paramref name="definition"/> is a call statement that was proven to
    /// preserve <paramref name="value"/>.</summary>
    private static bool IsCallDefinition(Definition definition, IrSsaValue value,
        Dictionary<IrStatement, HashSet<IrSsaValue>> preservedByCall) =>
        definition.Statement is { } statement
        && preservedByCall.TryGetValue(statement, out HashSet<IrSsaValue>? preserved)
        && preserved.Contains(value);

    /// <summary>Whether <paramref name="statement"/> writes a byte of the register without being a
    /// call that preserves <paramref name="value"/>: after it the register no longer holds the
    /// local's value.</summary>
    private static bool Disturbs(IrStatement statement, IrSsaValue value, CpuValues registerBytes,
        Dictionary<IrStatement, HashSet<IrSsaValue>> preservedByCall) =>
        ((statement.Effects.MustWrite | statement.Effects.MayWrite) & registerBytes) != CpuValues.None
        && !(preservedByCall.TryGetValue(statement, out HashSet<IrSsaValue>? preserved)
            && preserved.Contains(value));

    /// <summary>The use's statement position in its block's current statement order; a use
    /// without a statement sits after the block.</summary>
    private static int PositionOf(IrSsaUse site) {
        if (site.Statement is not { } owner) {
            return site.Block.Statements.Count;
        }
        return site.Block.IndexOfStatement(owner);
    }

    /// <summary>The statement or terminator a use sits at; uses sharing a site are one use.</summary>
    private static object UseSite(Use use) {
        if (use.Site.Statement is { } statement) {
            return statement;
        }
        if (use.Site.Terminator is { } terminator) {
            return terminator;
        }
        return use.Site;
    }

    /// <summary>Whether <paramref name="use"/> of <paramref name="register"/>'s web is explicit:
    /// a statement or a branch or switch terminator whose register reads can print as the local,
    /// recorded into <paramref name="reads"/>. A fault site, a partial definition's version link or
    /// an exit terminator is implicit; so is a use whose copy still reads a byte of the local's
    /// register. The roots the substituted instances live in are added to
    /// <paramref name="explicitRoots"/>.</summary>
    private static bool Classify(ValueNode register, string name, Use use,
        HashSet<IVisitableAstNode> nonSubstitutable, IReadOnlyList<ValueNode> defTargets,
        List<(IVisitableAstNode Node, IVisitableAstNode? Parent)> reads,
        HashSet<IVisitableAstNode> explicitRoots) {
        IrSsaUse site = use.Site;
        IVisitableAstNode? root;
        bool mayFault;
        if (site.Statement is { } statement) {
            if (site.ConditionalNewVersion >= 0 || statement.Effects.MayFault) {
                return false;
            }
            root = statement.Node;
            mayFault = statement.Effects.MayFault;
        } else {
            switch (site.Terminator) {
                case IrBranch branch:
                    root = branch.Condition;
                    mayFault = branch.Effects.MayFault;
                    break;
                case IrSwitch @switch:
                    root = @switch.Value;
                    mayFault = @switch.Effects.MayFault;
                    break;
                default:
                    return false;
            }
        }
        List<(IVisitableAstNode Node, IVisitableAstNode? Parent)> replaced = [];
        HashSet<IVisitableAstNode> defTargetSet = new(defTargets,
            ReferenceEqualityComparer.Instance);
        int sharedTargetReads = 0;
        IVisitableAstNode copy = ReplaceReads(root, null);
        IrEffects copyEffects = IrEffectsVisitor.Of(copy, mayFault);
        if ((replaced.Count == 0 && sharedTargetReads == 0) || copyEffects.MayFault
            || (copyEffects.Reads & MaskOf(register)) != CpuValues.None) {
            return false;
        }
        reads.AddRange(replaced);
        static bool SpecialSlot(IVisitableAstNode node, IVisitableAstNode parent) => parent switch {
            BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assignment
                => ReferenceEquals(assignment.Left, node),
            TupleSwapNode or MultiplicationNode or DivisionNode or InterruptServiceCallNode
                or SegmentedPointerNode => true,
            _ => false
        };
        if (site.Statement is { } owner) {
            explicitRoots.Add(owner.Node);
        } else if (site.Terminator is { } terminator) {
            foreach (IVisitableAstNode terminatorRoot in IrAstChildren.RootsOf(terminator)) {
                explicitRoots.Add(terminatorRoot);
            }
        }
        return true;

        IVisitableAstNode ReplaceReads(IVisitableAstNode node, IVisitableAstNode? parent) {
            if (node is RegisterNode or CpuFlagNode && node.Equals(register)) {
                if (nonSubstitutable.Contains(node)) {
                    if (defTargetSet.Contains(node) && parent is not null
                        && !SpecialSlot(node, parent)) {
                        sharedTargetReads++;
                        return new RegisterLocalNode(register, name);
                    }
                    return node;
                }
                replaced.Add((node, parent));
                return new RegisterLocalNode(register, name);
            }
            return IrAstChildren.MapChildren(node, child => ReplaceReads(child, node));
        }
    }

    /// <summary>Whether <paramref name="use"/> is a statement that partially defines
    /// <paramref name="value"/> and keeps bytes of <paramref name="register"/>'s web it does not
    /// rewrite: the register must then hold the local's value before the statement runs, like the
    /// conditional link of <see cref="IrSsaUse.ConditionalNewVersion"/> already requires.</summary>
    private static bool PartialWriteNeedsRegister(ValueNode register, IrSsaValue value, Use use) {
        if (use.Site.Statement is not { } statement
            || (IrSsaValues.WriteBits(statement.Effects).Partial & (1u << (int)value)) == 0) {
            return false;
        }
        CpuValues written = statement.Effects.MustWrite & IrSsaValues.MaskOf(value);
        return (MaskOf(register) & ~written) != CpuValues.None;
    }

    /// <summary>Whether <paramref name="use"/> is a killing use: a statement that reads no byte of
    /// <paramref name="register"/> and overwrites all of it without faulting. The local never held
    /// the register there, so such a use takes no write-back and is not printed; it ends the
    /// register's currency in the block. The typical site is the version link of the next
    /// full-width definition (<see cref="IrSsaUse.ConditionalNewVersion"/>).</summary>
    private static bool IsKillingUse(ValueNode register, Use use) =>
        use.Site.Statement is { } statement
        && !statement.Effects.MayFault
        && (statement.Effects.Reads & MaskOf(register)) == CpuValues.None
        && (MaskOf(register) & ~statement.Effects.MustWrite) == CpuValues.None;

    /// <summary>Assignment targets, tuple-swap operands, division results and pointer segments may
    /// never print as a local: the printer needs the register there. The reads map substitutes by
    /// instance, so a node shared between a read and any of those positions may not be substituted
    /// at all.</summary>
    private static HashSet<IVisitableAstNode> NonSubstitutableInstances(IrMethod method) {
        HashSet<IVisitableAstNode> instances = new(ReferenceEqualityComparer.Instance);
        foreach (IrBlock block in method.Blocks) {
            foreach (IVisitableAstNode root in block.Statements.Select(statement => statement.Node)
                    .Concat(IrAstChildren.RootsOf(block.Terminator))) {
                foreach (IVisitableAstNode node in IrAstChildren.Descendants(root)) {
                    switch (node) {
                        case BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assignment:
                            instances.Add(assignment.Left);
                            break;
                        case TupleSwapNode swap:
                            instances.Add(swap.First);
                            instances.Add(swap.Second);
                            break;
                        case DivisionNode division:
                            instances.Add(division.Division.Low);
                            instances.Add(division.Division.High);
                            break;
                        case MultiplicationNode multiplication:
                            instances.Add(multiplication.Low);
                            instances.Add(multiplication.High);
                            break;
                        case InterruptServiceCallNode serviceCall:
                            foreach (ValueNode result in serviceCall.Results) {
                                instances.Add(result);
                            }
                            break;
                        case SegmentedPointerNode pointer:
                            instances.Add(pointer.Segment);
                            if (pointer.DefaultSegment is { } defaultSegment) {
                                instances.Add(defaultSegment);
                            }
                            break;
                    }
                }
            }
        }
        return instances;
    }

    /// <summary>Whether the write-back of <paramref name="implicitUse"/> is legal: no web
    /// definition sits earlier in the same instruction, no earlier statement of that instruction
    /// writes the value, and a terminator use is an exit.</summary>
    private static bool WriteBackLegal(IrBlock block, IrSsaValue value,
        List<Definition> definitions, Use implicitUse) {
        if (implicitUse.Site.Terminator is not null and not IrExit) {
            return false;
        }
        foreach (Definition definition in definitions) {
            if (definition.Statement is { } statement
                && ReferenceEquals(definition.Block, block)
                && statement.Origin.Equals(implicitUse.Origin)
                && definition.Index < implicitUse.Position) {
                return false;
            }
        }
        CpuValues bytes = IrSsaValues.MaskOf(value);
        for (int i = 0; i < implicitUse.Position && i < block.Statements.Count; i++) {
            IrStatement statement = block.Statements[i];
            if (implicitUse.Origin.Equals(statement.Origin)
                && ((statement.Effects.MustWrite | statement.Effects.MayWrite) & bytes) != CpuValues.None) {
                return false;
            }
        }
        return true;
    }

    /// <summary>Whether a mid-block entry lands inside the range between a web definition and a
    /// later use of the web in the same block (a phi definition counts at block start).</summary>
    private static bool MidBlockEntryInside(IrMethod method, List<Definition> definitions,
        List<Use> uses) {
        foreach (IrEntry entry in method.Entries) {
            IrBlock block = entry.Block;
            if (entry.Node.Equals(block.EntryOrigin)) {
                continue;
            }
            int position = block.IndexOfStatement(statement => statement.Origin.Equals(entry.Node));
            if (position < 0) {
                position = block.Statements.Count;
            }
            bool definedBefore = definitions.Any(definition => ReferenceEquals(definition.Block, block)
                && definition.Index < position);
            if (definedBefore && uses.Any(use => ReferenceEquals(use.Site.Block, block)
                    && use.Position >= position)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>The semantic name <paramref name="candidate"/>'s sites imply: the single
    /// sink name (a contract or callee parameter the web is passed to), the single source name (a
    /// callee output or frame slot it is defined from), a role name, or <see langword="null"/>
    /// for the generic fallback.</summary>
    private static string? CandidateHint(Candidate candidate,
        Dictionary<IVisitableAstNode, List<IVisitableAstNode>> occurrences,
        RoleNames.RoleNameContext roles) {
        IrSsaValue? maybeValue = TypedSignatures.ValueOf(candidate.Register);
        if (maybeValue is not { } value) {
            return null;
        }
        List<IVisitableAstNode> roots = [];
        List<IVisitableAstNode> definitions = [];
        List<CfgInstruction> origins = [];
        HashSet<IVisitableAstNode> readNodes = new(ReferenceEqualityComparer.Instance);
        foreach ((IVisitableAstNode node, IVisitableAstNode? _) in candidate.Reads) {
            readNodes.Add(node);
            if (occurrences.TryGetValue(node, out List<IVisitableAstNode>? readRoots)) {
                roots.AddRange(readRoots);
            }
        }
        foreach (Use use in candidate.Uses.ImplicitUses.Concat(candidate.Uses.KillingUses)) {
            if (use.Site.Statement is { } statement) {
                roots.Add(statement.Node);
            }
            if (use.Site.Terminator is { } terminator) {
                roots.AddRange(IrAstChildren.TerminatorNodes(terminator));
            }
            if (use.Origin is CfgInstruction instruction) {
                origins.Add(instruction);
            }
        }
        foreach (Definition site in candidate.Sites) {
            if (site.Statement is not { } definition) {
                continue;
            }
            roots.Add(definition.Node);
            definitions.Add(definition.Node);
            if (definition.Origin is CfgInstruction instruction) {
                origins.Add(instruction);
            }
        }
        HashSet<string> sinks = RoleNames.SinkNames(roots, value,
            candidate.Register as RegisterNode, readNodes, roles.Signatures);
        if (RoleNames.UniqueName(sinks) is { } sink) {
            return sink;
        }
        HashSet<string> sources = RoleNames.SourceNames(definitions, value,
            candidate.Register as RegisterNode, roles.Signatures, roles.FramePlan);
        if (RoleNames.UniqueName(sources) is { } source) {
            return source;
        }
        return RoleNames.RoleName(roots, value, candidate.Register as RegisterNode,
            candidate.Register is SegmentRegisterNode, origins);
    }

    /// <summary>Every root (statement node or terminator root) each register node instance occurs
    /// in, per occurrence: a node the parser shares between two positions lists every position's
    /// root, so instance-level substitution can be checked against them.</summary>
    private static Dictionary<IVisitableAstNode, List<IVisitableAstNode>> RegisterOccurrences(
        IrMethod method) {
        Dictionary<IVisitableAstNode, List<IVisitableAstNode>> occurrences =
            new(ReferenceEqualityComparer.Instance);
        foreach (IrBlock block in method.Blocks) {
            foreach (IVisitableAstNode root in block.Statements.Select(statement => statement.Node)
                    .Concat(IrAstChildren.RootsOf(block.Terminator))) {
                foreach (IVisitableAstNode node in IrAstChildren.Descendants(root)) {
                    if (node is not RegisterNode and not SegmentRegisterNode
                            and not CpuFlagNode) {
                        continue;
                    }
                    if (!occurrences.TryGetValue(node, out List<IVisitableAstNode>? roots)) {
                        roots = [];
                        occurrences[node] = roots;
                    }
                    roots.Add(root);
                }
            }
        }
        return occurrences;
    }

    /// <summary>Whether every occurrence of <paramref name="target"/> belongs to the same web as
    /// <paramref name="value"/>: an instance shared between the assignment target of
    /// <paramref name="definition"/> and an operand of the same statement (the parser gives
    /// <c>inc bx</c> and <c>and dx,imm</c> one node for target and operand) or into another
    /// statement or terminator root may print the local everywhere only when each such site's used
    /// version is a member of the web - otherwise it would rename a read of another version.</summary>
    private static bool OccurrencesInWeb(IrMethod method,
        Dictionary<IVisitableAstNode, List<IVisitableAstNode>> occurrences, ValueNode target,
        IrStatement definition, IrSsa ssa, IrSsaValue value, HashSet<int> memberVersions) {
        if (!occurrences.TryGetValue(target, out List<IVisitableAstNode>? roots)) {
            return true;
        }
        // Roots are whole statements: one def root can still hold the instance in operand
        // positions too (inc bx, cmc). Count positions in the definition itself - the operand
        // positions read the previous version, so they are only safe when that version is a
        // web member.
        int selfReads = IrAstChildren.Descendants(definition.Node)
            .Count(node => ReferenceEquals(node, target));
        foreach (IVisitableAstNode root in roots) {
            if (ReferenceEquals(root, definition.Node)) {
                continue;
            }
            if (SiteOf(method, root) is not { } site
                || !memberVersions.Contains(VersionUsedAt(ssa, site, value))) {
                return false;
            }
        }
        return selfReads <= 1 || memberVersions.Contains(ssa.VersionUsed(definition, value));
    }

    /// <summary>The statement or terminator a statement node or terminator root belongs to,
    /// resolved by identity over the method's blocks.</summary>
    private static object? SiteOf(IrMethod method, IVisitableAstNode root) {
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                if (ReferenceEquals(statement.Node, root)) {
                    return statement;
                }
            }
            if (IrAstChildren.RootsOf(block.Terminator).Contains(root)) {
                return block.Terminator;
            }
        }
        return null;
    }

    /// <summary>The version <paramref name="site"/> uses of <paramref name="value"/>, for a
    /// statement or terminator site.</summary>
    private static int VersionUsedAt(IrSsa ssa, object site, IrSsaValue value) =>
        site is IrStatement statement
            ? ssa.VersionUsed(statement, value)
            : ssa.VersionUsed((IrTerminator)site, value);

    /// <summary>Whether every substituted read instance occurs only inside the positions that
    /// substituted it: an instance shared with another position would print the local where it
    /// reads another version or holds a position the local may never take.</summary>
    private static bool ReadsStayInside(
        Dictionary<IVisitableAstNode, List<IVisitableAstNode>> occurrences,
        List<(IVisitableAstNode Node, IVisitableAstNode? Parent)> reads,
        HashSet<IVisitableAstNode> explicitRoots) {
        foreach ((IVisitableAstNode node, IVisitableAstNode? _) in reads) {
            if (occurrences.TryGetValue(node, out List<IVisitableAstNode>? roots)
                && roots.Any(root => !explicitRoots.Contains(root))) {
                return false;
            }
        }
        return true;
    }

    /// <summary>Whether <paramref name="candidate"/>'s live range overlaps an accepted same-name
    /// range of its block.</summary>
    private static bool Overlaps(List<(IrBlock Block, string Name, int Start, int End)> ranges,
        Candidate candidate) => ranges.Any(range => ReferenceEquals(range.Block, candidate.Block)
            && range.Name == candidate.BaseName
            && range.Start <= candidate.End && candidate.DefIndex <= range.End);

    /// <summary>Whether <paramref name="register"/> covers only bytes of <paramref name="value"/>,
    /// which also pins a segment register to its value.</summary>
    private static bool RegisterMatchesValue(ValueNode register, IrSsaValue value) {
        if (register is CpuFlagNode flag) {
            return TypedSignatures.ValueOf(flag) == value;
        }
        if (register is SegmentRegisterNode segment) {
            return value == (IrSsaValue)((int)IrSsaValue.Es + segment.RegisterIndex);
        }
        return value <= IrSsaValue.Edi
            && (MaskOf(register) & ~IrSsaValues.MaskOf(value)) == CpuValues.None;
    }

    /// <summary>The <see cref="CpuValues"/> a promotable target node stands for.</summary>
    private static CpuValues MaskOf(ValueNode target) => target switch {
        CpuFlagNode flag => IrValueSets.FromStatusFlags((StatusFlags)flag.FlagMask),
        RegisterNode register => IrValueSets.Of(register),
        _ => CpuValues.None
    };

    /// <summary>The register bytes a web definition target stands for: a register target's own
    /// bytes or a call output element's mask.</summary>
    private static CpuValues TargetMask(object target) => target is CallOutputTarget output
        ? output.Mask
        : MaskOf((ValueNode)target);

    /// <summary>A register node for a web definition target: the target itself, or a fresh node
    /// standing for the call output's register bytes.</summary>
    private static ValueNode TargetNode(object target) => target is ValueNode node
        ? node
        : IrValueSets.NodeOf(((CallOutputTarget)target).Mask);

    /// <summary>The node inside the write-back's boundary the insertion searches for: the implicit
    /// use's statement node, or the transfer of the exit terminator.</summary>
    private static IVisitableAstNode AnchorOf(Use implicitUse) {
        if (implicitUse.Site.Statement is { } statement) {
            return statement.Node;
        }
        if (implicitUse.Site.Terminator is IrExit exit) {
            return exit.Transfer;
        }
        throw new InvalidOperationException("A write-back use is neither a statement nor an exit.");
    }

    /// <summary>A register equal to <paramref name="register"/> but a distinct instance: the
    /// write-back's register and local must not share the target's instance, which the reads map
    /// prints as the local.</summary>
    private static ValueNode FreshRegister(ValueNode register) {
        if (register is SegmentRegisterNode segment) {
            return new SegmentRegisterNode(segment.RegisterIndex);
        }
        if (register is CpuFlagNode flag) {
            return new CpuFlagNode(flag.FlagMask);
        }
        return new RegisterNode(register.DataType, ((RegisterNode)register).RegisterIndex);
    }

    /// <summary>Whether <paramref name="node"/> converts a value to <paramref name="dataType"/>.</summary>
    private static bool ConvertsTo(IVisitableAstNode? node, DataType dataType) => node switch {
        TypeConversionNode conversion => conversion.DataType == dataType,
        ExplicitCastNode cast => cast.DataType == dataType,
        _ => false
    };

    /// <summary>Whether <paramref name="node"/> converts a value of <paramref name="dataType"/>.</summary>
    private static bool ConvertsFrom(IVisitableAstNode? node, DataType dataType) => node switch {
        TypeConversionNode conversion => conversion.Value.DataType == dataType,
        ExplicitCastNode { Value: ValueNode value } => value.DataType == dataType,
        _ => false
    };

    /// <summary>The local name of <paramref name="register"/>: the lowercase register name.</summary>
    private static string LocalName(ValueNode register) {
        if (register is CpuFlagNode flag) {
            return FlagLocalNames[(int)(TypedSignatures.ValueOf(flag) ?? IrSsaValue.Carry)
                - (int)IrSsaValue.Carry];
        }
        if (register is SegmentRegisterNode segment) {
            return RegisterNames.Segments[segment.RegisterIndex];
        }
        return register.DataType.BitWidth switch {
            BitWidth.BYTE_8 => ByteName(((RegisterNode)register).RegisterIndex),
            BitWidth.WORD_16 => RegisterNames.Word16[((RegisterNode)register).RegisterIndex],
            _ => $"e{RegisterNames.Word16[((RegisterNode)register).RegisterIndex]}"
        };
    }

    private static string ByteName(int registerIndex) {
        string half = UInt8HighLowRegistersIndexer.IsHigh((uint)registerIndex) ? "h" : "l";
        int parent = (int)UInt8HighLowRegistersIndexer.ComputeRegisterIndexInArray((uint)registerIndex);
        return $"{RegisterNames.Word16[parent][..1]}{half}";
    }

    /// <summary>One event of the currency walk: an implicit or killing use, or a web definition,
    /// sorted by statement index then order (uses before definitions at the same index).</summary>
    private sealed record WriteBackEvent(int Index, int Order, Use? Use, bool Kills);

    /// <summary>One use of a web version: its site and its statement position in its block.</summary>
    private sealed record Use(IrSsaUse Site, int Position) {
        public ICfgNode Origin {
            get {
                if (Site.Statement?.Origin is { } statementOrigin) {
                    return statementOrigin;
                }
                if (Site.Terminator?.Origin is { } terminatorOrigin) {
                    return terminatorOrigin;
                }
                throw new InvalidOperationException("A use is neither a statement nor a terminator.");
            }
        }
    }

    /// <summary>One definition of a web version: its block, its statement index (-1 for a phi at
    /// block start) and its defining statement when it has one.</summary>
    private sealed record Definition(IrBlock Block, int Index, IrStatement? Statement);

    /// <summary>A typed call output element a web was defined by: the (call, output index) pair
    /// the printed left side substitutes with the local's name through
    /// <see cref="RegisterLocalPlan.CallOutputs"/>, and the output's register bytes.</summary>
    private sealed record CallOutputTarget(ProcedureCallHelperNode Call, int Element,
        CpuValues Mask, TypedSignature Signature);

    /// <summary>The definitions <see cref="CollectWebDefinitions"/> collected: the targets of the
    /// web's definitions (a <see cref="ValueNode"/> register target or a
    /// <see cref="CallOutputTarget"/>), every definition site (a phi counts with index -1),
    /// whether a call counts as a definition, and whether a typed call output defines the web.
    /// </summary>
    private sealed record WebDefinitions(List<object> DefTargets, List<Definition> Sites,
        bool HasCall, bool HasCallOutput, IrBlock? EntryBlock);

    /// <summary>The uses <see cref="ClassifyWebUses"/> split: the explicit reads and their roots,
    /// the implicit uses that take a write-back, the killing uses that end the live range, and the
    /// typed-return exit uses the local serves through the <c>return</c> elements.</summary>
    private sealed record WebUses(
        List<(IVisitableAstNode Node, IVisitableAstNode? Parent)> WebReads,
        HashSet<IVisitableAstNode> ExplicitRoots, List<Use> ImplicitUses, List<Use> KillingUses,
        List<(ProcedureReturnHelperNode Helper, int Element)> ReturnUses);

    /// <summary>A web that may promote: a block of it, whether it lives in one block, the ordering
    /// keys of its first definition, the start and end of its live range for the single-block
    /// overlap rule, the local's register and base name, the definition targets and read nodes to
    /// substitute (each with its direct parent), the implicit uses that take a write-back, and the
    /// signed-type vote of the web: reads under a same-width signed conversion and definitions from
    /// one on the signed side, every other read, definition and write-back on the unsigned side.
    /// </summary>
    private sealed record Candidate(IrBlock Block, bool SingleBlock, int FirstBlockOrder,
        int FirstIndex, int DefIndex, int End, ValueNode Register, string BaseName,
        IReadOnlyList<object> DefTargets, IReadOnlyList<Definition> Sites,
        IReadOnlyList<(IVisitableAstNode Node, IVisitableAstNode? Parent)> Reads,
        IReadOnlyList<Use> WriteBackUses, int SignedVotes, int UnsignedVotes,
        SegmentedAddress FirstAddress, WebUses Uses, TypedParameter? Parameter);
}
