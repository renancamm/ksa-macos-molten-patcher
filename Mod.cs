using StarMap.API;

namespace MacOSMoltenPatcher
{
    // Before KSA starts, in a Sikarugir wrapper only, applies the files in patches/ in name order:
    // *.patch (a diff) or *.replace (a whole file). Each changes one file and keeps the original as <file>.orig.
    [StarMapMod]
    public sealed class Mod
    {
        [StarMapBeforeMain]
        public void OnBeforeMain()
        {
            // Errors are logged and ignored: the game must always start.
            try { ApplyAll(); }
            catch (Exception e) { Log($"Stopped: {e.Message}"); }
        }

        static void ApplyAll()
        {
            // e.g. /Users/me/Applications/Sikarugir/KSA.app/Contents/SharedSupport/prefix; unset on Windows.
            string? prefix = Environment.GetEnvironmentVariable("WINEPREFIX");
            int app = prefix?.IndexOf(".app/Contents/", StringComparison.Ordinal) ?? -1;
            if (app < 0) return;

            // Z: is the Mac's root folder.
            string wrapper = "Z:" + prefix![..(app + ".app/Contents".Length)];

            // One bad patch doesn't stop the others.
            string patches = Path.Combine(ModFolder, "patches");
            if (!Directory.Exists(patches)) return;
            var files = Directory.GetFiles(patches, "*.patch").Concat(Directory.GetFiles(patches, "*.replace"));
            foreach (string patch in files.Order())
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

            if (patch.EndsWith(".replace"))
                Replace(name, lines, wrapper);
            else
                TextPatch(name, lines, wrapper);
        }

        // "target [wrapper:]<path>" + "sha256 <hash>" ("$ " build lines are for build.sh). The new file
        // is files/wrapper/<path> or files/game/<path>, used only if its hash matches.
        static void Replace(string name, string[] lines, string wrapper)
        {
            string target = lines.First(l => l.StartsWith("target "))["target ".Length..].Trim();
            string sha256 = lines.First(l => l.StartsWith("sha256 "))["sha256 ".Length..].Trim().ToLowerInvariant();
            var (file, inWrapper, rel) = Resolve(target, wrapper);
            string source = Path.Combine(ModFolder, "files", inWrapper ? "wrapper" : "game", rel);

            if (!File.Exists(file)) { Log($"Skipped {name}: {file} not found"); return; }
            if (Sha256(File.ReadAllBytes(file)) == sha256) return;  // already done

            byte[] data = File.ReadAllBytes(source);
            if (Sha256(data) != sha256) { Log($"Skipped {name}: {source} doesn't match the patch's sha256"); return; }

            // Only the first time, to keep the wrapper's own file.
            if (!File.Exists(file + ".orig")) File.Copy(file, file + ".orig");
            Write(file, data);

            // macOS re-reads a cached app only when the .app's date changes.
            Directory.SetLastWriteTime(Path.GetDirectoryName(wrapper)!, DateTime.Now);
            Log($"Applied {name} to {file}");
        }

        // A diff of one file. Hunks are matched by text, not line numbers: done if all new
        // lines are present, applied only if all old lines occur exactly once, else skipped.
        static void TextPatch(string name, string[] lines, string wrapper)
        {
            // "+++ b/[wrapper:]<path>", maybe followed by a tab and a date
            string target = lines.First(l => l.StartsWith("+++ "))[4..].Split('\t')[0];
            if (target.StartsWith("b/")) target = target[2..];
            string file = Resolve(target, wrapper).File;

            // Old text = " " and "-" lines, new text = " " and "+" lines; \n at both ends matches whole lines.
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

            // Work with \n, write back with the file's own line endings.
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

            // Not patched yet, so this is the current game version's original.
            File.Copy(file, file + ".orig", overwrite: true);
            Write(file, System.Text.Encoding.UTF8.GetBytes(crlf ? patched.Replace("\n", "\r\n") : patched));
            Log($"Applied {name} to {file}");
        }

        // Via <file>.new and a rename, so a half-written file is never used.
        static void Write(string file, byte[] data)
        {
            File.WriteAllBytes(file + ".new", data);
            File.Move(file + ".new", file, overwrite: true);
        }

        // "wrapper:<path>" is in the wrapper's .app/Contents, anything else in the game folder.
        static (string File, bool InWrapper, string Rel) Resolve(string target, string wrapper)
        {
            bool inWrapper = target.StartsWith("wrapper:");
            string rel = Inside(inWrapper ? target["wrapper:".Length..] : target);
            return (inWrapper ? wrapper + "/" + rel : rel, inWrapper, rel);
        }

        // A patch may only change files inside its folder (game or wrapper), never elsewhere on the Mac.
        static string Inside(string path) =>
            Path.IsPathRooted(path) || path.Split('/', '\\').Contains("..")
                ? throw new InvalidDataException($"target outside its folder: {path}")
                : path;

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
