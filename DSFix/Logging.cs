using System;
using System.IO;
using TaleWorlds.Library;

namespace DSFix
{
    internal static class DSLog
    {
        private static readonly object Sync = new object();

        internal static void Write(string message, bool showInGame = false)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [DSFix] " + message;
            try
            {
                lock (Sync)
                {
                    string directory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "Mount and Blade II Bannerlord",
                        "Configs");
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(Path.Combine(directory, "DSFix.log"), line + Environment.NewLine);
                }
            }
            catch
            {
            }

            if (!showInGame)
                return;

            try
            {
                InformationManager.DisplayMessage(new InformationMessage("[DSFix] " + message));
            }
            catch
            {
            }
        }
    }
}
