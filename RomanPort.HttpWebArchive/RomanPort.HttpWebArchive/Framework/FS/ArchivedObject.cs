using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace RomanPort.HttpWebArchive.Framework.FS
{
    public abstract class ArchivedObject
    {
        public string path;
        public ArchiveSite site;

        public string path_urlsafe { get { return GetUrlSafePath(); } }

        public ArchivedObject(ArchiveSite site)
        {
            this.site = site;
        }

        public string GetUrlSafePath()
        {
            string[] s = path.Split('/');
            string o = "";
            for(int i = 0; i<s.Length; i+=1)
            {
                if (i != 0)
                    o += "/";
                o += HttpUtility.UrlEncode(s[i]).Replace("+", "%20");
            }
            return o;
        }

        public abstract string GetName();
        public abstract long GetSize();
        public abstract DateTime GetLastModified();
        public abstract DateTime GetUploadedDate();
        public abstract void RelocateToDir(string newPath);

        public abstract Task WriteListing(Stream s, string customClasses);

        public abstract Task OnHttpRequest(HttpContext e);

        public const string COOKIE_PREVIOUS_ITEM = "RomanPortArchives-PreviousItem";
        public const string COOKIE_BEFORE_PREVIOUS_ITEM = "RomanPortArchives-BeforePreviousItem";

        public void SaveCurrentItemCookie(HttpContext e)
        {
            //Move the previous item into the before-previous item
            if (e.Request.Cookies.ContainsKey(COOKIE_PREVIOUS_ITEM))
                e.Response.Cookies.Append(COOKIE_BEFORE_PREVIOUS_ITEM, e.Request.Cookies[COOKIE_PREVIOUS_ITEM].ToString(), new CookieOptions
                {
                    MaxAge = new TimeSpan(0, 15, 0)
                });

            //Store the current item
            e.Response.Cookies.Append(COOKIE_PREVIOUS_ITEM, GetName(), new CookieOptions
            {
                MaxAge = new TimeSpan(0, 15, 0)
            });
        }

        public void TrashMedia()
        {
            //Validate that we have a trash dir set in the config
            if (site.config.trash_dir == null)
                throw new Exception("No trash directory is set!");

            //Create a trash folder
            string trashFolder = site.config.trash_dir + $"TRASH-D{DateTime.UtcNow.Year}_{DateTime.UtcNow.Month}_{DateTime.UtcNow.Day}-T{DateTime.UtcNow.Hour}_{DateTime.UtcNow.Minute}-{new Random().Next()}/";
            Directory.CreateDirectory(trashFolder);

            //Relocate
            RelocateToDir(trashFolder);
        }

        public string GetSizeString()
        {
            double s = GetSize();
            return Math.Round(s / 1024 / 1024, 1) + " MB";
        }

        public string GetLastModifiedString(bool shortVersion = false)
        {
            DateTime time = GetLastModified();
            if (time.Ticks == 0)
                return "";
            else if (shortVersion)
            {
                return time.ToShortDateString();
            } else if (time.Hour == 12 && time.Minute == 0 && time.Second == 0) //Approx time
            {
                return time.ToLongDateString();
            }
            else
            {
                DateTime t = time.AddHours(-6); //CST
                return $"{t.ToShortDateString()} {t.ToLongTimeString()} CST";
            }
        }
    }
}
