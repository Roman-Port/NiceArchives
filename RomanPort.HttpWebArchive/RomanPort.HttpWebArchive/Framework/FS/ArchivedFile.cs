using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using RomanPort.HttpWebArchive.Framework.Util;
using SixLabors.ImageSharp.PixelFormats;
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
        private string metadataFilePath;

        public ArchivedFile(ArchiveSite site, ArchivedDirectory dir, string metadataFilePath) : base(site)
        {
            //Set
            this.parent = dir;
            this.metadataFilePath = metadataFilePath;

            //Load metadata
            metadata = JsonConvert.DeserializeObject<ArchivedFileMetadata>(File.ReadAllText(metadataFilePath));

            //Get uploaded date
            if(metadata.uploaded_date.HasValue)
            {
                uploadedDate = metadata.uploaded_date.Value;
            } else
            {
                uploadedDate = new FileInfo(metadataFilePath).LastWriteTimeUtc;
                metadata.uploaded_date = uploadedDate;
            }

            //Get file if it's not remote
            if (!metadata.is_remote)
                file = new FileInfo(metadataFilePath.Substring(0, metadataFilePath.Length - 5)); //trim off .meta

            //Create path
            path = dir.path + GetName();

            //Check if we have rich metadata
            if (metadata.rich_metadata == null)
                rich_metadata_status = MetadataStatus.NOT_GENERATED;
            else
                rich_metadata_status = MetadataStatus.OK;

            //Add
            this.site.AddStoredObject(this);
        }

        public void SaveMetadata()
        {
            File.WriteAllText(metadataFilePath, JsonConvert.SerializeObject(metadata));
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

        public override MetadataStatus GenerateRichMetadata()
        {
            if(metadata.template_type == "FILE_AUDIO")
            {
                //Generate audio metadata
                metadata.rich_metadata = RichMetadataProviders.AudioRichMetadataGenerator.GenerateRichMetadata(file.FullName);
                SaveMetadata();
                return MetadataStatus.OK;
            }
            return MetadataStatus.NOT_GENERATED;
        }

        public override async Task WriteListing(Stream s, string customClasses)
        {
            var args = new Dictionary<string, string>
            {
                {"ITEM_NAME", GetName() },
                {"ITEM_PATH", site.config.client_pathname_prefix + path },
                {"ITEM_SIZE", GetSizeString() },
                {"ITEM_MODIFIED", GetLastModifiedString(false) },
                {"ITEM_MODIFIED_SHORT", GetLastModifiedString(true) },
                {"CUSTOM_CLASSES", customClasses }
            };

            //If we have rich metadata, add keys for that too
            if(metadata.template_type == "FILE_AUDIO")
            {
                if(metadata.rich_metadata == null)
                {
                    args.Add("AUDIO_TIME", "");
                } else
                {
                    TimeSpan time = new TimeSpan(0, 0, (int)metadata.rich_metadata["DURATION_SECONDS"]);
                    if(time.TotalHours >= 1)
                        args.Add("AUDIO_TIME", (time.Hours + (time.Days * 24)).ToString().PadLeft(2, '0') + ":" + time.Minutes.ToString().PadLeft(2, '0') + ":" + time.Seconds.ToString().PadLeft(2, '0'));
                    else
                        args.Add("AUDIO_TIME", time.Minutes.ToString().PadLeft(2, '0') + ":" + time.Seconds.ToString().PadLeft(2, '0'));
                }
            }

            await TemplateManager.WriteTemplate(s, "ITEM." + metadata.template_type, args);
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

        public byte[] LoadAudioPreview()
        {
            return File.ReadAllBytes(file.FullName + ".audiothumb");
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
            if (action == "play")
            {
                using (Stream s = OpenFile())
                {
                    //Check if we've requested a partial range. We should improve this function at some point.
                    long start = 0;
                    long end = s.Length;
                    bool partialRequested = false;
                    if (e.Request.Headers.ContainsKey("Range"))
                    {
                        if (e.Request.Headers["Range"].ToString().StartsWith("bytes="))
                        {
                            string[] args = e.Request.Headers["Range"].ToString().Substring("bytes=".Length).Split('-');
                            start = long.Parse(args[0]);
                            if (args[1] != "")
                                end = long.Parse(args[1]) - 1;
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
                    if (end - start != s.Length)
                        e.Response.StatusCode = 206;
                    e.Response.ContentType = "audio/mpeg";
                    e.Response.ContentLength = end - start;
                    e.Response.Headers.Add("Content-Range", $"bytes {start}-{end - 1}/{s.Length}");
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
            } else if (action == "audio_meta_preview" && metadata.rich_metadata != null && metadata.template_type == "FILE_AUDIO")
            {
                //Render an image as an audio preview

                //Load
                byte[] previewData = LoadAudioPreview();

                //Get parameters
                int padding = 0;
                int height = 255;
                int decim = 1;
                float radius = 0;
                Rgba32 foreColor = new Rgba32(255, 255, 255);
                Rgba32 backColor = new Rgba32(0, 0, 0, 0);
                if (e.Request.Query.ContainsKey("thumb_padding"))
                    padding = int.Parse(e.Request.Query["thumb_padding"]);
                if (e.Request.Query.ContainsKey("thumb_height"))
                    height = int.Parse(e.Request.Query["thumb_height"]);
                if (e.Request.Query.ContainsKey("thumb_decim"))
                    decim = int.Parse(e.Request.Query["thumb_decim"]);
                if (e.Request.Query.ContainsKey("thumb_radius"))
                    radius = float.Parse(e.Request.Query["thumb_radius"]);
                if (e.Request.Query.ContainsKey("thumb_fore_color"))
                    foreColor = Rgba32.ParseHex(e.Request.Query["thumb_fore_color"]);
                if (e.Request.Query.ContainsKey("thumb_back_color"))
                    backColor = Rgba32.ParseHex(e.Request.Query["thumb_back_color"]);

                //Generate
                e.Response.ContentType = "image/png";
                await AudioWavformGenerator.CreateImage(e.Response.Body, previewData, padding, height, decim, radius, foreColor, backColor);
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
            string pathPlain = "";
            var parentElement = this.parent;
            while (parentElement != null)
            {
                string name;
                if (parentElement.isRoot)
                    name = "<span class=\"material-icons\"> folder </span>";
                else
                {
                    name = HttpUtility.HtmlEncode(parentElement.GetName());
                    pathPlain = " > " + name + pathPlain;
                }
                pathElements = "<div class=\"head_path_item\"><a href=\"" + site.config.client_pathname_prefix + parentElement.path_urlsafe + "\">" + name + "</a></div><div class=\"head_path_divider\">/</div>" + pathElements; 
                parentElement = parentElement.parent;
            }

            //Create HTML meta headers
            string metas = "";
            metas += $"<meta property=\"og:site_name\" content=\"RomanPort Archives{pathPlain}\">\n";
            metas += $"<meta name=\"theme-color\" content=\"#3882dc\">\n";
            metas += $"<meta property=\"og:title\" content=\"{HttpUtility.HtmlEncode(GetName())} (in {HttpUtility.HtmlEncode(parent.metadata.title)})\">\n";
            metas += $"<meta property=\"og:description\" content=\"{HttpUtility.HtmlEncode(GetSizeString())} - Recorded {HttpUtility.HtmlEncode(GetDateString())}\n{HttpUtility.HtmlEncode(metadata.description)}\">\n";
            if(metadata.template_type == "FILE_AUDIO")
            {
                //Add audio metadatas
                metas += $"<meta name=\"twitter:card\" content=\"summary_large_image\">\n"; //Makes the image appear large
                metas += $"<meta name=\"og:image\" content=\"{HttpUtility.HtmlEncode(site.config.absolute_pathname_prefix + GetUrlSafePath() + "?action=audio_meta_preview&thumb_padding=15&thumb_height=100&thumb_fore_color=3882dc&thumb_back_color=202225&thumb_decim=2&thumb_radius=8")}\">\n"; //Audio wavform
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
                {"PATH", pathElements },
                {"META_TAGS", metas }
            });
        }
    }
}
