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
using System.Threading.Tasks;

namespace RomanPort.HttpWebArchive
{
    public class ArchiveSite
    {
        public ArchiveConfig config;
        public Dictionary<string, ArchivedObject> objects; //List of objects stored by their path
        public ArchivedDirectory root;

        public string activeToken;

        public ArchiveSite(string configPath)
        {
            config = JsonConvert.DeserializeObject<ArchiveConfig>(File.ReadAllText(configPath));
            objects = new Dictionary<string, ArchivedObject>();
            TemplateManager.LoadTemplates(config.templates_dir);
            RefreshObjects();
        }

        public void RefreshObjects()
        {
            objects.Clear();
            root = ArchivedDirectory.GetRootDirectory(this, config.archives_dir);
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
