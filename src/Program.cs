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

using FolderIconPlus.Internals;
using System;
using System.IO;
using System.Windows.Forms;

namespace FolderIconPlus
{
    static class Program
    {
        // Command line (this is what the context menu registry entries call):
        //   FolderIconPlus.exe --select    "C:\Some\Folder"   UI, search prefilled with the folder name, closes after applying
        //   FolderIconPlus.exe --recursive "C:\Some\Folder"   same, but "include all subfolders" is ticked
        //   FolderIconPlus.exe --ui [folder]                  plain UI, stays open
        //   FolderIconPlus.exe --reset     "C:\Some\Folder"   no UI, restores the default icon
        [STAThread]
        static int Main(string[] args)
        {
#if NET6_0_OR_GREATER
            ApplicationConfiguration.Initialize();
#else
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#endif
            try
            {
                string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "--ui";
                string path = args.Length > 1 ? NormalizePath(args[1]) : null;

                switch (mode)
                {
                    case "--reset":
                        if (path == null || !Directory.Exists(path)) return 1;
                        FolderIconService.Reset(path);
                        FolderIconService.RefreshShell();
                        return 0;

                    case "--select":
                        Application.Run(new MainForm(path, false, true));
                        return 0;

                    case "--recursive":
                        Application.Run(new MainForm(path, true, true));
                        return 0;

                    default:
                        Application.Run(new MainForm(path, false, false));
                        return 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "FolderIcon+", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // A path like "C:\" passed as "%1" in quotes arrives as  C:"  (the backslash escapes the quote).
        static string NormalizePath(string p)
        {
            p = p.Replace("\"", "").Trim();
            if (p.Length == 2 && p[1] == ':') p += "\\";
            return p;
        }
    }
}
