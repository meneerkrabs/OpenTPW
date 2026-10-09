using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Compiled x86: the official 1998 patch engine exports a stdcall entrypoint.
internal static class ApplyOfficialPatch
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr Callback(uint id, IntPtr value);
    [DllImport("patchw32.dll", EntryPoint = "RTPatchApply32@12", ExactSpelling = true,
        CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern uint Apply(string command, Callback callback, int wait);
    private static readonly IntPtr Continue = Marshal.StringToHGlobalAnsi("");
    private static readonly IntPtr Ansi = Marshal.StringToHGlobalAnsi("ANSI");
    private static readonly SortedDictionary<uint, ulong> CallbackCounts = new SortedDictionary<uint, ulong>();
    private static uint FirstUnsupported;
    private static bool HadError;
    private static bool UnsupportedPrompt;
    private static IntPtr OnMessage(uint id, IntPtr value)
    {
        // Record only bounded numeric diagnostics, never native strings/payloads.
        if (CallbackCounts.ContainsKey(id)) CallbackCounts[id]++;
        else if (CallbackCounts.Count < 64) CallbackCounts.Add(id, 1);
        // Callback 12 is an encoding query: the DLL compares this response to
        // literal ANSI at RVA 0x273f4, before opening any patch files.
        if (id == 12) return Ansi;
        // 23..26 are notifications: original engine ignores their return value
        // except its common null/abort check; original wrapper continues them.
        if (id >= 2 && id <= 4) HadError = true;
        if ((id >= 1 && id <= 11) || id == 14 || id == 21 || (id >= 22 && id <= 26)) return Continue;
        if (!UnsupportedPrompt) FirstUnsupported = id;
        UnsupportedPrompt = true;
        return IntPtr.Zero; // Abort directory/media/password/confirmation prompts.
    }
    private static int Main(string[] args)
    {
        if (args.Length != 2 || !Directory.Exists(args[0]) || !File.Exists(args[1])) return 120;
        Directory.SetCurrentDirectory(Path.GetFullPath(args[0]));
        string patch = Path.GetFullPath(args[1]);
        string target = Path.GetFullPath(args[0]);
        if (patch.IndexOf('"') >= 0 || target.IndexOf('"') >= 0) return 120;
        Callback callback = OnMessage;
        uint result = Apply("-NOPATHSEARCH -NOSUBDIRSEARCH -NOIGNOREMISSING -NOCONFIRM -NOBACKUP -NOMESSAGE -NOERRORFILE \"" + patch + "\" \"" + target + "\"", callback, 1);
        GC.KeepAlive(callback);
        var counts = new List<string>();
        foreach (var entry in CallbackCounts) counts.Add(entry.Key + ":" + entry.Value);
        Console.WriteLine("engineResult=" + result + "; diagnostic=" + HadError + "; unsupportedPrompt=" + UnsupportedPrompt
            + "; callbacks=" + string.Join(",", counts) + "; firstUnsupported=" + FirstUnsupported);
        return result == 0 && !HadError && !UnsupportedPrompt ? 0 : 121;
    }
}
