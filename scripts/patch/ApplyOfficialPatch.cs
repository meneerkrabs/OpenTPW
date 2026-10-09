using System;
using System.IO;
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
    private static bool HadError;
    private static bool UnsupportedPrompt;
    private static IntPtr OnMessage(uint id, IntPtr value)
    {
        // Never copy native diagnostic text into public logs. IDs 1..4 are
        // warnings/errors (2..4); 1, 9..11 log ordinary status. 5..8 report
        // progress; 14, 21, 22 are notification/poll/end callbacks.
        if (id >= 2 && id <= 4) HadError = true;
        if ((id >= 1 && id <= 11) || id == 14 || id == 21 || id == 22) return Continue;
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
        Console.WriteLine("engineResult=" + result + "; diagnostic=" + HadError + "; unsupportedPrompt=" + UnsupportedPrompt);
        return result == 0 && !HadError && !UnsupportedPrompt ? 0 : 121;
    }
}
