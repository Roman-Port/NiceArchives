using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace RomanPort.HttpWebArchive.Framework.FS
{
    public class ArchivedFile : ArchivedObject
    {
        //Relative path. For example, /test/file.wav

        public ArchivedFileMetadata metadata;
        public ArchivedDirectory parent;
        public FileInfo file; //May be null for remote files
        public DateTime uploadedDate;

        public ArchivedFile(ArchiveSite site, ArchivedDirectory dir, string metadataFilePath) : base(site)
        {
            //Set
            this.parent = dir;
            
            //Load metadata
            metadata = JsonConvert.DeserializeObject<ArchivedFileMetadata>(File.ReadAllText(metadataFilePath));
            uploadedDate = new FileInfo(metadataFilePath).LastWriteTimeUtc;

            //Get file if it's not remote
            if (!metadata.is_remote)
                file = new FileInfo(metadataFilePath.Substring(0, metadataFilePath.Length - 5)); //trim off .meta

            //Create path
            path = dir.path + GetName();

            //Add
            this.site.AddStoredObject(this);
        }

        /// <summary>
        /// Returns the name of the file. For example, file.wav
        /// </summary>
        /// <returns></returns>
        public override string GetName()
        {
            if (metadata.is_remote)
                return metadata.name;
            else if (metadata.name != null)
                return metadata.name;
            else
                return file.Name;
        }

        public override long GetSize()
        {
            if (metadata.is_remote)
                return metadata.remote_size;
            else
                return file.Length;
        }

        public DateTime GetDate()
        {
            if (metadata.is_remote)
                return metadata.time.Value;
            else if(metadata.time.HasValue)
                return metadata.time.Value;
            else
                return file.LastWriteTimeUtc;
        }

        public override DateTime GetUploadedDate()
        {
            return uploadedDate;
        }

        public override DateTime GetLastModified()
        {
            return GetDate();
        }

        public Stream OpenFile()
        {
            return new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
        }

        public string GetDateString()
        {
            if (!metadata.time_approx)
            {
                DateTime t = GetDate().AddHours(-6); //CST
                return $"{t.ToShortDateString()} {t.ToLongTimeString()} CST";
            }
            else
            {
                return $"On {GetDate().ToLongDateString()}";
            }
        }

        public override async Task WriteListing(Stream s, string customClasses)
        {
            await TemplateManager.WriteTemplate(s, "ITEM." + metadata.template_type, new Dictionary<string, string>
            {
                {"ITEM_NAME", GetName() },
                {"ITEM_PATH", site.config.client_pathname_prefix + path },
                {"ITEM_SIZE", GetSizeString() },
                {"ITEM_MODIFIED", GetLastModifiedString(false) },
                {"ITEM_MODIFIED_SHORT", GetLastModifiedString(true) },
                {"CUSTOM_CLASSES", customClasses }
            });
        }

        public string CreateTagsHtml()
        {
            string t = "";
            foreach(var tag in metadata.tags)
            {
                t += $"<div class=\"archive_tag\">{HttpUtility.HtmlEncode(tag)}</div> ";
            }
            return t;
        }

        public override void RelocateToDir(string newPath)
        {
            File.Move(file.FullName, newPath + file.Name);
        }

        public override async Task OnHttpRequest(HttpContext e)
        {
            //Check query to see what we want to do with this
            if (e.Request.Query.ContainsKey("action"))
                await OnActionRequest(e, e.Request.Query["action"]);
            else
                await OnStandardRequest(e);
        }

        public async Task OnActionRequest(HttpContext e, string action)
        {
            if(action == "play")
            {
                using (Stream s = OpenFile())
                {
                    //Check if we've requested a partial range. We should improve this function at some point.
                    long start = 0;
                    long end = s.Length;
                    bool partialRequested = false;
                    if(e.Request.Headers.ContainsKey("Range"))
                    {
                        if(e.Request.Headers["Range"].ToString().StartsWith("bytes="))
                        {
                            string[] args = e.Request.Headers["Range"].ToString().Substring("bytes=".Length).Split('-');
                            start = long.Parse(args[0]);
                            if(args[1] != "")
                                end = long.Parse(args[1])-1;
                            partialRequested = true;
                        }
                    }

                    var r = e.Request.GetTypedHeaders().Range.Ranges.FirstOrDefault();
                    start = r.From.Value;
                    if (r.To.HasValue)
                        end = r.To.Value;
                    else
                        end = s.Length;
                    partialRequested = true;

                    //Set headers
                    if(end - start != s.Length)
                        e.Response.StatusCode = 206;
                    e.Response.ContentType = "audio/mpeg";
                    e.Response.ContentLength = end - start;
                    e.Response.Headers.Add("Content-Range", $"bytes {start}-{end-1}/{s.Length}");
                    e.Response.Headers.Add("Accept-Ranges", "bytes");

                    //Write
                    s.Position = start;
                    long remaining = end - start;
                    byte[] buffer = new byte[2048];
                    int read;
                    while (remaining > 0 && !e.RequestAborted.IsCancellationRequested)
                    {
                        read = s.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        e.Response.Body.Write(buffer, 0, read);
                        remaining -= read;
                    }
                }
            } else if (action == "download")
            {
                e.Response.ContentType = "application/octet-stream";
                e.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{GetName()}\"");
                e.Response.ContentLength = GetSize();
                using (Stream s = OpenFile())
                    await s.CopyToAsync(e.Response.Body);
            }
        }

        public async Task OnStandardRequest(HttpContext e)
        {
            //Write headers
            e.Response.ContentType = "text/html";

            //Set cookie
            SaveCurrentItemCookie(e);

            //Create path
            string pathElements = "<div class=\"head_path_item\">" + GetName() + "</div>";
            var parentElement = this.parent;
            while (parentElement != null)
            {
                string name;
                if (parentElement.isRoot)
                    name = "<span class=\"material-icons\"> folder </span>";
                else
                    name = HttpUtility.HtmlEncode(parentElement.GetName());
                pathElements = "<div class=\"head_path_item\"><a href=\"" + site.config.client_pathname_prefix + parentElement.path_urlsafe + "\">" + name + "</a></div><div class=\"head_path_divider\">/</div>" + pathElements;
                parentElement = parentElement.parent;
            }

            //Write pre
            await TemplateManager.WriteTemplate(e.Response.Body, "PAGE.FILE", new Dictionary<string, string>
            {
                {"FOLDER_TITLE", HttpUtility.HtmlEncode(parent.metadata.title) },
                {"FOLDER_DESCRIPTION", HttpUtility.HtmlEncode(parent.metadata.description) },
                {"FILE_NAME", HttpUtility.HtmlEncode(GetName()) },
                {"DATE", HttpUtility.HtmlEncode(GetDateString()) },
                {"SIZE", HttpUtility.HtmlEncode(GetSizeString()) },
                {"FILE_DESCRIPTION", HttpUtility.HtmlEncode(metadata.description) },
                {"TAGS", CreateTagsHtml() },
                {"URL", site.config.client_pathname_prefix + path_urlsafe },
                {"PATH", pathElements }
            });
        }
    }
}
