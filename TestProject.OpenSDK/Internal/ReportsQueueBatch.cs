// <copyright file="ReportsQueue.cs" company="TestProject">
// Copyright 2020 TestProject (https://testproject.io)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

namespace TestProject.OpenSDK.Internal
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using NLog;
    using RestSharp;
    using TestProject.OpenSDK.Exceptions;
    using TestProject.OpenSDK.Internal.Helpers;
    using TestProject.OpenSDK.Internal.Rest.Messages;
    using static TestProject.OpenSDK.Internal.Rest.AgentClient;

    /// <summary>
    /// A queue class managing the sending of report items to the Agent in a separate thread.
    /// This queue class sending reports as batches instead of sending reports one by one.
    /// </summary>
    public class ReportsQueueBatch : ReportsQueue
    {
        /// <summary>
        /// Default batch report size is maximum 10 reports.
        /// </summary>
        private const int MaxReportsBatchSize = 10;

        /// <summary>
        /// Name of the environment variable that stores the max reports batch size.
        /// Default value of <see cref="MaxReportsBatchSize"> can be override by this environment variable.
        /// </summary>
        private const string TpMaxBatchSize = "TP_MAX_BATCH_SIZE";

        /// <summary>
        /// The max batch size used in reports batch creation.
        /// In case environment variable <see cref="tpMaxBatchSize"> was defined - this will be the batch size.
        /// Default value defined as <see cref="MaxReportsBatchSize">.
        /// </summary>
        private readonly int maxBatchSize;

        /// <summary>
        /// Logger instance for this class.
        /// </summary>
        private static Logger Logger { get; set; } = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Configuration settings for (de-)serializing objects to / from JSON.
        /// This configuration is needed because the type field we send to the agent should be Pascal case instead of camel case.
        /// </summary>
        private JsonSerializerSettings serializerSettings;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReportsQueueBatch"/> class.
        /// </summary>
        /// <param name="client">The <see cref="RestClient"/> HTTP client to send reports to the Agent.</param>
        public ReportsQueueBatch(RestClient client)
            : base(client)
        {
            this.serializerSettings = CustomJsonSerializer.Populate(new JsonSerializerSettings());

            // Override default converter of enum to string since agent is case sensitive.
            this.serializerSettings.Converters.Clear();
            this.serializerSettings.Converters.Add(new StringEnumConverter());

            // Try to get maximum report batch size from env variable.
            string tpMaxBatchSizeEnvVal = Environment.GetEnvironmentVariable(TpMaxBatchSize);
            this.maxBatchSize = (tpMaxBatchSizeEnvVal != null) ? int.Parse(tpMaxBatchSizeEnvVal) : MaxReportsBatchSize;
        }

        /// <summary>
        ///  Overriding the base method to handle reports at batches.
        ///  While there are reports in the queue - collect up to 10 reports and send them at batch.
        /// </summary>
        /// <exception>FailedReportException if cannot send report to the agent more than <see cref="MaxReportFailureAttempts"/> attempts.</exception>
        protected override void HandleReport()
        {
            // LinkedList to store the reports batch before sending them.
            LinkedList<Report> batchReports = new LinkedList<Report>();

            // Extract and remove up to 10 items or till queue is empty from queue - without blocking it.
            while (this.ReportItems.Count > 0 && batchReports.Count < this.maxBatchSize)
            {
                QueueItem item;

                // Get the first item in the queue without blocking it.
                bool taken = this.ReportItems.TryTake(out item);

                if (taken && item != null && item.Report != null)
                {
                    batchReports.AddLast(item.Report);
                }
            }

            if (batchReports.Count > 0)
            {
                // Build REST request.
                RestRequest sendReportsBatchRequest = new RestRequest(Endpoints.REPORT_BATCH, Method.POST);
                sendReportsBatchRequest.RequestFormat = DataFormat.Json;
                string json = CustomJsonSerializer.ToJson(batchReports, this.serializerSettings);
                sendReportsBatchRequest.AddJsonBody(json);

                IRestResponse response;
                int reportAttemptsCount = MaxReportFailureAttempts;
                do
                {
                    // Send REST request.
                    response = this.Client.Execute(sendReportsBatchRequest);

                    if (response.IsSuccessful)
                    {
                        break;
                    }

                    Logger.Error($"Agent returned HTTP {(int)response.StatusCode} with message: {response.ErrorMessage}");
                    reportAttemptsCount--;
                    Logger.Error($"Attempt to send report again to the Agent. {reportAttemptsCount} more attempts are left.");
                }
                while (!response.IsSuccessful && reportAttemptsCount > 0);

                // In case all attepts to send the report are failed.
                if (reportAttemptsCount == 0)
                {
                    Logger.Error($"All {MaxReportFailureAttempts} attempts to send report {sendReportsBatchRequest.Body.ToJson()} failed");
                    throw new FailedReportException($"All {MaxReportFailureAttempts} attempts to send report {sendReportsBatchRequest.Body.ToJson()} failed");
                }

                // Clear all reports in the list after they were reported.
                batchReports.Clear();
            }
        }
    }
}