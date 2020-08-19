using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace RomanPort.HttpWebArchive.Framework
{
    public class SiteAuthEngine
    {
        private static bool busy;
        private static RNGCryptoServiceProvider provider = new RNGCryptoServiceProvider();

        public static async Task OnLoginRequest(ArchiveSite site, HttpContext e)
        {
            if (e.Request.Method == "GET")
            {
                //Write form
                e.Response.ContentType = "text/html";
                await e.WriteString($"<u>Sign in</u><br><br><form method=\"post\"><textarea id=\"key\" name=\"key\" placeholder=\"Key\" rows=\"4\" cols=\"80\"></textarea><br><br><input type=\"hidden\" value=\"{e.Request.Headers["referer"]}\" id=\"return\" name=\"return\" /><input type=\"submit\" value=\"Sign In\"></form>");
            }
            else
            {
                //Delay
                if(busy)
                {
                    //Multiple requests at once!
                    e.Response.ContentType = "text/html";
                    await e.WriteString("<span style=\"color: red;\">There is an ongoing login attempt. Please wait and try again.</span>");
                }
                busy = true;
                await Task.Delay(3000);
                busy = false;
                
                //Parse form
                var form = await e.Request.ReadFormAsync();
                if (AuthenticateForm(site, form))
                {
                    //Generate a token string
                    site.activeToken = GenerateSecureString(32, "1234567890ABCDEF".ToCharArray());

                    //Set it
                    e.Response.Cookies.Append(ArchiveSite.ADMIN_TOKEN_COOKIE, site.activeToken);

                    //Redirect
                    RedirectBack(site, e, form["return"]);
                }
                else
                {
                    //Failed
                    e.Response.ContentType = "text/html";
                    await e.WriteString("<span style=\"color: red;\">Could not log in. Please try again.</span>");
                }
            }
        }

        public static async Task OnSignoutRequest(ArchiveSite site, HttpContext e)
        {
            //Clear token
            site.activeToken = null;

            //Return
            RedirectBack(site, e);
        }

        public static void RedirectBack(ArchiveSite site, HttpContext e, string overridePath = null)
        {
            string returnPath = site.config.client_pathname_prefix;
            if(e.Request.Headers.ContainsKey("Referer"))
                returnPath = e.Request.Headers["Referer"];
            if (overridePath != null)
                returnPath = overridePath;
            e.Response.Redirect(returnPath, false);
        }

        private static bool AuthenticateForm(ArchiveSite site, IFormCollection form)
        {
            if (!form.ContainsKey("key"))
                return false;
            if (site.config.admin_key == null)
                return false;
            return site.config.admin_key == form["key"];
        }

        public static string GenerateSecureString(int len, char[] charset)
        {
            var byteArray = new byte[len];
            provider.GetBytes(byteArray);
            char[] outputChars = new char[len];
            for (var i = 0; i < len; i++)
            {
                char c = charset[byteArray[i] % (charset.Length - 1)];
                outputChars[i] = c;
            }
            return new string(outputChars);
        }
    }
}
