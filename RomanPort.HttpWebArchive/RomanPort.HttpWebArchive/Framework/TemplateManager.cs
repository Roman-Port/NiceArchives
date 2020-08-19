using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace RomanPort.HttpWebArchive.Framework
{
    public static class TemplateManager
    {
        private static Dictionary<string, string> templates = new Dictionary<string, string>();

        public static void LoadTemplates(string path)
        {
            string[] files = Directory.GetFiles(path);
            foreach (var f in files)
            {
                string name = new FileInfo(f).Name;
                if (name.EndsWith(".html"))
                    name = name.Substring(0, name.Length - 5);
                templates.Add(name, File.ReadAllText(f));
            }
        }
        
        public static async Task WriteTemplate(Stream e, string name, Dictionary<string, string> values)
        {
            string v = templates[name];
            foreach (var value in values)
                v = v.Replace("{" + value.Key + "}", value.Value);
            byte[] vb = Encoding.UTF8.GetBytes(v);
            await e.WriteAsync(vb, 0, vb.Length);
        }
    }
}
