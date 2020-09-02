using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace RomanPort.HttpWebArchive.Framework.FS
{
    public class ArchivedDirectory : ArchivedObject
    {
        //Reletaive path with trailing slash. For example, /test/
        public DirectoryInfo info;
        public ArchivedDirectory parent;
        public List<ArchivedDirectory> subDirs;
        public List<ArchivedFile> subFiles;
        public ArchivedDirectoryMetadata metadata;
        public bool isRoot;
        public string footer; //A custom footer for this
        public DateTime uploadedDate;

        public ArchivedDirectory(ArchiveSite site, DirectoryInfo dir) : base(site)
        {
            subDirs = new List<ArchivedDirectory>();
            subFiles = new List<ArchivedFile>();
            this.info = dir;

            //Load the metadata if it has some. If it doesn't create it
            if (File.Exists(dir.FullName + "/INFO.dirmeta"))
                metadata = JsonConvert.DeserializeObject<ArchivedDirectoryMetadata>(File.ReadAllText(dir.FullName + "/INFO.dirmeta"));
            else
                throw new Exception("No info found for dir!");
            uploadedDate = new FileInfo(dir.FullName + "/INFO.dirmeta").LastWriteTimeUtc;

            //Load the custom footer, if any
            if (File.Exists(dir.FullName + "/FOOTER.dirmeta"))
                footer = File.ReadAllText(dir.FullName + "/FOOTER.dirmeta");
            else
                footer = "";
        }

        public override string GetName()
        {
            return info.Name;
        }

        public override long GetSize()
        {
            long size = 0;
            foreach (var p in subDirs)
                size += p.GetSize();
            foreach (var p in subFiles)
                size += p.GetSize();
            return size;
        }

        public override DateTime GetLastModified()
        {
            long latest = 0;
            foreach (var p in subDirs)
                latest = Math.Max(latest, p.GetLastModified().Ticks);
            foreach (var p in subFiles)
                latest = Math.Max(latest, p.GetLastModified().Ticks);
            return new DateTime(latest);
        }

        public override DateTime GetUploadedDate()
        {
            return uploadedDate;
        }

        public int GetFileCount()
        {
            int count = subFiles.Count;
            foreach (var p in subDirs)
                count += p.GetFileCount();
            return count;
        }

        public static ArchivedDirectory GetRootDirectory(ArchiveSite site, string dirPathname)
        {
            ArchivedDirectory d = new ArchivedDirectory(site, new DirectoryInfo(dirPathname));
            d.path = "/";
            d.SeekSubObjects();
            d.isRoot = true;
            site.AddStoredObject(d);
            return d;
        }

        public static ArchivedDirectory GetSubDirectory(ArchivedDirectory parent, string dirPathname)
        {
            ArchivedDirectory d = new ArchivedDirectory(parent.site, new DirectoryInfo(dirPathname));
            d.parent = parent;
            d.path = parent.path + d.GetName() + "/";
            d.SeekSubObjects();
            d.isRoot = false;
            d.site.AddStoredObject(d);
            return d;
        }

        private void SeekSubObjects()
        {
            string[] dirs = Directory.GetDirectories(info.FullName);
            string[] files = Directory.GetFiles(info.FullName);
            foreach (var d in dirs)
                subDirs.Add(ArchivedDirectory.GetSubDirectory(this, d));
            foreach (var f in files)
            {
                if(f.EndsWith(".meta"))
                    subFiles.Add(new ArchivedFile(this.site, this, f));
            }
        }

        public override void RelocateToDir(string newPath)
        {
            Directory.Move(info.FullName, newPath + info.Name);
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
            if (action == "zip")
                await CreateZipFile(e);
            else if (action == "admin_upload" && site.IsAdminAuthenticated(e))
                await OnAdminUploadRequest(e);
            else if (action == "admin_mkdir" && site.IsAdminAuthenticated(e))
                await OnAdminCreateDir(e);
            else if (action == "admin_modify" && site.IsAdminAuthenticated(e))
                await OnAdminEditDir(e);
            else if (action == "admin_delete" && site.IsAdminAuthenticated(e))
                await OnAdminDeleteDir(e);
            else if (action == "admin_audio_editor")
                await OnAdminAudioEditorFrontend(e);
            else if (action == "admin_rest_audio_upload" && site.IsAdminAuthenticated(e))
                await OnAdminAudioEditorUpload(e);
        }

        public async Task OnStandardRequest(HttpContext e)
        {
            //Write headers
            e.Response.ContentType = "text/html";

            //Set cookie
            SaveCurrentItemCookie(e);

            //Get the sort type
            DirectorySortType sort = metadata.default_sort;
            bool sortReverse = false;
            if(e.Request.Query.ContainsKey("sort"))
                Enum.TryParse<DirectorySortType>(e.Request.Query["sort"], out sort);
            if (e.Request.Query.ContainsKey("sort_reverse"))
                sortReverse = bool.TryParse(e.Request.Query["sort_reverse"], out sortReverse);

            //Create path
            string pathElements = "";
            var parentElement = this;
            while(parentElement != null)
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
            await TemplateManager.WriteTemplate(e.Response.Body, "PAGE.DIR.PRE_CONTENT", new Dictionary<string, string>
            {
                {"FOLDER_TITLE", HttpUtility.HtmlEncode(metadata.title) },
                {"FOLDER_DESCRIPTION", HttpUtility.HtmlEncode(metadata.description) },
                {"URL", site.config.client_pathname_prefix + path_urlsafe },
                {"PATH", pathElements },
                {"CURRENT_SORT", sort.ToString() }
            });

            //Write the up directory button
            if(!isRoot)
            {
                await TemplateManager.WriteTemplate(e.Response.Body, "ITEM.UP", new Dictionary<string, string>
                {
                    {"PATH", site.config.client_pathname_prefix + parent.path_urlsafe + "?last=" + HttpUtility.UrlEncode(GetName()) },
                    {"SELECT_SORT_0", sort == DirectorySortType.DEFAULT ? " selected" : "" },
                    {"SELECT_SORT_1", sort == DirectorySortType.FILE_DATE ? " selected" : "" },
                    {"SELECT_SORT_2", sort == DirectorySortType.NAME ? " selected" : "" },
                    {"SELECT_SORT_3", sort == DirectorySortType.SIZE ? " selected" : "" },
                    {"SELECT_SORT_4", sort == DirectorySortType.UPLOADED_DATE ? " selected" : "" }
                });
            }

            //Attempt to get the last served from the cookie. We add a highlight to that item
            string last = "";
            if (e.Request.Cookies.ContainsKey(COOKIE_PREVIOUS_ITEM))
                last = e.Request.Cookies[COOKIE_PREVIOUS_ITEM];

            //Write folders
            foreach (var f in SortObjects(subDirs.ToArray(), sort, sortReverse))
                await WriteObjectListingHere(e, f, last);

            //Write files
            foreach (var f in SortObjects(subFiles.ToArray(), sort, sortReverse))
                await WriteObjectListingHere(e, f, last);

            //Write admin bar if authenticated
            if (site.IsAdminAuthenticated(e))
                await e.WriteString($"<div class=\"adminbar\"><b>Archive Admin</b> - <a href=\"?action=admin_upload\">[Upload File]</a> <a href=\"?action=admin_audio_editor\">[Edit &amp; Upload Audio]</a> <a href=\"?action=admin_mkdir\">[Create Directory]</a> - <a href=\"?action=admin_modify\">[Modify Directory]</a> <a href=\"?action=admin_delete\">[Delete Directory]</a> - <a href=\"{site.config.client_pathname_prefix}/signout\">[Log Out]</a></div>");

            //Write end
            await TemplateManager.WriteTemplate(e.Response.Body, "PAGE.DIR.POST_CONTENT", new Dictionary<string, string>
            {
                {"FOOTER", footer },
                {"ADMIN_SIGNIN_URL", site.config.client_pathname_prefix + "/signin" }
            });
        }

        private IEnumerable<ArchivedObject> SortObjects(ArchivedObject[] objects, DirectorySortType sort, bool reverse)
        {
            if (sort == DirectorySortType.DEFAULT && !reverse)
                return objects;

            IEnumerable<ArchivedObject> result;
            if (sort == DirectorySortType.DEFAULT)
                result = objects.ToArray();
            else if (sort == DirectorySortType.FILE_DATE)
                result = objects.OrderByDescending(x => x.GetLastModified());
            else if (sort == DirectorySortType.NAME)
                result = objects.OrderBy(x => x.GetName());
            else if (sort == DirectorySortType.SIZE)
                result = objects.OrderByDescending(x => x.GetSize());
            else if (sort == DirectorySortType.UPLOADED_DATE)
                result = objects.OrderByDescending(x => x.GetUploadedDate());
            else
                throw new Exception("Unsupported sort method.");

            //Flip
            if (reverse)
                return result.Reverse();
            else
                return result;
        }

        private async Task WriteObjectListingHere(HttpContext e, ArchivedObject f, string last)
        {
            //Check if this was the last file
            if(last == f.GetName())
                await f.WriteListing(e.Response.Body, " aitem_last");
            else
                await f.WriteListing(e.Response.Body, "");
        }

        public override async Task WriteListing(Stream s, string customClasses)
        {
            await TemplateManager.WriteTemplate(s, "ITEM.DIR", new Dictionary<string, string>
            {
                {"ITEM_NAME", info.Name },
                {"ITEM_PATH", site.config.client_pathname_prefix + path },
                {"CUSTOM_CLASSES", customClasses },
                {"ITEM_SIZE", GetSizeString() },
                {"ITEM_MODIFIED", GetLastModifiedString(false) },
                {"ITEM_MODIFIED_SHORT", GetLastModifiedString(true) },
                {"ITEM_COUNT", GetFileCount().ToString() },
                {"ITEM_COUNT_LABEL", GetFileCount() == 1 ? "file" : "files" }
            });
        }

        private async Task CreateZipFile(HttpContext e)
        {
            //Write headers
            e.Response.ContentType = "application/zip";
            e.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"RomanPortArchives-{GetName()}-{new Random().Next()}.zip\"");
            
            //Create ZIP
            using(ZipArchive za = new ZipArchive(e.Response.Body, ZipArchiveMode.Create, true))
            {
                //Create error list
                List<string> errors = new List<string>();

                //Write all recursively
                await CreateZipFile_WriteDir(za, this, errors);

                //Create metadata string
                string meta = $"Downloaded from RomanPort Archives\n\nMetadata version: 1\nCreated at: {DateTime.UtcNow.ToShortDateString()} {DateTime.UtcNow.ToLongTimeString()} UTC\nPath: {path}";
                if(errors.Count > 0)
                {
                    meta += "\n\nThe following files were stored remotely and need to be downloaded separately. They are not stored in this file!\n";
                    foreach (var err in errors)
                        meta += err + "\n";
                } else
                {
                    meta += "\n\nAll files were successfully downloaded.\n";
                }

                //Write metadata
                byte[] metadata = Encoding.UTF8.GetBytes(meta);
                var entry = za.CreateEntry("INFO.txt", CompressionLevel.Optimal);
                using (Stream entryStream = entry.Open())
                    entryStream.Write(metadata, 0, metadata.Length);
            }
        }

        private async Task CreateZipFile_WriteDir(ZipArchive za, ArchivedDirectory dir, List<string> failedFiles)
        {
            //Add all files here
            foreach(var f in dir.subFiles)
            {
                //Create path
                string filePath = f.path.Substring(path.Length);

                //Check if this is remote
                if (f.metadata.is_remote)
                {
                    failedFiles.Add($"{filePath} - Download at {f.metadata.remote_url} ({f.metadata.remote_size} bytes)");
                }
                
                //Write
                var entry = za.CreateEntry(filePath, CompressionLevel.Optimal);
                entry.LastWriteTime = f.GetDate();
                using (Stream entryStream = entry.Open())
                using (Stream fileStream = f.OpenFile())
                    await fileStream.CopyToAsync(entryStream);
            }

            //Add all files in subdirs
            foreach (var f in dir.subDirs)
                await CreateZipFile_WriteDir(za, f, failedFiles);
        }

        public async Task OnAdminUploadRequest(HttpContext e)
        {
            //Check if we need to show the form
            if (e.Request.Method.ToUpper() != "POST")
            {
                //Set headers
                e.Response.ContentType = "text/html";

                //Make default tags
                string tags = "";
                if(subFiles.Count > 0)
                {
                    for (int i = 0; i < subFiles[0].metadata.tags.Length; i+=1)
                    {
                        tags += subFiles[0].metadata.tags[i];
                        if (i != subFiles[0].metadata.tags.Length - 1)
                            tags += ",";
                    }
                }

                //Show form
                await TemplateManager.WriteTemplate(e.Response.Body, "ADMIN.UPLOAD_FILE", new Dictionary<string, string>
                {
                    {"PATH", path },
                    {"TAGS", tags }
                });
            } else
            {
                //Run
                var form = await e.Request.ReadFormAsync();

                //Read data
                string name = form["file_name"];
                string type = form["file_type"];
                string description = form["description"];
                string[] tags = form["tags"].ToString().Split(',');
                DateTime time = new DateTime(int.Parse(form["dt_year"]), int.Parse(form["dt_month"]), int.Parse(form["dt_day"]), 12, 0, 0);

                //Get the pathname
                string pathname = info.FullName + "/" + name;

                //Check
                if(File.Exists(pathname))
                {
                    e.Response.ContentType = "text/html";
                    await e.WriteString("<span style=\"color: red;\">The file you're attempting to create already exists. Aborting!</span>");
                    return;
                }

                //Save file
                using (FileStream fs = new FileStream(pathname, FileMode.Create))
                using (Stream f = form.Files["file_upload"].OpenReadStream())
                    await f.CopyToAsync(fs);

                //Create metadata
                var metadata = new ArchivedFileMetadata
                {
                    template_type = type,
                    tags = tags,
                    description = description,
                    custom_data = new Dictionary<string, string>(),
                    time = time,
                    time_approx = true
                };
                File.WriteAllText(pathname + ".meta", JsonConvert.SerializeObject(metadata));

                //Refresh
                site.RefreshObjects();

                //Redirect to this
                e.Response.Redirect(site.config.client_pathname_prefix + path_urlsafe, false);
            }
        }

        public async Task OnAdminCreateDir(HttpContext e)
        {
            //Check if we need to show the form
            if (e.Request.Method.ToUpper() != "POST")
            {
                //Set headers
                e.Response.ContentType = "text/html";

                //Show form
                await TemplateManager.WriteTemplate(e.Response.Body, "ADMIN.CREATE_DIR", new Dictionary<string, string>
                {
                    {"PATH", path }
                });
            }
            else
            {
                //Run
                var form = await e.Request.ReadFormAsync();

                //Read data
                string name = form["dir_name"];
                string title = form["dir_title"];
                string description = form["dir_description"];
                string footer = form["footer"];

                //Get the pathname
                string pathname = info.FullName + "/" + name + "/";

                //Check
                if (Directory.Exists(pathname))
                {
                    e.Response.ContentType = "text/html";
                    await e.WriteString("<span style=\"color: red;\">The directory you're attempting to create already exists. Aborting!</span>");
                    return;
                }

                //Create directory
                Directory.CreateDirectory(pathname);

                //Create metadata
                var metadata = new ArchivedDirectoryMetadata
                {
                    title = title,
                    description = description
                };
                File.WriteAllText(pathname + "INFO.dirmeta", JsonConvert.SerializeObject(metadata));

                //Create footer (if any)
                if(footer.Length > 0)
                    File.WriteAllText(pathname + "FOOTER.dirmeta", footer);

                //Refresh
                site.RefreshObjects();

                //Redirect to this
                e.Response.Redirect(site.config.client_pathname_prefix + path_urlsafe, false);
            }
        }

        public async Task OnAdminEditDir(HttpContext e)
        {
            //Check if we need to show the form
            if (e.Request.Method.ToUpper() != "POST")
            {
                //Set headers
                e.Response.ContentType = "text/html";

                //Show form
                await TemplateManager.WriteTemplate(e.Response.Body, "ADMIN.EDIT_DIR", new Dictionary<string, string>
                {
                    {"PATH", path },
                    {"DIR_TITLE", metadata.title },
                    {"DIR_DESCRIPTION", HttpUtility.HtmlEncode(metadata.description) },
                    {"DIR_FOOTER", HttpUtility.HtmlEncode(footer) },
                });
            }
            else
            {
                //Run
                var form = await e.Request.ReadFormAsync();

                //Read data
                string title = form["dir_title"];
                string description = form["dir_description"];
                string newFooter = form["footer"];

                //Create metadata
                var metadata = new ArchivedDirectoryMetadata
                {
                    title = title,
                    description = description
                };
                File.WriteAllText(info.FullName + "/INFO.dirmeta", JsonConvert.SerializeObject(metadata));

                //Create footer (if any)
                if (newFooter.Length > 0)
                    File.WriteAllText(info.FullName + "/FOOTER.dirmeta", newFooter);
                else if (File.Exists(info.FullName + "/FOOTER.dirmeta"))
                    File.Delete(info.FullName + "/FOOTER.dirmeta");

                //Update
                metadata.title = title;
                metadata.description = description;
                footer = newFooter;

                //Redirect to this
                e.Response.Redirect(site.config.client_pathname_prefix + path_urlsafe, false);
            }
        }

        public async Task OnAdminDeleteDir(HttpContext e)
        {
            //Check if this is the root dir
            if (isRoot)
            {
                e.Response.ContentType = "text/html";
                await e.WriteString("<span style=\"color: red;\">The root folder cannot be removed. Aborting!</span>");
                return;
            }

            //Check if we need to show the form
            if (e.Request.Method.ToUpper() != "POST")
            {
                //Set headers
                e.Response.ContentType = "text/html";

                //Show form
                await TemplateManager.WriteTemplate(e.Response.Body, "ADMIN.DELETE_DIR", new Dictionary<string, string>
                {
                    {"PATH", path },
                    {"DIR_TITLE", HttpUtility.HtmlEncode(metadata.title) }
                });
            }
            else
            {
                //Run
                var form = await e.Request.ReadFormAsync();

                //Read data
                string title = form["confirm_title"];

                //Validate
                if(metadata.title != title)
                {
                    e.Response.ContentType = "text/html";
                    await e.WriteString("<span style=\"color: red;\">You failed to confirm removal. Aborting!</span>");
                    return;
                }

                //Trash
                TrashMedia();

                //Redirect to parent
                e.Response.Redirect(site.config.client_pathname_prefix + parent.path_urlsafe, false);
            }
        }

        /* Audio editor */

        public async Task OnAdminAudioEditorFrontend(HttpContext e)
        {
            //Set headers
            e.Response.ContentType = "text/html";

            //Make default tags
            string tags = "";
            if (subFiles.Count > 0)
            {
                for (int i = 0; i < subFiles[0].metadata.tags.Length; i += 1)
                {
                    tags += subFiles[0].metadata.tags[i];
                    if (i != subFiles[0].metadata.tags.Length - 1)
                        tags += ",";
                }
            }

            //Show form
            await TemplateManager.WriteTemplate(e.Response.Body, "ADMIN.AUDIO_EDITOR", new Dictionary<string, string>
            {
                {"PATH", path },
                {"TAGS", tags }
            });
        }

        public async Task OnAdminAudioEditorUpload(HttpContext e)
        {
            //Run
            var form = await e.Request.ReadFormAsync();

            //Get the pathname
            string pathname = info.FullName + "/" + form["file_name"];

            //Check
            if (File.Exists(pathname))
            {
                await e.WriteString(JsonConvert.SerializeObject(new AudioEditorResponseData
                {
                    ok = false,
                    dir_url = null,
                    error_string = "The file you're attempting to create already exists. Aborting!"
                }));
                return;
            }

            //Read audio parameters
            string audioSampleRate = form["audio_sample_rate"];
            string audioChannels = form["audio_channels"];
            string audioFormat = form["audio_format"];
            string audioGain = form["audio_gain"];

            //Open destination and source for streaming
            using (FileStream destination = new FileStream(pathname, FileMode.Create))
            using (Stream source = form.Files["audio_payload"].OpenReadStream())
            {
                //Create FFMPEG parameters
                string args = $"-f {audioFormat} -ar {audioSampleRate} -ac {audioChannels} -i - -filter:a \"volume={audioGain}\" -f mp3 -";

                //Start FFMPEG instance
                Process ffmpeg = Process.Start(new ProcessStartInfo
                {
                    FileName = Program.site.config.ffmpeg_path,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true
                });

                //Copy
                Task copyToTask = source.CopyToAsync(ffmpeg.StandardInput.BaseStream);
                Task copyFromTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(destination);

                //Wait for copy to finish
                await copyToTask;

                //Close input stream
                ffmpeg.StandardInput.BaseStream.Close();

                //Wait for application to end
                await copyFromTask;
                ffmpeg.WaitForExit();
            }

            //Save metadata and refresh
            SaveUploadedFileMetadata(form);

            //Return
            await e.WriteString(JsonConvert.SerializeObject(new AudioEditorResponseData
            {
                ok = true,
                dir_url = site.config.client_pathname_prefix + path_urlsafe,
                error_string = null
            }));
        }

        class AudioEditorResponseData
        {
            public bool ok;
            public string dir_url;
            public string error_string;
        }

        private void SaveUploadedFileMetadata(IFormCollection form)
        {
            //Read data
            string name = form["file_name"];
            string type = form["file_type"];
            string description = form["description"];
            string[] tags = form["tags"].ToString().Split(',');
            DateTime time = new DateTime(int.Parse(form["dt_year"]), int.Parse(form["dt_month"]), int.Parse(form["dt_day"]), 12, 0, 0);

            //Get the pathname
            string pathname = info.FullName + "/" + name;

            //Create metadata
            var metadata = new ArchivedFileMetadata
            {
                template_type = type,
                tags = tags,
                description = description,
                custom_data = new Dictionary<string, string>(),
                time = time,
                time_approx = true
            };
            File.WriteAllText(pathname + ".meta", JsonConvert.SerializeObject(metadata));

            //Refresh
            site.RefreshObjects();
        }
    }
}
