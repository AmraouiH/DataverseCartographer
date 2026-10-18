using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DataverseCartographer.Services.Analysis
{
    /// <summary>
    /// Minimal reader of the ECMA-335 metadata of a .NET assembly (PE file): tables, heaps and method bodies.
    /// The assembly is never loaded, so any plugin DLL can be read whatever the SDK version it was built against.
    /// </summary>
    internal sealed class CliMetadata
    {
        #region Table ids

        public const int Module = 0x00, TypeRef = 0x01, TypeDef = 0x02, FieldPtr = 0x03, Field = 0x04, MethodPtr = 0x05, MethodDef = 0x06,
            ParamPtr = 0x07, Param = 0x08, InterfaceImpl = 0x09, MemberRef = 0x0A, Constant = 0x0B, CustomAttribute = 0x0C, FieldMarshal = 0x0D,
            DeclSecurity = 0x0E, ClassLayout = 0x0F, FieldLayout = 0x10, StandAloneSig = 0x11, EventMap = 0x12, EventPtr = 0x13, Event = 0x14,
            PropertyMap = 0x15, PropertyPtr = 0x16, Property = 0x17, MethodSemantics = 0x18, MethodImpl = 0x19, ModuleRef = 0x1A, TypeSpec = 0x1B,
            ImplMap = 0x1C, FieldRva = 0x1D, EncLog = 0x1E, EncMap = 0x1F, Assembly = 0x20, AssemblyProcessor = 0x21, AssemblyOs = 0x22,
            AssemblyRef = 0x23, AssemblyRefProcessor = 0x24, AssemblyRefOs = 0x25, File = 0x26, ExportedType = 0x27, ManifestResource = 0x28,
            NestedClass = 0x29, GenericParam = 0x2A, MethodSpec = 0x2B, GenericParamConstraint = 0x2C;

        #endregion

        #region Coded indexes

        private static readonly int[] TypeDefOrRefTables = { TypeDef, TypeRef, TypeSpec };
        private static readonly int[] HasConstantTables = { Field, Param, Property };
        private static readonly int[] HasCustomAttributeTables =
        {
            MethodDef, Field, TypeRef, TypeDef, Param, InterfaceImpl, MemberRef, Module, DeclSecurity, Property, Event, StandAloneSig, ModuleRef,
            TypeSpec, Assembly, AssemblyRef, File, ExportedType, ManifestResource, GenericParam, GenericParamConstraint, MethodSpec
        };
        private static readonly int[] HasFieldMarshalTables = { Field, Param };
        private static readonly int[] HasDeclSecurityTables = { TypeDef, MethodDef, Assembly };
        private static readonly int[] MemberRefParentTables = { TypeDef, TypeRef, ModuleRef, MethodDef, TypeSpec };
        private static readonly int[] HasSemanticsTables = { Event, Property };
        private static readonly int[] MethodDefOrRefTables = { MethodDef, MemberRef };
        private static readonly int[] MemberForwardedTables = { Field, MethodDef };
        private static readonly int[] ImplementationTables = { File, AssemblyRef, ExportedType };
        private static readonly int[] CustomAttributeTypeTables = { -1, -1, MethodDef, MemberRef, -1 };
        private static readonly int[] ResolutionScopeTables = { Module, ModuleRef, AssemblyRef, TypeRef };
        private static readonly int[] TypeOrMethodDefTables = { TypeDef, MethodDef };

        #endregion

        private readonly byte[] _data;
        private readonly List<(uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize)> _sections = new List<(uint, uint, uint, uint)>();
        private int _strings, _userStrings, _blob;
        private int _stringSize = 2, _guidSize = 2, _blobSize = 2;

        private readonly int[] _rows = new int[64];
        private readonly int[] _tableOffset = new int[64];
        private readonly int[][] _columns = new int[64][];
        private readonly int[] _rowSize = new int[64];

        public CliMetadata(byte[] data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            ReadPe();
        }

        public int Rows(int table) => _rows[table];

        #region PE / metadata root

        private void ReadPe()
        {
            if (_data.Length < 0x80 || _data[0] != 'M' || _data[1] != 'Z') throw new InvalidDataException("Not a PE file.");
            var pe = I32(0x3C);
            if (U32(pe) != 0x00004550) throw new InvalidDataException("Invalid PE signature.");
            var coff = pe + 4;
            var sectionCount = U16(coff + 2);
            var optionalSize = U16(coff + 16);
            var optional = coff + 20;
            var magic = U16(optional);
            var directories = optional + (magic == 0x20B ? 112 : 96);
            var cliRva = U32(directories + 14 * 8);
            if (cliRva == 0) throw new InvalidDataException("Not a .NET assembly.");

            var section = optional + optionalSize;
            for (var i = 0; i < sectionCount; i++, section += 40)
            {
                _sections.Add((U32(section + 12), U32(section + 8), U32(section + 20), U32(section + 16)));
            }

            var cli = Offset(cliRva);
            var root = Offset(U32(cli + 8));
            if (U32(root) != 0x424A5342) throw new InvalidDataException("Invalid metadata signature.");
            var versionLength = I32(root + 12);
            var p = root + 16 + versionLength;
            var streams = U16(p + 2);
            p += 4;
            var tables = -1;
            for (var i = 0; i < streams; i++)
            {
                var offset = I32(p);
                var start = p + 8;
                var end = start;
                while (_data[end] != 0) end++;
                var name = Encoding.ASCII.GetString(_data, start, end - start);
                switch (name)
                {
                    case "#~":
                    case "#-":
                        tables = root + offset;
                        break;
                    case "#Strings":
                        _strings = root + offset;
                        break;
                    case "#US":
                        _userStrings = root + offset;
                        break;
                    case "#Blob":
                        _blob = root + offset;
                        break;
                }
                p = start + ((end - start + 1 + 3) & ~3);
            }
            if (tables < 0) throw new InvalidDataException("No metadata tables.");
            ReadTables(tables);
        }

        private void ReadTables(int stream)
        {
            var heapSizes = _data[stream + 6];
            if ((heapSizes & 0x01) != 0) _stringSize = 4;
            if ((heapSizes & 0x02) != 0) _guidSize = 4;
            if ((heapSizes & 0x04) != 0) _blobSize = 4;
            var valid = BitConverter.ToUInt64(_data, stream + 8);
            var p = stream + 24;
            for (var i = 0; i < 64; i++)
            {
                if ((valid & (1UL << i)) == 0) continue;
                _rows[i] = I32(p);
                p += 4;
            }
            if ((heapSizes & 0x40) != 0) p += 4; // extra data (EnC)

            for (var i = 0; i < 64; i++)
            {
                _columns[i] = Schema(i);
                var size = 0;
                foreach (var column in _columns[i]) size += column;
                _rowSize[i] = size;
            }
            for (var i = 0; i < 64; i++)
            {
                if (_rows[i] == 0) continue;
                if (_columns[i].Length == 0) throw new InvalidDataException($"Unknown metadata table 0x{i:X2}.");
                _tableOffset[i] = p;
                p += _rows[i] * _rowSize[i];
            }
        }

        private int Index(int table) => _rows[table] < 65536 ? 2 : 4;

        private int Coded(int[] tables)
        {
            var bits = 0;
            while ((1 << bits) < tables.Length) bits++;
            var max = 0;
            foreach (var table in tables)
            {
                if (table >= 0) max = Math.Max(max, _rows[table]);
            }
            return max < (1 << (16 - bits)) ? 2 : 4;
        }

        /// <summary>Column sizes of each table (ECMA-335 II.22).</summary>
        private int[] Schema(int table)
        {
            int s = _stringSize, g = _guidSize, b = _blobSize;
            switch (table)
            {
                case Module: return new[] { 2, s, g, g, g };
                case TypeRef: return new[] { Coded(ResolutionScopeTables), s, s };
                case TypeDef: return new[] { 4, s, s, Coded(TypeDefOrRefTables), Index(Field), Index(MethodDef) };
                case FieldPtr: return new[] { Index(Field) };
                case Field: return new[] { 2, s, b };
                case MethodPtr: return new[] { Index(MethodDef) };
                case MethodDef: return new[] { 4, 2, 2, s, b, Index(Param) };
                case ParamPtr: return new[] { Index(Param) };
                case Param: return new[] { 2, 2, s };
                case InterfaceImpl: return new[] { Index(TypeDef), Coded(TypeDefOrRefTables) };
                case MemberRef: return new[] { Coded(MemberRefParentTables), s, b };
                case Constant: return new[] { 2, Coded(HasConstantTables), b };
                case CustomAttribute: return new[] { Coded(HasCustomAttributeTables), Coded(CustomAttributeTypeTables), b };
                case FieldMarshal: return new[] { Coded(HasFieldMarshalTables), b };
                case DeclSecurity: return new[] { 2, Coded(HasDeclSecurityTables), b };
                case ClassLayout: return new[] { 2, 4, Index(TypeDef) };
                case FieldLayout: return new[] { 4, Index(Field) };
                case StandAloneSig: return new[] { b };
                case EventMap: return new[] { Index(TypeDef), Index(Event) };
                case EventPtr: return new[] { Index(Event) };
                case Event: return new[] { 2, s, Coded(TypeDefOrRefTables) };
                case PropertyMap: return new[] { Index(TypeDef), Index(Property) };
                case PropertyPtr: return new[] { Index(Property) };
                case Property: return new[] { 2, s, b };
                case MethodSemantics: return new[] { 2, Index(MethodDef), Coded(HasSemanticsTables) };
                case MethodImpl: return new[] { Index(TypeDef), Coded(MethodDefOrRefTables), Coded(MethodDefOrRefTables) };
                case ModuleRef: return new[] { s };
                case TypeSpec: return new[] { b };
                case ImplMap: return new[] { 2, Coded(MemberForwardedTables), s, Index(ModuleRef) };
                case FieldRva: return new[] { 4, Index(Field) };
                case EncLog: return new[] { 4, 4 };
                case EncMap: return new[] { 4 };
                case Assembly: return new[] { 4, 2, 2, 2, 2, 4, b, s, s };
                case AssemblyProcessor: return new[] { 4 };
                case AssemblyOs: return new[] { 4, 4, 4 };
                case AssemblyRef: return new[] { 2, 2, 2, 2, 4, b, s, s, b };
                case AssemblyRefProcessor: return new[] { 4, Index(AssemblyRef) };
                case AssemblyRefOs: return new[] { 4, 4, 4, Index(AssemblyRef) };
                case File: return new[] { 4, s, b };
                case ExportedType: return new[] { 4, 4, s, s, Coded(ImplementationTables) };
                case ManifestResource: return new[] { 4, 4, s, Coded(ImplementationTables) };
                case NestedClass: return new[] { Index(TypeDef), Index(TypeDef) };
                case GenericParam: return new[] { 2, 2, Coded(TypeOrMethodDefTables), s };
                case MethodSpec: return new[] { Coded(MethodDefOrRefTables), b };
                case GenericParamConstraint: return new[] { Index(GenericParam), Coded(TypeDefOrRefTables) };
                default: return new int[0];
            }
        }

        #endregion

        #region Values

        /// <summary>Value of a column (row is 1-based).</summary>
        public uint Get(int table, int row, int column)
        {
            var offset = _tableOffset[table] + (row - 1) * _rowSize[table];
            var sizes = _columns[table];
            for (var i = 0; i < column; i++) offset += sizes[i];
            switch (sizes[column])
            {
                case 1: return _data[offset];
                case 2: return U16(offset);
                default: return U32(offset);
            }
        }

        public string String(uint index)
        {
            if (index == 0) return string.Empty;
            var start = _strings + (int)index;
            var end = start;
            while (_data[end] != 0) end++;
            return Encoding.UTF8.GetString(_data, start, end - start);
        }

        /// <summary>String literal of an ldstr token (#US heap).</summary>
        public string UserString(int token)
        {
            var p = _userStrings + (token & 0x00FFFFFF);
            var length = CompressedUInt(ref p);
            if (length <= 1) return string.Empty;
            return Encoding.Unicode.GetString(_data, p, (int)length - 1);
        }

        /// <summary>Offset of the content of a blob and its length.</summary>
        public int Blob(uint index, out int length)
        {
            var p = _blob + (int)index;
            length = (int)CompressedUInt(ref p);
            return p;
        }

        public byte Byte(int offset) => _data[offset];

        public uint CompressedUInt(ref int p)
        {
            var b = _data[p];
            if ((b & 0x80) == 0)
            {
                p += 1;
                return b;
            }
            if ((b & 0xC0) == 0x80)
            {
                var value = (uint)(((b & 0x3F) << 8) | _data[p + 1]);
                p += 2;
                return value;
            }
            var result = (uint)(((b & 0x1F) << 24) | (_data[p + 1] << 16) | (_data[p + 2] << 8) | _data[p + 3]);
            p += 4;
            return result;
        }

        /// <summary>IL code of a method (null for abstract / extern methods).</summary>
        public byte[] MethodBody(uint rva)
        {
            if (rva == 0) return null;
            var p = Offset(rva);
            var header = _data[p];
            int codeSize, start;
            if ((header & 0x03) == 0x02)
            {
                codeSize = header >> 2;
                start = p + 1;
            }
            else
            {
                var flags = U16(p);
                start = p + ((flags >> 12) & 0x0F) * 4;
                codeSize = I32(p + 4);
            }
            if (codeSize <= 0 || start + codeSize > _data.Length) return null;
            var code = new byte[codeSize];
            Buffer.BlockCopy(_data, start, code, 0, codeSize);
            return code;
        }

        public int Offset(uint rva)
        {
            foreach (var section in _sections)
            {
                var size = Math.Max(section.VirtualSize, section.RawSize);
                if (rva >= section.VirtualAddress && rva < section.VirtualAddress + size) return (int)(rva - section.VirtualAddress + section.RawPointer);
            }
            throw new InvalidDataException($"RVA 0x{rva:X} outside of the sections.");
        }

        private ushort U16(int offset) => BitConverter.ToUInt16(_data, offset);
        private uint U32(int offset) => BitConverter.ToUInt32(_data, offset);
        private int I32(int offset) => BitConverter.ToInt32(_data, offset);

        #endregion
    }
}
