using System.Buffers.Binary;
using System.Text;

namespace ModernRegedit.Services;

internal static class RegValueDecoder
{
    public static string Decode(RegKind kind, byte[] data) => kind switch
    {
        RegKind.Sz => ReadString(data),
        RegKind.ExpandSz => ReadString(data),
        RegKind.Dword when data.Length >= 4 => BitConverter.ToUInt32(data).ToString(),
        RegKind.DwordBigEndian when data.Length >= 4 => BinaryPrimitives.ReadUInt32BigEndian(data).ToString(),
        RegKind.Qword when data.Length >= 8 => BitConverter.ToUInt64(data).ToString(),
        RegKind.MultiSz => string.Join(" | ", ReadMultiString(data)),
        RegKind.None when data.Length == 0 => "(zero length)",
        RegKind.Binary or _ => HexPreview(data),
    };

    public static string KindName(RegKind kind) => kind switch
    {
        RegKind.Sz => "REG_SZ",
        RegKind.ExpandSz => "REG_EXPAND_SZ",
        RegKind.Binary => "REG_BINARY",
        RegKind.Dword => "REG_DWORD",
        RegKind.DwordBigEndian => "REG_DWORD_BIG_ENDIAN",
        RegKind.Link => "REG_LINK",
        RegKind.MultiSz => "REG_MULTI_SZ",
        RegKind.ResourceList => "REG_RESOURCE_LIST",
        RegKind.FullResourceDescriptor => "REG_FULL_RESOURCE_DESCRIPTOR",
        RegKind.ResourceRequirementsList => "REG_RESOURCE_REQUIREMENTS_LIST",
        RegKind.Qword => "REG_QWORD",
        _ => "REG_NONE",
    };

    private static string ReadString(byte[] data)
    {
        string value = Encoding.Unicode.GetString(data);
        return value.TrimEnd('\0');
    }

    private static IEnumerable<string> ReadMultiString(byte[] data)
    {
        string raw = Encoding.Unicode.GetString(data).TrimEnd('\0');
        return raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string HexPreview(byte[] data)
    {
        const int previewLength = 16;
        int take = Math.Min(previewLength, data.Length);
        var builder = new StringBuilder(take * 3 + 16);

        for (int i = 0; i < take; i++)
        {
            builder.Append(data[i].ToString("X2"));
            builder.Append(' ');
        }

        if (take > 0)
        {
            builder.Length--;
        }

        if (data.Length > previewLength)
        {
            builder.Append($"  … ({data.Length} bytes)");
        }

        return builder.ToString();
    }
}
