using StarMap.API;

namespace MacOSMoltenPatcher
{
    // Before KSA starts, when running in a Sikarugir wrapper on macOS, this mod applies the
    // patches in its patches/ folder, in file name order. Each one is a text file: a title and a
    // message saying why it's needed, a "---" line, then what it changes. There are two kinds:
    //   - a replacement swaps a whole file for one from this mod's files/ folder, for example
    //     the wrapper's libMoltenVK.dylib;
    //   - a text patch (normal diff) edits one game file, for example a shader.
    // Each patch changes one file, checks whether it's already done, and keeps the original as
    // <file>.orig. Two patches must not change the same file. On Windows the mod does nothing.
    [StarMapMod]
    public sealed class Mod
    {
        [StarMapBeforeMain]
        public void OnBeforeMain()
        {
            // Under Wine, WINEPREFIX is the wrapper's prefix, for example
            // /Users/me/Applications/Sikarugir/KSA.app/Contents/SharedSupport/prefix
            // On Windows it doesn't exist, and the mod does nothing.
            string? prefix = Environment.GetEnvironmentVariable("WINEPREFIX");
            int app = prefix?.IndexOf(".app/Contents/", StringComparison.Ordinal) ?? -1;
            if (app < 0) return;

            // Wine's Z: drive is the Mac's root folder.
            string wrapper = "Z:" + prefix![..(app + ".app/Contents".Length)];

            // KSA loads MoltenVK and compiles its shaders after this, so everything works in the
            // same launch. Every error is printed and ignored: the game must always start.
            string patches = Path.Combine(ModFolder, "patches");
            if (!Directory.Exists(patches)) return;
            foreach (string patch in Directory.GetFiles(patches, "*.patch").Order())
            {
                try { Apply(patch, wrapper); }
                catch (Exception e) { Log($"Skipped {Path.GetFileName(patch)}: {e.Message}"); }
            }
        }

        static void Apply(string patch, string wrapper)
        {
            string name = Path.GetFileNameWithoutExtension(patch);
            string[] lines = File.ReadAllText(patch).Replace("\r\n", "\n").Split('\n');
            lines = lines[(Array.IndexOf(lines, "---") + 1)..];  // skip the title and message

            if (lines[0].StartsWith("replace "))
                Replace(name, lines, wrapper);
            else
                TextPatch(name, lines);
        }

        // A replacement is two lines:
        //   replace wrapper:Frameworks/libMoltenVK.dylib
        //   sha256  <hash of the new file>
        // The target is relative to the game folder, or to the wrapper's .app/Contents with
        // "wrapper:". The new file is at the same path in files/wrapper/... or files/game/...,
        // and is used only if it has exactly that hash.
        static void Replace(string name, string[] lines, string wrapper)
        {
            string target = lines[0]["replace ".Length..].Trim();
            string sha256 = lines.First(l => l.StartsWith("sha256 "))["sha256 ".Length..].Trim().ToLowerInvariant();
            bool inWrapper = target.StartsWith("wrapper:");
            string file = inWrapper ? wrapper + "/" + target["wrapper:".Length..] : target;
            string source = Path.Combine(ModFolder, "files", inWrapper ? "wrapper/" + target["wrapper:".Length..] : "game/" + target);

            if (!File.Exists(file)) { Log($"Skipped {name}: {file} not found"); return; }
            if (Sha256(File.ReadAllBytes(file)) == sha256) return;  // already done

            byte[] data = File.ReadAllBytes(source);
            if (Sha256(data) != sha256) { Log($"Skipped {name}: {source} doesn't match the patch's sha256"); return; }

            // Only the first time: later the target is an older version of our file.
            if (!File.Exists(file + ".orig")) File.Copy(file, file + ".orig");
            Write(file, data);
            Log($"Applied {name} to {file}");
        }

        // A text patch is a normal diff of one file in the game folder. Hunks are found by their
        // text, not by line numbers. The patch is already done if every hunk's new lines are in
        // the file, and is applied only if every hunk's old lines are in the file exactly once.
        // Otherwise a game update changed the file, and the patch is left out.
        static void TextPatch(string name, string[] lines)
        {
            // "+++ b/Content/Core/Shaders/Common/TextureSet.glsl" (maybe with a date after a tab)
            string file = lines.First(l => l.StartsWith("+++ "))[4..].Split('\t')[0];
            if (file.StartsWith("b/")) file = file[2..];

            // Each hunk starts with "@@ -8,14 +8,16 @@". In its lines, " " is in both the old and
            // the new text, "-" only in the old, "+" only in the new. The \n at both ends makes
            // them match whole lines.
            var hunks = new List<(string Old, string New)>();
            foreach (string hunk in string.Join("\n", lines).TrimEnd('\n').Split("\n@@ ").Skip(1))
            {
                string old = "\n", @new = "\n";
                foreach (string line in hunk.Split('\n').Skip(1))
                {
                    if (line.StartsWith('\\')) continue;  // "\ No newline at end of file"
                    string text = line.Length > 0 ? line[1..] : "";
                    if (!line.StartsWith('+')) old += text + "\n";
                    if (!line.StartsWith('-')) @new += text + "\n";
                }
                hunks.Add((old, @new));
            }
            if (hunks.Count == 0) throw new FormatException("no hunks");

            if (!File.Exists(file)) { Log($"Skipped {name}: {file} not found"); return; }

            // Work with \n line endings, and write the file back with the ones it had.
            string content = File.ReadAllText(file);
            bool crlf = content.Contains("\r\n");
            string patched = "\n" + content.Replace("\r\n", "\n") + "\n";

            if (hunks.All(h => patched.Contains(h.New))) return;  // already done

            foreach (var (old, @new) in hunks)
            {
                if (patched.Split(old).Length != 2) { Log($"Skipped {name}: {file} changed"); return; }
                patched = patched.Replace(old, @new);
            }
            patched = patched[1..^1];

            // The file doesn't have the patch yet, so it's the current game version's original.
            File.Copy(file, file + ".orig", overwrite: true);
            Write(file, System.Text.Encoding.UTF8.GetBytes(crlf ? patched.Replace("\n", "\r\n") : patched));
            Log($"Applied {name} to {file}");
        }

        // Written to <file>.new first, then swapped in one step, so a half-written file is never used.
        static void Write(string file, byte[] data)
        {
            File.WriteAllBytes(file + ".new", data);
            File.Move(file + ".new", file, overwrite: true);
        }

        static string ModFolder => Path.GetDirectoryName(typeof(Mod).Assembly.Location)!;

        static string Sha256(byte[] data) =>
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data));

        static void Log(string message) => Console.WriteLine("[MacOSMoltenPatcher] " + message);
    }
}

// StarMap finds these attributes by name, so no package reference is needed.
namespace StarMap.API
{
    [AttributeUsage(AttributeTargets.Class)] internal class StarMapModAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] internal class StarMapBeforeMainAttribute : Attribute { }
}
