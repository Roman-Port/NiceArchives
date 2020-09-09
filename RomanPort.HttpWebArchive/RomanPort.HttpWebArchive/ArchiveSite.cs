using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using RomanPort.HttpWebArchive.Framework;
using RomanPort.HttpWebArchive.Framework.FS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RomanPort.HttpWebArchive
{
    public class ArchiveSite
    {
        public ArchiveConfig config;
        public Dictionary<string, ArchivedObject> objects; //List of objects stored by their path
        public ArchivedDirectory root;

        private Thread metadataGeneratorThread;

        public string activeToken;

        public ArchiveSite(string configPath)
        {
            config = JsonConvert.DeserializeObject<ArchiveConfig>(File.ReadAllText(configPath));
            objects = new Dictionary<string, ArchivedObject>();
            TemplateManager.LoadTemplates(config.templates_dir);
            RefreshObjects();
        }

        private void StartMetadataGeneratorThread()
        {
            metadataGeneratorThread = new Thread(() =>
            {
                //Search through objects and find files that need metadata generated
                foreach (var f in objects)
                {
                    //Check if we should generate metadata
                    if (f.Value.rich_metadata_status == ArchivedObject.MetadataStatus.NOT_GENERATED)
                    {
                        Console.WriteLine("Starting metadata generation on " + f.Key);
                        try
                        {
                            f.Value.rich_metadata_status = f.Value.GenerateRichMetadata();
                        }
                        catch
                        {
                            f.Value.rich_metadata_status = ArchivedObject.MetadataStatus.FAILED;
                        }
                        Console.WriteLine("Result code: " + f.Value.rich_metadata_status.ToString());
                    }
                }

                //Set to null
                metadataGeneratorThread = null;
            });
            metadataGeneratorThread.IsBackground = true;
            metadataGeneratorThread.Start();
        }

        public void RefreshObjects()
        {
            //Kill metadata thread
            if (metadataGeneratorThread != null)
                metadataGeneratorThread.Abort();
            
            //Clear objects
            objects.Clear();

            //Discover all
            root = ArchivedDirectory.GetRootDirectory(this, config.archives_dir);

            //Start metadata generator thread
            StartMetadataGeneratorThread();
        }

        public void AddStoredObject(ArchivedObject o)
        {
            objects.Add(o.path, o);
        }

        public Task RunAsync()
        {
            var host = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    IPAddress addr = IPAddress.Any;
                    options.Listen(addr, config.port);

                })
                .UseStartup<ArchiveSite>()
                .Configure(Configure)
                .Build();

            return host.RunAsync();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.Run(OnHTTPRequest);
        }

        public async Task OnHTTPRequest(HttpContext e)
        {
            //Try to find the requested object
            try
            {
                if(e.Request.Path == "/signin")
                {
                    await SiteAuthEngine.OnLoginRequest(this, e);
                }
                else if (e.Request.Path == "/signout")
                {
                    await SiteAuthEngine.OnSignoutRequest(this, e);
                }
                else if (objects.ContainsKey(e.Request.Path.Value))
                {
                    //Run
                    await objects[e.Request.Path.Value].OnHttpRequest(e);
                }
                else
                {
                    //Not found. TODO
                    await e.WriteString("NOT FOUND");
                }
            } catch (Exception ex)
            {
                Console.WriteLine($"ERROR! {ex.Message} {ex.StackTrace}");
                e.Response.StatusCode = 500;
                await e.WriteString($"Error!\n\n{ex.Message} {ex.StackTrace}");
            }
        }

        public const string ADMIN_TOKEN_COOKIE = "RomanPortArchives-AdminToken";

        public bool IsAdminAuthenticated(HttpContext e)
        {
            //Make sure we have a token
            if (activeToken == null)
                return false;
            
            //Look for cookie
            if (!e.Request.Cookies.ContainsKey(ADMIN_TOKEN_COOKIE))
                return false;

            //Check if the cookie matches
            return e.Request.Cookies[ADMIN_TOKEN_COOKIE] == activeToken;
        }
    }
}
