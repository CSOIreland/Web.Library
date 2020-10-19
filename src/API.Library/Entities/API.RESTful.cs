﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.SessionState;

namespace API
{
    /// <summary>
    /// RESTful implementation
    /// </summary>
    public class RESTful : Common, IHttpHandler, IRequiresSessionState
    {

        #region Properties
        /// <summary>
        ///  List of URL Request Parameters
        /// </summary>
        private List<string> RequestParams = new List<string>();
        #endregion

        #region Methods
        /// <summary>
        /// ProcessRequest executed automatically by the iHttpHandler interface
        /// </summary>
        /// <param name="context"></param>
        public void ProcessRequest(HttpContext context)
        {
            Log.Instance.Info("API Interface Opened");

            // Set Mime-Type for the Content Type and override the Charset
            context.Response.Charset = null;
            // Set CacheControl to no-cache
            context.Response.CacheControl = "no-cache";

            // Extract the request parameters from the URL
            ParseRequest(ref context);

            // Check for the maintenance flag
            if (Maintenance)
            {
                ParseError(ref context, HttpStatusCode.ServiceUnavailable, "System maintenance");
            }

            // Authenticate and append credentials
            if (Authenticate(ref context) == false)
            {
                ParseError(ref context, HttpStatusCode.Unauthorized, "Invalid authentication");
            }

            // Get results from the relevant method with the params
            RESTful_Output result = GetResult(ref context);

            if (result == null)
            {
                ParseError(ref context, HttpStatusCode.InternalServerError, "Internal Error");
            }
            else if (result.statusCode == HttpStatusCode.OK)
            {
                context.Response.StatusCode = (int)result.statusCode;
                context.Response.ContentType = result.mimeType;

                if (!String.IsNullOrEmpty(result.fileName))
                {
                    context.Response.AppendHeader("Content-Disposition", new ContentDisposition { Inline = true, FileName = result.fileName }.ToString());
                }

                if (result.response?.GetType() == typeof(byte[]))
                {
                    context.Response.BinaryWrite(result.response);
                }
                else
                {
                    context.Response.Write(result.response);
                }
            }
            else
            {
                ParseError(ref context, result.statusCode, result.response);
            }

            Log.Instance.Info("API Interface Closed");
        }

        /// <summary>
        /// Parse the API error returning a HTTP status
        /// </summary>
        /// <param name="context"></param>
        /// <param name="statusCode"></param>
        /// <param name="statusDescription"></param>
        private void ParseError(ref HttpContext context, HttpStatusCode statusCode, string statusDescription = "")
        {
            Log.Instance.Info("IP: " + Utility.IpAddress + ", Status Code: " + statusCode.ToString() + ", Status Description: " + statusDescription);

            context.Response.StatusCode = (int)statusCode;
            if (!string.IsNullOrEmpty(statusDescription))
                context.Response.StatusDescription = statusDescription;
            context.Response.End();
        }

        /// <summary>
        /// Parse and validate the request
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        private void ParseRequest(ref HttpContext context)
        {
            try
            {
                /*
                URL : http://localhost:8080/mysite/page.aspx?p1=1&p2=2

                Value of HttpContext.Current.Request.Url.Host
                localhost

                Value of HttpContext.Current.Request.Url.Authority
                localhost:8080

                Value of HttpContext.Current.Request.Url.AbsolutePath
                /mysite/page.aspx

                Value of HttpContext.Current.Request.ApplicationPath
                /

                Value of HttpContext.Current.Request.Url.AbsoluteUri
                http://localhost:8080/mysite/page.aspx?p1=1&p2=2

                Value of HttpContext.Current.Request.RawUrl
                /mysite/page.aspx?p1=1&p2=2

                Value of HttpContext.Current.Request.Url.PathAndQuery
                /mysite/page.aspx?p1=1&p2=2
                */

                // Read the URL parameters and split the URL Absolute Path
                Log.Instance.Info("URL Absolute Path: " + context.Request.Url.AbsolutePath);
                RequestParams = Regex.Split(context.Request.Url.AbsolutePath, "api.restful/", RegexOptions.IgnoreCase).ToList();

                // Validate the Application path
                if (RequestParams.Count() != 2)
                {
                    ParseError(ref context, HttpStatusCode.BadRequest, "Invalid RESTful handler");
                }
                // Get the RESTful parameters
                RequestParams = RequestParams[1].Split('/').ToList();
                Log.Instance.Info("Request params: " + Utility.JsonSerialize_IgnoreLoopingReference(RequestParams));

                // Validate the request
                if (RequestParams.Count() == 0)
                {
                    ParseError(ref context, HttpStatusCode.BadRequest, "Invalid RESTful parameters");
                }

                // Verify the method exists
                if (!ValidateMethod(RequestParams))
                {
                    ParseError(ref context, HttpStatusCode.BadRequest, "RESTful method not found");
                }
            }
            catch (Exception e)
            {
                Log.Instance.Fatal(e);
                ParseError(ref context, HttpStatusCode.BadRequest, e.Message);
            }
        }

        /// <summary>
        /// Validate the requested method
        /// </summary>
        /// <param name="requestParams"></param>
        /// <returns></returns>
        private static bool ValidateMethod(List<string> requestParams)
        {
            MethodInfo methodInfo = MapMethod(requestParams);
            if (methodInfo == null)
                return false;
            else
                return true;
        }

        /// <summary>
        /// Map the request against the method
        /// </summary>
        /// <param name="requestParams"></param>
        /// <returns></returns>
        private static MethodInfo MapMethod(List<string> requestParams)
        {
            // Get Namespace(s).Class.Method
            string[] mapping = requestParams[0].Split('.');

            // At least 1 Namespace, 1 Class and 1 Method (3 in total) must be present
            if (mapping.Length < 3)
                return null;

            // Get method name
            string methodName = mapping[mapping.Length - 1];

            // Get the method path
            Array.Resize(ref mapping, mapping.Length - 1);
            string methodPath = string.Join(".", mapping);

            // Never allow to call Public Methods in the API Namespace
            if (mapping[0].ToUpperInvariant() == "API")
                return null;

            // Search in the entire Assemplies till finding the right one
            foreach (Assembly currentassembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type StaticClass = currentassembly.GetType(methodPath, false, true);
                if (StaticClass != null)
                {
                    MethodInfo methodInfo = StaticClass.GetMethod(methodName, new Type[] { typeof(RESTful_API) });
                    if (methodInfo == null)
                        return null;
                    else
                        return methodInfo;
                }
            }

            return null;
        }

        /// <summary>
        /// Invoke and return the results from the mapped method
        /// </summary>
        /// <returns></returns>
        private dynamic GetResult(ref HttpContext context)
        {
            // Set the API object
            RESTful_API apiRequest = new RESTful_API();
            apiRequest.method = RequestParams[0];
            apiRequest.parameters = RequestParams;
            apiRequest.userPrincipal = UserPrincipal;
            apiRequest.ipAddress = Utility.IpAddress;
            apiRequest.userAgent = Utility.UserAgent;

            // Hide password from logs
            Log.Instance.Info("API Request: " + Utility.JsonSerialize_IgnoreLoopingReference(apiRequest));

            // Verify the method exists
            MethodInfo methodInfo = MapMethod(RequestParams);

            //Invoke the API Method
            return methodInfo.Invoke(null, new object[] { apiRequest });
        }

        /// <summary>
        /// Handle reusable IHttpHandler instances 
        /// </summary>
        public bool IsReusable
        {
            // Set to false to ensure thread safe operations
            get { return true; }
        }

        #endregion
    }

    /// <summary>
    /// Define the Output structure required by the exposed API
    /// </summary>
    public class RESTful_Output
    {
        #region Properties
        /// <summary>
        /// RESTful response
        /// </summary>
        public dynamic response { get; set; }

        /// <summary>
        /// RESTful mime type
        /// </summary>
        public string mimeType { get; set; }

        /// <summary>
        /// RESTful status code
        /// </summary>
        public HttpStatusCode statusCode { get; set; }

        /// <summary>
        /// RESTful filename (optional)
        /// </summary>
        public string fileName { get; set; }
        #endregion
    }

    /// <summary>
    /// Define the API Class to pass to the exposed API 
    /// </summary>
    public class RESTful_API
    {
        #region Properties
        /// <summary>
        /// API method
        /// </summary>
        public string method { get; internal set; }

        /// <summary>
        /// API parameters
        /// </summary>
        public dynamic parameters { get; set; }

        /// <summary>
        /// Active Directory userPrincipal
        /// </summary>
        public dynamic userPrincipal { get; internal set; }

        /// <summary>
        /// Client IP address
        /// </summary>
        public string ipAddress { get; internal set; }

        /// <summary>
        /// Client user agent
        /// </summary>
        public string userAgent { get; internal set; }

        #endregion
    }
}
