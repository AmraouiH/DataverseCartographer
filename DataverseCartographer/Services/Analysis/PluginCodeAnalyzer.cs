using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using DataverseCartographer.Models;

namespace DataverseCartographer.Services.Analysis
{
    /// <summary>
    /// Static analysis of a plugin assembly: for each plugin type, which tables its code creates, updates, deletes or reads.
    /// Heuristic, on the IL: table names come from string literals (new Entity("account"), QueryExpression, Retrieve, FetchXML),
    /// from early-bound classes ([EntityLogicalName]) and are combined with the SDK calls of the same method
    /// (IOrganizationService.Create / Update / Delete…, XxxRequest, OrganizationServiceContext). Calls to the other methods
    /// of the assembly are followed (helpers, services, base plugin class).
    /// </summary>
    internal sealed class PluginCodeAnalyzer
    {
        private const ushort AbstractFlag = 0x0400;
        private const ushort VirtualFlag = 0x0040;
        private const uint InterfaceFlag = 0x20;

        /// <summary>Methods followed at most from one plugin type (protects against huge ILMerged assemblies).</summary>
        private const int MaxReachableMethods = 6000;

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null))
            .GroupBy(o => o.Value)
            .ToDictionary(g => g.Key, g => g.First());

        private static readonly Regex FetchEntity = new Regex(@"<(?:link-)?entity\b[^>]*?\bname\s*=\s*['""]([A-Za-z0-9_]+)['""]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> ServiceTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IOrganizationService", "IOrganizationServiceAsync", "IOrganizationServiceAsync2", "OrganizationServiceProxy", "CrmServiceClient", "ServiceClient", "OrganizationServiceContext"
        };

        private readonly CliMetadata _md;
        private readonly HashSet<string> _entities;
        private readonly int[] _methodOwner;
        private readonly Dictionary<int, string> _typeNames = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _logicalNames = new Dictionary<int, string>();
        private readonly Dictionary<int, MethodFacts> _facts = new Dictionary<int, MethodFacts>();

        // Indexes (an ILMerged plugin can hold tens of thousands of methods)
        private readonly string[] _methodNames;
        private readonly List<int>[] _methodsByType;
        private readonly Dictionary<string, List<int>> _methodsBySimpleName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        private readonly int[] _enclosing;
        private readonly int[] _baseType;
        private readonly Dictionary<int, List<int>> _interfaces = new Dictionary<int, List<int>>();
        private readonly Dictionary<long, bool> _derives = new Dictionary<long, bool>();

        /// <summary>Last request built in the method being scanned: its table comes later, from « Target = new EntityReference("x", id) ».</summary>
        private PendingOperation _currentRequest;

        private sealed class PendingOperation
        {
            public string Operation;
            public string Entity;
            public bool UsesConstructed;
        }

        private sealed class MethodFacts
        {
            public string Name;
            public readonly List<PendingOperation> Operations = new List<PendingOperation>();
            public readonly HashSet<string> Constructed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Reads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<int> Callees = new List<int>();
        }

        public PluginCodeAnalyzer(byte[] assembly, IEnumerable<string> entityNames)
        {
            _md = new CliMetadata(assembly);
            _entities = new HashSet<string>(entityNames, StringComparer.OrdinalIgnoreCase);

            // Owner type of each method (TypeDef.MethodList ranges)
            var methods = _md.Rows(CliMetadata.MethodDef);
            var types = _md.Rows(CliMetadata.TypeDef);
            _methodOwner = new int[methods + 1];
            _methodsByType = new List<int>[types + 1];
            _baseType = new int[types + 1];
            _enclosing = new int[types + 1];
            for (var t = 1; t <= types; t++)
            {
                _methodsByType[t] = new List<int>();
                var first = (int)_md.Get(CliMetadata.TypeDef, t, 5);
                var last = t < types ? (int)_md.Get(CliMetadata.TypeDef, t + 1, 5) : methods + 1;
                for (var m = first; m < last && m <= methods; m++)
                {
                    _methodOwner[m] = t;
                    _methodsByType[t].Add(m);
                }
                var extends = _md.Get(CliMetadata.TypeDef, t, 3);
                _baseType[t] = (extends & 3) == 0 ? (int)(extends >> 2) : 0;
            }

            _methodNames = new string[methods + 1];
            for (var m = 1; m <= methods; m++)
            {
                var name = _md.String(_md.Get(CliMetadata.MethodDef, m, 3));
                _methodNames[m] = name;
                // Explicit interface implementations are named « Namespace.IService.Method ».
                var separator = name.LastIndexOf('.');
                var simple = separator > 0 ? name.Substring(separator + 1) : name;
                if (!_methodsBySimpleName.TryGetValue(simple, out var list)) _methodsBySimpleName[simple] = list = new List<int>();
                list.Add(m);
            }

            for (var i = 1; i <= _md.Rows(CliMetadata.NestedClass); i++)
            {
                var nested = (int)_md.Get(CliMetadata.NestedClass, i, 0);
                if (nested > 0 && nested <= types) _enclosing[nested] = (int)_md.Get(CliMetadata.NestedClass, i, 1);
            }
            for (var i = 1; i <= _md.Rows(CliMetadata.InterfaceImpl); i++)
            {
                var implemented = _md.Get(CliMetadata.InterfaceImpl, i, 1);
                if ((implemented & 3) != 0) continue;
                var type = (int)_md.Get(CliMetadata.InterfaceImpl, i, 0);
                if (!_interfaces.TryGetValue(type, out var list)) _interfaces[type] = list = new List<int>();
                list.Add((int)(implemented >> 2));
            }

            ReadEarlyBoundNames();
        }

        #region Types

        /// <summary>Full name of a TypeDef, nested types as « Outer+Inner » (like plugintype.typename).</summary>
        public string TypeDefName(int row)
        {
            if (_typeNames.TryGetValue(row, out var cached)) return cached;
            var name = _md.String(_md.Get(CliMetadata.TypeDef, row, 1));
            var ns = _md.String(_md.Get(CliMetadata.TypeDef, row, 2));
            var enclosing = _enclosing[row];
            var full = enclosing > 0 && enclosing != row ? TypeDefName(enclosing) + "+" + name : string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            _typeNames[row] = full;
            return full;
        }

        private (string Namespace, string Name) TypeRefName(int row) =>
            (_md.String(_md.Get(CliMetadata.TypeRef, row, 2)), _md.String(_md.Get(CliMetadata.TypeRef, row, 1)));

        /// <summary>Namespace and name of a TypeDefOrRef coded index (generic instances reduced to their generic type).</summary>
        private (string Namespace, string Name, int TypeDefRow) DecodeType(uint coded)
        {
            var row = (int)(coded >> 2);
            switch (coded & 3)
            {
                case 0:
                    return (_md.String(_md.Get(CliMetadata.TypeDef, row, 2)), _md.String(_md.Get(CliMetadata.TypeDef, row, 1)), row);
                case 1:
                    var reference = TypeRefName(row);
                    return (reference.Namespace, reference.Name, 0);
                default:
                    return TypeSpecName(row);
            }
        }

        private (string Namespace, string Name, int TypeDefRow) TypeSpecName(int row)
        {
            var p = _md.Blob(_md.Get(CliMetadata.TypeSpec, row, 0), out var length);
            if (length < 2) return (string.Empty, string.Empty, 0);
            var kind = _md.Byte(p);
            if (kind == 0x15) // GENERICINST
            {
                p++;
                var element = _md.Byte(p++);
                if (element == 0x12 || element == 0x11) return DecodeType(_md.CompressedUInt(ref p));
            }
            else if (kind == 0x12 || kind == 0x11)
            {
                p++;
                return DecodeType(_md.CompressedUInt(ref p));
            }
            return (string.Empty, string.Empty, 0);
        }

        /// <summary>Early-bound classes: [EntityLogicalName("account")] on the TypeDefs of the assembly.</summary>
        private void ReadEarlyBoundNames()
        {
            for (var i = 1; i <= _md.Rows(CliMetadata.CustomAttribute); i++)
            {
                var parent = _md.Get(CliMetadata.CustomAttribute, i, 0);
                if ((parent & 0x1F) != 3) continue; // TypeDef
                var constructor = _md.Get(CliMetadata.CustomAttribute, i, 1);
                string attributeName;
                var row = (int)(constructor >> 3);
                switch (constructor & 7)
                {
                    case 2:
                        attributeName = _md.String(_md.Get(CliMetadata.TypeDef, _methodOwner[row], 1));
                        break;
                    case 3:
                        attributeName = MemberParent(row).Name;
                        break;
                    default:
                        continue;
                }
                if (attributeName != "EntityLogicalNameAttribute") continue;

                var p = _md.Blob(_md.Get(CliMetadata.CustomAttribute, i, 2), out var length);
                if (length < 3) continue;
                p += 2; // prolog 0x0001
                if (_md.Byte(p) == 0xFF) continue;
                var size = (int)_md.CompressedUInt(ref p);
                var bytes = new byte[size];
                for (var k = 0; k < size; k++) bytes[k] = _md.Byte(p + k);
                _logicalNames[(int)(parent >> 5)] = System.Text.Encoding.UTF8.GetString(bytes);
            }
        }

        private (string Namespace, string Name, int TypeDefRow) MemberParent(int memberRef)
        {
            var parent = _md.Get(CliMetadata.MemberRef, memberRef, 0);
            var row = (int)(parent >> 3);
            switch (parent & 7)
            {
                case 0: return (_md.String(_md.Get(CliMetadata.TypeDef, row, 2)), _md.String(_md.Get(CliMetadata.TypeDef, row, 1)), row);
                case 1:
                    var reference = TypeRefName(row);
                    return (reference.Namespace, reference.Name, 0);
                case 3:
                    var owner = _methodOwner[row];
                    return (_md.String(_md.Get(CliMetadata.TypeDef, owner, 2)), _md.String(_md.Get(CliMetadata.TypeDef, owner, 1)), owner);
                case 4: return TypeSpecName(row);
                default: return (string.Empty, string.Empty, 0);
            }
        }

        /// <summary>Table of a class: [EntityLogicalName] of the assembly, or the class name when it is a table name (early-bound class of another DLL).</summary>
        private string LogicalNameOf((string Namespace, string Name, int TypeDefRow) type)
        {
            if (type.TypeDefRow > 0 && _logicalNames.TryGetValue(type.TypeDefRow, out var name)) return name;
            var candidate = type.Name?.ToLowerInvariant();
            return candidate != null && _entities.Contains(candidate) && type.Namespace != "Microsoft.Xrm.Sdk" ? candidate : null;
        }

        #endregion

        #region Methods

        private sealed class CallTarget
        {
            public string Namespace;
            public string Type;
            public string Method;
            public int TypeDefRow;
            public int MethodDefRow;
            public string GenericArgumentEntity;
        }

        private CallTarget ResolveCall(int token)
        {
            var row = token & 0x00FFFFFF;
            switch (token >> 24)
            {
                case 0x06:
                {
                    var owner = _methodOwner[row];
                    return new CallTarget
                    {
                        Namespace = _md.String(_md.Get(CliMetadata.TypeDef, owner, 2)),
                        Type = _md.String(_md.Get(CliMetadata.TypeDef, owner, 1)),
                        Method = _md.String(_md.Get(CliMetadata.MethodDef, row, 3)),
                        TypeDefRow = owner,
                        MethodDefRow = row
                    };
                }
                case 0x0A:
                {
                    var parent = MemberParent(row);
                    return new CallTarget
                    {
                        Namespace = parent.Namespace,
                        Type = parent.Name,
                        Method = _md.String(_md.Get(CliMetadata.MemberRef, row, 1)),
                        TypeDefRow = parent.TypeDefRow
                    };
                }
                case 0x2B:
                {
                    var method = _md.Get(CliMetadata.MethodSpec, row, 0);
                    var target = ResolveCall((int)((method & 1) == 0 ? 0x06000000 : 0x0A000000) | (int)(method >> 1));
                    var p = _md.Blob(_md.Get(CliMetadata.MethodSpec, row, 1), out var length);
                    if (target != null && length > 2 && _md.Byte(p) == 0x0A)
                    {
                        p += 1;
                        var count = _md.CompressedUInt(ref p);
                        var element = _md.Byte(p++);
                        if (count >= 1 && (element == 0x12 || element == 0x11)) target.GenericArgumentEntity = LogicalNameOf(DecodeType(_md.CompressedUInt(ref p)));
                    }
                    return target;
                }
                default:
                    return null;
            }
        }

        private MethodFacts Facts(int method)
        {
            if (_facts.TryGetValue(method, out var facts)) return facts;
            facts = new MethodFacts
            {
                Name = TypeDefName(_methodOwner[method]) + "." + _methodNames[method]
            };
            _facts[method] = facts;

            byte[] code;
            try
            {
                code = _md.MethodBody(_md.Get(CliMetadata.MethodDef, method, 0));
            }
            catch (System.IO.InvalidDataException)
            {
                return facts;
            }
            if (code != null) Scan(code, facts);
            return facts;
        }

        /// <summary>Walks the IL of a method and records the table names, SDK calls and calls to other methods of the assembly.</summary>
        private void Scan(byte[] code, MethodFacts facts)
        {
            string lastEntity = null;
            string lastString = null;
            _currentRequest = null;
            var p = 0;
            while (p < code.Length)
            {
                short value = code[p++];
                if (value == 0xFE && p < code.Length) value = (short)(0xFE00 | code[p++]);
                if (!OpCodesByValue.TryGetValue(value, out var opcode)) return;

                var operand = 0;
                switch (opcode.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        p += 1;
                        break;
                    case OperandType.InlineVar:
                        p += 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        p += 8;
                        break;
                    case OperandType.InlineSwitch:
                        if (p + 4 > code.Length) return;
                        var targets = BitConverter.ToInt32(code, p);
                        p += 4 + 4 * targets;
                        break;
                    default:
                        if (p + 4 > code.Length) return;
                        operand = BitConverter.ToInt32(code, p);
                        p += 4;
                        break;
                }

                if (opcode.Value == OpCodes.Ldstr.Value)
                {
                    string text;
                    try
                    {
                        text = _md.UserString(operand);
                    }
                    catch (IndexOutOfRangeException)
                    {
                        continue;
                    }
                    lastString = text;
                    if (_entities.Contains(text))
                    {
                        lastEntity = text.ToLowerInvariant();
                    }
                    else if (text.IndexOf("<fetch", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("<entity", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        foreach (Match match in FetchEntity.Matches(text))
                        {
                            if (_entities.Contains(match.Groups[1].Value)) facts.Reads.Add(match.Groups[1].Value.ToLowerInvariant());
                        }
                    }
                    continue;
                }

                var isCall = opcode.Value == OpCodes.Call.Value || opcode.Value == OpCodes.Callvirt.Value;
                var isNew = opcode.Value == OpCodes.Newobj.Value;
                if (!isCall && !isNew) continue;

                CallTarget target;
                try
                {
                    target = ResolveCall(operand);
                }
                catch (Exception error) when (error is IndexOutOfRangeException || error is ArgumentOutOfRangeException || error is System.IO.InvalidDataException)
                {
                    continue;
                }
                if (target == null) continue;
                if (target.MethodDefRow > 0) facts.Callees.Add(target.MethodDefRow);

                if (isNew) OnConstructor(target, facts, lastEntity, lastString);
                else OnCall(target, facts, lastEntity);
            }
        }

        private void OnConstructor(CallTarget target, MethodFacts facts, string lastEntity, string lastString)
        {
            var sdk = target.Namespace.StartsWith("Microsoft.Xrm.Sdk", StringComparison.Ordinal) || target.Namespace.StartsWith("Microsoft.Crm.Sdk", StringComparison.Ordinal);
            if (sdk && target.Type == "Entity")
            {
                if (lastEntity != null) facts.Constructed.Add(lastEntity);
                return;
            }
            if (sdk && (target.Type == "QueryExpression" || target.Type == "QueryByAttribute"))
            {
                if (lastEntity != null) facts.Reads.Add(lastEntity);
                return;
            }
            if (target.Type.EndsWith("Request", StringComparison.Ordinal) && target.Type.Length > 7)
            {
                var operation = target.Type.Substring(0, target.Type.Length - 7);
                switch (operation)
                {
                    case "Create":
                    case "Update":
                    case "Upsert":
                        facts.Operations.Add(new PendingOperation { Operation = operation, UsesConstructed = true });
                        return;
                    case "Retrieve":
                    case "RetrieveMultiple":
                        if (lastEntity != null) facts.Reads.Add(lastEntity);
                        return;
                    case "Organization":
                        // new OrganizationRequest("new_MyAction"): custom API / action call
                        if (!string.IsNullOrEmpty(lastString) && !_entities.Contains(lastString)) facts.Operations.Add(new PendingOperation { Operation = "Execute " + lastString });
                        return;
                    case "Execute":
                    case "ExecuteMultiple":
                    case "ExecuteTransaction":
                        return;
                    default:
                        if (sdk || target.TypeDefRow > 0)
                        {
                            // Object initializer: the request is built before its Target is set (see set_Target).
                            _currentRequest = new PendingOperation { Operation = operation };
                            facts.Operations.Add(_currentRequest);
                        }
                        return;
                }
            }

            var logical = LogicalNameOf((target.Namespace, target.Type, target.TypeDefRow));
            if (logical != null) facts.Constructed.Add(logical);
        }

        private void OnCall(CallTarget target, MethodFacts facts, string lastEntity)
        {
            var method = target.Method;
            if (target.Type == "Entity" && method == "set_LogicalName" && lastEntity != null)
            {
                facts.Constructed.Add(lastEntity);
                return;
            }
            if (method == "set_Target" && target.Type.EndsWith("Request", StringComparison.Ordinal) && _currentRequest != null)
            {
                _currentRequest.Entity = lastEntity;
                _currentRequest = null;
                return;
            }
            if ((target.Type == "QueryExpression" || target.Type == "QueryByAttribute") && method == "set_EntityName" && lastEntity != null)
            {
                facts.Reads.Add(lastEntity);
                return;
            }

            if (ServiceTypes.Contains(target.Type) || target.Type.EndsWith("ServiceContext", StringComparison.Ordinal))
            {
                if (method.EndsWith("Async", StringComparison.Ordinal)) method = method.Substring(0, method.Length - 5);
                switch (method)
                {
                    case "Create":
                    case "Update":
                    case "Upsert":
                        facts.Operations.Add(new PendingOperation { Operation = method, UsesConstructed = true });
                        return;
                    case "AddObject":
                        facts.Operations.Add(new PendingOperation { Operation = "Create", UsesConstructed = true });
                        return;
                    case "UpdateObject":
                        facts.Operations.Add(new PendingOperation { Operation = "Update", UsesConstructed = true });
                        return;
                    case "DeleteObject":
                        facts.Operations.Add(new PendingOperation { Operation = "Delete", UsesConstructed = true });
                        return;
                    case "Delete":
                    case "Associate":
                    case "Disassociate":
                        facts.Operations.Add(new PendingOperation { Operation = method, Entity = lastEntity });
                        return;
                    case "Retrieve":
                        if (lastEntity != null) facts.Reads.Add(lastEntity);
                        return;
                    case "CreateQuery":
                        var queried = target.GenericArgumentEntity ?? lastEntity;
                        if (queried != null) facts.Reads.Add(queried);
                        return;
                }
            }

            // Early-bound LINQ: context.AccountSet
            if (method.StartsWith("get_", StringComparison.Ordinal) && method.EndsWith("Set", StringComparison.Ordinal) && method.Length > 7)
            {
                var candidate = method.Substring(4, method.Length - 7).ToLowerInvariant();
                if (_entities.Contains(candidate)) facts.Reads.Add(candidate);
            }
        }

        #endregion

        #region Plugin types

        /// <summary>Operations of every requested plugin type (keyed by full type name). Types not found in the assembly are absent.</summary>
        public Dictionary<string, List<CodeOperation>> Analyze(IEnumerable<string> pluginTypeNames)
        {
            var typesByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var t = 1; t <= _md.Rows(CliMetadata.TypeDef); t++) typesByName[TypeDefName(t)] = t;

            var result = new Dictionary<string, List<CodeOperation>>(StringComparer.Ordinal);
            foreach (var name in pluginTypeNames.Distinct())
            {
                if (name == null || !typesByName.TryGetValue(name, out var type)) continue;
                result[name] = AnalyzeType(type);
            }
            return result;
        }

        private int BaseTypeDef(int type) => type > 0 && type < _baseType.Length ? _baseType[type] : 0;

        private IEnumerable<int> MethodsOf(int type) => type > 0 && type < _methodsByType.Length ? _methodsByType[type] : Enumerable.Empty<int>();

        /// <summary>The type is the ancestor, or inherits from it, or implements it (directly or through its base types).</summary>
        private bool DerivesFrom(int type, int ancestor)
        {
            var key = ((long)type << 32) | (uint)ancestor;
            if (_derives.TryGetValue(key, out var known)) return known;
            var result = false;
            var guard = 0;
            for (var t = type; t > 0 && guard++ < 64; t = BaseTypeDef(t))
            {
                if (t == ancestor || _interfaces.TryGetValue(t, out var implemented) && implemented.Any(i => i == ancestor || i != t && DerivesFrom(i, ancestor)))
                {
                    result = true;
                    break;
                }
            }
            _derives[key] = result;
            return result;
        }

        private List<CodeOperation> AnalyzeType(int type)
        {
            // Inheritance chain of the plugin inside the assembly: virtual calls of the base class resolve to this plugin's overrides.
            var chain = new List<int>();
            for (var t = type; t > 0 && !chain.Contains(t); t = BaseTypeDef(t)) chain.Add(t);

            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            foreach (var t in chain)
            {
                foreach (var m in MethodsOf(t)) queue.Enqueue(m);
            }

            while (queue.Count > 0 && visited.Count < MaxReachableMethods)
            {
                var method = queue.Dequeue();
                if (!visited.Add(method)) continue;
                foreach (var callee in Facts(method).Callees)
                {
                    foreach (var target in Dispatch(callee, chain)) if (!visited.Contains(target)) queue.Enqueue(target);
                }
            }

            var operations = new List<CodeOperation>();
            var unresolved = new List<(string Operation, string Method)>();
            var usedConstructed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allConstructed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var method in visited)
            {
                var facts = Facts(method);
                allConstructed.UnionWith(facts.Constructed);
                foreach (var operation in facts.Operations)
                {
                    if (operation.UsesConstructed)
                    {
                        if (facts.Constructed.Count == 0)
                        {
                            unresolved.Add((operation.Operation, facts.Name));
                            continue;
                        }
                        foreach (var entity in facts.Constructed)
                        {
                            operations.Add(new CodeOperation { Operation = operation.Operation, Entity = entity, Method = facts.Name });
                            usedConstructed.Add(entity);
                        }
                    }
                    else
                    {
                        operations.Add(new CodeOperation { Operation = operation.Operation, Entity = operation.Entity, Method = facts.Name });
                    }
                }
                foreach (var read in facts.Reads)
                {
                    operations.Add(new CodeOperation { Operation = "Retrieve", Entity = read, Method = facts.Name });
                }
            }

            // Entity built in one method, saved in another: link them, flagged as inferred.
            var orphans = allConstructed.Except(usedConstructed, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var (operation, method) in unresolved)
            {
                if (orphans.Count == 0 || orphans.Count > 3)
                {
                    operations.Add(new CodeOperation { Operation = operation, Method = method });
                    continue;
                }
                foreach (var entity in orphans)
                {
                    operations.Add(new CodeOperation { Operation = operation, Entity = entity, Method = method, Inferred = true });
                }
            }

            return operations
                .GroupBy(o => (o.Operation, Entity: o.Entity ?? string.Empty))
                .Select(g => g.OrderBy(o => o.Inferred).First())
                .OrderBy(o => o.IsRead)
                .ThenBy(o => o.Entity ?? "~")
                .ThenBy(o => o.Operation)
                .ToList();
        }

        /// <summary>
        /// Methods possibly run by a call: the method itself, or for an abstract / virtual / interface method,
        /// the override of the plugin's chain (base class pattern) or every implementation of the assembly.
        /// </summary>
        private IEnumerable<int> Dispatch(int callee, List<int> chain)
        {
            var flags = (ushort)_md.Get(CliMetadata.MethodDef, callee, 2);
            var owner = _methodOwner[callee];
            var isInterface = (_md.Get(CliMetadata.TypeDef, owner, 0) & InterfaceFlag) != 0;
            var polymorphic = isInterface || (flags & (AbstractFlag | VirtualFlag)) != 0;
            if ((flags & AbstractFlag) == 0) yield return callee;
            if (!polymorphic) yield break;

            var name = _methodNames[callee];
            if (chain.Contains(owner))
            {
                foreach (var type in chain)
                {
                    if (type == owner) yield break;
                    var match = MethodsOf(type).FirstOrDefault(m => _methodNames[m] == name);
                    if (match > 0)
                    {
                        yield return match;
                        yield break;
                    }
                }
                yield break;
            }

            if (!_methodsBySimpleName.TryGetValue(name, out var candidates)) yield break;
            foreach (var m in candidates)
            {
                if (m != callee && _methodOwner[m] != owner && DerivesFrom(_methodOwner[m], owner)) yield return m;
            }
        }

        #endregion
    }
}
