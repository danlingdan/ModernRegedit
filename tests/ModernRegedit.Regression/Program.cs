using System.Text;
using ModernRegedit.Services;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
    passed++;
}

string path = @"HKEY_CURRENT_USER\Software\ModernRegedit.Regression-" + Guid.NewGuid().ToString("N");
string directory = Path.Combine(Path.GetTempPath(), "ModernRegedit.Regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var service = new RegistryService();
try
{
    foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
    {
        service.CreateKey(path, view);
        foreach (RegKind kind in Enum.GetValues<RegKind>())
        {
            byte[] data = kind == RegKind.Sz ? Encoding.Unicode.GetBytes("中\\文\"test\0")
                : kind == RegKind.Dword ? BitConverter.GetBytes(uint.MaxValue)
                : new byte[] { 0, 1, 127, 255 };
            service.SetValue(path, kind.ToString(), kind, data, view);
        }
        string longName = new('N', 1000);
        byte[] large = Enumerable.Range(0, 120000).Select(i => (byte)i).ToArray();
        service.SetValue(path, longName, RegKind.Binary, large, view);
        Check(service.GetValues(path, view).Any(v => v.Name == longName && v.Data.SequenceEqual(large)), $"{view}: long name and 120 KB data");

        var op = new WriteOperation(WriteOpKind.SetValue, path, "NewValue", Data: Encoding.Unicode.GetBytes("new\0"));
        var before = await WriteUndo.SnapshotForOpsAsync(service, [op], view);
        service.SetValue(path, op.Name!, op.ValueType, op.Data!, view);
        var undo = WriteUndo.BuildUndoOps([op], before);
        Check(undo.Length == 1 && undo[0].Kind == WriteOpKind.DeleteValue, "new value undo deletes the new value");
        Check(RegistryWriteService.Execute(undo, view).Ok && !service.GetValues(path, view).Any(v => v.Name == op.Name), "new value undo executes");
        Check(WriteUndo.BuildUndoOps([op, op], before).Length == 1, "repeated value writes restore once");
        Check(WriteUndo.BuildUndoOps([new(WriteOpKind.CreateKey, path)], before).Length == 0, "create existing key keeps existing data");

        string file = Path.Combine(directory, "roundtrip.reg");
        await RegFile.ExportAsync(service, path, view, file);
        var parsed = RegFile.Parse(file);
        var values = ((RegFile.RegKeyStatement)parsed.Statements.Single()).Values;
        Check(service.GetValues(path, view).All(v => values.Any(p => p.Name == v.Name && p.Kind == v.Kind && p.Data.SequenceEqual(v.Data))), $"{view}: all value kinds round trip byte-for-byte");
        Check(RegFile.BuildImportPlan(service, parsed, view).Operations.Count == 0, "identical import has no writes");
        service.DeleteKey(path, view);
    }

    string input = Path.Combine(directory, "input.reg");
    File.WriteAllText(input, RegFile.Header + "\n[HKCU\\Software\\Test]\n\"bytes\"=hex:01,02,\\\n 03,04,\\\n 05,ff\n");
    var multiline = (RegFile.RegKeyStatement)RegFile.Parse(input).Statements.Single();
    Check(multiline.Values.Single().Data.SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 255 }), "three-line hex continuation");
    foreach (string invalid in new[] { "dword:zz", "dword:100000000", "hex:0g", "hex:1", "hex:0102", "hex(c):00" })
    {
        File.WriteAllText(input, RegFile.Header + "\n[HKCU\\Software\\Test]\n\"x\"=" + invalid);
        bool rejected = false;
        try { RegFile.Parse(input); } catch (FormatException) { rejected = true; }
        Check(rejected, "reject invalid " + invalid);
    }
    File.WriteAllText(input, "not a registry file");
    bool headerRejected = false;
    try { RegFile.Parse(input); } catch (FormatException) { headerRejected = true; }
    Check(headerRejected, "reject missing header");
    Console.WriteLine($"{passed} checks passed.");
}
finally
{
    foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
    {
        try { service.DeleteKey(path, view); } catch (IOException) { }
    }
    Directory.Delete(directory, recursive: true);
}
