using System;

namespace RomanPort.HttpWebArchive
{
    class Program
    {
        public static ArchiveSite site;
        
        static void Main(string[] args)
        {
            site = new ArchiveSite(args[0]);
            site.RunAsync().GetAwaiter().GetResult();
        }
    }
}
