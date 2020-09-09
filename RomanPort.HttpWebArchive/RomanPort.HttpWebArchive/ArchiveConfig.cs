using System;
using System.Collections.Generic;
using System.Text;

namespace RomanPort.HttpWebArchive
{
    public class ArchiveConfig
    {
        public string templates_dir;
        public string archives_dir;
        public string trash_dir;
        public string client_pathname_prefix; //The prefix to add before an <a> html
        public string absolute_pathname_prefix; //The prefix to the absolute url. Example "https://romanport.com/archives"
        public int port;
        public string admin_key;
        public string ffmpeg_path;
        public string ffprobe_path;
    }
}
