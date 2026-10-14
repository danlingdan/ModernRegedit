using System.Globalization;
using System.Text;

namespace ModernRegedit.Services;

/// <summary>.reg 文件（Registry Editor 5.00 格式）导出、解析与差异计算。</summary>
internal static class RegFile
{
    public const string Header = "Windows Registry Editor Version 5.00";

    // ---------- 导出 ----------

    public static async Task ExportAsync(RegistryService service, string rootPath, RegistryView view, string filePath)
    {
        await Task.Run(() =>
        {
            using var writer = new StreamWriter(filePath, false, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
            writer.WriteLine(Header);
            writer.WriteLine();
            ExportKey(service, rootPath, view, writer);
        });
    }

    private static void ExportKey(RegistryService service, string path, RegistryView view, StreamWriter writer)
    {
        IReadOnlyList<RegistryValueInfo> values;
        IReadOnlyList<RegistryKeyName> children;
        values = service.GetValues(path, view);
        children = service.GetSubKeyNames(path, view);

        writer.WriteLine($"[{path}]");
        foreach (RegistryValueInfo value in values)
        {
            writer.WriteLine(ValueLine(value.Name, value.Kind, value.Data));
        }

        writer.WriteLine();
        foreach (RegistryKeyName child in children)
        {
            ExportKey(service, child.FullPath, view, writer);
        }
    }

    internal static string ValueLine(string name, RegKind kind, byte[] data)
    {
        string prefix = name.Length == 0 ? "@" : $"\"{Escape(name)}\"";
        return kind switch
        {
            RegKind.Sz => $"{prefix}=\"{Escape(DecodeString(data))}\"",
            RegKind.Dword => $"{prefix}=dword:{(data.Length >= 4 ? BitConverter.ToUInt32(data) : 0):x8}",
            RegKind.Binary => $"{prefix}=hex:{HexList(data)}",
            RegKind.ExpandSz => $"{prefix}=hex(2):{HexList(data)}",
            RegKind.MultiSz => $"{prefix}=hex(7):{HexList(data)}",
            RegKind.None => $"{prefix}=hex(0):{HexList(data)}",
            RegKind.DwordBigEndian => $"{prefix}=hex(5):{HexList(data)}",
            RegKind.Link => $"{prefix}=hex(6):{HexList(data)}",
            RegKind.Qword => $"{prefix}=hex(b):{HexList(data)}",
            RegKind.ResourceList => $"{prefix}=hex(8):{HexList(data)}",
            RegKind.FullResourceDescriptor => $"{prefix}=hex(9):{HexList(data)}",
            RegKind.ResourceRequirementsList => $"{prefix}=hex(a):{HexList(data)}",
            _ => $"{prefix}=hex:{HexList(data)}",
        };
    }

    internal static string HexList(byte[] data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(data.Length * 3);
        foreach (byte b in data)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            sb.Append(',');
        }

        sb.Length--;
        return sb.ToString();
    }

    internal static string Escape(string text) => text
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n");

    /// <summary>REG_SZ/EXPAND_SZ 数据可能缺尾 NUL 或长度为奇数：丢弃孤立尾字节后解码。</summary>
    internal static string DecodeString(byte[] data)
    {
        if (data.Length % 2 != 0)
        {
            data = data[..^1];
        }

        return Encoding.Unicode.GetString(data).TrimEnd('\0');
    }

    internal static string Unescape(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                sb.Append(text[i] switch
                {
                    'n' => '\n',
                    '\\' => '\\',
                    '"' => '"',
                    var c => c,
                });
            }
            else
            {
                sb.Append(text[i]);
            }
        }

        return sb.ToString();
    }

    // ---------- 解析 ----------

    public sealed record RegKeyStatement(string Path, List<(string Name, RegKind Kind, byte[] Data)> Values);
    public sealed record RegDeleteKeyStatement(string Path);
    public sealed record RegDeleteValueStatement(string Path, string Name);
    public sealed record ParsedRegFile(List<object> Statements);

    public static ParsedRegFile Parse(string filePath)
    {
        string content;
        using (var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            content = reader.ReadToEnd();
        }

        var statements = new List<object>();
        string[] lines = content.Replace("\r\n", "\n").Split('\n');
        if (lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() is not (Header or "REGEDIT4"))
        {
            throw new FormatException("Missing registry file header.");
        }
        RegKeyStatement? current = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('['))
            {
                int close = line.IndexOf(']');
                if (close < 0)
                {
                    continue;
                }

                string body = line[1..close];
                if (body.StartsWith('-'))
                {
                    statements.Add(new RegDeleteKeyStatement(NormalizeRoot(body[1..])));
                    current = null;
                }
                else
                {
                    current = new RegKeyStatement(NormalizeRoot(body), new List<(string, RegKind, byte[])>());
                    statements.Add(current);
                }

                continue;
            }

            if (current is null)
            {
                continue; // 头部行或游离行
            }

            // hex 续行：以 ',' 或 ',\' 结尾时拼接下一行
            while (line.EndsWith('\\'))
            {
                if (++i >= lines.Length)
                {
                    throw new FormatException("Incomplete hex continuation.");
                }
                line = line[..^1] + lines[i].Trim();
            }

            try { ParseValueLine(line, current.Values); }
            catch (FormatException ex) { throw new FormatException($"Line {i + 1}: {ex.Message}", ex); }
        }

        return new ParsedRegFile(statements);
    }

    private static string NormalizeRoot(string path)
    {
        path = path.Trim();
        string head = path.Contains('\\') ? path[..path.IndexOf('\\')] : path;
        string full = head.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => "HKEY_LOCAL_MACHINE",
            "HKCU" or "HKEY_CURRENT_USER" => "HKEY_CURRENT_USER",
            "HKCR" or "HKEY_CLASSES_ROOT" => "HKEY_CLASSES_ROOT",
            "HKU" or "HKEY_USERS" => "HKEY_USERS",
            "HKCC" or "HKEY_CURRENT_CONFIG" => "HKEY_CURRENT_CONFIG",
            _ => head,
        };
        return path.Contains('\\') ? $"{full}{path[path.IndexOf('\\')..]}" : full;
    }

    private static void ParseValueLine(string line, List<(string Name, RegKind Kind, byte[] Data)> values)
    {
        string name;
        string rest;
        if (line.StartsWith('@'))
        {
            name = string.Empty;
            rest = line[1..];
        }
        else if (line.StartsWith('"'))
        {
            int close = FindClosingQuote(line);
            if (close < 0)
            {
                return;
            }

            name = Unescape(line[1..close]);
            rest = line[(close + 1)..];
        }
        else
        {
            return;
        }

        if (!rest.StartsWith('='))
        {
            return;
        }

        rest = rest[1..];
        if (rest == "-")
        {
            values.Add((name, RegKind.None, DeleteValueMarker));
            return;
        }

        // 带引号的裸字符串 = REG_SZ；必须先于 type: 前缀判断
        if (rest.StartsWith('"'))
        {
            int close = FindClosingQuote(rest);
            if (close < 0)
            {
                return;
            }

            values.Add((name, RegKind.Sz, Encoding.Unicode.GetBytes(Unescape(rest[1..close]) + "\0")));
            return;
        }

        int colon = rest.IndexOf(':');
        string typePart = colon >= 0 ? rest[..colon] : rest;
        string dataPart = colon >= 0 ? rest[(colon + 1)..] : string.Empty;

        if (typePart == "dword")
        {
            if (!uint.TryParse(dataPart.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
                throw new FormatException("Invalid DWORD data.");
            byte[] bytes = BitConverter.GetBytes(v);
            values.Add((name, RegKind.Dword, bytes));
            return;
        }

        RegKind kind = typePart switch
        {
            "hex" => RegKind.Binary,
            "hex(0)" => RegKind.None,
            "hex(1)" => RegKind.Sz,
            "hex(2)" => RegKind.ExpandSz,
            "hex(3)" => RegKind.Binary,
            "hex(4)" => RegKind.Dword,
            "hex(5)" => RegKind.DwordBigEndian,
            "hex(6)" => RegKind.Link,
            "hex(7)" => RegKind.MultiSz,
            "hex(8)" => RegKind.ResourceList,
            "hex(9)" => RegKind.FullResourceDescriptor,
            "hex(a)" => RegKind.ResourceRequirementsList,
            "hex(b)" => RegKind.Qword,
            _ => throw new FormatException($"Unknown value type '{typePart}'."),
        };

        values.Add((name, kind, ParseHexList(dataPart)));
    }

    public static readonly byte[] DeleteValueMarker = [1];

    private static int FindClosingQuote(string line)
    {
        for (int i = 1; i < line.Length; i++)
        {
            if (line[i] == '\\')
            {
                i++;
                continue;
            }

            if (line[i] == '"')
            {
                return i;
            }
        }

        return -1;
    }

    private static byte[] ParseHexList(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        string[] parts = text.Split(',');
        var bytes = new byte[parts.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.Length != 2 || !byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                throw new FormatException("Invalid hex byte.");
        }

        return bytes;
    }

    // ---------- 差异 ----------

    public sealed record DiffRow(string Change, string Path, string Value, string Detail);

    public sealed record ImportPlan(IReadOnlyList<WriteOperation> Operations, IReadOnlyList<DiffRow> Rows, int Added, int Modified, int Deleted);

    /// <summary>把解析出的语句与当前注册表比对，产出预览行与写入操作序列。</summary>
    public static ImportPlan BuildImportPlan(RegistryService service, ParsedRegFile parsed, RegistryView view)
    {
        var operations = new List<WriteOperation>();
        var rows = new List<DiffRow>();
        int added = 0, modified = 0, deleted = 0;

        foreach (object statement in parsed.Statements)
        {
            switch (statement)
            {
                case RegKeyStatement key:
                {
                    bool keyExists = KeyExists(service, key.Path, view);
                    if (!keyExists)
                    {
                        operations.Add(new WriteOperation(WriteOpKind.CreateKey, key.Path));
                        rows.Add(new DiffRow("Add", key.Path, string.Empty, "(key)"));
                        added++;
                    }

                    foreach ((string name, RegKind kind, byte[] data) in key.Values)
                    {
                        if (ReferenceEquals(data, DeleteValueMarker))
                        {
                            if (TryGetValue(service, key.Path, name, view, out byte[]? existing, out RegKind _))
                            {
                                operations.Add(new WriteOperation(WriteOpKind.DeleteValue, key.Path, name));
                                rows.Add(new DiffRow("Delete", key.Path, name.Length == 0 ? "(Default)" : name, "(value)"));
                                deleted++;
                            }

                            continue;
                        }

                        if (!TryGetValue(service, key.Path, name, view, out byte[]? existingData, out RegKind existingKind))
                        {
                            operations.Add(new WriteOperation(WriteOpKind.SetValue, key.Path, name, ValueType: kind, Data: data));
                            rows.Add(new DiffRow("Add", key.Path, name.Length == 0 ? "(Default)" : name, RegValueDecoder.KindName(kind)));
                            added++;
                        }
                        else if (!existingData.AsSpan().SequenceEqual(data) || existingKind != kind)
                        {
                            operations.Add(new WriteOperation(WriteOpKind.SetValue, key.Path, name, ValueType: kind, Data: data));
                            rows.Add(new DiffRow("Modify", key.Path, name.Length == 0 ? "(Default)" : name,
                                $"{RegValueDecoder.KindName(existingKind)} → {RegValueDecoder.KindName(kind)}"));
                            modified++;
                        }
                    }

                    break;
                }

                case RegDeleteKeyStatement del:
                {
                    if (KeyExists(service, del.Path, view))
                    {
                        operations.Add(new WriteOperation(WriteOpKind.DeleteKey, del.Path));
                        rows.Add(new DiffRow("Delete key", del.Path, string.Empty, "(subtree)"));
                        deleted++;
                    }

                    break;
                }
            }
        }

        return new ImportPlan(operations, rows, added, modified, deleted);
    }

    private static bool KeyExists(RegistryService service, string path, RegistryView view)
    {
        try
        {
            service.GetSubKeyCount(path, view);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryGetValue(RegistryService service, string path, string name, RegistryView view, out byte[]? data, out RegKind kind)
    {
        data = null;
        kind = RegKind.None;
        try
        {
            foreach (RegistryValueInfo value in service.GetValues(path, view))
            {
                if (string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    data = value.Data;
                    kind = value.Kind;
                    return true;
                }
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
