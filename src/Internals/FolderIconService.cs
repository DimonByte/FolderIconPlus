//MIT License

//Copyright(c) 2026 Dimon

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace FolderIconPlus.Internals
{
    // How Windows folder icons work: Explorer reads a hidden "desktop.ini" inside the folder
    //   [.ShellClassInfo]
    //   IconResource=icon.ico,0
    // and only honours it if the folder itself has the ReadOnly (or System) attribute.
    // We copy the chosen icon INTO the folder (hidden), so the icon keeps working even if the
    // icon pack is moved/deleted or the folder is copied to another PC.
    public static class FolderIconService
    {
        const string IconPrefix = "FolderIcon_";
        const string Section = "[.ShellClassInfo]";

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        const int SHCNE_ASSOCCHANGED = 0x08000000;
        const uint SHCNF_IDLIST = 0x0000;
        const uint SHCNF_FLUSH = 0x1000;

        /// <summary>Tell Explorer to refresh icons. Call once after a batch of changes.</summary>
        public static void RefreshShell()
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
        }

        // ---------------------------------------------------------------- apply

        public static void Apply(string folder, string iconSource)
        {
            ApplyBytes(folder, BuildIco(iconSource));
        }

        public static void ApplyBytes(string folder, byte[] icoBytes)
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);

            string iniPath = Path.Combine(folder, "desktop.ini");
            List<string> lines = ReadIni(iniPath);

            // Remove the icon we put there last time.
            DeleteOwnIcon(folder, GetIniValue(lines, "IconResource"));
            DeleteOwnIcon(folder, GetIniValue(lines, "IconFile"));

            // New file name every time, otherwise Explorer keeps showing its cached version.
            string icoName = string.Concat(IconPrefix, Guid.NewGuid().ToString("N").AsSpan(0, 8), ".ico");
            string icoPath = Path.Combine(folder, icoName);
            File.WriteAllBytes(icoPath, icoBytes);
            File.SetAttributes(icoPath, FileAttributes.Hidden | FileAttributes.System);

            SetIniValue(lines, "IconResource", icoName + ",0");
            SetIniValue(lines, "IconFile", icoName);
            SetIniValue(lines, "IconIndex", "0");
            WriteIni(iniPath, lines);

            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
        }

        public static int ApplyTree(string root, string iconSource, bool includeRoot, List<string> errors)
        {
            byte[] ico = BuildIco(iconSource);
            var targets = new List<string>();
            if (includeRoot) targets.Add(root);
            targets.AddRange(EnumerateSubfolders(root));

            int count = 0;
            foreach (string f in targets)
            {
                try { ApplyBytes(f, ico); count++; }
                catch (Exception ex) { errors.Add(f + ": " + ex.Message); }
            }
            return count;
        }

        /// <summary>For each subfolder, finds an icon whose name matches the folder name and applies it.</summary>
        public static int AutoMatchTree(string root, List<IconEntry> icons, bool includeRoot, List<string> errors)
        {
            var targets = new List<string>();
            if (includeRoot) targets.Add(root);
            targets.AddRange(EnumerateSubfolders(root));

            var cache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            foreach (string f in targets)
            {
                try
                {
                    IconHit hit = IconIndex.BestMatch(icons, Path.GetFileName(f));
                    if (hit == null) continue;

                    if (!cache.TryGetValue(hit.Icon.FilePath, out byte[] ico))
                    {
                        ico = BuildIco(hit.Icon.FilePath);
                        cache[hit.Icon.FilePath] = ico;
                    }
                    ApplyBytes(f, ico);
                    count++;
                }
                catch (Exception ex) { errors.Add(f + ": " + ex.Message); }
            }
            return count;
        }

        // ---------------------------------------------------------------- reset

        public static bool Reset(string folder)
        {
            string iniPath = Path.Combine(folder, "desktop.ini");
            if (!File.Exists(iniPath)) return false;

            List<string> lines = ReadIni(iniPath);
            DeleteOwnIcon(folder, GetIniValue(lines, "IconResource"));
            DeleteOwnIcon(folder, GetIniValue(lines, "IconFile"));
            RemoveIniValue(lines, "IconResource");
            RemoveIniValue(lines, "IconFile");
            RemoveIniValue(lines, "IconIndex");

            bool nothingLeft = lines.All(l => string.IsNullOrWhiteSpace(l) ||
                                              l.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase));
            if (nothingLeft)
            {
                File.SetAttributes(iniPath, FileAttributes.Normal);
                File.Delete(iniPath);
                File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.ReadOnly);
            }
            else
            {
                WriteIni(iniPath, lines);   // folder had other customisations (tooltip, localized name...) - keep them
            }
            return true;
        }

        // ---------------------------------------------------------------- recursion helper

        public static IEnumerable<string> EnumerateSubfolders(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                string[] subs;
                try { subs = Directory.GetDirectories(cur); }
                catch (Exception) { continue; }

                foreach (string s in subs)
                {
                    FileAttributes a;
                    try { a = File.GetAttributes(s); }
                    catch (Exception) { continue; }

                    if ((a & FileAttributes.ReparsePoint) != 0) continue;                                   // junctions/symlinks
                    if ((a & FileAttributes.Hidden) != 0 && (a & FileAttributes.System) != 0) continue;     // $RECYCLE.BIN etc.

                    yield return s;
                    stack.Push(s);
                }
            }
        }

        // ---------------------------------------------------------------- icon building

        // .ico files are used as-is. PNG/JPG/BMP are letterboxed onto a 256x256 canvas and wrapped
        // in an ICO container (PNG-compressed, supported since Windows Vista).
        static byte[] BuildIco(string source)
        {
            if (Path.GetExtension(source).Equals(".ico", StringComparison.OrdinalIgnoreCase))
                return File.ReadAllBytes(source);

            byte[] png;
            using (var fs = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (Image img = Image.FromStream(fs))
            using (var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float scale = Math.Min(256f / img.Width, 256f / img.Height);
                    float w = img.Width * scale, h = img.Height * scale;
                    g.DrawImage(img, (256f - w) / 2f, (256f - h) / 2f, w, h);
                }
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                png = ms.ToArray();
            }

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((short)0);      // reserved
                bw.Write((short)1);      // type: icon
                bw.Write((short)1);      // image count
                bw.Write((byte)0);       // width  (0 = 256)
                bw.Write((byte)0);       // height (0 = 256)
                bw.Write((byte)0);       // palette
                bw.Write((byte)0);       // reserved
                bw.Write((short)1);      // planes
                bw.Write((short)32);     // bits per pixel
                bw.Write(png.Length);    // data size
                bw.Write(22);            // data offset (6 header + 16 entry)
                bw.Write(png);
                bw.Flush();
                return ms.ToArray();
            }
        }

        // ---------------------------------------------------------------- desktop.ini helpers

        static void DeleteOwnIcon(string folder, string iniValue)
        {
            if (string.IsNullOrEmpty(iniValue)) return;
            string name = iniValue.Split(',')[0].Trim().Trim('"');
            if (name.IndexOfAny(['\\', '/', ':']) >= 0) return;
            if (!name.StartsWith(IconPrefix, StringComparison.OrdinalIgnoreCase)) return;   // never delete files we didn't create

            string p = Path.Combine(folder, name);
            try
            {
                if (File.Exists(p))
                {
                    File.SetAttributes(p, FileAttributes.Normal);
                    File.Delete(p);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static List<string> ReadIni(string path)
        {
            return File.Exists(path) ? [.. File.ReadAllLines(path)] : [];
        }

        static void WriteIni(string path, List<string> lines)
        {
            if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);   // hidden+system files can't be overwritten
            File.WriteAllLines(path, lines, Encoding.Unicode);                        // UTF-16 with BOM, which Explorer reads fine
            File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);
        }

        static bool SectionRange(List<string> lines, out int start, out int end)
        {
            start = -1;
            end = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (start < 0)
                {
                    if (t.Equals(Section, StringComparison.OrdinalIgnoreCase)) start = i;
                }
                else if (t.StartsWith('['))
                {
                    end = i;
                    break;
                }
            }
            return start >= 0;
        }

        static bool KeyMatches(string line, string key)
        {
            int eq = line.IndexOf('=');
            return eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        static string GetIniValue(List<string> lines, string key)
        {
            if (!SectionRange(lines, out int s, out int e)) return null;
            for (int i = s + 1; i < e; i++)
                if (KeyMatches(lines[i], key))
                    return lines[i][(lines[i].IndexOf('=') + 1)..].Trim();
            return null;
        }

        static void SetIniValue(List<string> lines, string key, string value)
        {
            if (!SectionRange(lines, out int s, out int e))
            {
                lines.Insert(0, Section);
                lines.Insert(1, key + "=" + value);
                return;
            }
            for (int i = s + 1; i < e; i++)
            {
                if (KeyMatches(lines[i], key))
                {
                    lines[i] = key + "=" + value;
                    return;
                }
            }
            lines.Insert(s + 1, key + "=" + value);
        }

        static void RemoveIniValue(List<string> lines, string key)
        {
            if (!SectionRange(lines, out int s, out int e)) return;
            for (int i = e - 1; i > s; i--)
                if (KeyMatches(lines[i], key))
                    lines.RemoveAt(i);
        }
    }
}
