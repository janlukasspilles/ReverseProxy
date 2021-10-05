using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ReverseProxy
{
    /// <summary>
    /// Http redirecting reverse proxy
    /// Denis Voituron - https://www.codeproject.com/Articles/31329/Simple-Reverse-Proxy-in-C-description-and-depl
    /// </summary>
    public class HttpRedirectingProxy
    {
        String _targetUrl;
        String _urlToListen;
        private readonly Action<string> _logMethod;

        public HttpRedirectingProxy(String urlToListen, String targetUrl, Action<string> logMethod = null)
        {
            _urlToListen = urlToListen;
            _targetUrl = targetUrl;
            _logMethod = logMethod;
        }
        ///
        /// Method is called when new request to redirecting proxy is received
        ///
        /// HTTP context
        public void ProcessRequest(HttpListenerContext ctx)
        {
            try
            {
                // Create a request
                HttpWebRequest redirectedRequest = CreateRedirectedRequest(ctx);

                // Send the request to the remote server and get the response
                HttpWebResponse redirectedResponse = SendRedirectedRequest(redirectedRequest);

                //using (var redirectedResponse = (HttpWebResponse) await SendRedirectedRequest())

                    // Copy headers
                    ctx.Response.Headers.Clear();
                foreach (String hdrName in redirectedResponse.Headers.AllKeys)
                {
                    // exclude headers which should be modified using special properties
                    if (hdrName == "Content-Type" || hdrName == "Content-Length" || hdrName == "Connection")
                        continue;
                    try
                    {
                        ctx.Response.Headers.Add(hdrName, redirectedResponse.Headers.Get(hdrName));
                    }
                    catch (Exception ex)
                    {
                        string err = String.Format(@"HttpRedirectingProxy – Exception processing request from { 3} – { 0}
                        { 1}: { 2} – copying response headers failed: { 4}", ctx.Request.HttpMethod, ctx.Request.Url, ex, ctx.Request.RemoteEndPoint, hdrName);
                        log(err);
                    }
                }

                // Copy content
                ctx.Response.ContentType = redirectedResponse.ContentType;
                byte[] responseData = GetResponseStreamBytes(redirectedResponse);
                ctx.Response.ContentEncoding = Encoding.UTF8; // .GetEncoding(redirectedResponse.ContentEncoding);
                ctx.Response.ContentLength64 = responseData.Length;
                ctx.Response.OutputStream.Write(responseData, 0, responseData.Length);

                // Copy cookies
                foreach (Cookie receivedCookie in redirectedResponse.Cookies)
                {
                    Cookie c = new Cookie(receivedCookie.Name,
                    receivedCookie.Value);
                    c.Domain = ctx.Request.Url.Host;
                    c.Expires = receivedCookie.Expires;
                    c.HttpOnly = receivedCookie.HttpOnly;
                    c.Path = receivedCookie.Path;
                    c.Secure = receivedCookie.Secure;
                    ctx.Response.Cookies.Add(c);
                }

                // Close streams
                redirectedResponse.Close();

                log(String.Format(@"HttpRedirectingProxy – request from { 4} – { 0}
                { 1} / response { 2}
                { 3}", ctx.Request.HttpMethod, ctx.Request.Url, ctx.Response.StatusCode, ctx.Response.StatusDescription, ctx.Request.RemoteEndPoint));
            }
            catch (Exception ex)
            {
                string err = String.Format(@"HttpRedirectingProxy – Exception processing request from { 3} – { 0}
                { 1}: { 2}", ctx.Request.HttpMethod, ctx.Request.Url, ex, ctx.Request.RemoteEndPoint);
                log(err);
                ctx.Response.StatusCode = 500;
                byte[] buf = Encoding.UTF8.GetBytes(err);
                ctx.Response.ContentLength64 = buf.Length;
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                ctx.Response.ContentType = "text / html";
            }
        }

        /// <summary>
        /// Create redirected request to the target server
        /// </summary>
        /// Request to send to the server
        HttpWebRequest CreateRedirectedRequest(HttpListenerContext ctx)
        {
            // Create a request to the actual server
            string redirectedUrl = ctx.Request.Url.AbsoluteUri.Replace(_urlToListen, _targetUrl);
            HttpWebRequest redirectedRequest = (HttpWebRequest)WebRequest.Create(redirectedUrl);
            redirectedRequest.ServicePoint.Expect100Continue = false; // https://stackoverflow.com/questions/14063327/how-to-disable-the-expect-100-continue-header-in-httpwebrequest-for-a-single

            // Set some options
            redirectedRequest.Method = ctx.Request.HttpMethod;
            redirectedRequest.UserAgent = ctx.Request.UserAgent;
            redirectedRequest.KeepAlive = true;
            redirectedRequest.ContentType = ctx.Request.ContentType;

            // Copy cookies
            redirectedRequest.CookieContainer = new CookieContainer();
            for (int i = 0; i < ctx.Request.Cookies.Count; i++)
            {
                Cookie navigatorCookie = ctx.Request.Cookies[i];
                Cookie c = new Cookie(navigatorCookie.Name, navigatorCookie.Value);
                c.Domain = new Uri(redirectedUrl).Host;
                c.Expires = navigatorCookie.Expires;
                c.HttpOnly = navigatorCookie.HttpOnly;
                c.Path = navigatorCookie.Path;
                c.Secure = navigatorCookie.Secure;
                redirectedRequest.CookieContainer.Add(c);
            }

            // If body exists - write the body data extracted from the incoming request
            // Is it request with Body?
            String body = "";
            if (ctx.Request.HasEntityBody)
            {
                using (System.IO.Stream bodyStream = ctx.Request.InputStream) // here we have data
                {
                    using (System.IO.StreamReader reader = new System.IO.StreamReader(bodyStream, Encoding.UTF8))
                    {
                        body = reader.ReadToEnd();
                    }
                }
                byte[] data = Encoding.UTF8.GetBytes(body);
                using (var stream = redirectedRequest.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }

            }

            return redirectedRequest;
        }

        /// <summary>
        /// Send the request to the target server and return the response
        /// </summary>
        /// Request to send to the server 
        /// Response received from the remote server
        ///           or null if page not found 
        HttpWebResponse SendRedirectedRequest(HttpWebRequest request)
        {
            HttpWebResponse response;

            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (WebException we)
            {
                response = we.Response as HttpWebResponse;
                if (response == null)
                    throw;
            }

            return response;
        }

        /// <summary>
        /// Return the response in bytes array format
        /// </summary>
        /// Response received
        ///             from the remote server 
        /// Response in bytes 
        byte[] GetResponseStreamBytes(HttpWebResponse response)
        {
            int bufferSize = 256;
            byte[] buffer = new byte[bufferSize];
            Stream responseStream;
            MemoryStream memoryStream = new MemoryStream();
            int remoteResponseCount;
            byte[] responseData;

            responseStream = response.GetResponseStream();
            remoteResponseCount = responseStream.Read(buffer, 0, bufferSize);

            while (remoteResponseCount > 0)
            {
                memoryStream.Write(buffer, 0, remoteResponseCount);
                remoteResponseCount = responseStream.Read(buffer, 0, bufferSize);
            }

            responseData = memoryStream.ToArray();

            memoryStream.Close();
            responseStream.Close();

            memoryStream.Dispose();
            responseStream.Dispose();

            return responseData;
        }

        /// <summary>
        /// Log callback
        /// </summary>
        /// 
        void log(string msg)
        {
            if (_logMethod != null)
            {
                _logMethod(msg);
            }
        }

    }
}
