using System;
using System.Collections.Generic;
using System.Text;

namespace RomanPort.HttpWebArchive.Framework.FS
{
    public class ArchivedDirectoryMetadata
    {
        //Required
        public string title;
        public string description;

        //Optional
        public DirectorySortType default_sort = DirectorySortType.FILE_DATE;
    }
}
