using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace RomanPort.HttpWebArchive.Framework.Util
{
    /// <summary>
    /// Uses FFMPEG to produce file info
    /// </summary>
    public class AudioFileInfo
    {
        public Dictionary<string, Dictionary<string, string>> data;

        public static AudioFileInfo GetAudioFileInfo(string filePath)
        {
            //Create FFMPEG parameters
            string args = $"-v error -show_format -show_streams \"{filePath}\"";

            //Start FFMPEG instance
            Process ffmpeg = Process.Start(new ProcessStartInfo
            {
                FileName = Program.site.config.ffprobe_path,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });

            //Wait for end
            ffmpeg.WaitForExit();

            //Read each
            Dictionary<string, Dictionary<string, string>> data = new Dictionary<string, Dictionary<string, string>>();
            string currentKey = null;
            Dictionary<string, string> currentData = null;
            string lastLine = ffmpeg.StandardOutput.ReadLine();
            while (lastLine != null)
            {
                if (lastLine.StartsWith("[/"))
                {
                    //End segment
                    data.Add(currentKey, currentData);
                }
                else if (lastLine.StartsWith("["))
                {
                    //Start segment
                    currentKey = lastLine.Substring(1, lastLine.Length - 2);
                    currentData = new Dictionary<string, string>();
                }
                else
                {
                    //Data
                    int index = lastLine.IndexOf('=');
                    if (index == -1)
                        throw new Exception("Invalid data.");
                    currentData.Add(lastLine.Substring(0, index), lastLine.Substring(index + 1));
                }
                lastLine = ffmpeg.StandardOutput.ReadLine();
            }

            return new AudioFileInfo
            {
                data = data
            };
        }
    }
}
