﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using TabularEditor.TOMWrapper;
using TabularEditor.UI;

namespace TabularEditor.Scripting
{
    public static class ScriptHelper
    {
        [ScriptMethod]
        public static string ReadFile(string filePath)
        {
            using (var fileStream = new System.IO.FileStream(filePath,
                System.IO.FileMode.Open,
                System.IO.FileAccess.Read,
                System.IO.FileShare.ReadWrite))
            using (var textReader = new System.IO.StreamReader(fileStream))
            {
                return textReader.ReadToEnd();
            }
        }

        [ScriptMethod]
        public static void SaveFile(string filePath, string content)
        {
            System.IO.File.WriteAllText(filePath, content);
        }

        [ScriptMethod]
        public static void Output(this object value, int lineNumber = -1)
        {
            // TODO: Make this output to the console, when running in command-line mode

            if (ScriptOutputForm.DontShow) return;

            var caption = string.Format("Script output{0}", lineNumber > 0 ? " at line " + lineNumber : "");

            var isHourglass = Hourglass.Enabled;
            if (isHourglass) Hourglass.Enabled = false;
            ScriptOutputForm.ShowObject(value, caption);
            if (isHourglass) Hourglass.Enabled = true;
        }
        public static void OutputErrors(IEnumerable<TabularNamedObject> items)
        {
            if (ScriptOutputForm.DontShow) return;

            var caption = string.Format("Objects with errors ({0})", items.Count());
            ScriptOutputForm.ShowObject(items, caption, true);
        }
    }
}
