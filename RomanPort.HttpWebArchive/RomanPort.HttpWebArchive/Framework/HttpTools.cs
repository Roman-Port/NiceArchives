using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace RomanPort.HttpWebArchive.Framework
{
    public static class HttpTools
    {
        public static async Task WriteString(this HttpContext e, string text)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            await e.Response.Body.WriteAsync(b);
        }
    }
}
