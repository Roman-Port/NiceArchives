using Newtonsoft.Json.Linq;
using RomanPort.HttpWebArchive.Framework.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace RomanPort.HttpWebArchive.Framework.RichMetadataProviders
{
    public static class AudioRichMetadataGenerator
    {
        public const int RESOLUTION = 1024;
        
        public static JObject GenerateRichMetadata(string filePath)
        {
            //Get file info
            var info = AudioFileInfo.GetAudioFileInfo(filePath);

            //Get details
            float durationSeconds = float.Parse(info.data["STREAM"]["duration"]);
            int sampleRate = int.Parse(info.data["STREAM"]["sample_rate"]);

            //Determine how many samples will be in each data point
            //This will break for very short audio files, but this will likely never be encountered
            float samplesPerDataPoint = (float)(sampleRate * durationSeconds) / (float)RESOLUTION;
            if (samplesPerDataPoint < 1)
                throw new Exception("Audio is too short!");

            //Start FFMPEG and instruct it to output in 8-bits-per-sample mono. This will mix down stereo tracks to mono
            string args = $"-i \"{filePath}\" -f s8 -acodec pcm_s8 -ac 1 -";
            Process ffmpeg = Process.Start(new ProcessStartInfo
            {
                FileName = Program.site.config.ffmpeg_path,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });

            //Open file where we'll save this info
            long read = 0;
            using (FileStream fs = new FileStream(filePath + ".audiothumb", FileMode.Create))
            {
                //Run for resolution
                for(int j = 0; j<RESOLUTION; j+=1)
                {
                    //Deteremine how many to read
                    //Find the end byte
                    long endByte = (long)(j * samplesPerDataPoint);
                    
                    //Find the max in these samples
                    byte max = 0;
                    while(read < endByte) {
                        int value = (sbyte)ffmpeg.StandardOutput.BaseStream.ReadByte();
                        max = Math.Max(max, (byte)Math.Abs(value));
                        read++;
                    }

                    //Write
                    fs.WriteByte(max);
                }
            }

            //Wait for end
            ffmpeg.StandardOutput.BaseStream.Close();
            ffmpeg.WaitForExit();

            //Create rich metadata
            JObject meta = new JObject();
            meta["DURATION_SECONDS"] = durationSeconds;
            meta["SAMPLE_RATE"] = sampleRate;
            meta["CHANNELS"] = int.Parse(info.data["STREAM"]["channels"]);
            meta["AUDIO_THUMB_RESOLUTION"] = RESOLUTION;
            meta["AUDIO_THUMB_VERSION"] = 1;
            meta["AUDIO_THUMB_GENERATED_AT"] = DateTime.UtcNow.Ticks;
            return meta;
        }
    }
}
