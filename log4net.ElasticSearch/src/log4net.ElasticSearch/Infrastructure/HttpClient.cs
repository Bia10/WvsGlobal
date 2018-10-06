﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security;
using System.Text;
using log4net.ElasticSearch.Models;
using Uri = System.Uri;

namespace log4net.ElasticSearch.Infrastructure
{
    public interface IHttpClient
    {
        void Post(Uri uri, logEvent item);
        void PostBulk(Uri uri, IEnumerable<logEvent> items);
    }

    public class HttpClient : IHttpClient
    {
        const string ContentType = "application/json";
        const string Method = "POST";

        public void Post(Uri uri, logEvent item)
        {
            try
            {
                var httpWebRequest = RequestFor(uri);

                using (var streamWriter = GetRequestStream(httpWebRequest))
                {
                    streamWriter.Write(item.ToJson());
                    streamWriter.Flush();

                    var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                    httpResponse.Close();

                    if (httpResponse.StatusCode != HttpStatusCode.Created)
                    {
                        throw new WebException(
                            "Failed to post {0} to {1}.".With(item.GetType().Name, uri));
                    }
                }
            }
            catch (Exception ex)
            {
                //TODO this is a hotfix
            }

        }

        /// <summary>
        /// Post the events to the Elasticsearch _bulk API for faster inserts
        /// </summary>
        /// <typeparam name="T">Type/item being inserted. Should be a list of events</typeparam>
        /// <param name="uri">Fully formed URI to the ES endpoint</param>
        /// <param name="items">List of logEvents</param>
        public void PostBulk(Uri uri, IEnumerable<logEvent> items)
        {
            try
            {
                var httpWebRequest = RequestFor(uri);
                using (var requestStream = httpWebRequest.GetRequestStream())
                using (var streamWriter = new StreamWriter(requestStream))
                {
                    // For each logEvent, we build a bulk API request which consists of one line for
                    // the action, one line for the document. In this case "index" (idempotent) and then the doc
                    // Since we're appending _bulk to the end of the Uri, ES will default to using the
                    // index and type already specified in the Uri segments
                    foreach (var item in items)
                    {
                        streamWriter.WriteLine("{\"index\" : {} }");
                        streamWriter.WriteLine(item.ToJson());
                    }
                    streamWriter.Flush();
                }

                try
                {
                    using (var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse())
                    {
                        if (httpResponse.StatusCode != HttpStatusCode.Created &&
                            httpResponse.StatusCode != HttpStatusCode.OK)
                        {
                            Trace.WriteLine($"Failed to post to {uri}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Failed to post to {uri}. {ex}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to post to {uri}. {ex}");
            }
        }

        public static HttpWebRequest RequestFor(Uri uri)
        {
            var httpWebRequest = (HttpWebRequest)WebRequest.Create(uri);

            httpWebRequest.ContentType = ContentType;
            httpWebRequest.Method = Method;

            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                httpWebRequest.Headers.Remove(HttpRequestHeader.Authorization);
                httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(uri.UserInfo)));
            }

            return httpWebRequest;
        }

        static StreamWriter GetRequestStream(WebRequest httpWebRequest)
        {
            return new StreamWriter(httpWebRequest.GetRequestStream());
        }
    }
}