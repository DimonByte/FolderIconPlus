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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FolderIconPlus.Internals
{
    public class IconEntry
    {
        public string FilePath;
        public string Name;          // file name without extension
        public string NameLower;
        public string DirLower;      // sub-folder inside the icon pack root (so pack/category names are searchable too)
        public string[] NameTokens;
    }

    public class IconHit
    {
        public IconEntry Icon;
        public int Score;
        public int NameWordsMatched;
    }

    public static class IconIndex
    {
        static readonly string[] Extensions = [".ico", ".png", ".jpg", ".jpeg", ".bmp"];
        static readonly Regex Splitter = new(@"[^\p{L}\p{N}]+");

        // Per-user pack folder (always writable) and a pack folder next to the exe (for bundled packs).
        public static string UserPackDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "FolderIcon+", "IconPacks");
            }
        }

        public static string InstallPackDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IconPacks"); }
        }

        public static List<IconEntry> Load()
        {
            try { Directory.CreateDirectory(UserPackDir); } catch (Exception) { }

            var list = new List<IconEntry>();
            foreach (string root in new[] { InstallPackDir, UserPackDir }.Distinct(StringComparer.OrdinalIgnoreCase))
                if (Directory.Exists(root))
                    Walk(root, root, list);
            return list;
        }

        static void Walk(string root, string dir, List<IconEntry> list)
        {
            try
            {
                string rel = dir.Length > root.Length ? dir[root.Length..].Trim('\\', '/') : "";
                foreach (string f in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(Extensions, ext) < 0) continue;
                    string name = Path.GetFileNameWithoutExtension(f);
                    list.Add(new IconEntry
                    {
                        FilePath = f,
                        Name = name,
                        NameLower = name.ToLowerInvariant(),
                        DirLower = rel.ToLowerInvariant(),
                        NameTokens = Words(name)
                    });
                }
                foreach (string d in Directory.GetDirectories(dir))
                {
                    if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                    Walk(root, d, list);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        public static string[] Words(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return [];
            return [.. Splitter.Split(text.ToLowerInvariant()).Where(w => w.Length > 0).Distinct()];
        }

        // Every word of the query is searched separately ("Steam Games" -> "steam" + "games").
        // An icon matches if ANY word matches; icons matching more words (and in the file name rather
        // than just the pack folder name) rank higher.
        public static List<IconHit> Search(List<IconEntry> all, string query)
        {
            string[] words = Words(query);
            if (words.Length == 0)
                return [.. all.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(i => new IconHit { Icon = i })];

            var hits = new List<IconHit>();
            foreach (IconEntry icon in all)
            {
                int score = 0, nameHits = 0;
                foreach (string w in words)
                {
                    if (icon.NameLower.Contains(w))
                    {
                        nameHits++;
                        score += 2;
                        if (Array.IndexOf(icon.NameTokens, w) >= 0) score += 1;   // whole-word match beats substring
                    }
                    else if (icon.DirLower.Contains(w))
                    {
                        score += 1;
                    }
                }
                if (score > 0)
                    hits.Add(new IconHit { Icon = icon, Score = score, NameWordsMatched = nameHits });
            }

            return [.. hits.OrderByDescending(h => h.Score)
                       .ThenBy(h => h.Icon.Name.Length)
                       .ThenBy(h => h.Icon.Name, StringComparer.OrdinalIgnoreCase)];
        }

        // Used by the "auto-match subfolders" feature: best icon, but only if at least half of the
        // folder name's words appear in the icon's file name (avoids junk matches).
        public static IconHit BestMatch(List<IconEntry> all, string folderName)
        {
            string[] words = Words(folderName);
            if (words.Length == 0) return null;
            IconHit best = Search(all, folderName).FirstOrDefault();
            if (best == null) return null;
            return best.NameWordsMatched * 2 >= words.Length ? best : null;
        }
    }
}
