﻿namespace Microsoft.IIS.Administration.WebServer.HttpRequestTracing
{
    using Core;
    using Core.Utils;
    using Files;
    using System;
    using System.Collections.Generic;
    using System.Dynamic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Xml;
    using Web.Administration;

    sealed class TracesHelper
    {
        private static readonly Fields RefFields = new Fields("url", "id", "http_status", "method", "time_taken");

        private static readonly XmlReaderSettings _xmlReaderSettings = new XmlReaderSettings() {
            Async = true
        };

        private IFileProvider _provider;
        private Site _site;

        public TracesHelper(IFileProvider provider, Site site)
        {
            _provider = provider;
            _site = site;
        }

        public async Task<IEnumerable<TraceInfo>> GetTraces()
        {
            IEnumerable<IFileInfo> files = null;
            string dir = _site.TraceFailedRequestsLogging.Directory;
            string path = string.IsNullOrEmpty(dir) ? null : Path.Combine(PathUtil.GetFullPath(dir), "W3SVC" + _site.Id);

            if (path != null) {
                files = _provider.GetFiles(path, "*.xml");
            }

            return await Task.WhenAll(files.Select(f => GetTraceInternal(f)));
        }

        public async Task<TraceInfo> GetTrace(string id)
        {
            return (await GetTraces()).FirstOrDefault(t => t.File.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        public object ToJsonModel(TraceInfo trace, Fields fields = null, bool full = true)
        {
            TraceId traceId = new TraceId(_site.Id, trace.File.Name);

            if (fields == null) {
                fields = Fields.All;
            }

            dynamic obj = new ExpandoObject();

            //
            // id
            obj.id = traceId.Uuid;

            //
            // url
            if (fields.Exists("url") && !string.IsNullOrEmpty(trace.Url)) {
                obj.url = trace.Url;
            }

            //
            // method
            if (fields.Exists("method") && !string.IsNullOrEmpty(trace.Method)) {
                obj.method = trace.Method;
            }

            //
            // status_code
            if (fields.Exists("status_code") && trace.StatusCode > 0) {
                obj.status_code = trace.StatusCode;
            }

            //
            // date
            if (fields.Exists("date")) {
                obj.date = trace.Date;
            }

            //
            // time_taken
            if (fields.Exists("time_taken")) {
                obj.time_taken = trace.TimeTaken;
            }

            //
            // process_id
            if (fields.Exists("process_id") && !string.IsNullOrEmpty(trace.ProcessId)) {
                obj.process_id = trace.ProcessId;
            }

            //
            // activity_id
            if (fields.Exists("activity_id") && !string.IsNullOrEmpty(trace.ActivityId)) {
                obj.activity_id = trace.ActivityId;
            }

            //
            // file_info
            if (fields.Exists("file_info")) {
                obj.file_info = new FilesHelper(_provider).ToJsonModelRef(trace.File, fields.Filter("file_info"));
            }

            //
            // request_tracing
            if (fields.Exists("request_tracing")) {
                obj.request_tracing = Helper.ToJsonModelRef(_site, "/");
            }

            return Core.Environment.Hal.Apply(Defines.TracesResource.Guid, obj, full); ;
        }

        public object ToJsonModelRef(TraceInfo trace, Fields fields = null)
        {
            if (fields == null || !fields.HasFields) {
                return ToJsonModel(trace, RefFields, false);
            }
            else {
                return ToJsonModel(trace, fields, false);
            }
        }

        public static string GetLocation(string id)
        {
            return $"/{Defines.TRACES_PATH}/{id}";
        }

        private Task<TraceInfo> GetTraceInternal(IFileInfo trace)
        {
            return ParseTrace(trace);
        }

        private async Task<TraceInfo> ParseTrace(IFileInfo trace)
        {
            TraceInfo info = null;


            using (var stream = _provider.GetFileStream(trace.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = XmlReader.Create(stream, _xmlReaderSettings)) {
                try {

                    while (await reader.ReadAsync()) {
                        if (reader.NodeType == XmlNodeType.Element && reader.Name.Equals("failedRequest")) {
                            
                            info = new TraceInfo() {
                                File = trace,
                                Url = reader.GetAttribute("url"),
                                Method = reader.GetAttribute("verb"),
                                Date = trace.Created,
                                ProcessId = reader.GetAttribute("processId"),
                                ActivityId = reader.GetAttribute("activityId")
                            };

                            float.TryParse(reader.GetAttribute("triggerStatusCode"), out info.StatusCode);
                            int.TryParse(reader.GetAttribute("timeTaken"), out info.TimeTaken);
                            
                            break;
                        }
                    }
                }
                catch(XmlException) {
                    // Ignore malformatted XML
                }
            }
            
            if (info == null) {
                info = new TraceInfo() {
                    File = trace
                };
            }

            return info;
        }
    }
}
