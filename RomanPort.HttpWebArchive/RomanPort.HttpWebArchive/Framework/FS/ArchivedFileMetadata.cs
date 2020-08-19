﻿using System;
using System.Collections.Generic;
using System.Text;

namespace RomanPort.HttpWebArchive.Framework.FS
{
    public class ArchivedFileMetadata
    {
        //Required
        public string template_type; //One of "FILE", "FILE_AUDIO"
        public string[] tags;
        public string description;
        public Dictionary<string, string> custom_data; //Custom data for rendering the details panel

        //Optional
        public string name; //Overwrites the filename
        public DateTime? time; //Replaces the time loaded from disk
        public bool time_approx; //Doesn't display the time (only the date) and also doesn't do timezone work

        //Remote-only
        public bool is_remote; //Redirects to a URL elsewhere. REQUIRES name, remote_size, and remote_url to be set
        public string remote_url; //The remote URL to go to. Only used if is_remote is set to true
        public long remote_size; //The size of the remote file for display uses
    }
}
